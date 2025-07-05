using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class TypeRepository
{
    private readonly Dictionary<string, StructDefinitionNode> _structs;
    private readonly Dictionary<string, EnumDefinitionNode> _enums;
    private readonly Dictionary<string, CompilationUnitNode> _structUnitMap;
    private readonly Dictionary<string, CompilationUnitNode> _enumUnitMap;

    public TypeRepository(ProgramNode program)
    {
        _structs = program.CompilationUnits.SelectMany(cu => cu.Structs)
            .ToDictionary(s => GetFullyQualifiedName(s));
        _enums = program.CompilationUnits.SelectMany(cu => cu.Enums)
            .ToDictionary(e => GetFullyQualifiedName(e));

        _structUnitMap = new Dictionary<string, CompilationUnitNode>();
        foreach (var cu in program.CompilationUnits)
            foreach (var s in cu.Structs)
                _structUnitMap[GetFullyQualifiedName(s)] = cu;

        _enumUnitMap = new Dictionary<string, CompilationUnitNode>();
        foreach (var cu in program.CompilationUnits)
            foreach (var e in cu.Enums)
                _enumUnitMap[GetFullyQualifiedName(e)] = cu;
    }

    public static string GetFullyQualifiedName(StructDefinitionNode s) => s.Namespace != null ? $"{s.Namespace}::{s.Name}" : s.Name;
    public static string GetFullyQualifiedName(EnumDefinitionNode e) => e.Namespace != null ? $"{e.Namespace}::{e.Name}" : e.Name;

    public StructDefinitionNode? FindStruct(string qualifiedName) => _structs.TryGetValue(qualifiedName, out var def) ? def : null;

    public StructDefinitionNode? FindStructByUnqualifiedName(string name, string? currentNamespace)
    {
        var fqn = currentNamespace != null ? $"{currentNamespace}::{name}" : name;
        if (_structs.TryGetValue(fqn, out var def)) return def;
        return _structs.TryGetValue(name, out def) ? def : null;
    }

    public EnumDefinitionNode? FindEnum(string qualifiedName) => _enums.TryGetValue(qualifiedName, out var def) ? def : null;

    public IEnumerable<StructDefinitionNode> GetAllStructs() => _structs.Values;
    public IEnumerable<EnumDefinitionNode> GetAllEnums() => _enums.Values;

    public CompilationUnitNode GetCompilationUnitForStruct(string structFqn) => _structUnitMap[structFqn];
    public CompilationUnitNode GetCompilationUnitForEnum(string enumFqn) => _enumUnitMap[enumFqn];

    public bool IsStruct(string typeName) => _structs.ContainsKey(typeName.TrimEnd('*'));

    public static string GetTypeNameFromNode(TypeNode node)
    {
        return node switch
        {
            SimpleTypeNode s => s.TypeToken.Value,
            PointerTypeNode p => GetTypeNameFromNode(p.BaseType) + "*",
            GenericInstantiationTypeNode g => $"{g.BaseType.Value}<{string.Join(",", g.TypeArguments.Select(GetTypeNameFromNode))}>",
            _ => throw new NotImplementedException($"GetTypeNameFromNode not implemented for {node.GetType().Name}")
        };
    }
}