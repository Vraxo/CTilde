using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class TypeResolver
{
    private readonly TypeRepository _typeRepository;

    public TypeResolver(TypeRepository typeRepository)
    {
        _typeRepository = typeRepository;
    }

    public static string ResolveQualifier(ExpressionNode expr)
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
                return _typeRepository.FindStruct(fqn) != null ? fqn : throw new InvalidOperationException($"Type '{name}' with aliased namespace '{nsPart}' not found.");
            }
            return _typeRepository.FindStruct(name) != null ? name : throw new InvalidOperationException($"Type '{name}' not found.");
        }

        var candidates = new List<string>();
        if (currentNamespace != null)
        {
            var fqn = $"{currentNamespace}::{name}";
            if (_typeRepository.FindStruct(fqn) != null) candidates.Add(fqn);
        }
        foreach (var u in context.Usings.Where(u => u.Alias == null))
        {
            string fqn = $"{u.Namespace}::{name}";
            if (_typeRepository.FindStruct(fqn) != null) candidates.Add(fqn);
        }
        if (_typeRepository.FindStruct(name) != null) candidates.Add(name);

        if (candidates.Count == 0) throw new InvalidOperationException($"Type '{name}' could not be resolved in the current context.");
        if (candidates.Distinct().Count() > 1) throw new InvalidOperationException($"Type '{name}' is ambiguous between: {string.Join(", ", candidates.Distinct())}");
        return candidates.First();
    }

    public string? ResolveEnumTypeName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        if (name.Contains("::"))
        {
            var parts = name.Split("::");
            var aliased = context.Usings.FirstOrDefault(u => u.Alias == parts[0]);
            var fqn = aliased != null ? $"{aliased.Namespace}::{parts[1]}" : name;
            return _typeRepository.FindEnum(fqn) != null ? fqn : null;
        }

        var namespacesToCheck = new List<string?> { currentNamespace }
            .Concat(context.Usings.Where(u => u.Alias == null).Select(u => u.Namespace))
            .Append(null);

        foreach (var ns in namespacesToCheck.Distinct())
        {
            var fqn = ns != null ? $"{ns}::{name}" : name;
            if (_typeRepository.FindEnum(fqn) != null) return fqn;
        }

        return null;
    }
}