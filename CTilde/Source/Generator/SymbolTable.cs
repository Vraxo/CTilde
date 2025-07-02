using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class SymbolTable
{
    private readonly Dictionary<string, (int Offset, string Type, bool IsConst)> _symbols = new(); // Added IsConst

    public int TotalLocalSize { get; }

    public SymbolTable(FunctionDeclarationNode function, TypeManager typeManager, CompilationUnitNode currentUnit)
    {
        var allLocalDeclarations = new List<DeclarationStatementNode>();
        CollectDeclarations(function.Body, allLocalDeclarations); // Recursively collect all declarations

        // Pre-calculate space for all local variables
        TotalLocalSize = 0;
        foreach (var d in allLocalDeclarations)
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

            // Parameters are not marked as 'const' in this simple implementation, they are effectively copies.
            _symbols[param.Name.Value] = (currentParamOffset, resolvedTypeName, false);
            currentParamOffset += Math.Max(4, typeManager.GetSizeOfType(resolvedTypeName, currentUnit)); // Arguments on stack are 4-byte aligned
        }

        // Map local variable offsets (negative on stack frame)
        int currentLocalOffset = 0;
        foreach (var decl in allLocalDeclarations) // Use all collected declarations
        {
            var rawTypeName = typeManager.GetTypeName(decl.Type, decl.PointerLevel);

            // Resolve the base type name, then append the pointer suffix back if any
            string baseTypeName = rawTypeName.TrimEnd('*');
            string pointerSuffix = new string('*', rawTypeName.Length - baseTypeName.Length);

            string resolvedTypeName;
            if (decl.Type.Type == TokenType.Keyword || baseTypeName.Equals("void", StringComparison.OrdinalIgnoreCase))
            {
                resolvedTypeName = rawTypeName;
            }
            else
            {
                resolvedTypeName = typeManager.ResolveTypeName(baseTypeName, function.Namespace, currentUnit) + pointerSuffix;
            }

            int size = typeManager.GetSizeOfType(resolvedTypeName, currentUnit);
            currentLocalOffset -= size;
            _symbols[decl.Identifier.Value] = (currentLocalOffset, resolvedTypeName, decl.IsConst); // Store IsConst
        }
    }

    private void CollectDeclarations(AstNode? node, List<DeclarationStatementNode> declarations)
    {
        if (node == null) return;

        if (node is DeclarationStatementNode decl)
        {
            declarations.Add(decl);
        }
        else if (node is BlockStatementNode block)
        {
            foreach (var stmt in block.Statements)
            {
                CollectDeclarations(stmt, declarations);
            }
        }
        else if (node is IfStatementNode ifStmt)
        {
            CollectDeclarations(ifStmt.ThenBody, declarations);
            CollectDeclarations(ifStmt.ElseBody, declarations);
        }
        else if (node is WhileStatementNode whileStmt)
        {
            CollectDeclarations(whileStmt.Body, declarations);
        }
        // Add other statement types if they can contain declarations
    }

    // Updated TryGetSymbol to include isConst
    public bool TryGetSymbol(string name, out int offset, out string type, out bool isConst)
    {
        if (_symbols.TryGetValue(name, out var symbol))
        {
            offset = symbol.Offset;
            type = symbol.Type;
            isConst = symbol.IsConst;
            return true;
        }

        offset = 0;
        type = string.Empty;
        isConst = false;
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

    // This method is now less critical as TryGetSymbol is updated, but kept for clarity
    public bool IsSymbolConst(string name)
    {
        if (_symbols.TryGetValue(name, out var symbol))
        {
            return symbol.IsConst;
        }
        return false;
    }
}