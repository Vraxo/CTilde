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
    private readonly Dictionary<string, EnumDefinitionNode> _enums;
    private readonly Dictionary<string, CompilationUnitNode> _structUnitMap;
    private readonly Dictionary<string, CompilationUnitNode> _enumUnitMap;

    public TypeManager(ProgramNode program)
    {
        _program = program;
        _structs = program.CompilationUnits.SelectMany(cu => cu.Structs)
            .ToDictionary(s => s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name);
        _functions = program.CompilationUnits.SelectMany(cu => cu.Functions).ToList();
        _enums = program.CompilationUnits.SelectMany(cu => cu.Enums)
            .ToDictionary(e => e.Namespace != null ? $"{e.Namespace}::{e.Name}" : e.Name);

        _structUnitMap = new Dictionary<string, CompilationUnitNode>();
        _enumUnitMap = new Dictionary<string, CompilationUnitNode>();

        foreach (var cu in program.CompilationUnits)
        {
            foreach (var s in cu.Structs)
            {
                _structUnitMap[s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name] = cu;
            }
            // Populate _enumUnitMap
            foreach (var e in cu.Enums)
            {
                _enumUnitMap[e.Namespace != null ? $"{e.Namespace}::{e.Name}" : e.Name] = cu;
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
        foreach (UsingDirectiveNode? u in context.Usings.Where(u => u.Alias == null))
        {
            string fqn = $"{u.Namespace}::{name}";

            if (!_structs.ContainsKey(fqn))
            {
                continue;
            }

            candidates.Add(fqn);
        }

        // 3. Check global namespace
        if (_structs.ContainsKey(name)) candidates.Add(name);

        if (candidates.Count == 0) throw new InvalidOperationException($"Type '{name}' could not be resolved in the current context.");
        if (candidates.Distinct().Count() > 1) throw new InvalidOperationException($"Type '{name}' is ambiguous between: {string.Join(", ", candidates.Distinct())}");

        return candidates.First();
    }

    private string ResolveQualifier(ExpressionNode expr)
    {
        return expr switch
        {
            VariableExpressionNode v => v.Identifier.Value,
            QualifiedAccessExpressionNode q => $"{ResolveQualifier(q.Left)}::{q.Member.Value}",
            _ => throw new InvalidOperationException($"Cannot resolve qualifier from expression of type {expr.GetType().Name}")
        };
    }

    public FunctionDeclarationNode ResolveFunctionCall(ExpressionNode callee, CompilationUnitNode context, FunctionDeclarationNode currentFunction)
    {
        if (callee is VariableExpressionNode varNode)
        {
            return ResolveFunctionByName(varNode.Identifier.Value, currentFunction.Namespace, context);
        }
        if (callee is QualifiedAccessExpressionNode qNode)
        {
            // The new qNode is recursive. We need to resolve the qualifier part first.
            string qualifier = ResolveQualifier(qNode.Left);
            var funcName = qNode.Member.Value;

            // Now, `qualifier` could be a namespace or an alias.
            string? resolvedNamespace = qualifier;
            var aliased = context.Usings.FirstOrDefault(u => u.Alias == qualifier);
            if (aliased != null) resolvedNamespace = aliased.Namespace;

            var func = _functions.FirstOrDefault(f => f.OwnerStructName == null && f.Namespace == resolvedNamespace && f.Name == funcName);
            if (func == null) throw new InvalidOperationException($"Function '{resolvedNamespace}::{funcName}' not found.");
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
        return method == null ? throw new InvalidOperationException($"Method '{name}' not found on struct '{owner.Name}'") : method;
    }

    /// <summary>
    /// Resolves an unqualified or aliased enum type name to its fully qualified name.
    /// </summary>
    /// <param name="name">The unqualified or aliased enum type name (e.g., "KeyboardKey", "rl::KeyboardKey").</param>
    /// <param name="currentNamespace">The namespace of the current context.</param>
    /// <param name="context">The compilation unit context to get `using` directives.</param>
    /// <returns>The fully qualified enum type name (e.g., "raylib::KeyboardKey"), or null if not found.</returns>
    public string? ResolveEnumTypeName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        if (name.Contains("::"))
        {
            var parts = name.Split("::");
            var nsPart = parts[0];
            var enumName = parts[1];

            var aliased = context.Usings.FirstOrDefault(u => u.Alias == nsPart);
            if (aliased != null)
            {
                var fqn = $"{aliased.Namespace}::{enumName}";
                return _enums.ContainsKey(fqn) ? fqn : null;
            }
            // If it's already a qualified name and not an alias, check it directly
            return _enums.ContainsKey(name) ? name : null;
        }

        // Try current namespace
        if (currentNamespace != null)
        {
            var fqn = $"{currentNamespace}::{name}";
            if (_enums.ContainsKey(fqn)) return fqn;
        }

        // Try using namespaces (without aliases)
        foreach (var u in context.Usings.Where(u => u.Alias == null))
        {
            var fqn = $"{u.Namespace}::{name}";
            if (_enums.ContainsKey(fqn)) return fqn;
        }

        // Try global namespace
        if (_enums.ContainsKey(name)) return name;

        return null; // Enum type not found
    }


    /// <summary>
    /// Gets the integer value of an enum member given its fully qualified enum type name.
    /// </summary>
    /// <param name="enumFQN">The fully qualified name of the enum type (e.g., "raylib::KeyboardKey").</param>
    /// <param name="memberName">The name of the enum member (e.g., "KEY_D").</param>
    /// <returns>The integer value of the enum member, or null if not found.</returns>
    public int? GetEnumValue(string enumFQN, string memberName)
    {
        if (_enums.TryGetValue(enumFQN, out var enumDef))
        {
            var member = enumDef.Members.FirstOrDefault(m => m.Name.Value == memberName);
            if (member != null) return member.Value;
        }
        return null;
    }


    /// <summary>
    /// Resolves an unqualified enum member access to its integer value.
    /// Used for bare identifiers like `KEY_D` when `using` directives or current namespace apply.
    /// </summary>
    /// <param name="memberName">The enum member name (e.g., "KEY_D").</param>
    /// <param name="context">The compilation unit context for `using` directives.</param>
    /// <param name="currentContextNamespace">The namespace of the current context.</param>
    /// <returns>The integer value of the enum member, or null if not found.</returns>
    public int? ResolveUnqualifiedEnumMember(string memberName, CompilationUnitNode context, string? currentContextNamespace)
    {
        // 1. Check current namespace for any enum containing this member
        if (currentContextNamespace != null)
        {
            foreach (var enumDef in _enums.Values.Where(e => e.Namespace == currentContextNamespace))
            {
                var member = enumDef.Members.FirstOrDefault(m => m.Name.Value == memberName);
                if (member != null) return member.Value;
            }
        }

        // 2. Check using namespaces (without aliases) for any enum containing this member
        foreach (var u in context.Usings.Where(u => u.Alias == null))
        {
            foreach (var enumDef in _enums.Values.Where(e => e.Namespace == u.Namespace))
            {
                var member = enumDef.Members.FirstOrDefault(m => m.Name.Value == memberName);
                if (member != null) return member.Value;
            }
        }

        // 3. Check global namespace (enums with null namespace) for any enum containing this member
        foreach (var enumDef in _enums.Values.Where(e => e.Namespace == null))
        {
            var member = enumDef.Members.FirstOrDefault(m => m.Name.Value == memberName);
            if (member != null) return member.Value;
        }
        return null;
    }


    public StructDefinitionNode? FindStruct(string qualifiedName) => _structs.TryGetValue(qualifiedName, out var def) ? def : null;

    public (StructDefinitionNode Def, string FullName) GetStructTypeFromUnqualifiedName(string name, string? currentNamespace)
    {
        var qualifiedName = currentNamespace != null ? $"{currentNamespace}::{name}" : name;
        if (_structs.TryGetValue(qualifiedName, out var def)) return (def, qualifiedName);
        if (currentNamespace != null && _structs.TryGetValue(name, out def)) return (def, name); // Check global scope if not found in current namespace
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

        if (_structs.TryGetValue(typeName, out var structDef))
        {
            var structDefUnit = _structUnitMap[typeName];
            int size = 0;

            // Add size of base class, if any
            if (structDef.BaseStructName != null)
            {
                string baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, structDefUnit);
                size += GetSizeOfType(baseFqn, structDefUnit); // Recursive call
            }

            // Add size of own members
            size += structDef.Members.Sum(mem =>
            {
                var rawMemberType = GetTypeName(mem.Type, mem.PointerLevel);
                string baseMemberName = rawMemberType.TrimEnd('*');
                string pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);
                string resolvedMemberTypeForSize;
                if (mem.Type.Type == TokenType.Keyword || baseMemberName.Equals("void", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedMemberTypeForSize = rawMemberType;
                }
                else
                {
                    resolvedMemberTypeForSize = ResolveTypeName(baseMemberName, structDef.Namespace, structDefUnit) + pointerSuffix;
                }
                return GetSizeOfType(resolvedMemberTypeForSize, structDefUnit);
            });
            return size;
        }
        throw new InvalidOperationException($"Unknown type '{typeName}' for size calculation.");
    }

    public bool IsStruct(string typeName) => _structs.ContainsKey(typeName.TrimEnd('*')); // Check if base type name is a struct

    public (int offset, string type) GetMemberInfo(string structName, string memberName, CompilationUnitNode context)
    {
        if (!_structs.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        var structDefUnit = _structUnitMap[structName];

        int baseSize = 0;
        if (structDef.BaseStructName != null)
        {
            string baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, structDefUnit);
            baseSize = GetSizeOfType(baseFqn, structDefUnit);
        }

        int currentMemberOffset = 0;
        foreach (var mem in structDef.Members)
        {
            var rawMemberType = GetTypeName(mem.Type, mem.PointerLevel);
            string baseMemberName = rawMemberType.TrimEnd('*');
            string pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);
            string resolvedMemberType;
            if (mem.Type.Type == TokenType.Keyword || baseMemberName.Equals("void", StringComparison.OrdinalIgnoreCase))
            {
                resolvedMemberType = rawMemberType;
            }
            else
            {
                resolvedMemberType = ResolveTypeName(baseMemberName, structDef.Namespace, structDefUnit) + pointerSuffix;
            }

            if (mem.Name.Value == memberName)
            {
                return (baseSize + currentMemberOffset, resolvedMemberType);
            }
            currentMemberOffset += GetSizeOfType(resolvedMemberType, structDefUnit);
        }

        // If not found in current struct's members, check base class
        if (structDef.BaseStructName != null)
        {
            string baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, structDefUnit);
            return GetMemberInfo(baseFqn, memberName, context); // Recursive call
        }

        throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");
    }

    public bool IsMemberConst(string structName, string memberName)
    {
        if (!_structs.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        var member = structDef.Members.FirstOrDefault(m => m.Name.Value == memberName);
        if (member != null)
        {
            return member.IsConst;
        }

        // If not found, check the base class
        if (structDef.BaseStructName != null)
        {
            var structDefUnit = _structUnitMap[structName];
            string baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, structDefUnit);
            return IsMemberConst(baseFqn, memberName);
        }

        throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'.");
    }
}