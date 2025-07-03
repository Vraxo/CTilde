using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTilde;

public class TypeManager
{
    private readonly ProgramNode _program;
    private readonly Dictionary<string, StructDefinitionNode> _structs;
    private readonly Dictionary<string, EnumDefinitionNode> _enums;
    private readonly Dictionary<string, CompilationUnitNode> _structUnitMap;

    private readonly Dictionary<string, List<AstNode>> _vtableCache = new();
    private readonly Dictionary<string, bool> _hasVTableCache = new();

    public TypeManager(ProgramNode program)
    {
        _program = program;
        _structs = program.CompilationUnits.SelectMany(cu => cu.Structs)
            .ToDictionary(s => GetFullyQualifiedName(s));
        _enums = program.CompilationUnits.SelectMany(cu => cu.Enums)
            .ToDictionary(e => e.Namespace != null ? $"{e.Namespace}::{e.Name}" : e.Name);
        _structUnitMap = new Dictionary<string, CompilationUnitNode>();
        foreach (var cu in program.CompilationUnits)
            foreach (var s in cu.Structs)
                _structUnitMap[GetFullyQualifiedName(s)] = cu;
    }

    public string GetFullyQualifiedName(StructDefinitionNode s) => s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name;
    private string MangleName(string? ns, string? owner, string name) => $"_{ns?.Replace("::", "_")}_{owner}_{name}".Replace("___", "_").Replace("__", "_");
    public string Mangle(FunctionDeclarationNode f) => MangleName(f.Namespace, f.OwnerStructName, f.Name);
    public string Mangle(ConstructorDeclarationNode c) => MangleName(c.Namespace, c.OwnerStructName, $"{c.OwnerStructName}_ctor{c.Parameters.Count}");
    public string Mangle(DestructorDeclarationNode d) => MangleName(d.Namespace, d.OwnerStructName, $"{d.OwnerStructName}_dtor");
    public string GetVTableLabel(StructDefinitionNode s) => $"_vtable_{s.Namespace?.Replace("::", "_")}_{s.Name}".Replace("__", "_");

    private string ResolveQualifier(ExpressionNode expr) => expr switch
    {
        VariableExpressionNode v => v.Identifier.Value,
        QualifiedAccessExpressionNode q => $"{ResolveQualifier(q.Left)}::{q.Member.Value}",
        _ => throw new InvalidOperationException($"Cannot resolve qualifier from expression of type {expr.GetType().Name}")
    };

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

    public FunctionDeclarationNode ResolveFunctionCall(ExpressionNode callee, CompilationUnitNode context, FunctionDeclarationNode? currentFunction)
    {
        if (callee is VariableExpressionNode varNode) return ResolveFunctionByName(varNode.Identifier.Value, currentFunction?.Namespace, context);
        if (callee is QualifiedAccessExpressionNode qNode)
        {
            string qualifier = ResolveQualifier(qNode.Left);
            var funcName = qNode.Member.Value;
            string? resolvedNamespace = qualifier;
            var aliased = context.Usings.FirstOrDefault(u => u.Alias == qualifier);
            if (aliased != null) resolvedNamespace = aliased.Namespace;

            var globalFunctions = _program.CompilationUnits.SelectMany(cu => cu.Functions);
            var func = globalFunctions.FirstOrDefault(f => f.OwnerStructName == null && f.Namespace == resolvedNamespace && f.Name == funcName);
            if (func == null) throw new InvalidOperationException($"Function '{resolvedNamespace}::{funcName}' not found.");
            return func;
        }
        throw new NotSupportedException($"Unsupported callee type for resolution: {callee.GetType().Name}");
    }

    private FunctionDeclarationNode ResolveFunctionByName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        var globalFunctions = _program.CompilationUnits.SelectMany(cu => cu.Functions);
        var candidates = globalFunctions.Where(f => f.OwnerStructName == null && f.Name == name)
            .Where(f => f.Namespace == currentNamespace || f.Namespace == null || context.Usings.Any(u => u.Alias == null && u.Namespace == f.Namespace)).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException($"Function '{name}' could not be resolved in the current context.");
        if (candidates.Select(f => f.Namespace).Distinct().Count() > 1) throw new InvalidOperationException($"Function call '{name}' is ambiguous.");
        return candidates.First();
    }

    public FunctionDeclarationNode ResolveMethod(StructDefinitionNode owner, string name) => owner.Methods.FirstOrDefault(m => m.Name == name) ?? throw new InvalidOperationException($"Method '{name}' not found on struct '{owner.Name}'");
    public ConstructorDeclarationNode? FindConstructor(string fqn, int argCount) => _structs[fqn].Constructors.FirstOrDefault(c => c.Parameters.Count == argCount);
    public DestructorDeclarationNode? FindDestructor(string fqn) => _structs.ContainsKey(fqn) ? _structs[fqn].Destructors.FirstOrDefault() : null;

    public bool HasVTable(string structFqn)
    {
        if (_hasVTableCache.TryGetValue(structFqn, out var hasVTable)) return hasVTable;
        if (!_structs.TryGetValue(structFqn, out var structDef)) return false;

        bool result = structDef.Methods.Any(m => m.IsVirtual) || structDef.Destructors.Any(d => d.IsVirtual);
        if (result) { _hasVTableCache[structFqn] = true; return true; }

        if (structDef.BaseStructName != null)
        {
            var unit = _structUnitMap[structFqn];
            var baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
            result = HasVTable(baseFqn);
        }
        _hasVTableCache[structFqn] = result;
        return result;
    }

    public List<AstNode> GetVTable(string structFqn)
    {
        if (_vtableCache.TryGetValue(structFqn, out var vtable)) return vtable;
        if (!_structs.TryGetValue(structFqn, out var structDef)) throw new InvalidOperationException($"Struct {structFqn} not found.");

        var newVTable = new List<AstNode>();
        if (structDef.BaseStructName != null)
        {
            var unit = _structUnitMap[structFqn];
            var baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
            newVTable.AddRange(GetVTable(baseFqn));
        }

        var dtor = structDef.Destructors.FirstOrDefault();
        if (dtor?.IsVirtual ?? false)
        {
            if (newVTable.Any(n => n is DestructorDeclarationNode)) newVTable[0] = dtor;
            else newVTable.Insert(0, dtor);
        }

        foreach (var method in structDef.Methods)
        {
            int index = newVTable.FindIndex(m => m is FunctionDeclarationNode f && f.Name == method.Name);
            if (method.IsOverride)
            {
                if (index == -1) throw new InvalidOperationException($"Method '{method.Name}' marked 'override' but no virtual method found in base class.");
                newVTable[index] = method;
            }
            else if (method.IsVirtual)
            {
                if (index != -1) throw new InvalidOperationException($"Virtual method '{method.Name}' cannot be redeclared. Use 'override'.");
                newVTable.Add(method);
            }
        }
        _vtableCache[structFqn] = newVTable;
        return newVTable;
    }

    public int GetMethodVTableIndex(string structFqn, string methodName)
    {
        var vtable = GetVTable(structFqn);
        var index = vtable.FindIndex(n => n is FunctionDeclarationNode f && f.Name == methodName);
        if (index == -1) throw new InvalidOperationException($"Method '{methodName}' is not in the vtable for struct '{structFqn}'.");
        return index;
    }

    public string? ResolveEnumTypeName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        if (name.Contains("::"))
        {
            var parts = name.Split("::");
            var aliased = context.Usings.FirstOrDefault(u => u.Alias == parts[0]);
            var fqn = aliased != null ? $"{aliased.Namespace}::{parts[1]}" : name;
            return _enums.ContainsKey(fqn) ? fqn : null;
        }
        var namespacesToCheck = new List<string?> { currentNamespace }.Concat(context.Usings.Where(u => u.Alias == null).Select(u => u.Namespace)).Append(null);
        foreach (var ns in namespacesToCheck.Distinct())
        {
            var fqn = ns != null ? $"{ns}::{name}" : name;
            if (_enums.ContainsKey(fqn)) return fqn;
        }
        return null;
    }

    public int? GetEnumValue(string enumFQN, string memberName) => _enums.TryGetValue(enumFQN, out var ed) ? ed.Members.FirstOrDefault(m => m.Name.Value == memberName)?.Value : null;

    public int? ResolveUnqualifiedEnumMember(string memberName, CompilationUnitNode context, string? currentContextNamespace)
    {
        var namespacesToCheck = new List<string?> { currentContextNamespace }.Concat(context.Usings.Where(u => u.Alias == null).Select(u => u.Namespace)).Append(null);
        foreach (var ns in namespacesToCheck.Distinct())
            foreach (var enumDef in _enums.Values.Where(e => e.Namespace == ns))
                if (enumDef.Members.Any(m => m.Name.Value == memberName)) return GetEnumValue(GetFullyQualifiedName(enumDef), memberName);
        return null;
    }
    private string GetFullyQualifiedName(EnumDefinitionNode e) => e.Namespace != null ? $"{e.Namespace}::{e.Name}" : e.Name;

    public StructDefinitionNode? FindStruct(string qualifiedName) => _structs.TryGetValue(qualifiedName, out var def) ? def : null;

    public StructDefinitionNode? FindStructByUnqualifiedName(string name, string? currentNamespace)
    {
        var fqn = currentNamespace != null ? $"{currentNamespace}::{name}" : name;
        if (_structs.TryGetValue(fqn, out var def)) return def;
        if (_structs.TryGetValue(name, out def)) return def; // Check global
        return null;
    }

    public (StructDefinitionNode Def, string FullName) GetStructTypeFromFullName(string fullName) => (_structs.TryGetValue(fullName, out var def) ? def : throw new InvalidOperationException($"Could not find struct definition for '{fullName}'."), fullName);
    public string GetTypeName(Token type, int pointerLevel) => type.Value + new string('*', pointerLevel);

    public int GetSizeOfType(string typeNameFqn, CompilationUnitNode context)
    {
        if (typeNameFqn.EndsWith("*")) return 4;
        if (typeNameFqn == "int") return 4;
        if (typeNameFqn == "char") return 1;

        if (_structs.TryGetValue(typeNameFqn, out var structDef))
        {
            int size = 0;
            if (structDef.BaseStructName != null)
            {
                string baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, context);
                size += GetSizeOfType(baseFqn, _structUnitMap[baseFqn]);
            }
            else if (HasVTable(typeNameFqn))
            {
                size += 4;
            }

            foreach (var member in structDef.Members)
            {
                var ownUnit = _structUnitMap[typeNameFqn];
                var rawMemberType = GetTypeName(member.Type, member.PointerLevel);
                string baseMemberName = rawMemberType.TrimEnd('*');
                string pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);
                string resolvedMemberType = member.Type.Type == TokenType.Keyword || baseMemberName.Equals("void") ? rawMemberType : ResolveTypeName(baseMemberName, structDef.Namespace, ownUnit) + pointerSuffix;

                var memberUnit = IsStruct(resolvedMemberType) ? _structUnitMap[resolvedMemberType.TrimEnd('*')] : ownUnit;
                size += GetSizeOfType(resolvedMemberType, memberUnit);
            }

            return size;
        }
        throw new InvalidOperationException($"Unknown type '{typeNameFqn}' for size calculation.");
    }

    public bool IsStruct(string typeName) => _structs.ContainsKey(typeName.TrimEnd('*'));

    public (int offset, string type) GetMemberInfo(string structName, string memberName, CompilationUnitNode context)
    {
        var member = GetAllMembers(structName, context).FirstOrDefault(m => m.name == memberName);
        if (member == default) throw new InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");
        return (member.offset, member.type);
    }

    public List<(string name, string type, int offset, bool isConst)> GetAllMembers(string structFqn, CompilationUnitNode context)
    {
        if (!_structs.TryGetValue(structFqn, out var structDef)) throw new InvalidOperationException($"Struct '{structFqn}' not found.");

        var allMembers = new List<(string, string, int, bool)>();
        int currentOffset = 0;

        if (structDef.BaseStructName != null)
        {
            string baseFqn = ResolveTypeName(structDef.BaseStructName, structDef.Namespace, _structUnitMap[structFqn]);
            allMembers.AddRange(GetAllMembers(baseFqn, _structUnitMap[baseFqn]));
            currentOffset = GetSizeOfType(baseFqn, _structUnitMap[baseFqn]);
        }
        else if (HasVTable(structFqn))
        {
            currentOffset = 4;
        }

        foreach (var mem in structDef.Members)
        {
            var ownUnit = _structUnitMap[structFqn];
            var rawMemberType = GetTypeName(mem.Type, mem.PointerLevel);
            var baseMemberName = rawMemberType.TrimEnd('*');
            var pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);
            var resolvedMemberType = mem.Type.Type == TokenType.Keyword || baseMemberName.Equals("void") ? rawMemberType : ResolveTypeName(baseMemberName, structDef.Namespace, ownUnit) + pointerSuffix;
            allMembers.Add((mem.Name.Value, resolvedMemberType, currentOffset, mem.IsConst));
            currentOffset += GetSizeOfType(resolvedMemberType, ownUnit);
        }
        return allMembers;
    }
}