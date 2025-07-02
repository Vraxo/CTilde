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
            
            // Resolve the base type name, then append the pointer suffix back if any
            string baseTypeName = rawTypeName.TrimEnd('*');
            string pointerSuffix = new string('*', rawTypeName.Length - baseTypeName.Length);
            
            string resolvedTypeName;
            if (d.Type.Type == TokenType.Keyword || baseTypeName.Equals("void", StringComparison.OrdinalIgnoreCase))
            {
                resolvedTypeName = rawTypeName; // Primitive types like int, char, or void don't need resolution
            }
            else
            {
                resolvedTypeName = typeManager.ResolveTypeName(baseTypeName, function.Namespace, currentUnit) + pointerSuffix;
            }
            
            TotalLocalSize += typeManager.GetSizeOfType(resolvedTypeName, currentUnit);
        }
        
        // Map parameter offsets (positive on stack frame)
        int currentParamOffset = 8; // EBP + 8 is first parameter
        foreach (var param in function.Parameters)
        {
            var rawTypeName = typeManager.GetTypeName(param.Type, param.PointerLevel);

            // Resolve the base type name, then append the pointer suffix back if any
            string baseTypeName = rawTypeName.TrimEnd('*');
            string pointerSuffix = new string('*', rawTypeName.Length - baseTypeName.Length);

            string resolvedTypeName;
            if (param.Type.Type == TokenType.Keyword || baseTypeName.Equals("void", StringComparison.OrdinalIgnoreCase))
            {
                resolvedTypeName = rawTypeName;
            }
            else
            {
                // For 'this' pointer in methods, its type is already fully qualified from Parser
                if (param.Name.Value == "this" && param.Type.Value.Contains("::"))
                {
                    resolvedTypeName = rawTypeName;
                }
                else
                {
                    resolvedTypeName = typeManager.ResolveTypeName(baseTypeName, function.Namespace, currentUnit) + pointerSuffix;
                }
            }
            
            _symbols[param.Name.Value] = (currentParamOffset, resolvedTypeName);
            currentParamOffset += Math.Max(4, typeManager.GetSizeOfType(resolvedTypeName, currentUnit)); // Arguments on stack are 4-byte aligned
        }

        // Map local variable offsets (negative on stack frame)
        int currentLocalOffset = 0;
        foreach (var stmt in localDeclarations)
        {
            var rawTypeName = typeManager.GetTypeName(stmt.Type, stmt.PointerLevel);
            
            // Resolve the base type name, then append the pointer suffix back if any
            string baseTypeName = rawTypeName.TrimEnd('*');
            string pointerSuffix = new string('*', rawTypeName.Length - baseTypeName.Length);

            string resolvedTypeName;
            if (stmt.Type.Type == TokenType.Keyword || baseTypeName.Equals("void", StringComparison.OrdinalIgnoreCase))
            {
                resolvedTypeName = rawTypeName;
            }
            else
            {
                resolvedTypeName = typeManager.ResolveTypeName(baseTypeName, function.Namespace, currentUnit) + pointerSuffix;
            }

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

    public string GetSymbolType(string name)
    {
        if (_symbols.TryGetValue(name, out var symbol))
        {
            return symbol.Type;
        }
        throw new InvalidOperationException($"Symbol '{name}' not found in current scope.");
    }
}