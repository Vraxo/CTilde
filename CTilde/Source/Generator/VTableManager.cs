using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class VTableManager
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;

    private readonly Dictionary<string, List<AstNode>> _vtableCache = new();
    private readonly Dictionary<string, bool> _hasVTableCache = new();

    public VTableManager(TypeRepository typeRepository, TypeResolver typeResolver)
    {
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
    }

    public bool HasVTable(string structFqn)
    {
        if (_hasVTableCache.TryGetValue(structFqn, out var hasVTable)) return hasVTable;

        var structDef = _typeRepository.FindStruct(structFqn);
        if (structDef == null) return false;

        bool result = structDef.Methods.Any(m => m.IsVirtual) || structDef.Destructors.Any(d => d.IsVirtual);
        if (result)
        {
            _hasVTableCache[structFqn] = true;
            return true;
        }

        if (structDef.BaseStructName != null)
        {
            var unit = _typeRepository.GetCompilationUnitForStruct(structFqn);
            var baseFqn = _typeResolver.ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
            result = HasVTable(baseFqn);
        }

        _hasVTableCache[structFqn] = result;
        return result;
    }

    public List<AstNode> GetVTable(string structFqn)
    {
        if (_vtableCache.TryGetValue(structFqn, out var vtable)) return vtable;

        var structDef = _typeRepository.FindStruct(structFqn) ?? throw new InvalidOperationException($"Struct {structFqn} not found.");

        var newVTable = new List<AstNode>();
        if (structDef.BaseStructName != null)
        {
            var unit = _typeRepository.GetCompilationUnitForStruct(structFqn);
            var baseFqn = _typeResolver.ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
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
}