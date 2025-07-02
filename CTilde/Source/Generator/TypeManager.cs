using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTilde;

public class TypeManager
{
    private readonly ProgramNode _program;
    private readonly Dictionary<string, StructDefinitionNode> _structs;
    private readonly List<FunctionDeclarationNode> _functions;

    public TypeManager(ProgramNode program)
    {
        _program = program;
        _structs = program.CompilationUnits.SelectMany(cu => cu.Structs)
            .ToDictionary(s => s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name);
        _functions = program.CompilationUnits.SelectMany(cu => cu.Functions).ToList();
    }

    public string ResolveTypeName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        if (name.Contains("::"))
        {
            var parts = name.Split("::");
            var ns = parts[0];
            var typeName = parts[1];

            var aliased = context.Usings.FirstOrDefault(u => u.Alias == ns);
            if (aliased != null)
            {
                var fqn = $"{aliased.Namespace}::{typeName}";
                return _structs.ContainsKey(fqn) ? fqn : throw new InvalidOperationException($"Type '{name}' with aliased namespace not found.");
            }
            return _structs.ContainsKey(name) ? name : throw new InvalidOperationException($"Type '{name}' not found.");
        }

        var candidates = new List<string>();

        if (currentNamespace != null)
        {
            var fqn = $"{currentNamespace}::{name}";
            if (_structs.ContainsKey(fqn)) candidates.Add(fqn);
        }

        foreach (var u in context.Usings.Where(u => u.Alias == null))
        {
            var fqn = $"{u.Namespace}::{name}";
            if (_structs.ContainsKey(fqn)) candidates.Add(fqn);
        }

        if (_structs.ContainsKey(name)) candidates.Add(name);

        if (candidates.Count == 0) throw new InvalidOperationException($"Type '{name}' could not be resolved in the current context.");
        if (candidates.Distinct().Count() > 1) throw new InvalidOperationException($"Type '{name}' is ambiguous between: {string.Join(", ", candidates.Distinct())}");

        return candidates.First();
    }

    public FunctionDeclarationNode ResolveFunctionCall(ExpressionNode callee, CompilationUnitNode context)
    {
        if (callee is VariableExpressionNode varNode)
        {
            var function = (FunctionDeclarationNode)callee.Ancestors().First(a => a is FunctionDeclarationNode);
            return ResolveFunctionByName(varNode.Identifier.Value, function.Namespace, context);
        }
        if (callee is QualifiedAccessExpressionNode qNode)
        {
            var ns = qNode.Namespace.Value;
            var funcName = qNode.Member.Value;
            var aliased = context.Usings.FirstOrDefault(u => u.Alias == ns);
            if (aliased != null) ns = aliased.Namespace;

            var func = _functions.FirstOrDefault(f => f.OwnerStructName == null && f.Namespace == ns && f.Name == funcName);
            if (func == null) throw new InvalidOperationException($"Function '{ns}::{funcName}' not found.");
            return func;
        }
        throw new NotSupportedException($"Unsupported callee type for resolution: {callee.GetType().Name}");
    }

    private FunctionDeclarationNode ResolveFunctionByName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        var candidates = new List<FunctionDeclarationNode>();

        if (currentNamespace != null)
        {
            candidates.AddRange(_functions.Where(f => f.OwnerStructName == null && f.Namespace == currentNamespace && f.Name == name));
        }

        foreach (var u in context.Usings.Where(u => u.Alias == null))
        {
            candidates.AddRange(_functions.Where(f => f.OwnerStructName == null && f.Namespace == u.Namespace && f.Name == name));
        }

        candidates.AddRange(_functions.Where(f => f.OwnerStructName == null && f.Namespace == null && f.Name == name));

        var distinctCandidates = candidates.Distinct().ToList();

        if (distinctCandidates.Count == 0) throw new InvalidOperationException($"Function '{name}' could not be resolved in the current context.");
        if (distinctCandidates.Count > 1)
        {
            var sigs = distinctCandidates.Select(f => f.Namespace != null ? $"{f.Namespace}::{f.Name}" : f.Name);
            throw new InvalidOperationException($"Function call '{name}' is ambiguous between: {string.Join(", ", sigs)}");
        }

        return distinctCandidates.First();
    }

    public FunctionDeclarationNode ResolveMethod(StructDefinitionNode owner, string name)
    {
        var method = _functions.FirstOrDefault(f => f.OwnerStructName == owner.Name && f.Namespace == owner.Namespace && f.Name == name);
        if (method == null) throw new InvalidOperationException($"Method '{name}' not found on struct '{owner.Name}'");
        return method;
    }

    public StructDefinitionNode? FindStruct(string qualifiedName) => _structs.TryGetValue(qualifiedName, out var def) ? def : null;

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

    public int GetSizeOfType(string typeName, CompilationUnitNode context)
    {
        if (typeName.EndsWith("*")) return 4;
        if (typeName == "int") return 4;
        if (typeName == "char") return 1;

        if (_structs.TryGetValue(typeName, out var structDef))
        {
            return structDef.Members.Sum(m =>
            {
                var rawMemberType = GetTypeName(m.Type, m.PointerLevel);
                if (m.Type.Type == TokenType.Identifier)
                {
                    string baseTypeName = rawMemberType.TrimEnd('*');
                    string pointerSuffix = rawMemberType.Substring(baseTypeName.Length);
                    var resolved = ResolveTypeName(baseTypeName, structDef.Namespace, context) + pointerSuffix;
                    return GetSizeOfType(resolved, context);
                }
                return GetSizeOfType(rawMemberType, context);
            });
        }
        throw new InvalidOperationException($"Unknown type '{typeName}' for size calculation.");
    }

    public bool IsStruct(string typeName) => _structs.ContainsKey(typeName.TrimEnd('*'));

    public (int offset, string type) GetMemberInfo(string structName, string memberName, CompilationUnitNode context)
    {
        if (!_structs.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        int currentOffset = 0;
        foreach (var member in structDef.Members)
        {
            var rawMemberType = GetTypeName(member.Type, member.PointerLevel);
            var resolvedMemberType = rawMemberType;

            if (member.Type.Type == TokenType.Identifier)
            {
                string baseTypeName = rawMemberType.TrimEnd('*');
                string pointerSuffix = rawMemberType.Substring(baseTypeName.Length);
                resolvedMemberType = ResolveTypeName(baseTypeName, structDef.Namespace, context) + pointerSuffix;
            }

            if (member.Name.Value == memberName)
            {
                return (currentOffset, resolvedMemberType);
            }
            currentOffset += GetSizeOfType(resolvedMemberType, context);
        }
        throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");
    }

    public string GetExpressionType(ExpressionNode expr, SymbolTable symbols, CompilationUnitNode context)
    {
        var function = (FunctionDeclarationNode)expr.Ancestors().First(a => a is FunctionDeclarationNode);

        switch (expr)
        {
            case IntegerLiteralNode: return "int";
            case StringLiteralNode: return "char*";
            case VariableExpressionNode v:
                if (symbols.TryGetSymbol(v.Identifier.Value, out _, out var type)) return type;
                if (function.OwnerStructName != null)
                {
                    try
                    {
                        var ownerType = GetStructTypeFromUnqualifiedName(function.OwnerStructName, function.Namespace);
                        var (_, memberType) = GetMemberInfo(ownerType.FullName, v.Identifier.Value, context);
                        return memberType;
                    }
                    catch (InvalidOperationException) { /* Fall through */ }
                }
                throw new InvalidOperationException($"Cannot determine type for undefined variable '{v.Identifier.Value}'.");

            case AssignmentExpressionNode a: return GetExpressionType(a.Left, symbols, context);

            case MemberAccessExpressionNode ma:
                var leftType = GetExpressionType(ma.Left, symbols, context);
                string baseStructType = leftType.TrimEnd('*');
                var (_, resolvedMemberType) = GetMemberInfo(baseStructType, ma.Member.Value, context);
                return resolvedMemberType;

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Ampersand:
                return GetExpressionType(u.Right, symbols, context) + "*";

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                var operandType = GetExpressionType(u.Right, symbols, context);
                if (!operandType.EndsWith("*")) throw new InvalidOperationException($"Cannot dereference non-pointer type '{operandType}'.");
                return operandType.Substring(0, operandType.Length - 1);

            case CallExpressionNode call:
                FunctionDeclarationNode func;
                if (call.Callee is MemberAccessExpressionNode callMemberAccess)
                {
                    var ownerTypeName = GetExpressionType(callMemberAccess.Left, symbols, context).TrimEnd('*');
                    var (ownerStruct, _) = GetStructTypeFromFullName(ownerTypeName);
                    func = ResolveMethod(ownerStruct, callMemberAccess.Member.Value);
                }
                else
                {
                    func = ResolveFunctionCall(call.Callee, context);
                }

                var returnTypeNameRaw = GetTypeName(func.ReturnType, func.ReturnPointerLevel);
                if (!returnTypeNameRaw.EndsWith("*") && func.ReturnType.Type != TokenType.Keyword)
                {
                    return ResolveTypeName(returnTypeNameRaw, func.Namespace, context);
                }
                return returnTypeNameRaw;

            case QualifiedAccessExpressionNode:
                throw new InvalidOperationException("Qualified access can only be used as part of a call.");

            case BinaryExpressionNode: return "int";

            default: throw new NotImplementedException($"GetExpressionType not implemented for {expr.GetType().Name}");
        }
    }
}