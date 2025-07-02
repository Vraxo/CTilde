using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class CodeGenerator
{
    private readonly ProgramNode _program;
    private readonly TypeManager _typeManager;
    private readonly FasmWriter _fasmWriter = new();
    private readonly AssemblyBuilder _builder = new();

    private int _labelCounter;
    private readonly Dictionary<string, string> _stringLiterals = new();
    private readonly HashSet<string> _externalFunctions = new();

    // State for the current function being generated
    private SymbolTable _currentSymbols = null!;
    private string? _currentMethodOwnerStruct;

    public CodeGenerator(ProgramNode program)
    {
        _program = program;
        _typeManager = new TypeManager(program);
    }

    public string Generate()
    {
        FindAllStringLiterals(_program);
        foreach (var f in _program.Functions.Where(f => f.Body == null))
        {
            _externalFunctions.Add(f.Name);
        }

        _fasmWriter.WritePreamble(_builder);
        _fasmWriter.WriteDataSection(_builder, _stringLiterals);
        _fasmWriter.WriteTextSectionHeader(_builder);
        _fasmWriter.WriteEntryPoint(_builder);

        foreach (var function in _program.Functions.Where(f => f.Body != null))
        {
            GenerateFunction(function);
            _builder.AppendBlankLine();
        }

        _fasmWriter.WriteImportDataSection(_builder, _program, _externalFunctions);

        return _builder.ToString();
    }

    private void FindAllStringLiterals(AstNode node)
    {
        if (node is StringLiteralNode str && !_stringLiterals.ContainsValue(str.Value))
        {
            _stringLiterals.Add(str.Label, str.Value);
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

    private void GenerateFunction(FunctionDeclarationNode function)
    {
        _currentMethodOwnerStruct = function.OwnerStructName;
        _currentSymbols = new SymbolTable(function, _typeManager);

        string mangledName = function.OwnerStructName != null
            ? $"_{function.OwnerStructName}_{function.Name}"
            : $"_{function.Name}";

        _builder.AppendLabel(mangledName);
        _builder.AppendInstruction("push ebp");
        _builder.AppendInstruction("mov ebp, esp");
        _builder.AppendInstruction("push ebx", "Preserve non-volatile registers");
        _builder.AppendInstruction("push esi");
        _builder.AppendInstruction("push edi");
        _builder.AppendBlankLine();

        int totalLocalSize = _currentSymbols.TotalLocalSize;
        if (totalLocalSize > 0)
        {
            _builder.AppendInstruction($"sub esp, {totalLocalSize}", "Allocate space for all local variables");
        }

        if (function.Body != null)
        {
            GenerateStatement(function.Body);
        }

        _builder.AppendBlankLine();
        _builder.AppendInstruction(null, "Implicit return cleanup");
        GenerateFunctionEpilogue();
    }

    private void GenerateFunctionEpilogue()
    {
        _builder.AppendInstruction("pop edi");
        _builder.AppendInstruction("pop esi");
        _builder.AppendInstruction("pop ebx");
        _builder.AppendInstruction("mov esp, ebp");
        _builder.AppendInstruction("pop ebp");
        _builder.AppendInstruction("ret");
    }

    private void GenerateReturn(ReturnStatementNode ret)
    {
        if (ret.Expression != null) GenerateExpression(ret.Expression);
        GenerateFunctionEpilogue();
    }

    private void GenerateWhile(WhileStatementNode w)
    {
        int i = _labelCounter++;
        _builder.AppendLabel($"_while_start_{i}");
        GenerateExpression(w.Condition);
        _builder.AppendInstruction("cmp eax, 0");
        _builder.AppendInstruction($"je _while_end_{i}");
        GenerateStatement(w.Body);
        _builder.AppendInstruction($"jmp _while_start_{i}");
        _builder.AppendLabel($"_while_end_{i}");
    }

    private void GenerateIf(IfStatementNode i)
    {
        int idx = _labelCounter++;
        GenerateExpression(i.Condition);
        _builder.AppendInstruction("cmp eax, 0");
        _builder.AppendInstruction(i.ElseBody != null ? $"je _if_else_{idx}" : $"je _if_end_{idx}");
        GenerateStatement(i.ThenBody);
        if (i.ElseBody != null)
        {
            _builder.AppendInstruction($"jmp _if_end_{idx}");
            _builder.AppendLabel($"_if_else_{idx}");
            GenerateStatement(i.ElseBody);
        }
        _builder.AppendLabel($"_if_end_{idx}");
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
                            if (memberSize == 1) _builder.AppendInstruction($"mov byte [ebp + {totalOffset}], al", $"Init member {member.Name.Value}");
                            else _builder.AppendInstruction($"mov dword [ebp + {totalOffset}], eax", $"Init member {member.Name.Value}");

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
                        _builder.AppendInstruction($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
                    }
                    else if (_currentMethodOwnerStruct != null)
                    {
                        try
                        {
                            var (memberOffset, _) = _typeManager.GetMemberInfo(_currentMethodOwnerStruct, varExpr.Identifier.Value);
                            _currentSymbols.TryGetSymbol("this", out var thisOffset, out _);
                            _builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get `this` pointer value");
                            if (memberOffset > 0)
                            {
                                _builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for implicit this->{varExpr.Identifier.Value}");
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
                        _builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for member {memberAccess.Operator.Value}{memberAccess.Member.Value}");
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
            case IntegerLiteralNode literal: _builder.AppendInstruction($"mov eax, {literal.Value}"); break;
            case StringLiteralNode str: _builder.AppendInstruction($"mov eax, {str.Label}"); break;
            case VariableExpressionNode varExpr:
                _currentSymbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out _);
                if (offset > 0)
                {
                    var exprType = _typeManager.GetExpressionType(varExpr, _currentSymbols, _currentMethodOwnerStruct);
                    if (_typeManager.IsStruct(exprType))
                    {
                        _builder.AppendInstruction($"lea eax, [ebp + {offset}]");
                    }
                    else
                    {
                        if (_typeManager.GetSizeOfType(exprType) == 1) _builder.AppendInstruction($"movzx eax, byte [ebp + {offset}]");
                        else _builder.AppendInstruction($"mov eax, [ebp + {offset}]");
                    }
                }
                else
                {
                    GenerateLValueAddress(varExpr);
                    var type = _typeManager.GetExpressionType(varExpr, _currentSymbols, _currentMethodOwnerStruct);
                    if (_typeManager.GetSizeOfType(type) == 1) _builder.AppendInstruction("movzx eax, byte [eax]");
                    else _builder.AppendInstruction("mov eax, [eax]");
                }
                break;
            case UnaryExpressionNode u:
                if (u.Operator.Type == TokenType.Ampersand) GenerateLValueAddress(u.Right);
                else
                {
                    GenerateExpression(u.Right);
                    if (u.Operator.Type == TokenType.Minus) _builder.AppendInstruction("neg eax");
                    else if (u.Operator.Type == TokenType.Star)
                    {
                        var type = _typeManager.GetExpressionType(u, _currentSymbols, _currentMethodOwnerStruct);
                        if (_typeManager.GetSizeOfType(type) == 1) _builder.AppendInstruction("movzx eax, byte [eax]");
                        else _builder.AppendInstruction("mov eax, [eax]");
                    }
                }
                break;
            case MemberAccessExpressionNode m:
                GenerateLValueAddress(m);
                var memberType = _typeManager.GetExpressionType(m, _currentSymbols, _currentMethodOwnerStruct);
                if (_typeManager.GetSizeOfType(memberType) == 1) _builder.AppendInstruction("movzx eax, byte [eax]");
                else _builder.AppendInstruction("mov eax, [eax]");
                break;
            case AssignmentExpressionNode assign:
                {
                    var lValueType = _typeManager.GetExpressionType(assign.Left, _currentSymbols, _currentMethodOwnerStruct);
                    var isStructAssign = _typeManager.IsStruct(lValueType);

                    if (isStructAssign)
                    {
                        GenerateLValueAddress(assign.Left);
                        _builder.AppendInstruction("push eax");
                        GenerateExpression(assign.Right);
                        _builder.AppendInstruction("pop edi");
                        _builder.AppendInstruction("mov esi, eax");

                        int size = _typeManager.GetSizeOfType(lValueType);
                        _builder.AppendInstruction($"mov ecx, {size / 4}");
                        _builder.AppendInstruction("rep movsd");
                        if (size % 4 > 0)
                        {
                            _builder.AppendInstruction($"mov ecx, {size % 4}");
                            _builder.AppendInstruction("rep movsb");
                        }
                    }
                    else
                    {
                        GenerateLValueAddress(assign.Left);
                        _builder.AppendInstruction("push eax");
                        GenerateExpression(assign.Right);
                        _builder.AppendInstruction("pop ecx");
                        if (_typeManager.GetSizeOfType(lValueType) == 1) _builder.AppendInstruction("mov [ecx], al");
                        else _builder.AppendInstruction("mov [ecx], eax");
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
                    _builder.AppendInstruction($"push dword [eax + {offset}]");
                }
                totalArgSize += argSize;
            }
            else
            {
                GenerateExpression(arg);
                _builder.AppendInstruction("push eax");
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
            _builder.AppendInstruction("push eax", "Push 'this' pointer");
            totalArgSize += 4;
            calleeTarget = $"_{baseStructType}_{memberAccess.Member.Value}";
        }
        else if (callExpr.Callee is VariableExpressionNode varNode)
        {
            string calleeName = varNode.Identifier.Value;
            calleeTarget = _externalFunctions.Contains(calleeName) ? $"[{calleeName}]" : $"_{calleeName}";
        }
        else throw new NotSupportedException($"Unsupported callee type for call: {callExpr.Callee.GetType().Name}");

        _builder.AppendInstruction($"call {calleeTarget}");

        if (totalArgSize > 0) _builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up args");
    }

    private void GenerateBinaryExpression(BinaryExpressionNode binExpr)
    {
        GenerateExpression(binExpr.Right);
        _builder.AppendInstruction("push eax");
        GenerateExpression(binExpr.Left);
        _builder.AppendInstruction("pop ecx");

        switch (binExpr.Operator.Type)
        {
            case TokenType.Plus: _builder.AppendInstruction("add eax, ecx"); break;
            case TokenType.Minus: _builder.AppendInstruction("sub eax, ecx"); break;
            case TokenType.Star: _builder.AppendInstruction("imul eax, ecx"); break;
            case TokenType.Slash: _builder.AppendInstruction("cdq"); _builder.AppendInstruction("idiv ecx"); break;
            case TokenType.DoubleEquals: _builder.AppendInstruction("cmp eax, ecx"); _builder.AppendInstruction("sete al"); _builder.AppendInstruction("movzx eax, al"); break;
            case TokenType.NotEquals: _builder.AppendInstruction("cmp eax, ecx"); _builder.AppendInstruction("setne al"); _builder.AppendInstruction("movzx eax, al"); break;
            case TokenType.LessThan: _builder.AppendInstruction("cmp eax, ecx"); _builder.AppendInstruction("setl al"); _builder.AppendInstruction("movzx eax, al"); break;
            case TokenType.GreaterThan: _builder.AppendInstruction("cmp eax, ecx"); _builder.AppendInstruction("setg al"); _builder.AppendInstruction("movzx eax, al"); break;
            default: throw new NotImplementedException($"Op: {binExpr.Operator.Type}");
        }
    }
}