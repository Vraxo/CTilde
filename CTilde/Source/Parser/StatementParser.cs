using System;
using System.Collections.Generic;

namespace CTilde;

internal class StatementParser
{
    private readonly Parser _parser;
    private readonly ExpressionParser _expressionParser;

    internal StatementParser(Parser parser, ExpressionParser expressionParser)
    {
        _parser = parser;
        _expressionParser = expressionParser;
    }

    internal BlockStatementNode ParseBlockStatement()
    {
        _parser.Eat(TokenType.LeftBrace);
        var statements = new List<StatementNode>();
        while (_parser.Current.Type != TokenType.RightBrace && _parser.Current.Type != TokenType.Unknown)
        {
            statements.Add(ParseStatement());
        }
        _parser.Eat(TokenType.RightBrace);
        return new BlockStatementNode(statements);
    }

    /// <summary>
    /// Peeks ahead in the token stream to determine if the upcoming sequence of tokens is a declaration.
    /// This is a classic C/C++ parsing problem, as `A * B;` could be a multiplication or a declaration.
    /// This lookahead is read-only and does not consume tokens or report errors.
    /// </summary>
    private bool IsDeclaration()
    {
        int originalPosition = _parser._position;
        try
        {
            // Create a new parser instance for a safe lookahead, to avoid modifying the main parser's state
            // and to suppress error reporting during the lookahead.
            var tempParser = new Parser(new List<Token>(_parser._tokens.ToArray()))
            {
                _position = originalPosition
            };

            // A declaration can optionally start with const.
            if (tempParser.Current.Type == TokenType.Keyword && tempParser.Current.Value == "const")
            {
                tempParser.AdvancePosition(1);
            }

            // Now, try to parse a type from the temporary state.
            tempParser.ParseTypeNode();

            // If we get here, it parsed a type. A declaration must be followed by an identifier.
            return tempParser.Current.Type == TokenType.Identifier;
        }
        catch
        {
            return false;
        }
    }


    internal StatementNode ParseStatement()
    {
        // First, check for keywords that unambiguously start a statement.
        if (_parser.Current.Type == TokenType.Keyword)
        {
            switch (_parser.Current.Value)
            {
                case "return": return ParseReturnStatement();
                case "if": return ParseIfStatement();
                case "while": return ParseWhileStatement();
                case "delete": return ParseDeleteStatement();
            }
        }

        // Check for block statements.
        if (_parser.Current.Type == TokenType.LeftBrace)
        {
            return ParseBlockStatement();
        }

        // Now, use the lookahead to resolve the ambiguity between a declaration and an expression.
        if (IsDeclaration())
        {
            return ParseDeclarationStatement();
        }

        // If it's not a declaration or any other statement type, it must be an expression.
        var expression = _expressionParser.ParseExpression();
        _parser.Eat(TokenType.Semicolon);
        return new ExpressionStatementNode(expression);
    }

    private DeleteStatementNode ParseDeleteStatement()
    {
        _parser.Eat(TokenType.Keyword); // delete
        var expr = _expressionParser.ParseExpression();
        _parser.Eat(TokenType.Semicolon);
        return new DeleteStatementNode(expr);
    }

    private IfStatementNode ParseIfStatement()
    {
        _parser.Eat(TokenType.Keyword);
        _parser.Eat(TokenType.LeftParen);
        var condition = _expressionParser.ParseExpression();
        _parser.Eat(TokenType.RightParen);
        var thenBody = ParseStatement();
        StatementNode? elseBody = null;
        if (_parser.Current.Type == TokenType.Keyword && _parser.Current.Value == "else")
        {
            _parser.Eat(TokenType.Keyword);
            elseBody = ParseStatement();
        }
        return new IfStatementNode(condition, thenBody, elseBody);
    }

    private StatementNode ParseDeclarationStatement()
    {
        bool isConst = false;
        if (_parser.Current.Type == TokenType.Keyword && _parser.Current.Value == "const")
        {
            isConst = true;
            _parser.Eat(TokenType.Keyword);
        }

        var typeNode = _parser.ParseTypeNode();
        var identifier = _parser.Eat(TokenType.Identifier);

        ExpressionNode? initializer = null;
        List<ExpressionNode>? ctorArgs = null;

        if (_parser.Current.Type == TokenType.Assignment)
        {
            _parser.Eat(TokenType.Assignment);
            if (_parser.Current.Type == TokenType.LeftBrace) initializer = _expressionParser.ParseInitializerListExpression();
            else initializer = _expressionParser.ParseExpression();
        }
        else if (_parser.Current.Type == TokenType.LeftParen)
        {
            _parser.Eat(TokenType.LeftParen);
            ctorArgs = new List<ExpressionNode>();
            if (_parser.Current.Type != TokenType.RightParen)
            {
                do { ctorArgs.Add(_expressionParser.ParseExpression()); }
                while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) is not null);
            }
            _parser.Eat(TokenType.RightParen);
        }
        else if (isConst)
        {
            _parser.ReportError($"Constant variable '{identifier.Value}' must be initialized.", identifier);
        }
        _parser.Eat(TokenType.Semicolon);
        return new DeclarationStatementNode(isConst, typeNode, identifier, initializer, ctorArgs);
    }

    private WhileStatementNode ParseWhileStatement()
    {
        _parser.Eat(TokenType.Keyword);
        _parser.Eat(TokenType.LeftParen);
        var condition = _expressionParser.ParseExpression();
        _parser.Eat(TokenType.RightParen);
        var body = ParseStatement();
        return new WhileStatementNode(condition, body);
    }

    private ReturnStatementNode ParseReturnStatement()
    {
        _parser.Eat(TokenType.Keyword);
        ExpressionNode? expression = null;
        if (_parser.Current.Type != TokenType.Semicolon) expression = _expressionParser.ParseExpression();
        _parser.Eat(TokenType.Semicolon);
        return new ReturnStatementNode(expression);
    }
}