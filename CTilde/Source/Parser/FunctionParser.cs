namespace CTilde;

internal class FunctionParser
{
    private readonly Parser _parser;
    private readonly StatementParser _statementParser;

    internal FunctionParser(Parser parser, StatementParser statementParser)
    {
        _parser = parser;
        _statementParser = statementParser;
    }

    internal FunctionDeclarationNode ParseGlobalFunction()
    {
        var returnType = _parser.ParseTypeNode();
        var identifier = _parser.Eat(TokenType.Identifier);
        return FinishParsingFunction(returnType, identifier.Value, null, AccessSpecifier.Public, false, false, _parser._currentNamespace, false);
    }

    internal FunctionDeclarationNode FinishParsingFunction(TypeNode returnType, string name, string? ownerStructName, AccessSpecifier accessLevel, bool isVirtual, bool isOverride, string? namespaceName, bool isMethod)
    {
        var parameters = ParseParameterList();

        if (isMethod && ownerStructName is not null)
        {
            // The `this` pointer type will be resolved later during semantic analysis.
            // For now, we create a placeholder.
            var thisType = new PointerTypeNode(new SimpleTypeNode(new Token(TokenType.Identifier, ownerStructName, -1, -1)));
            var thisName = new Token(TokenType.Identifier, "this", -1, -1);
            var thisParam = new ParameterNode(thisType, thisName);
            parameters.Insert(0, thisParam);
        }

        StatementNode? body = null;
        if (_parser.Current.Type == TokenType.LeftBrace)
        {
            body = _statementParser.ParseBlockStatement();
        }
        else
        {
            _parser.Eat(TokenType.Semicolon); // For function prototypes
        }

        return new FunctionDeclarationNode(returnType, name, parameters, body, ownerStructName, accessLevel, isVirtual, isOverride, namespaceName);
    }

    internal List<ParameterNode> ParseParameterList()
    {
        _parser.Eat(TokenType.LeftParen);
        var parameters = new List<ParameterNode>();
        if (_parser.Current.Type != TokenType.RightParen)
        {
            do
            {
                var paramType = _parser.ParseTypeNode();
                var paramName = _parser.Eat(TokenType.Identifier);
                parameters.Add(new ParameterNode(paramType, paramName));
            } while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) is not null);
        }
        _parser.Eat(TokenType.RightParen);
        return parameters;
    }
}