using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTilde;

public class Generator
{
    private readonly ProgramNode _program;
    private readonly StringBuilder _sb = new();
    private int _labelCounter;
    private readonly Dictionary<string, string> _stringLiterals = new();
    private readonly HashSet<string> _externalFunctions = new();


    // Rudimentary symbol table for a function's scope
    private Dictionary<string, int> _variables = new();
    private int _stackOffset;

    public Generator(ProgramNode program)
    {
        _program = program;
    }

    public string Generate()
    {
        // Pre-pass to find all string literals and external functions
        FindAllStringLiterals(_program);
        foreach (var func in _program.Functions.Where(f => f.Body == null))
        {
            _externalFunctions.Add(func.Name);
        }

        _sb.AppendLine("format PE console");
        _sb.AppendLine("entry start");
        _sb.AppendLine();
        _sb.AppendLine("include 'win32a.inc'");
        _sb.AppendLine();
        _sb.AppendLine("section '.data' data readable writeable");
        foreach (var (label, value) in _stringLiterals)
        {
            _sb.AppendLine($"    {label} db {FormatStringForFasm(value)}");
        }
        _sb.AppendLine();
        _sb.AppendLine("section '.text' code readable executable");
        _sb.AppendLine();
        _sb.AppendLine("start:");
        _sb.AppendLine("    call _main");
        _sb.AppendLine("    mov ebx, eax"); // EAX holds the return value from main
        _sb.AppendLine("    push ebx");
        _sb.AppendLine("    call [ExitProcess]");
        _sb.AppendLine();

        foreach (var function in _program.Functions.Where(f => f.Body != null))
        {
            GenerateFunction(function);
            _sb.AppendLine();
        }

        GenerateImportDataSection();

        return _sb.ToString();
    }

    private void GenerateImportDataSection()
    {
        _sb.AppendLine("section '.idata' import data readable");
        _sb.AppendLine();

        var libraries = new Dictionary<string, List<string>>
        {
            { "kernel32.dll", new List<string> { "ExitProcess" } },
            { "msvcrt.dll", new List<string> { "printf" } }
        };

        foreach (var import in _program.Imports)
        {
            if (!libraries.ContainsKey(import.LibraryName))
            {
                libraries[import.LibraryName] = new List<string>();
            }
        }

        foreach (var funcName in _externalFunctions)
        {
            bool found = false;
            foreach (var import in _program.Imports)
            {
                // This is a simplification. A real compiler would need to know which DLL a function belongs to.
                // For now, we try to add it to a user-specified DLL.
                if (libraries.ContainsKey(import.LibraryName))
                {
                    libraries[import.LibraryName].Add(funcName);
                    found = true;
                    break;
                }
            }
            if (!found && funcName != "printf")
            {
                // We could default to a common library like msvcrt or user32, or throw an error.
                // For now, let's assume it's in msvcrt if not specified.
                libraries["msvcrt.dll"].Add(funcName);
            }
        }

        var libDefinitions = new List<string>();
        foreach (var lib in libraries.Keys)
        {
            string libAlias = lib.Split('.')[0];
            libDefinitions.Add($"{libAlias},'{lib}'");
        }
        _sb.AppendLine($"    library {string.Join(", ", libDefinitions)}");
        _sb.AppendLine();

        foreach (var (libName, functions) in libraries)
        {
            if (functions.Count > 0)
            {
                string libAlias = libName.Split('.')[0];
                var importDefs = functions.Select(f => $"{f},'{f}'");
                _sb.AppendLine($"    import {libAlias}, {string.Join(", ", importDefs)}");
            }
        }
    }
    private string FormatStringForFasm(string value)
    {
        var parts = new List<string>();
        var currentString = new StringBuilder();

        foreach (char c in value)
        {
            if (c is '\n' or '\t' or '\r' or '\'' or '"')
            {
                if (currentString.Length > 0)
                {
                    parts.Add($"'{currentString}'");
                    currentString.Clear();
                }
                parts.Add(((byte)c).ToString());
            }
            else
            {
                currentString.Append(c);
            }
        }

        if (currentString.Length > 0)
        {
            parts.Add($"'{currentString}'");
        }

        parts.Add("0"); // Null terminator

        return string.Join(", ", parts);
    }

    private void FindAllStringLiterals(AstNode node)
    {
        if (node is StringLiteralNode str)
        {
            if (!_stringLiterals.ContainsValue(str.Value))
            {
                _stringLiterals.Add(str.Label, str.Value);
            }
        }

        foreach (var property in node.GetType().GetProperties())
        {
            if (property.Name == "Parent") continue;

            if (property.GetValue(node) is AstNode child)
            {
                FindAllStringLiterals(child);
            }
            else if (property.GetValue(node) is IEnumerable<AstNode> children)
            {
                foreach (var c in children)
                {
                    FindAllStringLiterals(c);
                }
            }
        }
    }


    private void AppendAsm(string instruction, string? comment = null)
    {
        const int commentColumn = 36;
        string indentedInstruction = $"    {instruction}";

        if (string.IsNullOrEmpty(comment))
        {
            _sb.AppendLine(indentedInstruction);
        }
        else
        {
            _sb.AppendLine(indentedInstruction.PadRight(commentColumn - 1) + $"; {comment}");
        }
    }

    private void GenerateFunction(FunctionDeclarationNode function)
    {
        _variables = new Dictionary<string, int>();
        _stackOffset = 0;

        _sb.AppendLine($"_{function.Name}:");
        AppendAsm("push ebp");
        AppendAsm("mov ebp, esp");
        _sb.AppendLine();

        int paramOffset = 8;
        foreach (var param in function.Parameters)
        {
            _variables[param.Name.Value] = paramOffset;
            paramOffset += 4;
        }

        // The body will not be null here because we filter in the main Generate loop.
        GenerateStatement(function.Body!);

        _sb.AppendLine();
        AppendAsm("mov esp, ebp", "Implicit return cleanup");
        AppendAsm("pop ebp");
        AppendAsm("ret");
    }

    private void GenerateStatement(StatementNode statement)
    {
        switch (statement)
        {
            case ReturnStatementNode ret:
                GenerateReturn(ret);
                break;
            case BlockStatementNode block:
                foreach (var stmt in block.Statements)
                {
                    GenerateStatement(stmt);
                }
                break;
            case WhileStatementNode whileStmt:
                GenerateWhile(whileStmt);
                break;
            case IfStatementNode ifStmt:
                GenerateIf(ifStmt);
                break;
            case DeclarationStatementNode decl:
                GenerateDeclaration(decl);
                break;
            case ExpressionStatementNode exprStmt:
                GenerateExpression(exprStmt.Expression);
                if (exprStmt.Expression is CallExpressionNode call)
                {
                    int bytesToClean = call.Arguments.Count * 4;
                    if (bytesToClean > 0)
                    {
                        AppendAsm($"add esp, {bytesToClean}", "Clean up stack for call in expr stmt");
                    }
                }
                else
                {
                    AppendAsm("", "; Expression statement result in EAX is discarded");
                }
                break;
            default:
                throw new NotImplementedException($"Unsupported statement type: {statement.GetType().Name}");
        }
    }

    private void GenerateDeclaration(DeclarationStatementNode decl)
    {
        if (_variables.ContainsKey(decl.Identifier.Value))
        {
            throw new InvalidOperationException($"Variable '{decl.Identifier.Value}' is already defined.");
        }

        _stackOffset -= 4;
        _variables[decl.Identifier.Value] = _stackOffset;
        AppendAsm("sub esp, 4", "Allocate space for variable " + decl.Identifier.Value);

        if (decl.Initializer != null)
        {
            GenerateExpression(decl.Initializer);
            AppendAsm($"mov [ebp + {_stackOffset}], eax");
        }
    }

    private void GenerateWhile(WhileStatementNode whileStmt)
    {
        int labelIndex = _labelCounter++;
        string startLabel = $"_while_start_{labelIndex}";
        string endLabel = $"_while_end_{labelIndex}";

        _sb.AppendLine($"{startLabel}:");

        GenerateExpression(whileStmt.Condition);
        AppendAsm("cmp eax, 0");
        AppendAsm($"je {endLabel}");
        GenerateStatement(whileStmt.Body);
        AppendAsm($"jmp {startLabel}");

        _sb.AppendLine($"{endLabel}:");
    }

    private void GenerateIf(IfStatementNode ifStmt)
    {
        int labelIndex = _labelCounter++;
        string elseLabel = $"_if_else_{labelIndex}";
        string endLabel = $"_if_end_{labelIndex}";

        GenerateExpression(ifStmt.Condition);
        AppendAsm("cmp eax, 0");

        if (ifStmt.ElseBody != null)
        {
            AppendAsm($"je {elseLabel}");
        }
        else
        {
            AppendAsm($"je {endLabel}");
        }

        GenerateStatement(ifStmt.ThenBody);

        if (ifStmt.ElseBody != null)
        {
            AppendAsm($"jmp {endLabel}");
            _sb.AppendLine($"{elseLabel}:");
            GenerateStatement(ifStmt.ElseBody);
        }

        _sb.AppendLine($"{endLabel}:");
    }

    private void GenerateReturn(ReturnStatementNode ret)
    {
        if (ret.Expression != null)
        {
            GenerateExpression(ret.Expression);
        }
        AppendAsm("mov esp, ebp");
        AppendAsm("pop ebp");
        AppendAsm("ret");
    }

    private void GenerateExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case IntegerLiteralNode literal:
                AppendAsm($"mov eax, {literal.Value}");
                break;
            case StringLiteralNode str:
                AppendAsm($"mov eax, {str.Label}");
                break;
            case VariableExpressionNode varExpr:
                if (!_variables.TryGetValue(varExpr.Identifier.Value, out var offset))
                {
                    throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
                }
                string sign = offset > 0 ? "+" : "";
                AppendAsm($"mov eax, [ebp {sign} {offset}]", $"Load variable/param {varExpr.Identifier.Value}");
                break;
            case AssignmentExpressionNode assignExpr:
                if (!_variables.TryGetValue(assignExpr.Identifier.Value, out var assignOffset))
                {
                    throw new InvalidOperationException($"Undeclared variable '{assignExpr.Identifier.Value}' for assignment.");
                }
                GenerateExpression(assignExpr.Value);
                string assignSign = assignOffset > 0 ? "+" : "";
                AppendAsm($"mov [ebp {assignSign} {assignOffset}], eax", $"Assign to variable/param {assignExpr.Identifier.Value}");
                break;
            case BinaryExpressionNode binExpr:
                GenerateBinaryExpression(binExpr);
                break;
            case CallExpressionNode callExpr:
                GenerateCallExpression(callExpr);
                break;
            default:
                throw new NotImplementedException($"Unsupported expression type: {expression.GetType().Name}");
        }
    }

    private void GenerateCallExpression(CallExpressionNode callExpr)
    {
        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            GenerateExpression(arg);
            AppendAsm("push eax");
        }

        string calleeName = callExpr.Callee.Value;
        string callTarget = _externalFunctions.Contains(calleeName) ? $"[{calleeName}]" : $"_{calleeName}";

        AppendAsm($"call {callTarget}");

        if (callExpr.Parent is not ExpressionStatementNode)
        {
            if (callExpr.Arguments.Count > 0)
            {
                int bytesToClean = callExpr.Arguments.Count * 4;
                AppendAsm($"add esp, {bytesToClean}", $"Clean up {callExpr.Arguments.Count} args from stack");
            }
        }
    }

    private void GenerateBinaryExpression(BinaryExpressionNode binExpr)
    {
        GenerateExpression(binExpr.Right);
        AppendAsm("push eax");
        GenerateExpression(binExpr.Left);
        AppendAsm("pop ecx");

        switch (binExpr.Operator.Type)
        {
            case TokenType.Plus: AppendAsm("add eax, ecx", "eax = eax + ecx"); break;
            case TokenType.Minus: AppendAsm("sub eax, ecx", "eax = eax - ecx"); break;
            case TokenType.Star: AppendAsm("imul eax, ecx", "eax = eax * ecx"); break;
            case TokenType.Slash:
                AppendAsm("cdq", "Sign-extend EAX into EDX:EAX");
                AppendAsm("idiv ecx", "eax = edx:eax / ecx");
                break;
            case TokenType.DoubleEquals:
                AppendAsm("cmp eax, ecx");
                AppendAsm("sete al", "Set AL if equal");
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;
            case TokenType.NotEquals:
                AppendAsm("cmp eax, ecx");
                AppendAsm("setne al", "Set AL if not equal");
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;
            case TokenType.LessThan:
                AppendAsm("cmp eax, ecx");
                AppendAsm("setl al", "Set AL if less");
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;
            case TokenType.GreaterThan:
                AppendAsm("cmp eax, ecx");
                AppendAsm("setg al", "Set AL if greater");
                AppendAsm("movzx eax, al", "Zero-extend AL to EAX");
                break;
            default:
                throw new NotImplementedException($"Unsupported binary operator: {binExpr.Operator.Type}");
        }
    }
}