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
    private Token Peek(int offset) => _position + offset < _tokens.Count ? _tokens[_position + offset] : new(TokenType.Unknown, string.Empty);


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

        if (Current.Type == TokenType.Keyword && Current.Value == "if")
        {
            return ParseIfStatement();
        }

        if (Current.Type == TokenType.Keyword && Current.Value == "int")
        {
            return ParseDeclarationStatement();
        }

        if (Current.Type == TokenType.LeftBrace)
        {
            return ParseBlockStatement();
        }

        // Otherwise, it must be an expression statement (e.g. assignment, call)
        var expression = ParseExpression();
        Eat(TokenType.Semicolon);
        return new ExpressionStatementNode(expression);
    }

    private StatementNode ParseIfStatement()
    {
        Eat(TokenType.Keyword); // "if"
        Eat(TokenType.LeftParen);
        var condition = ParseExpression();
        Eat(TokenType.RightParen);

        var thenBranch = ParseStatement();
        StatementNode? elseBranch = null;

        if (Current.Type == TokenType.Keyword && Current.Value == "else")
        {
            Eat(TokenType.Keyword); // "else"
            elseBranch = ParseStatement();
        }

        return new IfStatementNode(condition, thenBranch, elseBranch);
    }

    private StatementNode ParseDeclarationStatement()
    {
        Eat(TokenType.Keyword); // "int"
        var identifier = Eat(TokenType.Identifier);
        ExpressionNode? initializer = null;

        if (Current.Type == TokenType.Assignment)
        {
            Eat(TokenType.Assignment); // "="
            initializer = ParseExpression();
        }

        Eat(TokenType.Semicolon);
        return new DeclarationStatementNode(identifier, initializer);
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
        return ParseAssignmentExpression();
    }

    private ExpressionNode ParseAssignmentExpression()
    {
        // Parse the left-hand side, which could be a function call or variable
        var left = ParseCallExpression();

        if (Current.Type == TokenType.Assignment)
        {
            Eat(TokenType.Assignment);
            // Assignment is right-associative
            var right = ParseAssignmentExpression();

            // The target of an assignment must be a variable
            if (left is VariableExpressionNode varNode)
            {
                return new AssignmentExpressionNode(varNode.Identifier, right);
            }

            throw new InvalidOperationException("Invalid assignment target.");
        }

        return left;
    }

    private ExpressionNode ParseCallExpression()
    {
        // A call expression is a primary expression (like an identifier)
        // possibly followed by parentheses.
        var expr = ParsePrimaryExpression();

        if (Current.Type == TokenType.LeftParen)
        {
            // If we see a '(', it must be a function call.
            // The thing being called must have been a variable.
            if (expr is VariableExpressionNode varNode)
            {
                Eat(TokenType.LeftParen);
                // For now, only one argument is supported.
                var argument = ParseExpression();
                Eat(TokenType.RightParen);
                return new CallExpressionNode(varNode.Identifier, argument);
            }
            throw new InvalidOperationException("Expression is not callable.");
        }

        return expr;
    }

    private ExpressionNode ParsePrimaryExpression()
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

        if (Current.Type == TokenType.Identifier)
        {
            var token = Eat(TokenType.Identifier);
            return new VariableExpressionNode(token);
        }

        throw new InvalidOperationException($"Unexpected expression token: {Current.Type}");
    }
}