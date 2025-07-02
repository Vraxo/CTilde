using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class SymbolTable
{
    private readonly Dictionary<string, (int Offset, string Type)> _symbols = new();

    public int TotalLocalSize { get; }

    public SymbolTable(FunctionDeclarationNode function, TypeManager typeManager, CompilationUnitNode currentUnit)
    {
        var localDeclarations = function.Body is BlockStatementNode bodyBlock
            ? bodyBlock.Statements.OfType<DeclarationStatementNode>().ToList()
            : new List<DeclarationStatementNode>();

        // Pre-calculate space for all local variables
        TotalLocalSize = 0;
        foreach (var d in localDeclarations)
        {
            var rawTypeName = typeManager.GetTypeName(d.Type, d.PointerLevel);
            var resolvedTypeName = typeManager.ResolveTypeName(rawTypeName, function.Namespace, currentUnit);
            TotalLocalSize += typeManager.GetSizeOfType(resolvedTypeName, currentUnit);
        }

        // Map parameter offsets (positive on stack frame)
        int currentParamOffset = 8;
        foreach (var param in function.Parameters)
        {
            var rawTypeName = typeManager.GetTypeName(param.Type, param.PointerLevel);
            var resolvedTypeName = typeManager.ResolveTypeName(rawTypeName, function.Namespace, currentUnit);

            _symbols[param.Name.Value] = (currentParamOffset, resolvedTypeName);
            currentParamOffset += Math.Max(4, typeManager.GetSizeOfType(resolvedTypeName, currentUnit));
        }

        // Map local variable offsets (negative on stack frame)
        int currentLocalOffset = 0;
        foreach (var stmt in localDeclarations)
        {
            var rawTypeName = typeManager.GetTypeName(stmt.Type, stmt.PointerLevel);
            var resolvedTypeName = typeManager.ResolveTypeName(rawTypeName, function.Namespace, currentUnit);
            int size = typeManager.GetSizeOfType(resolvedTypeName, currentUnit);
            currentLocalOffset -= size;
            _symbols[stmt.Identifier.Value] = (currentLocalOffset, resolvedTypeName);
        }
    }

    public bool TryGetSymbol(string name, out int offset, out string type)
    {
        if (_symbols.TryGetValue(name, out var symbol))
        {
            offset = symbol.Offset;
            type = symbol.Type;
            return true;
        }

        offset = 0;
        type = string.Empty;
        return false;
    }
}