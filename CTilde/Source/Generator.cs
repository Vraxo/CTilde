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

    private readonly Dictionary<string, StructDefinitionNode> _structDefinitions = new();

    private Dictionary<string, int> _variables = new(); // name -> stack offset
    private Dictionary<string, string> _variableTypes = new(); // name -> type name (e.g. "int", "Color*")
    private int _stackOffset;

    public Generator(ProgramNode program)
    {
        _program = program;
    }

    public string Generate()
    {
        FindAllStringLiterals(_program);
        foreach (var s in _program.Structs) _structDefinitions[s.Name] = s;
        foreach (var f in _program.Functions.Where(f => f.Body == null)) _externalFunctions.Add(f.Name);

        _sb.AppendLine("format PE console");
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

    private int GetSizeOfType(string typeName)
    {
        if (typeName.EndsWith("*")) return 4;
        if (typeName == "int") return 4;
        if (typeName == "char") return 1;
        if (_structDefinitions.TryGetValue(typeName, out var structDef))
        {
            return structDef.Members.Sum(m => GetSizeOfType(m.Type.Value)); // Pointer level is 0 for struct members
        }
        throw new InvalidOperationException($"Unknown type '{typeName}'");
    }

    private (int offset, string type) GetMemberInfo(string structName, string memberName)
    {
        if (!_structDefinitions.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        int offset = 0;
        foreach (var member in structDef.Members)
        {
            if (member.Name.Value == memberName)
            {
                var sb = new StringBuilder(member.Type.Value);
                for (int i = 0; i < member.PointerLevel; i++) sb.Append('*');
                return (offset, sb.ToString());
            }

            var memberType = new StringBuilder(member.Type.Value);
            for (int i = 0; i < member.PointerLevel; i++) memberType.Append('*');

            offset += GetSizeOfType(memberType.ToString());
        }
        throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");
    }

    private void AppendAsm(string instruction, string? comment = null)
    {
        _sb.AppendLine($"    {instruction}".PadRight(35) + (comment == null ? "" : $"; {comment}"));
    }

    private void GenerateFunction(FunctionDeclarationNode function)
    {
        _variables.Clear();
        _variableTypes.Clear();
        _stackOffset = 0;

        _sb.AppendLine($"_{function.Name}:");
        AppendAsm("push ebp");
        AppendAsm("mov ebp, esp");
        _sb.AppendLine();

        int paramOffset = 8;
        foreach (var param in function.Parameters)
        {
            _variables[param.Name.Value] = paramOffset;

            var typeName = new StringBuilder(param.Type.Value);
            for (int i = 0; i < param.PointerLevel; i++) typeName.Append('*');
            _variableTypes[param.Name.Value] = typeName.ToString();

            paramOffset += GetSizeOfType(typeName.ToString());
        }

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
            case ReturnStatementNode ret: GenerateReturn(ret); break;
            case BlockStatementNode block: foreach (var s in block.Statements) GenerateStatement(s); break;
            case WhileStatementNode w: GenerateWhile(w); break;
            case IfStatementNode i: GenerateIf(i); break;
            case DeclarationStatementNode decl: GenerateDeclaration(decl); break;
            case ExpressionStatementNode exprStmt: GenerateExpression(exprStmt.Expression); break;
            default: throw new NotImplementedException($"Stmt: {statement.GetType().Name}");
        }
    }

    private void GenerateDeclaration(DeclarationStatementNode decl)
    {
        if (_variables.ContainsKey(decl.Identifier.Value)) throw new InvalidOperationException($"Var '{decl.Identifier.Value}' already defined.");

        var typeName = new StringBuilder(decl.Type.Value);
        for (int i = 0; i < decl.PointerLevel; i++) typeName.Append('*');
        _variableTypes[decl.Identifier.Value] = typeName.ToString();

        int size = GetSizeOfType(typeName.ToString());
        _stackOffset -= size;
        _variables[decl.Identifier.Value] = _stackOffset;

        AppendAsm($"sub esp, {size}", $"Allocate {size} bytes for var {decl.Identifier.Value}");

        if (decl.Initializer != null)
        {
            if (decl.Initializer is InitializerListExpressionNode initList)
            {
                if (!_structDefinitions.TryGetValue(decl.Type.Value, out var structDef))
                    throw new InvalidOperationException($"Cannot use initializer list for non-struct type '{decl.Type.Value}'");

                if (initList.Values.Count != structDef.Members.Count)
                    throw new InvalidOperationException($"Initializer list has {initList.Values.Count} values, but struct '{structDef.Name}' has {structDef.Members.Count} members.");

                int currentMemberOffset = 0;
                for (var i = 0; i < initList.Values.Count; i++)
                {
                    var valueExpr = initList.Values[i];
                    var member = structDef.Members[i];

                    var memberType = new StringBuilder(member.Type.Value);
                    for (int j = 0; j < member.PointerLevel; j++) memberType.Append('*');
                    var memberSize = GetSizeOfType(memberType.ToString());

                    GenerateExpression(valueExpr); // Result is in EAX

                    var totalOffset = _stackOffset + currentMemberOffset;
                    if (memberSize == 1)
                        AppendAsm($"mov [ebp + {totalOffset}], al", $"Initialize member {member.Name.Value}");
                    else if (memberSize == 4)
                        AppendAsm($"mov [ebp + {totalOffset}], eax", $"Initialize member {member.Name.Value}");
                    else
                        throw new NotSupportedException($"Struct member initialization for size {memberSize} not supported.");

                    currentMemberOffset += memberSize;
                }
            }
            else
            {
                if (size > 4) throw new NotSupportedException("Struct initialization must use an initializer list. Struct copy initialization is not supported.");
                GenerateExpression(decl.Initializer);
                AppendAsm($"mov [ebp + {_stackOffset}], eax");
            }
        }
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

    private void GenerateReturn(ReturnStatementNode ret)
    {
        if (ret.Expression != null) GenerateExpression(ret.Expression);
        AppendAsm("mov esp, ebp");
        AppendAsm("pop ebp");
        AppendAsm("ret");
    }

    private void GenerateLValueAddress(ExpressionNode expression)
    {
        switch (expression)
        {
            case VariableExpressionNode varExpr:
                if (!_variables.TryGetValue(varExpr.Identifier.Value, out var offset)) throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
                string sign = offset > 0 ? "+" : "";
                AppendAsm($"lea eax, [ebp {sign} {offset}]", $"Get address of var {varExpr.Identifier.Value}");
                break;
            case MemberAccessExpressionNode memberAccess:
                GenerateLValueAddress(memberAccess.Left); // Puts base address of struct in EAX
                var leftType = GetExpressionType(memberAccess.Left);
                var (memberOffset, _) = GetMemberInfo(leftType, memberAccess.Member.Value);
                AppendAsm($"add eax, {memberOffset}", $"Offset for member .{memberAccess.Member.Value}");
                break;
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star: // *ptr
                // The address of *ptr is the value held by ptr.
                GenerateExpression(u.Right); // puts value of ptr (the address it holds) into EAX.
                break;
            default: throw new InvalidOperationException($"Expression '{expression.GetType().Name}' is not a valid L-value.");
        }
    }

    private string GetExpressionType(ExpressionNode expr)
    {
        switch (expr)
        {
            case IntegerLiteralNode: return "int";
            case StringLiteralNode: return "char*"; // String literals are pointers to char
            case VariableExpressionNode v: return _variableTypes[v.Identifier.Value];
            case AssignmentExpressionNode a: return GetExpressionType(a.Right); // C-style: type of assignment is type of rhs
            case MemberAccessExpressionNode m:
                {
                    var leftType = GetExpressionType(m.Left);
                    if (leftType.EndsWith("*")) throw new InvalidOperationException($"Cannot use . on a pointer type '{leftType}'. Dereference it first.");
                    var (_, memberType) = GetMemberInfo(leftType, m.Member.Value);
                    return memberType;
                }
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Ampersand:
                {
                    var operandType = GetExpressionType(u.Right);
                    return operandType + "*";
                }
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                {
                    var operandType = GetExpressionType(u.Right);
                    if (!operandType.EndsWith("*")) throw new InvalidOperationException($"Cannot dereference non-pointer type '{operandType}'.");
                    return operandType.Substring(0, operandType.Length - 1);
                }
            case CallExpressionNode call:
                {
                    var func = _program.Functions.First(f => f.Name == call.Callee.Value);
                    var sb = new StringBuilder(func.ReturnType.Value);
                    for (int i = 0; i < func.ReturnPointerLevel; i++) sb.Append('*');
                    return sb.ToString();
                }
            case BinaryExpressionNode: return "int"; // Assume all binary ops result in int for now
            default: throw new NotImplementedException($"GetExpressionType not implemented for {expr.GetType().Name}");
        }
    }

    private void GenerateExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case IntegerLiteralNode literal: AppendAsm($"mov eax, {literal.Value}"); break;
            case StringLiteralNode str: AppendAsm($"mov eax, {str.Label}"); break;
            case UnaryExpressionNode u:
                if (u.Operator.Type == TokenType.Minus)
                {
                    GenerateExpression(u.Right);
                    AppendAsm("neg eax", "Negate value");
                }
                else if (u.Operator.Type == TokenType.Plus)
                {
                    GenerateExpression(u.Right); // No-op
                }
                else if (u.Operator.Type == TokenType.Ampersand)
                {
                    GenerateLValueAddress(u.Right); // Address-of
                }
                else // Star must be handled as a read from an L-value
                {
                    goto ReadLValue;
                }
                break;
            case VariableExpressionNode:
            case MemberAccessExpressionNode:
            ReadLValue:
                var exprType = GetExpressionType(expression);
                GenerateLValueAddress(expression);
                if (GetSizeOfType(exprType) == 1)
                {
                    AppendAsm("movzx eax, byte [eax]", "Dereference byte and zero-extend");
                }
                else
                {
                    AppendAsm("mov eax, [eax]", "Dereference address to get value");
                }
                break;
            case AssignmentExpressionNode assign:
                var lvalueType = GetExpressionType(assign.Left);
                GenerateLValueAddress(assign.Left);
                AppendAsm("push eax");
                GenerateExpression(assign.Right);
                AppendAsm("pop ecx");
                if (GetSizeOfType(lvalueType) == 1)
                {
                    AppendAsm("mov [ecx], al");
                }
                else
                {
                    AppendAsm("mov [ecx], eax");
                }
                break;
            case BinaryExpressionNode binExpr: GenerateBinaryExpression(binExpr); break;
            case CallExpressionNode callExpr: GenerateCallExpression(callExpr); break;
            default: throw new NotImplementedException($"Expr: {expression.GetType().Name}");
        }
    }

    private void GenerateCallExpression(CallExpressionNode callExpr)
    {
        int totalArgSize = 0;
        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            string argType = GetExpressionType(arg);
            int argSize = GetSizeOfType(argType);
            totalArgSize += argSize;

            if (argSize <= 4) // Pass by value for int/pointer
            {
                GenerateExpression(arg);
                AppendAsm("push eax");
            }
            else // Pass struct by value
            {
                GenerateLValueAddress(arg); // Get address of struct
                AppendAsm("mov esi, eax", $"Copy struct for call");
                for (int offset = argSize - 4; offset >= 0; offset -= 4)
                {
                    AppendAsm($"push dword [esi + {offset}]");
                }
            }
        }

        string calleeName = callExpr.Callee.Value;
        string callTarget = _externalFunctions.Contains(calleeName) ? $"[{calleeName}]" : $"_{calleeName}";

        AppendAsm($"call {callTarget}");

        if (totalArgSize > 0)
        {
            AppendAsm($"add esp, {totalArgSize}", $"Clean up {callExpr.Arguments.Count} args");
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