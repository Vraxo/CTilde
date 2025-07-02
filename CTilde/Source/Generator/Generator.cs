using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTilde;

public class Generator
{
    private readonly ProgramNode _program;
    private readonly TypeManager _typeManager;
    private readonly StringBuilder _sb = new();
    private int _labelCounter;
    private readonly Dictionary<string, string> _stringLiterals = new();
    private readonly HashSet<string> _externalFunctions = new();

    // State for the current function being generated
    private SymbolTable _currentSymbols = null!;
    private string? _currentMethodOwnerStruct;

    public Generator(ProgramNode program)
    {
        _program = program;
        _typeManager = new TypeManager(program);
    }

    public string Generate()
    {
        FindAllStringLiterals(_program);
        foreach (var f in _program.Functions.Where(f => f.Body == null)) _externalFunctions.Add(f.Name);

        _sb.AppendLine("format PE GUI 4.0");
        _sb.AppendLine("entry start");
        _sb.AppendLine();
        _sb.AppendLine("include 'win32a.inc'");
        _sb.AppendLine();
        _sb.AppendLine("section '.data' data readable writeable");
        foreach (var (label, value) in _stringLiterals) _sb.AppendLine($"    {label} db {FormatStringForFasm(value)}");
        _sb.AppendLine();
        _sb.AppendLine("section '.text' code readable executable");
        _sb.AppendLine();
        _sb.AppendLine("start:");
        _sb.AppendLine("    call _main");
        _sb.AppendLine("    mov ebx, eax");
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
            if (!libraries.ContainsKey(import.LibraryName)) libraries[import.LibraryName] = new List<string>();
        }

        foreach (var funcName in _externalFunctions)
        {
            bool found = false;
            foreach (var import in _program.Imports)
            {
                if (import.LibraryName != "kernel32.dll" && import.LibraryName != "msvcrt.dll")
                {
                    libraries[import.LibraryName].Add(funcName);
                    found = true;
                    break;
                }
            }
            if (!found && funcName != "printf")
            {
                if (!libraries.ContainsKey("user32.dll")) libraries["user32.dll"] = new List<string>();
                if (!libraries["user32.dll"].Contains(funcName)) libraries["user32.dll"].Add(funcName);
            }
        }

        var libDefs = libraries.Keys.Select(lib => $"{lib.Split('.')[0]},'{lib}'");
        _sb.AppendLine($"    library {string.Join(", ", libDefs)}");
        _sb.AppendLine();

        foreach (var (libName, functions) in libraries)
        {
            if (functions.Count > 0)
            {
                var libAlias = libName.Split('.')[0];
                var importDefs = functions.Distinct().Select(f => $"{f},'{f}'");
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
            else currentString.Append(c);
        }

        if (currentString.Length > 0) parts.Add($"'{currentString}'");
        parts.Add("0");
        return string.Join(", ", parts);
    }

    private void FindAllStringLiterals(AstNode node)
    {
        if (node is StringLiteralNode str && !_stringLiterals.ContainsValue(str.Value)) _stringLiterals.Add(str.Label, str.Value);
        foreach (var property in node.GetType().GetProperties())
        {
            if (property.Name == "Parent") continue;
            if (property.GetValue(node) is AstNode child) FindAllStringLiterals(child);
            else if (property.GetValue(node) is IEnumerable<AstNode> children) foreach (var c in children) FindAllStringLiterals(c);
        }
    }

    private void AppendAsm(string? instruction, string? comment = null)
    {
        var line = instruction == null ? "" : $"    {instruction}";
        _sb.AppendLine(line.PadRight(35) + (comment == null ? "" : $"; {comment}"));
    }

    private void GenerateFunctionEpilogue()
    {
        AppendAsm("pop edi");
        AppendAsm("pop esi");
        AppendAsm("pop ebx");
        AppendAsm("mov esp, ebp");
        AppendAsm("pop ebp");
        AppendAsm("ret");
    }

    private void GenerateFunction(FunctionDeclarationNode function)
    {
        _currentMethodOwnerStruct = function.OwnerStructName;
        _currentSymbols = new SymbolTable(function, _typeManager);

        string mangledName = function.OwnerStructName != null
            ? $"_{function.OwnerStructName}_{function.Name}"
            : $"_{function.Name}";

        _sb.AppendLine($"{mangledName}:");
        AppendAsm("push ebp");
        AppendAsm("mov ebp, esp");
        AppendAsm("push ebx", "Preserve non-volatile registers");
        AppendAsm("push esi");
        AppendAsm("push edi");
        _sb.AppendLine();

        int totalLocalSize = _currentSymbols.TotalLocalSize;
        if (totalLocalSize > 0)
        {
            AppendAsm($"sub esp, {totalLocalSize}", $"Allocate space for all local variables");
        }

        if (function.Body != null)
        {
            GenerateStatement(function.Body);
        }

        _sb.AppendLine();
        AppendAsm(null, "Implicit return cleanup");
        GenerateFunctionEpilogue();
    }

    private void GenerateReturn(ReturnStatementNode ret)
    {
        if (ret.Expression != null) GenerateExpression(ret.Expression);
        GenerateFunctionEpilogue();
    }

    private void GenerateWhile(WhileStatementNode w)
    {
        int i = _labelCounter++;
        _sb.AppendLine($"_while_start_{i}:");
        GenerateExpression(w.Condition);
        AppendAsm("cmp eax, 0");
        AppendAsm($"je _while_end_{i}");
        GenerateStatement(w.Body);
        AppendAsm($"jmp _while_start_{i}");
        _sb.AppendLine($"_while_end_{i}:");
    }

    private void GenerateIf(IfStatementNode i)
    {
        int idx = _labelCounter++;
        GenerateExpression(i.Condition);
        AppendAsm("cmp eax, 0");
        AppendAsm(i.ElseBody != null ? $"je _if_else_{idx}" : $"je _if_end_{idx}");
        GenerateStatement(i.ThenBody);
        if (i.ElseBody != null)
        {
            AppendAsm($"jmp _if_end_{idx}");
            _sb.AppendLine($"_if_else_{idx}:");
            GenerateStatement(i.ElseBody);
        }
        _sb.AppendLine($"_if_end_{idx}:");
    }

    private void GenerateStatement(StatementNode statement)
    {
        switch (statement)
        {
            case ReturnStatementNode ret: GenerateReturn(ret); break;
            case BlockStatementNode block: foreach (var s in block.Statements) GenerateStatement(s); break;
            case WhileStatementNode w: GenerateWhile(w); break;
            case IfStatementNode i: GenerateIf(i); break;
            case DeclarationStatementNode decl:
                if (decl.Initializer != null)
                {
                    if (decl.Initializer is InitializerListExpressionNode initList)
                    {
                        string typeName = _typeManager.GetTypeName(decl.Type, decl.PointerLevel);
                        if (!_typeManager.IsStruct(typeName))
                            throw new InvalidOperationException($"Initializer list can only be used for struct types, not '{typeName}'.");

                        var structDef = _program.Structs.First(s => s.Name == typeName);

                        if (initList.Values.Count > structDef.Members.Count)
                            throw new InvalidOperationException($"Too many values in initializer list for struct '{structDef.Name}'.");

                        _currentSymbols.TryGetSymbol(decl.Identifier.Value, out var structBaseOffset, out _);
                        int currentMemberOffset = 0;

                        for (int j = 0; j < initList.Values.Count; j++)
                        {
                            var member = structDef.Members[j];
                            var valueExpr = initList.Values[j];

                            var memberTypeName = _typeManager.GetTypeName(member.Type, member.PointerLevel);
                            var memberSize = _typeManager.GetSizeOfType(memberTypeName);
                            var totalOffset = structBaseOffset + currentMemberOffset;

                            GenerateExpression(valueExpr);
                            if (memberSize == 1) AppendAsm($"mov byte [ebp + {totalOffset}], al", $"Init member {member.Name.Value}");
                            else AppendAsm($"mov dword [ebp + {totalOffset}], eax", $"Init member {member.Name.Value}");

                            currentMemberOffset += memberSize;
                        }
                    }
                    else
                    {
                        var left = new VariableExpressionNode(decl.Identifier);
                        var assignment = new AssignmentExpressionNode(left, decl.Initializer);
                        GenerateExpression(assignment);
                    }
                }
                break;
            case ExpressionStatementNode exprStmt: GenerateExpression(exprStmt.Expression); break;
            default: throw new NotImplementedException($"Stmt: {statement.GetType().Name}");
        }
    }

    private void GenerateLValueAddress(ExpressionNode expression)
    {
        switch (expression)
        {
            case VariableExpressionNode varExpr:
                {
                    if (_currentSymbols.TryGetSymbol(varExpr.Identifier.Value, out var offset, out _))
                    {
                        string sign = offset > 0 ? "+" : "";
                        AppendAsm($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
                    }
                    else if (_currentMethodOwnerStruct != null)
                    {
                        try
                        {
                            var (memberOffset, _) = _typeManager.GetMemberInfo(_currentMethodOwnerStruct, varExpr.Identifier.Value);
                            _currentSymbols.TryGetSymbol("this", out var thisOffset, out _);
                            AppendAsm($"mov eax, [ebp + {thisOffset}]", "Get `this` pointer value");
                            if (memberOffset > 0)
                            {
                                AppendAsm($"add eax, {memberOffset}", $"Offset for implicit this->{varExpr.Identifier.Value}");
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
                    }
                    break;
                }
            case MemberAccessExpressionNode memberAccess:
                {
                    var leftType = _typeManager.GetExpressionType(memberAccess.Left, _currentSymbols, _currentMethodOwnerStruct);
                    string baseStructType = leftType.EndsWith("*") ? leftType.Substring(0, leftType.Length - 1) : leftType;

                    var structDef = _program.Structs.First(s => s.Name == baseStructType);
                    var memberVar = structDef.Members.FirstOrDefault(m => m.Name.Value == memberAccess.Member.Value);
                    if (memberVar == null) throw new InvalidOperationException($"Struct '{baseStructType}' has no member '{memberAccess.Member.Value}'");

                    if (memberVar.AccessLevel == AccessSpecifier.Private && _currentMethodOwnerStruct != baseStructType)
                    {
                        throw new InvalidOperationException($"Cannot access private member '{baseStructType}::{memberAccess.Member.Value}' from context '{_currentMethodOwnerStruct ?? "global"}'.");
                    }

                    if (memberAccess.Operator.Type == TokenType.Dot)
                    {
                        GenerateLValueAddress(memberAccess.Left);
                    }
                    else
                    {
                        GenerateExpression(memberAccess.Left);
                    }

                    var (memberOffset, _) = _typeManager.GetMemberInfo(baseStructType, memberAccess.Member.Value);
                    if (memberOffset > 0)
                    {
                        AppendAsm($"add eax, {memberOffset}", $"Offset for member {memberAccess.Operator.Value}{memberAccess.Member.Value}");
                    }
                    break;
                }
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                GenerateExpression(u.Right);
                break;
            default: throw new InvalidOperationException($"Expression '{expression.GetType().Name}' is not a valid L-value.");
        }
    }

    private void GenerateExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case IntegerLiteralNode literal: AppendAsm($"mov eax, {literal.Value}"); break;
            case StringLiteralNode str: AppendAsm($"mov eax, {str.Label}"); break;
            case VariableExpressionNode varExpr:
                _currentSymbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out _);
                if (offset > 0)
                {
                    var exprType = _typeManager.GetExpressionType(varExpr, _currentSymbols, _currentMethodOwnerStruct);
                    if (_typeManager.IsStruct(exprType))
                    {
                        AppendAsm($"lea eax, [ebp + {offset}]");
                    }
                    else
                    {
                        if (_typeManager.GetSizeOfType(exprType) == 1) AppendAsm($"movzx eax, byte [ebp + {offset}]");
                        else AppendAsm($"mov eax, [ebp + {offset}]");
                    }
                }
                else
                {
                    GenerateLValueAddress(varExpr);
                    var type = _typeManager.GetExpressionType(varExpr, _currentSymbols, _currentMethodOwnerStruct);
                    if (_typeManager.GetSizeOfType(type) == 1) AppendAsm("movzx eax, byte [eax]");
                    else AppendAsm("mov eax, [eax]");
                }
                break;
            case UnaryExpressionNode u:
                if (u.Operator.Type == TokenType.Ampersand) GenerateLValueAddress(u.Right);
                else
                {
                    GenerateExpression(u.Right);
                    if (u.Operator.Type == TokenType.Minus) AppendAsm("neg eax");
                    else if (u.Operator.Type == TokenType.Star)
                    {
                        var type = _typeManager.GetExpressionType(u, _currentSymbols, _currentMethodOwnerStruct);
                        if (_typeManager.GetSizeOfType(type) == 1) AppendAsm("movzx eax, byte [eax]");
                        else AppendAsm("mov eax, [eax]");
                    }
                }
                break;
            case MemberAccessExpressionNode m:
                GenerateLValueAddress(m);
                var memberType = _typeManager.GetExpressionType(m, _currentSymbols, _currentMethodOwnerStruct);
                if (_typeManager.GetSizeOfType(memberType) == 1) AppendAsm("movzx eax, byte [eax]");
                else AppendAsm("mov eax, [eax]");
                break;
            case AssignmentExpressionNode assign:
                {
                    var lValueType = _typeManager.GetExpressionType(assign.Left, _currentSymbols, _currentMethodOwnerStruct);
                    var isStructAssign = _typeManager.IsStruct(lValueType);

                    if (isStructAssign)
                    {
                        GenerateLValueAddress(assign.Left);
                        AppendAsm("push eax");
                        GenerateExpression(assign.Right);
                        AppendAsm("pop edi");
                        AppendAsm("mov esi, eax");

                        int size = _typeManager.GetSizeOfType(lValueType);
                        AppendAsm($"mov ecx, {size / 4}");
                        AppendAsm("rep movsd");
                        if (size % 4 > 0)
                        {
                            AppendAsm($"mov ecx, {size % 4}");
                            AppendAsm("rep movsb");
                        }
                    }
                    else
                    {
                        GenerateLValueAddress(assign.Left);
                        AppendAsm("push eax");
                        GenerateExpression(assign.Right);
                        AppendAsm("pop ecx");
                        if (_typeManager.GetSizeOfType(lValueType) == 1) AppendAsm("mov [ecx], al");
                        else AppendAsm("mov [ecx], eax");
                    }
                    break;
                }
            case BinaryExpressionNode binExpr: GenerateBinaryExpression(binExpr); break;
            case CallExpressionNode callExpr: GenerateCallExpression(callExpr); break;
            default: throw new NotImplementedException($"Expr: {expression.GetType().Name}");
        }
    }

    private void GenerateCallExpression(CallExpressionNode callExpr)
    {
        int totalArgSize = 0;
        string calleeTarget;

        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            var argType = _typeManager.GetExpressionType(arg, _currentSymbols, _currentMethodOwnerStruct);
            var isStruct = _typeManager.IsStruct(argType);

            if (isStruct)
            {
                int argSize = _typeManager.GetSizeOfType(argType);
                GenerateLValueAddress(arg);
                for (int offset = argSize - 4; offset >= 0; offset -= 4)
                {
                    AppendAsm($"push dword [eax + {offset}]");
                }
                totalArgSize += argSize;
            }
            else
            {
                GenerateExpression(arg);
                AppendAsm("push eax");
                totalArgSize += 4;
            }
        }

        if (callExpr.Callee is MemberAccessExpressionNode memberAccess)
        {
            var leftType = _typeManager.GetExpressionType(memberAccess.Left, _currentSymbols, _currentMethodOwnerStruct);
            var baseStructType = leftType.TrimEnd('*');
            var method = _program.Functions.First(f => f.OwnerStructName == baseStructType && f.Name == memberAccess.Member.Value);

            if (method.AccessLevel == AccessSpecifier.Private && _currentMethodOwnerStruct != baseStructType)
                throw new InvalidOperationException($"Cannot access private method '{baseStructType}::{memberAccess.Member.Value}'.");

            GenerateLValueAddress(memberAccess.Left);
            AppendAsm("push eax", "Push 'this' pointer");
            totalArgSize += 4;
            calleeTarget = $"_{baseStructType}_{memberAccess.Member.Value}";
        }
        else if (callExpr.Callee is VariableExpressionNode varNode)
        {
            string calleeName = varNode.Identifier.Value;
            calleeTarget = _externalFunctions.Contains(calleeName) ? $"[{calleeName}]" : $"_{calleeName}";
        }
        else throw new NotSupportedException($"Unsupported callee type for call: {callExpr.Callee.GetType().Name}");

        AppendAsm($"call {calleeTarget}");

        if (totalArgSize > 0) AppendAsm($"add esp, {totalArgSize}", "Clean up args");
    }

    private void GenerateBinaryExpression(BinaryExpressionNode binExpr)
    {
        GenerateExpression(binExpr.Right);
        AppendAsm("push eax");
        GenerateExpression(binExpr.Left);
        AppendAsm("pop ecx");

        switch (binExpr.Operator.Type)
        {
            case TokenType.Plus: AppendAsm("add eax, ecx"); break;
            case TokenType.Minus: AppendAsm("sub eax, ecx"); break;
            case TokenType.Star: AppendAsm("imul eax, ecx"); break;
            case TokenType.Slash: AppendAsm("cdq"); AppendAsm("idiv ecx"); break;
            case TokenType.DoubleEquals: AppendAsm("cmp eax, ecx"); AppendAsm("sete al"); AppendAsm("movzx eax, al"); break;
            case TokenType.NotEquals: AppendAsm("cmp eax, ecx"); AppendAsm("setne al"); AppendAsm("movzx eax, al"); break;
            case TokenType.LessThan: AppendAsm("cmp eax, ecx"); AppendAsm("setl al"); AppendAsm("movzx eax, al"); break;
            case TokenType.GreaterThan: AppendAsm("cmp eax, ecx"); AppendAsm("setg al"); AppendAsm("movzx eax, al"); break;
            default: throw new NotImplementedException($"Op: {binExpr.Operator.Type}");
        }
    }
}