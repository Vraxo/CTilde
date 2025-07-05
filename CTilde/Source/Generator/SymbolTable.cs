using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class SymbolTable
{
    private readonly Dictionary<string, (int Offset, string Type, bool IsConst, bool IsRead)> _symbols = new();
    public int TotalLocalSize { get; private set; }

    // Dummy constructor for semantic analysis pass where symbols aren't fully resolved yet.
    public SymbolTable() { }

    // Constructor for Functions/Methods
    public SymbolTable(FunctionDeclarationNode function, TypeResolver typeResolver, FunctionResolver functionResolver, MemoryLayoutManager memoryLayoutManager, CompilationUnitNode currentUnit)
    {
        var allLocalDeclarations = new List<DeclarationStatementNode>();
        CollectDeclarations(function.Body, allLocalDeclarations);
        Initialize(function.Parameters, allLocalDeclarations, typeResolver, memoryLayoutManager, function.Namespace, currentUnit);
    }

    // Constructor for Constructors
    public SymbolTable(ConstructorDeclarationNode ctor, TypeResolver typeResolver, FunctionResolver functionResolver, MemoryLayoutManager memoryLayoutManager, CompilationUnitNode currentUnit)
    {
        var allLocalDeclarations = new List<DeclarationStatementNode>();
        CollectDeclarations(ctor.Body, allLocalDeclarations);

        var thisTypeName = ctor.Namespace != null ? $"{ctor.Namespace}::{ctor.OwnerStructName}" : ctor.OwnerStructName;
        var thisTypeNode = new PointerTypeNode(new SimpleTypeNode(new Token(TokenType.Identifier, thisTypeName, -1, -1)));
        var thisParam = new ParameterNode(thisTypeNode, new Token(TokenType.Identifier, "this", -1, -1));

        var allParams = new List<ParameterNode> { thisParam };
        allParams.AddRange(ctor.Parameters);
        Initialize(allParams, allLocalDeclarations, typeResolver, memoryLayoutManager, ctor.Namespace, currentUnit);
    }

    // Constructor for Destructors
    public SymbolTable(DestructorDeclarationNode dtor, TypeResolver typeResolver, FunctionResolver functionResolver, MemoryLayoutManager memoryLayoutManager, CompilationUnitNode currentUnit)
    {
        var allLocalDeclarations = new List<DeclarationStatementNode>();
        CollectDeclarations(dtor.Body, allLocalDeclarations);

        var thisTypeName = dtor.Namespace != null ? $"{dtor.Namespace}::{dtor.OwnerStructName}" : dtor.OwnerStructName;
        var thisTypeNode = new PointerTypeNode(new SimpleTypeNode(new Token(TokenType.Identifier, thisTypeName, -1, -1)));
        var thisParam = new ParameterNode(thisTypeNode, new Token(TokenType.Identifier, "this", -1, -1));

        Initialize(new List<ParameterNode> { thisParam }, allLocalDeclarations, typeResolver, memoryLayoutManager, dtor.Namespace, currentUnit);
    }

    private void Initialize(List<ParameterNode> parameters, List<DeclarationStatementNode> localDeclarations, TypeResolver typeResolver, MemoryLayoutManager memoryLayoutManager, string? currentNamespace, CompilationUnitNode currentUnit)
    {
        TotalLocalSize = 0;
        foreach (var d in localDeclarations)
        {
            var baseTypeName = d.Type.GetBaseTypeName();
            if (baseTypeName == "unknown") continue;

            string resolvedTypeName = typeResolver.ResolveType(d.Type, currentNamespace, currentUnit);
            TotalLocalSize += memoryLayoutManager.GetSizeOfType(resolvedTypeName, currentUnit);
        }

        int currentParamOffset = 8; // EBP + 8 is first parameter
        foreach (var param in parameters)
        {
            var baseTypeName = param.Type.GetBaseTypeName();
            if (baseTypeName == "unknown") continue;

            string resolvedTypeName;
            if (param.Name.Value == "this" && param.Type is PointerTypeNode ptn && ptn.BaseType is SimpleTypeNode stn)
            {
                // The 'this' pointer for a monomorphized struct needs to use the mangled name.
                // We can get this from the owner's name which should already be mangled.
                var function = param.Ancestors().OfType<FunctionDeclarationNode>().FirstOrDefault();
                if (function != null && function.OwnerStructName != null && function.OwnerStructName.Contains("__"))
                {
                    resolvedTypeName = function.OwnerStructName + "*";
                }
                else
                {
                    resolvedTypeName = typeResolver.ResolveType(param.Type, currentNamespace, currentUnit);
                }
            }
            else
            {
                resolvedTypeName = typeResolver.ResolveType(param.Type, currentNamespace, currentUnit);
            }

            _symbols[param.Name.Value] = (currentParamOffset, resolvedTypeName, false, false); // isRead = false
            currentParamOffset += Math.Max(4, memoryLayoutManager.GetSizeOfType(resolvedTypeName, currentUnit));
        }

        int currentLocalOffset = 0;
        foreach (var decl in localDeclarations)
        {
            var baseTypeName = decl.Type.GetBaseTypeName();
            if (baseTypeName == "unknown") continue;

            string resolvedTypeName = typeResolver.ResolveType(decl.Type, currentNamespace, currentUnit);

            int size = memoryLayoutManager.GetSizeOfType(resolvedTypeName, currentUnit);
            currentLocalOffset -= size;
            _symbols[decl.Identifier.Value] = (currentLocalOffset, resolvedTypeName, decl.IsConst, false); // isRead = false
        }
    }

    public void MarkAsRead(string name)
    {
        if (_symbols.TryGetValue(name, out var symbol))
        {
            _symbols[name] = (symbol.Offset, symbol.Type, symbol.IsConst, true);
        }
    }

    public IEnumerable<(string Name, int Line, int Column)> GetUnreadLocals()
    {
        return _symbols
            .Where(kvp => kvp.Value.Offset < 0 && !kvp.Value.IsRead) // Only locals (negative offset)
            .Select(kvp => {
                // This is a bit of a hack to get the declaration location back.
                // A better approach would be to store the DeclarationStatementNode in the symbol table.
                return (kvp.Key, -1, -1);
            });
    }



    private void CollectDeclarations(AstNode? node, List<DeclarationStatementNode> declarations)
    {
        if (node == null) return;
        if (node is DeclarationStatementNode decl) declarations.Add(decl);
        else if (node is BlockStatementNode block) foreach (var stmt in block.Statements) CollectDeclarations(stmt, declarations);
        else if (node is IfStatementNode ifStmt) { CollectDeclarations(ifStmt.ThenBody, declarations); CollectDeclarations(ifStmt.ElseBody, declarations); }
        else if (node is WhileStatementNode whileStmt) CollectDeclarations(whileStmt.Body, declarations);
    }

    public List<(string Name, int Offset, string TypeFqn)> GetDestructibleLocals(FunctionResolver functionResolver)
    {
        var result = new List<(string, int, string)>();
        foreach (var (name, (offset, type, _, _)) in _symbols)
        {
            if (offset < 0 && functionResolver.FindDestructor(type) != null) // Locals have negative offset
            {
                result.Add((name, offset, type));
            }
        }
        return result;
    }

    public bool TryGetSymbol(string name, out int offset, out string type, out bool isConst)
    {
        if (_symbols.TryGetValue(name, out var symbol))
        {
            offset = symbol.Offset;
            type = symbol.Type;
            isConst = symbol.IsConst;
            return true;
        }
        offset = 0; type = string.Empty; isConst = false;
        return false;
    }

    public string GetSymbolType(string name)
    {
        return _symbols.TryGetValue(name, out var symbol)
            ? symbol.Type
            : throw new InvalidOperationException($"Symbol '{name}' not found in current scope.");
    }
}