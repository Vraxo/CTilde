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

    private readonly Dictionary<string, List<FunctionDeclarationNode>> _vtableCache = new();
    private readonly Dictionary<string, bool> _hasVTableCache = new();

    public TypeManager(ProgramNode program)
    {
        _program = program;
        _structs = program.CompilationUnits.SelectMany(cu => cu.Structs)
            .ToDictionary(s => GetFullyQualifiedName(s));
        _functions = program.CompilationUnits.SelectMany(cu => cu.Functions).ToList();
        _enums = program.CompilationUnits.SelectMany(cu => cu.Enums)
            .ToDictionary(e => e.Namespace != null ? $"{e.Namespace}::{e.Name}" : e.Name);

        _structUnitMap = new Dictionary<string, CompilationUnitNode>();
        _enumUnitMap = new Dictionary<string, CompilationUnitNode>();

        foreach (var cu in program.CompilationUnits)
        {
            foreach (var s in cu.Structs)
            {
                _structUnitMap[GetFullyQualifiedName(s)] = cu;
            }
            foreach (var e in cu.Enums)
            {
                _enumUnitMap[e.Namespace != null ? $"{e.Namespace}::{e.Name}" : e.Name] = cu;
            }
        }
    }

    public string GetFullyQualifiedName(StructDefinitionNode s) => s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name;

    public string Mangle(StructDefinitionNode s) => (s.Namespace != null ? $"{s.Namespace}_{s.Name}" : s.Name).Replace(":", "_");

    public string Mangle(FunctionDeclarationNode f)
    {
        var nameParts = new List<string?>();
        if (f.Namespace != null) nameParts.Add(f.Namespace);
        if (f.OwnerStructName != null) nameParts.Add(f.OwnerStructName);
        nameParts.Add(f.Name);
        return "_" + string.Join("_", nameParts).Replace(":", "_");
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
            return _structs.ContainsKey(name) ? name : throw new InvalidOperationException($"Type '{name}' not found.");
        }

        var candidates = new List<string>();
        if (currentNamespace != null)
        {
            var fqn = $"{currentNamespace}::{name}";
            if (_structs.ContainsKey(fqn)) candidates.Add(fqn);
        }

        foreach (UsingDirectiveNode? u in context.Usings.Where(u => u.Alias == null))
        {
            string fqn = $"{u.Namespace}::{name}";
            if (_structs.ContainsKey(fqn)) candidates.Add(fqn);
        }

        if (_structs.ContainsKey(name)) candidates.Add(name);

        if (candidates.Count == 0) throw new InvalidOperationException($"Type '{name}' could not be resolved in the current context.");
        if (candidates.Distinct().Count() > 1) throw new InvalidOperationException($"Type '{name}' is ambiguous between: {string.Join(", ", candidates.Distinct())}");

        return candidates.First();
    }

    public FunctionDeclarationNode ResolveFunctionCall(ExpressionNode callee, CompilationUnitNode context, FunctionDeclarationNode currentFunction)
    {
        if (callee is VariableExpressionNode varNode)
            return ResolveFunctionByName(varNode.Identifier.Value, currentFunction.Namespace, context);

        if (callee is QualifiedAccessExpressionNode qNode)
        {
            string qualifier = ResolveQualifier(qNode.Left);
            var funcName = qNode.Member.Value;

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
        var candidates = _functions.Where(f => f.OwnerStructName == null && f.Name == name)
            .Where(f => f.Namespace == currentNamespace || f.Namespace == null || context.Usings.Any(u => u.Alias == null && u.Namespace == f.Namespace))
            .ToList();

        if (candidates.Count == 0) throw new InvalidOperationException($"Function '{name}' could not be resolved in the current context.");
        if (candidates.Select(f => f.Namespace).Distinct().Count() > 1)
        {
            var sigs = candidates.Select(f => f.Namespace != null ? $"{f.Namespace}::{f.Name}" : f.Name);
            throw new InvalidOperationException($"Function call '{name}' is ambiguous between: {string.Join(", ", sigs)}");
        }
        return candidates.First();
    }

    public FunctionDeclarationNode ResolveMethod(StructDefinitionNode owner, string name)
    {
        var ownerFqn = GetFullyQualifiedName(owner);
        return _functions.FirstOrDefault(f => f.OwnerStructName == owner.Name && f.Namespace == owner.Namespace && f.Name == name)
            ?? throw new InvalidOperationException($"Method '{name}' not found on struct '{ownerFqn}'");
    }

    public bool HasVTable(string structFqn)
    {
        if (_hasVTableCache.TryGetValue(structFqn, out var hasVTable)) return hasVTable;

        if (!_structs.TryGetValue(structFqn, out var structDef)) return false;

        bool result = _functions.Any(f => f.OwnerStructName == structDef.Name && f.Namespace == structDef.Namespace && f.IsVirtual);
        if (result)
        {
            _hasVTableCache[structFqn] = true;
            return true;
        }

        if (structDef.BaseStructName != null)
        {
            var unit = _structUnitMap[structFqn];
            var baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
            result = HasVTable(baseFqn);
        }

        _hasVTableCache[structFqn] = result;
        return result;
    }

    public List<FunctionDeclarationNode> GetVTable(string structFqn)
    {
        if (_vtableCache.TryGetValue(structFqn, out var vtable)) return vtable;

        if (!_structs.TryGetValue(structFqn, out var structDef)) throw new InvalidOperationException($"Struct {structFqn} not found.");

        var newVTable = new List<FunctionDeclarationNode>();
        if (structDef.BaseStructName != null)
        {
            var unit = _structUnitMap[structFqn];
            var baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
            newVTable.AddRange(GetVTable(baseFqn)); // Inherit base vtable
        }

        var methods = _functions.Where(f => f.OwnerStructName == structDef.Name && f.Namespace == structDef.Namespace);
        foreach (var method in methods)
        {
            if (method.IsOverride)
            {
                int index = newVTable.FindIndex(m => m.Name == method.Name);
                if (index == -1) throw new InvalidOperationException($"Method '{method.Name}' marked 'override' but no virtual method found in base class.");
                newVTable[index] = method;
            }
            else if (method.IsVirtual)
            {
                if (newVTable.Any(m => m.Name == method.Name)) throw new InvalidOperationException($"Virtual method '{method.Name}' cannot be redeclared. Use 'override'.");
                newVTable.Add(method);
            }
        }

        _vtableCache[structFqn] = newVTable;
        return newVTable;
    }

    public int GetMethodVTableIndex(string structFqn, string methodName)
    {
        var vtable = GetVTable(structFqn);
        var index = vtable.FindIndex(f => f.Name == methodName);
        if (index == -1) throw new InvalidOperationException($"Method '{methodName}' is not in the vtable for struct '{structFqn}'.");
        return index;
    }

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
            return _enums.ContainsKey(name) ? name : null;
        }

        if (currentNamespace != null)
        {
            var fqn = $"{currentNamespace}::{name}";
            if (_enums.ContainsKey(fqn)) return fqn;
        }

        foreach (var u in context.Usings.Where(u => u.Alias == null))
        {
            var fqn = $"{u.Namespace}::{name}";
            if (_enums.ContainsKey(fqn)) return fqn;
        }

        if (_enums.ContainsKey(name)) return name;
        return null;
    }

    public int? GetEnumValue(string enumFQN, string memberName)
    {
        if (_enums.TryGetValue(enumFQN, out var enumDef))
            return enumDef.Members.FirstOrDefault(m => m.Name.Value == memberName)?.Value;
        return null;
    }

    public int? ResolveUnqualifiedEnumMember(string memberName, CompilationUnitNode context, string? currentContextNamespace)
    {
        var namespacesToCheck = new List<string?> { currentContextNamespace };
        namespacesToCheck.AddRange(context.Usings.Where(u => u.Alias == null).Select(u => u.Namespace));
        namespacesToCheck.Add(null); // Global namespace

        foreach (var ns in namespacesToCheck.Distinct())
        {
            foreach (var enumDef in _enums.Values.Where(e => e.Namespace == ns))
            {
                var member = enumDef.Members.FirstOrDefault(m => m.Name.Value == memberName);
                if (member != null) return member.Value;
            }
        }
        return null;
    }

    public StructDefinitionNode? FindStruct(string qualifiedName) => _structs.TryGetValue(qualifiedName, out var def) ? def : null;

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
            var structDefUnit = _structUnitMap[typeName];
            int size = HasVTable(typeName) ? 4 : 0; // Start with vptr size if applicable

            if (structDef.BaseStructName != null)
            {
                string baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, structDefUnit);
                size += GetSizeOfType(baseFqn, structDefUnit);
            }

            size += structDef.Members.Sum(mem =>
            {
                var rawMemberType = GetTypeName(mem.Type, mem.PointerLevel);
                string baseMemberName = rawMemberType.TrimEnd('*');
                string pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);
                string resolvedMemberTypeForSize = mem.Type.Type == TokenType.Keyword || baseMemberName.Equals("void", StringComparison.OrdinalIgnoreCase)
                    ? rawMemberType
                    : ResolveTypeName(baseMemberName, structDef.Namespace, structDefUnit) + pointerSuffix;
                return GetSizeOfType(resolvedMemberTypeForSize, structDefUnit);
            });
            return size;
        }
        throw new InvalidOperationException($"Unknown type '{typeName}' for size calculation.");
    }

    public bool IsStruct(string typeName) => _structs.ContainsKey(typeName.TrimEnd('*'));

    public (int offset, string type) GetMemberInfo(string structName, string memberName, CompilationUnitNode context)
    {
        if (!_structs.TryGetValue(structName, out var structDef))
            throw new InvalidOperationException($"Undefined struct '{structName}'");

        var allMembers = GetAllMembers(structName, context);
        var member = allMembers.FirstOrDefault(m => m.name == memberName);

        if (member == default)
            throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");

        return (member.offset, member.type);
    }

    public List<(string name, string type, int offset, bool isConst)> GetAllMembers(string structFqn, CompilationUnitNode context)
    {
        if (!_structs.TryGetValue(structFqn, out var structDef)) throw new InvalidOperationException($"Struct '{structFqn}' not found.");

        var allMembers = new List<(string name, string type, int offset, bool isConst)>();
        int currentOffset = 0;

        if (structDef.BaseStructName != null)
        {
            string baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, _structUnitMap[structFqn]);
            allMembers.AddRange(GetAllMembers(baseFqn, context));
            currentOffset = GetSizeOfType(baseFqn, context);
        }
        else if (HasVTable(structFqn))
        {
            currentOffset = 4; // vptr is the first and only thing if no base
        }

        foreach (var mem in structDef.Members)
        {
            var rawMemberType = GetTypeName(mem.Type, mem.PointerLevel);
            var baseMemberName = rawMemberType.TrimEnd('*');
            var pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);
            var resolvedMemberType = mem.Type.Type == TokenType.Keyword || baseMemberName.Equals("void", StringComparison.OrdinalIgnoreCase)
                ? rawMemberType
                : ResolveTypeName(baseMemberName, structDef.Namespace, _structUnitMap[structFqn]) + pointerSuffix;

            allMembers.Add((mem.Name.Value, resolvedMemberType, currentOffset, mem.IsConst));
            currentOffset += GetSizeOfType(resolvedMemberType, _structUnitMap[structFqn]);
        }
        return allMembers;
    }
}