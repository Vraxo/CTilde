using System;
using System.Collections.Generic;

namespace CTilde;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _position;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _position = 0;
    }

    private Token Current => _position < _tokens.Count ? _tokens[_position] : new(TokenType.Unknown, string.Empty);

    private Token Eat(TokenType expectedType)
    {
        var currentToken = Current;
        if (currentToken.Type == expectedType)
        {
            _position++;
            return currentToken;
        }
        throw new InvalidOperationException($"Expected token {expectedType} but got {currentToken.Type}");
    }

    public ProgramNode Parse()
    {
        var function = ParseFunction();
        return new ProgramNode(function);
    }

    private FunctionDeclarationNode ParseFunction()
    {
        Eat(TokenType.Keyword); // "int"
        var identifier = Eat(TokenType.Identifier); // "main"
        Eat(TokenType.LeftParen);
        Eat(TokenType.RightParen);

        var body = ParseBlockStatement();

        return new FunctionDeclarationNode(identifier.Value, body);
    }

    private BlockStatementNode ParseBlockStatement()
    {
        Eat(TokenType.LeftBrace);
        var statements = new List<StatementNode>();
        while (Current.Type != TokenType.RightBrace)
        {
            statements.Add(ParseStatement());
        }
        Eat(TokenType.RightBrace);
        return new BlockStatementNode(statements);
    }

    private StatementNode ParseStatement()
    {
        if (Current.Type == TokenType.Keyword && Current.Value == "return")
        {
            return ParseReturnStatement();
        }

        if (Current.Type == TokenType.Keyword && Current.Value == "while")
        {
            return ParseWhileStatement();
        }

        if (Current.Type == TokenType.LeftBrace)
        {
            return ParseBlockStatement();
        }

        throw new InvalidOperationException($"Unexpected statement starting with token {Current.Type}");
    }

    private WhileStatementNode ParseWhileStatement()
    {
        Eat(TokenType.Keyword); // "while"
        Eat(TokenType.LeftParen);
        var condition = ParseExpression();
        Eat(TokenType.RightParen);
        var body = ParseStatement();
        return new WhileStatementNode(condition, body);
    }

    private ReturnStatementNode ParseReturnStatement()
    {
        Eat(TokenType.Keyword); // "return"
        var expression = ParseExpression();
        Eat(TokenType.Semicolon);
        return new ReturnStatementNode(expression);
    }

    private ExpressionNode ParseExpression()
    {
        if (Current.Type == TokenType.IntegerLiteral)
        {
            var token = Eat(TokenType.IntegerLiteral);
            if (int.TryParse(token.Value, out int value))
            {
                return new IntegerLiteralNode(value);
            }
            throw new InvalidOperationException($"Could not parse integer literal: {token.Value}");
        }

        throw new InvalidOperationException($"Unexpected expression token: {Current.Type}");
    }
}