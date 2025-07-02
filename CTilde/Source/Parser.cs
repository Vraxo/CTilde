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
        var functions = new List<FunctionDeclarationNode>();
        while (_position < _tokens.Count)
        {
            functions.Add(ParseFunction());
        }
        return new ProgramNode(functions);
    }

    private FunctionDeclarationNode ParseFunction()
    {
        Eat(TokenType.Keyword); // "int"
        var identifier = Eat(TokenType.Identifier); // function name
        Eat(TokenType.LeftParen);
        Eat(TokenType.RightParen); // No parameters yet

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
        if (Current.Type == TokenType.Keyword)
        {
            return Current.Value switch
            {
                "return" => ParseReturnStatement(),
                "if" => ParseIfStatement(),
                "while" => ParseWhileStatement(),
                "int" => ParseDeclarationStatement(),
                _ => throw new InvalidOperationException($"Unexpected keyword '{Current.Value}' at the beginning of a statement."),// An 'else' on its own, or other future keywords, are syntax errors here.
            };
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

    private IfStatementNode ParseIfStatement()
    {
        Eat(TokenType.Keyword); // "if"
        Eat(TokenType.LeftParen);
        var condition = ParseExpression();
        Eat(TokenType.RightParen);

        var thenBody = ParseStatement();
        StatementNode? elseBody = null;

        if (Current.Type == TokenType.Keyword && Current.Value == "else")
        {
            Eat(TokenType.Keyword); // "else"
            elseBody = ParseStatement();
        }

        return new IfStatementNode(condition, thenBody, elseBody);
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
        var left = ParseEqualityExpression();

        if (Current.Type == TokenType.Assignment)
        {
            Eat(TokenType.Assignment);
            var right = ParseAssignmentExpression(); // Right-associative

            if (left is VariableExpressionNode varNode)
            {
                return new AssignmentExpressionNode(varNode.Identifier, right);
            }

            throw new InvalidOperationException("Invalid assignment target.");
        }

        return left;
    }

    private ExpressionNode ParseEqualityExpression()
    {
        var left = ParseRelationalExpression();
        while (Current.Type is TokenType.DoubleEquals or TokenType.NotEquals)
        {
            var op = Current;
            _position++;
            var right = ParseRelationalExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseRelationalExpression()
    {
        var left = ParseAdditiveExpression();
        while (Current.Type is TokenType.LessThan or TokenType.GreaterThan)
        {
            var op = Current;
            _position++;
            var right = ParseAdditiveExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseAdditiveExpression()
    {
        var left = ParseMultiplicativeExpression();
        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            var op = Current;
            _position++;
            var right = ParseMultiplicativeExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseMultiplicativeExpression()
    {
        var left = ParseCallExpression();
        while (Current.Type is TokenType.Star or TokenType.Slash)
        {
            var op = Current;
            _position++;
            var right = ParseCallExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseCallExpression()
    {
        var expr = ParsePrimaryExpression();

        if (Current.Type == TokenType.LeftParen)
        {
            if (expr is VariableExpressionNode varNode)
            {
                Eat(TokenType.LeftParen);

                var arguments = new List<ExpressionNode>();
                if (Current.Type != TokenType.RightParen)
                {
                    // For now, only one argument is supported.
                    // A proper implementation would loop on comma.
                    arguments.Add(ParseExpression());
                }

                Eat(TokenType.RightParen);
                return new CallExpressionNode(varNode.Identifier, arguments);
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

        if (Current.Type == TokenType.LeftParen)
        {
            Eat(TokenType.LeftParen);
            var expr = ParseExpression();
            Eat(TokenType.RightParen);
            return expr;
        }

        throw new InvalidOperationException($"Unexpected expression token: {Current.Type}");
    }
}