using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class SymbolTable
{
    private readonly Dictionary<string, (int Offset, string Type)> _symbols = new();

    public int TotalLocalSize { get; }

    public SymbolTable(FunctionDeclarationNode function, TypeManager typeManager)
    {
        // Pre-calculate space for all local variables
        TotalLocalSize = 0;
        if (function.Body is BlockStatementNode bodyBlock)
        {
            TotalLocalSize = bodyBlock.Statements
                .OfType<DeclarationStatementNode>()
                .Sum(d => typeManager.GetSizeOfType(typeManager.GetTypeName(d.Type, d.PointerLevel)));
        }

        // Map parameter offsets (positive on stack frame)
        int currentParamOffset = 8;
        foreach (var param in function.Parameters)
        {
            var typeName = typeManager.GetTypeName(param.Type, param.PointerLevel);
            _symbols[param.Name.Value] = (currentParamOffset, typeName);
            // Arguments on the stack are at least 4 bytes aligned.
            currentParamOffset += Math.Max(4, typeManager.GetSizeOfType(typeName));
        }

        // Map local variable offsets (negative on stack frame)
        int currentLocalOffset = 0;
        if (function.Body is BlockStatementNode block)
        {
            foreach (var stmt in block.Statements.OfType<DeclarationStatementNode>())
            {
                var typeName = typeManager.GetTypeName(stmt.Type, stmt.PointerLevel);
                int size = typeManager.GetSizeOfType(typeName);
                currentLocalOffset -= size;
                _symbols[stmt.Identifier.Value] = (currentLocalOffset, typeName);
            }
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