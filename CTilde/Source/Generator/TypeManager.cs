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

    public string ResolveTypeName(string name, string? currentNamespace, IEnumerable<string> activeUsings)
    {
        if (name.Contains("::"))
        {
            return _structs.ContainsKey(name) ? name : throw new InvalidOperationException($"Type '{name}' not found.");
        }

        var candidates = new List<string>();

        // 1. Check current namespace
        if (currentNamespace != null)
        {
            var fqn = $"{currentNamespace}::{name}";
            if (_structs.ContainsKey(fqn)) candidates.Add(fqn);
        }

        // 2. Check using namespaces
        foreach (var u in activeUsings)
        {
            var fqn = $"{u}::{name}";
            if (_structs.ContainsKey(fqn)) candidates.Add(fqn);
        }

        // 3. Check global namespace
        if (_structs.ContainsKey(name)) candidates.Add(name);

        if (candidates.Count == 0) throw new InvalidOperationException($"Type '{name}' could not be resolved in the current context.");
        if (candidates.Distinct().Count() > 1) throw new InvalidOperationException($"Type '{name}' is ambiguous between: {string.Join(", ", candidates.Distinct())}");

        return candidates.First();
    }

    public FunctionDeclarationNode ResolveFunction(string name, string? currentNamespace, IEnumerable<string> activeUsings)
    {
        // For non-method calls
        var candidates = new List<FunctionDeclarationNode>();

        // 1. Current namespace
        if (currentNamespace != null)
        {
            candidates.AddRange(_program.Functions.Where(f => f.OwnerStructName == null && f.Namespace == currentNamespace && f.Name == name));
        }

        // 2. Using namespaces
        foreach (var u in activeUsings)
        {
            candidates.AddRange(_program.Functions.Where(f => f.OwnerStructName == null && f.Namespace == u && f.Name == name));
        }

        // 3. Global namespace
        candidates.AddRange(_program.Functions.Where(f => f.OwnerStructName == null && f.Namespace == null && f.Name == name));

        var distinctCandidates = candidates.Distinct().ToList();

        if (distinctCandidates.Count == 0) throw new InvalidOperationException($"Function '{name}' could not be resolved in the current context.");
        if (distinctCandidates.Count > 1)
        {
            var sigs = distinctCandidates.Select(f => f.Namespace != null ? $"{f.Namespace}::{f.Name}" : f.Name);
            throw new InvalidOperationException($"Function call '{name}' is ambiguous between: {string.Join(", ", sigs)}");
        }

        return distinctCandidates.First();
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
            // Note: This recursive call relies on GetMemberInfo to resolve types correctly.
            // A more robust implementation might pre-calculate sizes.
            // To get the size, we need to know the 'usings' context of the file where the struct was defined.
            // Since we merged all files, the global 'usings' list is the best we have.
            var usings = _program.Usings.Select(u => u.Namespace).Distinct().ToList();
            return structDef.Members.Sum(m =>
            {
                var rawMemberType = GetTypeName(m.Type, m.PointerLevel);
                if (m.Type.Type == TokenType.Identifier)
                {
                    string baseTypeName = rawMemberType.TrimEnd('*');
                    string pointerSuffix = rawMemberType.Substring(baseTypeName.Length);
                    var resolved = ResolveTypeName(baseTypeName, structDef.Namespace, usings) + pointerSuffix;
                    return GetSizeOfType(resolved);
                }
                return GetSizeOfType(rawMemberType);
            });
        }

        throw new InvalidOperationException($"Unknown type '{typeName}' for size calculation.");
    }

    public bool IsStruct(string typeName) => _structs.ContainsKey(typeName.TrimEnd('*'));

    public (int offset, string type) GetMemberInfo(string structName, string memberName, IEnumerable<string> usings)
    {
        if (!_structs.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        int currentOffset = 0;
        foreach (var member in structDef.Members)
        {
            var rawMemberType = GetTypeName(member.Type, member.PointerLevel);
            var resolvedMemberType = rawMemberType;

            if (member.Type.Type == TokenType.Identifier) // Is a user-defined type (struct)
            {
                string baseTypeName = rawMemberType.TrimEnd('*');
                string pointerSuffix = rawMemberType.Substring(baseTypeName.Length);
                resolvedMemberType = ResolveTypeName(baseTypeName, structDef.Namespace, usings) + pointerSuffix;
            }

            if (member.Name.Value == memberName)
            {
                return (currentOffset, resolvedMemberType);
            }
            currentOffset += GetSizeOfType(resolvedMemberType);
        }
        throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");
    }

    public string GetExpressionType(ExpressionNode expr, SymbolTable symbols, string? currentMethodOwnerStruct, string? currentNamespace, IEnumerable<string> usings)
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
                        var (_, memberType) = GetMemberInfo(ownerType.FullName, v.Identifier.Value, usings);
                        return memberType;
                    }
                    catch (InvalidOperationException) { /* Fall through */ }
                }
                throw new InvalidOperationException($"Cannot determine type for undefined variable '{v.Identifier.Value}'.");

            case AssignmentExpressionNode a: return GetExpressionType(a.Left, symbols, currentMethodOwnerStruct, currentNamespace, usings);

            case MemberAccessExpressionNode m:
                {
                    var leftType = GetExpressionType(m.Left, symbols, currentMethodOwnerStruct, currentNamespace, usings);
                    string baseStructType = leftType.TrimEnd('*');
                    var (_, memberType) = GetMemberInfo(baseStructType, m.Member.Value, usings);
                    return memberType;
                }

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Ampersand:
                return GetExpressionType(u.Right, symbols, currentMethodOwnerStruct, currentNamespace, usings) + "*";

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                {
                    var operandType = GetExpressionType(u.Right, symbols, currentMethodOwnerStruct, currentNamespace, usings);
                    if (!operandType.EndsWith("*")) throw new InvalidOperationException($"Cannot dereference non-pointer type '{operandType}'.");
                    return operandType.Substring(0, operandType.Length - 1);
                }

            case CallExpressionNode call:
                {
                    FunctionDeclarationNode? func = null;

                    if (call.Callee is VariableExpressionNode v)
                    {
                        func = ResolveFunction(v.Identifier.Value, currentNamespace, usings);
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
                        var leftType = GetExpressionType(m.Left, symbols, currentMethodOwnerStruct, currentNamespace, usings).TrimEnd('*');
                        var (structDef, _) = GetStructTypeFromFullName(leftType);
                        func = _program.Functions.FirstOrDefault(f => f.Name == funcName && f.OwnerStructName == structDef.Name && f.Namespace == structDef.Namespace);
                    }

                    if (func == null) throw new InvalidOperationException($"Cannot determine return type of unresolved call expression: {call.Callee}");

                    var returnTypeNameRaw = GetTypeName(func.ReturnType, func.ReturnPointerLevel);
                    if (!returnTypeNameRaw.EndsWith("*") && func.ReturnType.Type != TokenType.Keyword)
                    {
                        return ResolveTypeName(returnTypeNameRaw, func.Namespace, usings);
                    }
                    return returnTypeNameRaw;
                }

            case QualifiedAccessExpressionNode:
                throw new InvalidOperationException("Qualified access can only be used as part of a call.");

            case BinaryExpressionNode: return "int"; // Simple for now

            default: throw new NotImplementedException($"GetExpressionType not implemented for {expr.GetType().Name}");
        }
    }
}