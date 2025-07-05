using System;
using System.Collections.Generic;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde;

public class Parser
{
    internal readonly List<Token> _tokens;
    internal int _position;
    internal string? _currentNamespace;
    internal readonly List<ImportDirectiveNode> _imports = new();
    private string _filePath = "";

    public List<Diagnostic> Diagnostics { get; } = new();

    private readonly ExpressionParser _expressionParser;
    private readonly StatementParser _statementParser;
    private readonly DeclarationParser _declarationParser;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _position = 0;
        _expressionParser = new ExpressionParser(this);
        _statementParser = new StatementParser(this, _expressionParser);
        _declarationParser = new DeclarationParser(this, _statementParser, _expressionParser);
    }

    internal Token Current => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];
    internal Token Previous => _position > 0 ? _tokens[_position - 1] : _tokens[0];
    internal Token Peek(int offset) => _position + offset < _tokens.Count ? _tokens[_position + offset] : _tokens[^1];

    internal void ReportError(string message, Token token)
    {
        Diagnostics.Add(new Diagnostic(_filePath, message, token.Line, token.Column));
    }

    internal void ReportErrorAfter(string message, Token previousToken)
    {
        var line = previousToken.Line;
        var col = previousToken.Column + previousToken.Value.Length;
        Diagnostics.Add(new Diagnostic(_filePath, message, line, col));
    }

    internal Token Eat(TokenType expectedType)
    {
        var currentToken = Current;
        if (currentToken.Type == expectedType)
        {
            _position++;
            return currentToken;
        }

        string message = $"Expected '{expectedType}' but got '{currentToken.Type}' ('{currentToken.Value}')";

        // Heuristic: If we expect a statement/block terminator, the error is likely at the end of the previous construct.
        if (expectedType is TokenType.Semicolon or TokenType.RightBrace or TokenType.RightParen)
        {
            // Report the error at the position immediately *after* the last successfully consumed token.
            ReportErrorAfter(message, Previous);
        }
        else
        {
            ReportError(message, currentToken);
        }

        return new Token(expectedType, string.Empty, currentToken.Line, currentToken.Column); // Return a dummy token
    }

    internal void AdvancePosition(int amount) => _position += amount;

    public List<ImportDirectiveNode> GetImports() => _imports;

    public CompilationUnitNode Parse(string filePath)
    {
        _filePath = filePath;
        var usings = new List<UsingDirectiveNode>();
        var structs = new List<StructDefinitionNode>();
        var functions = new List<FunctionDeclarationNode>();
        var enums = new List<EnumDefinitionNode>();

        while (Current.Type != TokenType.Unknown)
        {
            try
            {
                if (Current.Type == TokenType.Hash)
                {
                    var hashKeyword = Peek(1);
                    if (hashKeyword.Type == TokenType.Identifier && hashKeyword.Value == "import")
                    {
                        _imports.Add(_declarationParser.ParseImportDirective());
                    }
                    else if (hashKeyword.Type == TokenType.Identifier && hashKeyword.Value == "include")
                    {
                        _declarationParser.ParseIncludeDirective(); // Handle and skip #include
                    }
                    else
                    {
                        ReportError($"Unexpected directive after '#': '{hashKeyword.Value}'", hashKeyword);
                        AdvancePosition(2); // Skip '#' and the bad identifier
                    }
                }
                else if (Current.Type == TokenType.Keyword && Current.Value == "using")
                {
                    usings.Add(_declarationParser.ParseUsingDirective());
                }
                else if (Current.Type == TokenType.Keyword && Current.Value == "namespace")
                {
                    _declarationParser.ParseNamespaceDirective();
                }
                else if (Current.Type == TokenType.Keyword && Current.Value == "struct")
                {
                    structs.Add(_declarationParser.ParseStructDefinition());
                }
                else if (Current.Type == TokenType.Keyword && Current.Value == "enum")
                {
                    enums.Add(_declarationParser.ParseEnumDefinition());
                }
                else
                {
                    functions.Add(_declarationParser.ParseGlobalFunction());
                }
            }
            catch (Exception) // Catch potential cascading failures from bad tokens
            {
                // Synchronize to the next likely statement start to continue parsing
                while (Current.Type != TokenType.Semicolon && Current.Type != TokenType.RightBrace && Current.Type != TokenType.Unknown)
                {
                    AdvancePosition(1);
                }
                // Also consume the synchronizing token
                if (Current.Type != TokenType.Unknown) AdvancePosition(1);
            }
        }

        var unitNode = new CompilationUnitNode(filePath, usings, structs, functions, enums);
        SetParents(unitNode, null);
        return unitNode;
    }

    public void SetParents(AstNode node, AstNode? parent)
    {
        node.Parent = parent;
        foreach (var property in node.GetType().GetProperties())
        {
            if (property.CanWrite && property.Name == "Parent") continue;
            if (property.GetValue(node) is AstNode child)
            {
                SetParents(child, node);
            }
            else if (property.GetValue(node) is IEnumerable<AstNode> children)
            {
                foreach (var c in children.ToList()) // ToList to avoid mutation issues
                {
                    SetParents(c, node);
                }
            }
        }
    }

    internal TypeNode ParseTypeNode()
    {
        Token baseTypeToken;
        var current = Current;

        // 1. Parse the base name (which could be qualified)
        if (current.Type == TokenType.Keyword && current.Value == "struct")
        {
            Eat(TokenType.Keyword);
            baseTypeToken = Eat(TokenType.Identifier);
        }
        else if (current.Type == TokenType.Keyword && (current.Value is "int" or "char" or "void"))
        {
            baseTypeToken = Eat(TokenType.Keyword);
        }
        else if (current.Type == TokenType.Identifier)
        {
            baseTypeToken = Eat(TokenType.Identifier);
            // Check for `::`
            if (Current.Type == TokenType.DoubleColon)
            {
                Eat(TokenType.DoubleColon);
                var memberName = Eat(TokenType.Identifier);
                baseTypeToken = new Token(TokenType.Identifier, $"{baseTypeToken.Value}::{memberName.Value}", baseTypeToken.Line, baseTypeToken.Column);
            }
        }
        else
        {
            ReportError($"Expected a type name but found '{current.Type}' ('{current.Value}').", current);
            AdvancePosition(1); // Consume the bad token to prevent infinite loop
            return new SimpleTypeNode(new Token(TokenType.Identifier, "unknown", current.Line, current.Column));
        }

        // 2. Parse optional generic arguments
        TypeNode typeNode;
        if (Current.Type == TokenType.LessThan)
        {
            Eat(TokenType.LessThan);
            var typeArgs = new List<TypeNode>();
            do { typeArgs.Add(ParseTypeNode()); }
            while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
            Eat(TokenType.GreaterThan);
            typeNode = new GenericInstantiationTypeNode(baseTypeToken, typeArgs);
        }
        else
        {
            typeNode = new SimpleTypeNode(baseTypeToken);
        }

        // 3. Parse optional pointers
        while (Current.Type == TokenType.Star)
        {
            Eat(TokenType.Star);
            typeNode = new PointerTypeNode(typeNode);
        }
        return typeNode;
    }
}