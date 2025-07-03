using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class MemoryLayoutManager
{
    private readonly TypeManager _typeManager;

    public MemoryLayoutManager(TypeManager typeManager)
    {
        _typeManager = typeManager;
    }

    public int GetSizeOfType(string typeNameFqn, CompilationUnitNode context)
    {
        if (typeNameFqn.EndsWith("*")) return 4;
        if (typeNameFqn == "int") return 4;
        if (typeNameFqn == "char") return 1;

        if (_typeManager.FindStruct(typeNameFqn) is { } structDef)
        {
            int size = 0;
            var structUnit = _typeManager.GetCompilationUnitForStruct(typeNameFqn);
            if (structDef.BaseStructName != null)
            {
                string baseFqn = _typeManager.ResolveTypeName(structDef.BaseStructName, structDef.Namespace, structUnit);
                var baseUnit = _typeManager.GetCompilationUnitForStruct(baseFqn);
                size += GetSizeOfType(baseFqn, baseUnit);
            }
            else if (_typeManager.HasVTable(typeNameFqn))
            {
                size += 4; // vtable pointer
            }

            foreach (var member in structDef.Members)
            {
                var rawMemberType = TypeManager.GetTypeName(member.Type, member.PointerLevel);
                string baseMemberName = rawMemberType.TrimEnd('*');
                string pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);

                string resolvedMemberType = (member.Type.Type == TokenType.Keyword || baseMemberName.Equals("void"))
                    ? rawMemberType
                    : _typeManager.ResolveTypeName(baseMemberName, structDef.Namespace, structUnit) + pointerSuffix;

                var memberUnit = _typeManager.IsStruct(resolvedMemberType)
                    ? _typeManager.GetCompilationUnitForStruct(resolvedMemberType.TrimEnd('*'))
                    : structUnit;
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
        if (_typeManager.FindStruct(structFqn) is not { } structDef) throw new System.InvalidOperationException($"Struct '{structFqn}' not found.");

        var allMembers = new List<(string, string, int, bool)>();
        int currentOffset = 0;

        if (structDef.BaseStructName != null)
        {
            var structUnit = _typeManager.GetCompilationUnitForStruct(structFqn);
            string baseFqn = _typeManager.ResolveTypeName(structDef.BaseStructName, structDef.Namespace, structUnit);
            var baseUnit = _typeManager.GetCompilationUnitForStruct(baseFqn);
            allMembers.AddRange(GetAllMembers(baseFqn, baseUnit));
            currentOffset = GetSizeOfType(baseFqn, baseUnit);
        }
        else if (_typeManager.HasVTable(structFqn))
        {
            currentOffset = 4; // vtable pointer
        }

        foreach (var mem in structDef.Members)
        {
            var ownUnit = _typeManager.GetCompilationUnitForStruct(structFqn);
            var rawMemberType = TypeManager.GetTypeName(mem.Type, mem.PointerLevel);
            var baseMemberName = rawMemberType.TrimEnd('*');
            var pointerSuffix = new string('*', rawMemberType.Length - baseMemberName.Length);
            var resolvedMemberType = (mem.Type.Type == TokenType.Keyword || baseMemberName.Equals("void"))
                ? rawMemberType
                : _typeManager.ResolveTypeName(baseMemberName, structDef.Namespace, ownUnit) + pointerSuffix;

            allMembers.Add((mem.Name.Value, resolvedMemberType, currentOffset, mem.IsConst));

            var memberUnit = _typeManager.IsStruct(resolvedMemberType)
                ? _typeManager.GetCompilationUnitForStruct(resolvedMemberType.TrimEnd('*'))
                : ownUnit;
            currentOffset += GetSizeOfType(resolvedMemberType, memberUnit);
        }
        return allMembers;
    }
}