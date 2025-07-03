using System;
using System.Collections.Generic;
using System.Globalization;

namespace CTilde;

internal class ExpressionParser
{
    private readonly Parser _parser;
    private int _stringLabelCounter;

    internal ExpressionParser(Parser parser)
    {
        _parser = parser;
    }

    internal ExpressionNode ParseInitializerListExpression()
    {
        var openingBrace = _parser.Eat(TokenType.LeftBrace);
        var values = new List<ExpressionNode>();
        if (_parser.Current.Type != TokenType.RightBrace)
        {
            do { values.Add(ParseExpression()); }
            while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) != null);
        }
        _parser.Eat(TokenType.RightBrace);
        return new InitializerListExpressionNode(openingBrace, values);
    }

    internal ExpressionNode ParseExpression() => ParseAssignmentExpression();

    private ExpressionNode ParseAssignmentExpression()
    {
        var left = ParseEqualityExpression();
        if (_parser.Current.Type == TokenType.Assignment)
        {
            var operatorToken = _parser.Current; // Get token before eating
            _parser.Eat(TokenType.Assignment);
            var right = ParseAssignmentExpression();
            if (left is VariableExpressionNode or MemberAccessExpressionNode or UnaryExpressionNode) return new AssignmentExpressionNode(left, right);

            _parser.ReportError($"The left-hand side of an assignment must be a variable, property or indexer.", operatorToken);
            return left; // Return the invalid left-hand side to allow parsing to continue.
        }
        return left;
    }

    private ExpressionNode ParseEqualityExpression()
    {
        var left = ParseRelationalExpression();
        while (_parser.Current.Type is TokenType.DoubleEquals or TokenType.NotEquals)
        {
            var op = _parser.Current; _parser.AdvancePosition(1);
            var right = ParseRelationalExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseRelationalExpression()
    {
        var left = ParseAdditiveExpression();
        while (_parser.Current.Type is TokenType.LessThan or TokenType.GreaterThan)
        {
            var op = _parser.Current; _parser.AdvancePosition(1);
            var right = ParseAdditiveExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseAdditiveExpression()
    {
        var left = ParseMultiplicativeExpression();
        while (_parser.Current.Type is TokenType.Plus or TokenType.Minus)
        {
            var op = _parser.Current; _parser.AdvancePosition(1);
            var right = ParseMultiplicativeExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseMultiplicativeExpression()
    {
        var left = ParseUnaryExpression();
        while (_parser.Current.Type is TokenType.Star or TokenType.Slash)
        {
            var op = _parser.Current; _parser.AdvancePosition(1);
            var right = ParseUnaryExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseUnaryExpression()
    {
        if (_parser.Current.Type == TokenType.Keyword && _parser.Current.Value == "new")
        {
            return ParseNewExpression();
        }
        if (_parser.Current.Type is TokenType.Minus or TokenType.Plus or TokenType.Star or TokenType.Ampersand)
        {
            var op = _parser.Current; _parser.AdvancePosition(1);
            return new UnaryExpressionNode(op, ParseUnaryExpression());
        }
        return ParsePostfixExpression();
    }

    private NewExpressionNode ParseNewExpression()
    {
        _parser.Eat(TokenType.Keyword); // new
        var typeToken = _parser.Eat(TokenType.Identifier);

        _parser.Eat(TokenType.LeftParen);
        var arguments = new List<ExpressionNode>();
        if (_parser.Current.Type != TokenType.RightParen)
        {
            do { arguments.Add(ParseExpression()); }
            while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) != null);
        }
        _parser.Eat(TokenType.RightParen);

        return new NewExpressionNode(typeToken, arguments);
    }

    private ExpressionNode ParsePostfixExpression()
    {
        var expr = ParsePrimaryExpression();
        while (true)
        {
            if (_parser.Current.Type == TokenType.LeftParen)
            {
                _parser.Eat(TokenType.LeftParen);
                var arguments = new List<ExpressionNode>();
                if (_parser.Current.Type != TokenType.RightParen)
                {
                    do { arguments.Add(ParseExpression()); }
                    while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) != null);
                }
                _parser.Eat(TokenType.RightParen);
                expr = new CallExpressionNode(expr, arguments);
            }
            else if (_parser.Current.Type is TokenType.Dot or TokenType.Arrow)
            {
                var op = _parser.Current; _parser.AdvancePosition(1);
                var member = _parser.Eat(TokenType.Identifier);
                expr = new MemberAccessExpressionNode(expr, op, member);
            }
            else if (_parser.Current.Type == TokenType.DoubleColon)
            {
                _parser.Eat(TokenType.DoubleColon);
                var member = _parser.Eat(TokenType.Identifier);
                expr = new QualifiedAccessExpressionNode(expr, member);
            }
            else { break; }
        }
        return expr;
    }

    private ExpressionNode ParsePrimaryExpression()
    {
        var token = _parser.Current;
        switch (token.Type)
        {
            case TokenType.IntegerLiteral:
                _parser.Eat(TokenType.IntegerLiteral);
                if (int.TryParse(token.Value, out int v)) return new IntegerLiteralNode(token, v);
                _parser.ReportError($"Could not parse int: {token.Value}", token);
                return new IntegerLiteralNode(token, 0);

            case TokenType.HexLiteral:
                _parser.Eat(TokenType.HexLiteral);
                var hex = token.Value.StartsWith("0x") ? token.Value.Substring(2) : token.Value;
                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vHex)) return new IntegerLiteralNode(token, vHex);
                _parser.ReportError($"Could not parse hex: {token.Value}", token);
                return new IntegerLiteralNode(token, 0);

            case TokenType.StringLiteral:
                _parser.Eat(TokenType.StringLiteral);
                return new StringLiteralNode(token, token.Value, $"str{_stringLabelCounter++}");

            case TokenType.Identifier:
                return new VariableExpressionNode(_parser.Eat(TokenType.Identifier));

            case TokenType.LeftParen:
                _parser.Eat(TokenType.LeftParen);
                var expr = ParseExpression();
                _parser.Eat(TokenType.RightParen);
                return expr;
        }

        _parser.ReportError($"Unexpected token in expression: '{token.Type}'", token);
        // Advance past the bad token to prevent an infinite loop and return a dummy node.
        _parser.AdvancePosition(1);
        return new IntegerLiteralNode(token, 0);
    }
}