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
    private readonly Dictionary<string, CompilationUnitNode> _structUnitMap;


    public TypeManager(ProgramNode program)
    {
        _program = program;
        _structs = program.CompilationUnits.SelectMany(cu => cu.Structs)
            .ToDictionary(s => s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name);
        _functions = program.CompilationUnits.SelectMany(cu => cu.Functions).ToList();

        _structUnitMap = new Dictionary<string, CompilationUnitNode>();
        foreach (var cu in program.CompilationUnits)
        {
            foreach (var s in cu.Structs)
            {
                _structUnitMap[s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name] = cu;
            }
        }
    }

    /// <summary>
    /// Resolves an unqualified or aliased type name to its fully qualified name.
    /// Does NOT handle pointer suffixes (*). Expects base type name (e.g., "Color", "rl::Color").
    /// </summary>
    /// <param name="name">The unqualified or aliased type name (e.g., "Color", "rl::Color").</param>
    /// <param name="currentNamespace">The namespace of the current context (e.g., function's namespace).</param>
    /// <param name="context">The compilation unit context to get `using` directives.</param>
    /// <returns>The fully qualified type name (e.g., "raylib::Color").</returns>
    public string ResolveTypeName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        if (name.Contains("::"))
        {
            var parts = name.Split("::");
            var nsPart = parts[0];
            var typeName = parts[1];

            var aliased = context.Usings.FirstOrDefault(u => u.Alias == nsPart);
            if (aliased != null)
            {
                var fqn = $"{aliased.Namespace}::{typeName}";
                return _structs.ContainsKey(fqn) ? fqn : throw new InvalidOperationException($"Type '{name}' with aliased namespace '{nsPart}' not found.");
            }
            // If it's already a qualified name and not an alias, check it directly
            return _structs.ContainsKey(name) ? name : throw new InvalidOperationException($"Type '{name}' not found.");
        }

        var candidates = new List<string>();

        // 1. Check current namespace
        if (currentNamespace != null)
        {
            var fqn = $"{currentNamespace}::{name}";
            if (_structs.ContainsKey(fqn)) candidates.Add(fqn);
        }

        // 2. Check using namespaces (without aliases)
        foreach (var u in context.Usings.Where(u => u.Alias == null))
        {
            var fqn = $"{u.Namespace}::{name}";
            if (_structs.ContainsKey(fqn)) candidates.Add(fqn);
        }

        // 3. Check global namespace
        if (_structs.ContainsKey(name)) candidates.Add(name);

        if (candidates.Count == 0) throw new InvalidOperationException($"Type '{name}' could not be resolved in the current context.");
        if (candidates.Distinct().Count() > 1) throw new InvalidOperationException($"Type '{name}' is ambiguous between: {string.Join(", ", candidates.Distinct())}");

        return candidates.First();
    }

    public FunctionDeclarationNode ResolveFunctionCall(ExpressionNode callee, CompilationUnitNode context, FunctionDeclarationNode currentFunction)
    {
        if (callee is VariableExpressionNode varNode)
        {
            return ResolveFunctionByName(varNode.Identifier.Value, currentFunction.Namespace, context);
        }
        if (callee is QualifiedAccessExpressionNode qNode)
        {
            var nsPart = qNode.Namespace.Value;
            var funcName = qNode.Member.Value;
            var aliased = context.Usings.FirstOrDefault(u => u.Alias == nsPart);
            if (aliased != null) nsPart = aliased.Namespace;

            var func = _functions.FirstOrDefault(f => f.OwnerStructName == null && f.Namespace == nsPart && f.Name == funcName);
            if (func == null) throw new InvalidOperationException($"Function '{nsPart}::{funcName}' not found.");
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
        if (typeName.EndsWith("*")) return 4; // Pointers are always 4 bytes
        if (typeName == "int") return 4;
        if (typeName == "char") return 1;

        // If it's not a primitive or pointer, it must be a struct type.
        // It must be a fully qualified name here.
        if (_structs.TryGetValue(typeName, out var structDef))
        {
            // When calculating the size of a struct, resolve its members' types
            // using the *struct's own compilation unit context*, not the calling context.
            var structDefUnit = _structUnitMap[typeName]; // Get the unit where this struct was defined

            return structDef.Members.Sum(mem =>
            {
                var rawMemberType = GetTypeName(mem.Type, mem.PointerLevel);

                string baseMemberName = rawMemberType.TrimEnd('*');
                string pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);

                string resolvedMemberTypeForSize;
                if (mem.Type.Type == TokenType.Keyword || baseMemberName.Equals("void", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedMemberTypeForSize = rawMemberType; // Primitive, no resolution needed
                }
                else
                {
                    // Resolve member type using the namespace and usings of the *struct definition's unit*
                    resolvedMemberTypeForSize = ResolveTypeName(baseMemberName, structDef.Namespace, structDefUnit) + pointerSuffix;
                }
                return GetSizeOfType(resolvedMemberTypeForSize, structDefUnit); // Recursive call, passing struct's unit
            });
        }
        throw new InvalidOperationException($"Unknown type '{typeName}' for size calculation.");
    }

    public bool IsStruct(string typeName) => _structs.ContainsKey(typeName.TrimEnd('*')); // Check if base type name is a struct

    public (int offset, string type) GetMemberInfo(string structName, string memberName, CompilationUnitNode context)
    {
        if (!_structs.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        int currentOffset = 0;
        // Get the compilation unit where this struct was defined to use its usings for member type resolution
        var structDefUnit = _structUnitMap[structName];

        foreach (var mem in structDef.Members) // Changed loop variable from 'member' to 'mem' to avoid conflict
        {
            var rawMemberType = GetTypeName(mem.Type, mem.PointerLevel);

            string baseMemberName = rawMemberType.TrimEnd('*');
            string pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);

            string resolvedMemberType;
            if (mem.Type.Type == TokenType.Keyword || baseMemberName.Equals("void", StringComparison.OrdinalIgnoreCase))
            {
                resolvedMemberType = rawMemberType; // Primitive, no resolution needed
            }
            else
            {
                // Resolve member type using the namespace and usings of the *struct definition's unit*
                resolvedMemberType = ResolveTypeName(baseMemberName, structDef.Namespace, structDefUnit) + pointerSuffix;
            }

            if (mem.Name.Value == memberName)
            {
                return (currentOffset, resolvedMemberType);
            }
            currentOffset += GetSizeOfType(resolvedMemberType, structDefUnit); // Pass struct's unit here too
        }
        throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");
    }

    public bool IsMemberConst(string structName, string memberName)
    {
        if (!_structs.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        var member = structDef.Members.FirstOrDefault(m => m.Name.Value == memberName);
        if (member == null)
        {
            throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'.");
        }
        return member.IsConst;
    }

    public string GetExpressionType(ExpressionNode expr, SymbolTable symbols, CompilationUnitNode context, FunctionDeclarationNode currentFunction)
    {
        switch (expr)
        {
            case IntegerLiteralNode: return "int";
            case StringLiteralNode: return "char*";
            case VariableExpressionNode v:
                if (symbols.TryGetSymbol(v.Identifier.Value, out _, out _, out _)) return symbols.GetSymbolType(v.Identifier.Value); // Added out _
                if (currentFunction.OwnerStructName != null)
                {
                    try
                    {
                        var ownerType = GetStructTypeFromUnqualifiedName(currentFunction.OwnerStructName, currentFunction.Namespace);
                        var (_, memberTypeResolved) = GetMemberInfo(ownerType.FullName, v.Identifier.Value, context);
                        return memberTypeResolved;
                    }
                    catch (InvalidOperationException) { /* Fall through */ }
                }
                throw new InvalidOperationException($"Cannot determine type for undefined variable '{v.Identifier.Value}'.");

            case AssignmentExpressionNode a: return GetExpressionType(a.Left, symbols, context, currentFunction);

            case MemberAccessExpressionNode ma:
                var leftType = GetExpressionType(ma.Left, symbols, context, currentFunction);
                string baseStructType = leftType.TrimEnd('*');
                var (_, resolvedMemberType) = GetMemberInfo(baseStructType, ma.Member.Value, context);
                return resolvedMemberType;

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Ampersand:
                return GetExpressionType(u.Right, symbols, context, currentFunction) + "*";

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                var operandType = GetExpressionType(u.Right, symbols, context, currentFunction);
                if (!operandType.EndsWith("*")) throw new InvalidOperationException($"Cannot dereference non-pointer type '{operandType}'.");
                return operandType.Substring(0, operandType.Length - 1);

            case CallExpressionNode call:
                FunctionDeclarationNode func;
                if (call.Callee is MemberAccessExpressionNode callMemberAccess)
                {
                    var ownerTypeName = GetExpressionType(callMemberAccess.Left, symbols, context, currentFunction).TrimEnd('*');
                    var (ownerStruct, _) = GetStructTypeFromFullName(ownerTypeName);
                    func = ResolveMethod(ownerStruct, callMemberAccess.Member.Value);
                }
                else
                {
                    func = ResolveFunctionCall(call.Callee, context, currentFunction);
                }

                var returnTypeNameRaw = GetTypeName(func.ReturnType, func.ReturnPointerLevel);
                string baseReturnName = returnTypeNameRaw.TrimEnd('*');
                string returnPointerSuffix = new string('*', returnTypeNameRaw.Length - baseReturnName.Length);

                if (func.ReturnType.Type != TokenType.Keyword && !baseReturnName.Equals("void", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveTypeName(baseReturnName, func.Namespace, context) + returnPointerSuffix;
                }
                return returnTypeNameRaw;

            case QualifiedAccessExpressionNode:
                throw new InvalidOperationException("Qualified access can only be used as part of a call.");

            case BinaryExpressionNode: return "int";

            default: throw new NotImplementedException($"GetExpressionType not implemented for {expr.GetType().Name}");
        }
    }
}