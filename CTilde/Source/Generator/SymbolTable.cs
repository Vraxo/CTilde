using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class SymbolTable
{
    private readonly Dictionary<string, (int Offset, string Type)> _symbols = new();

    public int TotalLocalSize { get; }

    public SymbolTable(FunctionDeclarationNode function, TypeManager typeManager, IEnumerable<string> usings)
    {
        var localDeclarations = function.Body is BlockStatementNode bodyBlock
            ? bodyBlock.Statements.OfType<DeclarationStatementNode>().ToList()
            : new List<DeclarationStatementNode>();

        // Pre-calculate space for all local variables
        TotalLocalSize = 0;
        foreach (var d in localDeclarations)
        {
            var rawTypeName = typeManager.GetTypeName(d.Type, d.PointerLevel);
            var resolvedTypeName = rawTypeName;
            if (!rawTypeName.EndsWith("*") && d.Type.Type != TokenType.Keyword)
            {
                resolvedTypeName = typeManager.ResolveTypeName(rawTypeName, function.Namespace, usings);
            }
            TotalLocalSize += typeManager.GetSizeOfType(resolvedTypeName);
        }

        // Map parameter offsets (positive on stack frame)
        int currentParamOffset = 8;
        foreach (var param in function.Parameters)
        {
            var rawTypeName = typeManager.GetTypeName(param.Type, param.PointerLevel);
            var resolvedTypeName = rawTypeName;
            if (!rawTypeName.EndsWith("*") && param.Type.Type != TokenType.Keyword)
            {
                resolvedTypeName = typeManager.ResolveTypeName(rawTypeName, function.Namespace, usings);
            }

            _symbols[param.Name.Value] = (currentParamOffset, resolvedTypeName);
            // Arguments on the stack are at least 4 bytes aligned.
            currentParamOffset += Math.Max(4, typeManager.GetSizeOfType(resolvedTypeName));
        }

        // Map local variable offsets (negative on stack frame)
        int currentLocalOffset = 0;
        foreach (var stmt in localDeclarations)
        {
            var rawTypeName = typeManager.GetTypeName(stmt.Type, stmt.PointerLevel);
            var resolvedTypeName = rawTypeName;
            if (!rawTypeName.EndsWith("*") && stmt.Type.Type != TokenType.Keyword)
            {
                resolvedTypeName = typeManager.ResolveTypeName(rawTypeName, function.Namespace, usings);
            }
            int size = typeManager.GetSizeOfType(resolvedTypeName);
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