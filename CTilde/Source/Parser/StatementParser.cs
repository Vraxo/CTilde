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

    internal StatementNode ParseStatement()
    {
        if (_parser.Current.Type == TokenType.Keyword)
        {
            switch (_parser.Current.Value)
            {
                case "return": return ParseReturnStatement();
                case "if": return ParseIfStatement();
                case "while": return ParseWhileStatement();
                case "delete": return ParseDeleteStatement();
                case "const":
                case "int":
                case "char":
                case "struct":
                    return ParseDeclarationStatement();
            }
        }

        // Lookahead to distinguish between a declaration and an expression statement
        bool isDeclaration = false;
        if (_parser.Current.Type == TokenType.Identifier || (_parser.Current.Type == TokenType.Keyword && (_parser.Current.Value == "const" || _parser.Current.Value == "int" || _parser.Current.Value == "char" || _parser.Current.Value == "struct")))
        {
            int tempPos = _parser._position;
            try
            {
                if (_parser.Current.Type == TokenType.Keyword && _parser.Current.Value == "const") _parser.AdvancePosition(1);
                _parser.ParseType();
                if (_parser.Current.Type == TokenType.Identifier) isDeclaration = true;
            }
            catch
            {
                // Lookahead failed, assume not a declaration
                isDeclaration = false;
            }
            finally
            {
                _parser._position = tempPos;
            }
        }

        if (isDeclaration) return ParseDeclarationStatement();
        if (_parser.Current.Type == TokenType.LeftBrace) return ParseBlockStatement();

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

        var (typeToken, pointerLevel) = _parser.ParseType();
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
                while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) != null);
            }
            _parser.Eat(TokenType.RightParen);
        }
        else if (isConst)
        {
            _parser.ReportError($"Constant variable '{identifier.Value}' must be initialized.", identifier);
        }
        _parser.Eat(TokenType.Semicolon);
        return new DeclarationStatementNode(isConst, typeToken, pointerLevel, identifier, initializer, ctorArgs);
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