using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class MemoryLayoutManager
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;
    private readonly VTableManager _vtableManager;

    public MemoryLayoutManager(TypeRepository typeRepository, TypeResolver typeResolver, VTableManager vtableManager)
    {
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
        _vtableManager = vtableManager;
    }

    public int GetSizeOfType(string typeNameFqn, CompilationUnitNode context)
    {
        if (typeNameFqn.EndsWith("*")) return 4;
        if (typeNameFqn == "int") return 4;
        if (typeNameFqn == "char") return 1;
        if (typeNameFqn == "void") return 0; // Void has no size

        // Heuristic: If it's a single uppercase letter, assume it's a generic type parameter.
        // In the current implementation, generic types are treated like pointers/references.
        if (typeNameFqn.Length == 1 && char.IsUpper(typeNameFqn[0]))
        {
            return 4; // Treat as a pointer size.
        }

        // If it's not a primitive, try to find it as a struct
        if (_typeRepository.FindStruct(typeNameFqn) is { } structDef)
        {
            int size = 0;
            var structUnit = _typeRepository.GetCompilationUnitForStruct(typeNameFqn);
            if (structDef.BaseStructName != null)
            {
                var baseTypeNode = new SimpleTypeNode(new Token(TokenType.Identifier, structDef.BaseStructName, -1, -1));
                string baseFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, structUnit);

                // The baseUnit might be different if the base struct is in another file/namespace
                var baseUnit = _typeRepository.GetCompilationUnitForStruct(baseFqn);
                size += GetSizeOfType(baseFqn, baseUnit);
            }
            else if (_vtableManager.HasVTable(typeNameFqn))
            {
                size += 4; // vtable pointer
            }

            foreach (var member in structDef.Members)
            {
                var resolvedMemberType = _typeResolver.ResolveType(member.Type, structDef.Namespace, structUnit);

                var memberUnit = _typeRepository.IsStruct(resolvedMemberType)
                    ? _typeRepository.GetCompilationUnitForStruct(resolvedMemberType.TrimEnd('*'))
                    : structUnit; // If not a struct, use the owner struct's unit for context
                size += GetSizeOfType(resolvedMemberType, memberUnit);
            }
            return size;
        }
        throw new System.InvalidOperationException($"Unknown type '{typeNameFqn}' for size calculation.");
    }

    public (int offset, string type) GetMemberInfo(string structName, string memberName, CompilationUnitNode context)
    {
        var member = GetAllMembers(structName, context).FirstOrDefault(m => m.name == memberName);
        if (member == default) throw new System.InvalidOperationException($"Struct '{structName}' has no member '{memberName}'");
        return (member.offset, member.type);
    }

    public List<(string name, string type, int offset, bool isConst)> GetAllMembers(string structFqn, CompilationUnitNode context)
    {
        if (_typeRepository.FindStruct(structFqn) is not { } structDef) throw new System.InvalidOperationException($"Struct '{structFqn}' not found.");

        var allMembers = new List<(string, string, int, bool)>();
        int currentOffset = 0;

        if (structDef.BaseStructName != null)
        {
            var structUnit = _typeRepository.GetCompilationUnitForStruct(structFqn);
            var baseTypeNode = new SimpleTypeNode(new Token(TokenType.Identifier, structDef.BaseStructName, -1, -1));
            string baseFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, structUnit);
            var baseUnit = _typeRepository.GetCompilationUnitForStruct(baseFqn);
            allMembers.AddRange(GetAllMembers(baseFqn, baseUnit));
            currentOffset = GetSizeOfType(baseFqn, baseUnit);
        }
        else if (_vtableManager.HasVTable(structFqn))
        {
            currentOffset = 4; // vtable pointer
        }

        foreach (var mem in structDef.Members)
        {
            var ownUnit = _typeRepository.GetCompilationUnitForStruct(structFqn);
            string resolvedMemberType = _typeResolver.ResolveType(mem.Type, structDef.Namespace, ownUnit);

            allMembers.Add((mem.Name.Value, resolvedMemberType, currentOffset, mem.IsConst));

            var memberUnit = _typeRepository.IsStruct(resolvedMemberType)
                ? _typeRepository.GetCompilationUnitForStruct(resolvedMemberType.TrimEnd('*'))
                : ownUnit; // If not a struct, use the owner struct's unit for context
            currentOffset += GetSizeOfType(resolvedMemberType, memberUnit);
        }
        return allMembers;
    }
}