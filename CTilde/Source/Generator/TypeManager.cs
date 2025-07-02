using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTilde;

public class TypeManager
{
    private readonly ProgramNode _program;
    private readonly Dictionary<string, StructDefinitionNode> _structs;

    public TypeManager(ProgramNode program)
    {
        _program = program;
        _structs = program.Structs.ToDictionary(s => s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name);
    }

    public StructDefinitionNode? FindStruct(string qualifiedName)
    {
        _structs.TryGetValue(qualifiedName, out var def);
        return def;
    }

    public (StructDefinitionNode Def, string FullName) GetStructTypeFromUnqualifiedName(string name, string? currentNamespace)
    {
        var qualifiedName = currentNamespace != null ? $"{currentNamespace}::{name}" : name;
        if (_structs.TryGetValue(qualifiedName, out var def)) return (def, qualifiedName);
        if (currentNamespace != null && _structs.TryGetValue(name, out def)) return (def, name);
        throw new InvalidOperationException($"Could not resolve struct type '{name}' in context '{currentNamespace}'.");
    }

    public (StructDefinitionNode Def, string FullName) GetStructTypeFromFullName(string fullName)
    {
        if (_structs.TryGetValue(fullName, out var def)) return (def, fullName);
        throw new InvalidOperationException($"Could not find struct definition for '{fullName}'.");
    }

    public string GetTypeName(Token type, int pointerLevel)
    {
        var sb = new StringBuilder(type.Value);
        for (int i = 0; i < pointerLevel; i++) sb.Append('*');
        return sb.ToString();
    }

    public int GetSizeOfType(string typeName)
    {
        if (typeName.EndsWith("*")) return 4; // Pointers are 4 bytes
        if (typeName == "int") return 4;
        if (typeName == "char") return 1;

        if (_structs.TryGetValue(typeName, out var structDef))
        {
            return structDef.Members.Sum(m => GetSizeOfType(GetTypeName(m.Type, m.PointerLevel)));
        }

        throw new InvalidOperationException($"Unknown type '{typeName}' for size calculation.");
    }

    public bool IsStruct(string typeName) => _structs.ContainsKey(typeName.TrimEnd('*'));

    public (int offset, string type) GetMemberInfo(string structName, string memberName)
    {
        if (!_structs.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        int offset = 0;
        foreach (var member in structDef.Members)
        {
            if (member.Name.Value == memberName)
            {
                return (offset, GetTypeName(member.Type, member.PointerLevel));
            }
            offset += GetSizeOfType(GetTypeName(member.Type, member.PointerLevel));
        }
        throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");
    }

    public string GetExpressionType(ExpressionNode expr, SymbolTable symbols, string? currentMethodOwnerStruct, string? currentNamespace)
    {
        switch (expr)
        {
            case IntegerLiteralNode: return "int";
            case StringLiteralNode: return "char*";
            case VariableExpressionNode v:
                if (symbols.TryGetSymbol(v.Identifier.Value, out _, out var type))
                {
                    return type;
                }
                if (currentMethodOwnerStruct != null)
                {
                    try
                    {
                        var ownerType = GetStructTypeFromUnqualifiedName(currentMethodOwnerStruct, currentNamespace);
                        var (_, memberType) = GetMemberInfo(ownerType.FullName, v.Identifier.Value);
                        return memberType;
                    }
                    catch (InvalidOperationException) { /* Fall through */ }
                }
                throw new InvalidOperationException($"Cannot determine type for undefined variable '{v.Identifier.Value}'.");

            case AssignmentExpressionNode a: return GetExpressionType(a.Left, symbols, currentMethodOwnerStruct, currentNamespace);

            case MemberAccessExpressionNode m:
                {
                    var leftType = GetExpressionType(m.Left, symbols, currentMethodOwnerStruct, currentNamespace);
                    string baseStructType = leftType.TrimEnd('*');
                    var (_, memberType) = GetMemberInfo(baseStructType, m.Member.Value);
                    return memberType;
                }

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Ampersand:
                return GetExpressionType(u.Right, symbols, currentMethodOwnerStruct, currentNamespace) + "*";

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                {
                    var operandType = GetExpressionType(u.Right, symbols, currentMethodOwnerStruct, currentNamespace);
                    if (!operandType.EndsWith("*")) throw new InvalidOperationException($"Cannot dereference non-pointer type '{operandType}'.");
                    return operandType.Substring(0, operandType.Length - 1);
                }

            case CallExpressionNode call:
                {
                    FunctionDeclarationNode? func = null;

                    if (call.Callee is VariableExpressionNode v)
                    {
                        string funcName = v.Identifier.Value;
                        func = _program.Functions.FirstOrDefault(f => f.Name == funcName && f.OwnerStructName == null && f.Namespace == currentNamespace) ??
                               _program.Functions.FirstOrDefault(f => f.Name == funcName && f.OwnerStructName == null && f.Namespace == null);
                    }
                    else if (call.Callee is QualifiedAccessExpressionNode q)
                    {
                        string funcName = q.Member.Value;
                        string nsName = q.Namespace.Value;
                        func = _program.Functions.FirstOrDefault(f => f.Name == funcName && f.Namespace == nsName && f.OwnerStructName == null);
                    }
                    else if (call.Callee is MemberAccessExpressionNode m)
                    {
                        string funcName = m.Member.Value;
                        var leftType = GetExpressionType(m.Left, symbols, currentMethodOwnerStruct, currentNamespace).TrimEnd('*');
                        var (structDef, _) = GetStructTypeFromFullName(leftType);
                        func = _program.Functions.FirstOrDefault(f => f.Name == funcName && f.OwnerStructName == structDef.Name && f.Namespace == structDef.Namespace);
                    }

                    if (func == null) throw new InvalidOperationException("Cannot determine return type of unresolved call expression.");

                    return GetTypeName(func.ReturnType, func.ReturnPointerLevel);
                }

            case QualifiedAccessExpressionNode:
                throw new InvalidOperationException("Qualified access can only be used as part of a call.");

            case BinaryExpressionNode: return "int"; // Simple for now

            default: throw new NotImplementedException($"GetExpressionType not implemented for {expr.GetType().Name}");
        }
    }
}