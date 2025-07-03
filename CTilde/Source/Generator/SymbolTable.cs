using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class SymbolTable
{
    private readonly Dictionary<string, (int Offset, string Type, bool IsConst)> _symbols = new();
    public int TotalLocalSize { get; private set; }

    // Constructor for Functions/Methods
    public SymbolTable(FunctionDeclarationNode function, TypeManager typeManager, MemoryLayoutManager memoryLayoutManager, CompilationUnitNode currentUnit)
    {
        var allLocalDeclarations = new List<DeclarationStatementNode>();
        CollectDeclarations(function.Body, allLocalDeclarations);
        Initialize(function.Parameters, allLocalDeclarations, typeManager, memoryLayoutManager, function.Namespace, currentUnit);
    }

    // Constructor for Constructors
    public SymbolTable(ConstructorDeclarationNode ctor, TypeManager typeManager, MemoryLayoutManager memoryLayoutManager, CompilationUnitNode currentUnit)
    {
        var allLocalDeclarations = new List<DeclarationStatementNode>();
        CollectDeclarations(ctor.Body, allLocalDeclarations);

        var thisTypeName = ctor.Namespace != null ? $"{ctor.Namespace}::{ctor.OwnerStructName}" : ctor.OwnerStructName;
        var thisParam = new ParameterNode(new Token(TokenType.Identifier, thisTypeName), 1, new Token(TokenType.Identifier, "this"));

        var allParams = new List<ParameterNode> { thisParam };
        allParams.AddRange(ctor.Parameters);
        Initialize(allParams, allLocalDeclarations, typeManager, memoryLayoutManager, ctor.Namespace, currentUnit);
    }

    // Constructor for Destructors
    public SymbolTable(DestructorDeclarationNode dtor, TypeManager typeManager, MemoryLayoutManager memoryLayoutManager, CompilationUnitNode currentUnit)
    {
        var allLocalDeclarations = new List<DeclarationStatementNode>();
        CollectDeclarations(dtor.Body, allLocalDeclarations);

        var thisTypeName = dtor.Namespace != null ? $"{dtor.Namespace}::{dtor.OwnerStructName}" : dtor.OwnerStructName;
        var thisParam = new ParameterNode(new Token(TokenType.Identifier, thisTypeName), 1, new Token(TokenType.Identifier, "this"));

        Initialize(new List<ParameterNode> { thisParam }, allLocalDeclarations, typeManager, memoryLayoutManager, dtor.Namespace, currentUnit);
    }

    private void Initialize(List<ParameterNode> parameters, List<DeclarationStatementNode> localDeclarations, TypeManager typeManager, MemoryLayoutManager memoryLayoutManager, string? currentNamespace, CompilationUnitNode currentUnit)
    {
        TotalLocalSize = 0;
        foreach (var d in localDeclarations)
        {
            var rawTypeName = TypeManager.GetTypeName(d.Type, d.PointerLevel);
            var baseTypeName = rawTypeName.TrimEnd('*');
            var pointerSuffix = new string('*', rawTypeName.Length - baseTypeName.Length);
            var resolvedTypeName = d.Type.Type == TokenType.Keyword || baseTypeName.Equals("void", StringComparison.OrdinalIgnoreCase)
                ? rawTypeName
                : typeManager.ResolveTypeName(baseTypeName, currentNamespace, currentUnit) + pointerSuffix;
            TotalLocalSize += memoryLayoutManager.GetSizeOfType(resolvedTypeName, currentUnit);
        }

        int currentParamOffset = 8; // EBP + 8 is first parameter
        foreach (var param in parameters)
        {
            var rawTypeName = TypeManager.GetTypeName(param.Type, param.PointerLevel);
            var baseTypeName = rawTypeName.TrimEnd('*');
            var pointerSuffix = new string('*', rawTypeName.Length - baseTypeName.Length);
            string resolvedTypeName;

            if (param.Type.Type == TokenType.Keyword || baseTypeName.Equals("void", StringComparison.OrdinalIgnoreCase))
            {
                resolvedTypeName = rawTypeName;
            }
            else if (param.Type.Value.Contains("::"))
            {
                // This correctly handles the pre-qualified 'this' parameter type.
                resolvedTypeName = rawTypeName;
            }
            else
            {
                resolvedTypeName = typeManager.ResolveTypeName(baseTypeName, currentNamespace, currentUnit) + pointerSuffix;
            }

            _symbols[param.Name.Value] = (currentParamOffset, resolvedTypeName, false);
            currentParamOffset += Math.Max(4, memoryLayoutManager.GetSizeOfType(resolvedTypeName, currentUnit));
        }

        int currentLocalOffset = 0;
        foreach (var decl in localDeclarations)
        {
            var rawTypeName = TypeManager.GetTypeName(decl.Type, decl.PointerLevel);
            var baseTypeName = rawTypeName.TrimEnd('*');
            var pointerSuffix = new string('*', rawTypeName.Length - baseTypeName.Length);
            var resolvedTypeName = decl.Type.Type == TokenType.Keyword || baseTypeName.Equals("void", StringComparison.OrdinalIgnoreCase)
                ? rawTypeName
                : typeManager.ResolveTypeName(baseTypeName, currentNamespace, currentUnit) + pointerSuffix;

            int size = memoryLayoutManager.GetSizeOfType(resolvedTypeName, currentUnit);
            currentLocalOffset -= size;
            _symbols[decl.Identifier.Value] = (currentLocalOffset, resolvedTypeName, decl.IsConst);
        }
    }



    private void CollectDeclarations(AstNode? node, List<DeclarationStatementNode> declarations)
    {
        if (node == null) return;
        if (node is DeclarationStatementNode decl) declarations.Add(decl);
        else if (node is BlockStatementNode block) foreach (var stmt in block.Statements) CollectDeclarations(stmt, declarations);
        else if (node is IfStatementNode ifStmt) { CollectDeclarations(ifStmt.ThenBody, declarations); CollectDeclarations(ifStmt.ElseBody, declarations); }
        else if (node is WhileStatementNode whileStmt) CollectDeclarations(whileStmt.Body, declarations);
    }

    public List<(string Name, int Offset, string TypeFqn)> GetDestructibleLocals(TypeManager typeManager)
    {
        var result = new List<(string, int, string)>();
        foreach (var (name, (offset, type, _)) in _symbols)
        {
            if (offset < 0 && typeManager.FindDestructor(type) != null) // Locals have negative offset
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