using System;
using System.Collections.Generic;
using System.Globalization;

namespace CTilde;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _position;
    private int _stringLabelCounter;

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
        throw new InvalidOperationException($"Expected token {expectedType} but got {currentToken.Type} ('{currentToken.Value}')");
    }

    public ProgramNode Parse()
    {
        var imports = new List<ImportDirectiveNode>();
        var functions = new List<FunctionDeclarationNode>();

        while (Current.Type != TokenType.Unknown)
        {
            if (Current.Type == TokenType.Hash)
            {
                imports.Add(ParseImportDirective());
            }
            else
            {
                functions.Add(ParseFunction());
            }
        }

        var programNode = new ProgramNode(imports, functions);
        SetParents(programNode);
        return programNode;
    }

    private ImportDirectiveNode ParseImportDirective()
    {
        Eat(TokenType.Hash);
        var keyword = Eat(TokenType.Identifier);
        if (keyword.Value != "import")
        {
            throw new InvalidOperationException($"Expected 'import' after '#' but got '{keyword.Value}'");
        }
        var libNameToken = Eat(TokenType.StringLiteral);
        return new ImportDirectiveNode(libNameToken.Value);
    }

    private void SetParents(AstNode node)
    {
        foreach (var property in node.GetType().GetProperties())
        {
            if (property.CanWrite && property.Name == "Parent") continue;

            if (property.GetValue(node) is AstNode child)
            {
                var parentProp = child.GetType().GetProperty("Parent");
                if (parentProp != null && parentProp.CanWrite)
                {
                    parentProp.SetValue(child, node);
                }
                SetParents(child);
            }
            else if (property.GetValue(node) is IEnumerable<AstNode> children)
            {
                foreach (var c in children)
                {
                    var parentProp = c.GetType().GetProperty("Parent");
                    if (parentProp != null && parentProp.CanWrite)
                    {
                        parentProp.SetValue(c, node);
                    }
                    SetParents(c);
                }
            }
        }
    }

    private FunctionDeclarationNode ParseFunction()
    {
        var returnType = Eat(TokenType.Keyword);
        if (returnType.Value != "int" && returnType.Value != "void")
        {
            throw new InvalidOperationException($"Unsupported function return type: {returnType.Value}");
        }

        var identifier = Eat(TokenType.Identifier);
        Eat(TokenType.LeftParen);

        var parameters = new List<ParameterNode>();
        if (Current.Type != TokenType.RightParen)
        {
            do
            {
                var paramType = Eat(TokenType.Keyword);
                var paramName = Eat(TokenType.Identifier);
                parameters.Add(new ParameterNode(paramType, paramName));
            } while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
        }

        Eat(TokenType.RightParen);

        StatementNode? body = null;
        if (Current.Type == TokenType.LeftBrace)
        {
            body = ParseBlockStatement();
        }
        else
        {
            Eat(TokenType.Semicolon); // It's a prototype
        }

        return new FunctionDeclarationNode(returnType, identifier.Value, parameters, body);
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
            switch (Current.Value)
            {
                case "return":
                    return ParseReturnStatement();
                case "if":
                    return ParseIfStatement();
                case "while":
                    return ParseWhileStatement();
                case "int":
                    if (Peek(1).Type == TokenType.Identifier && Peek(2).Type != TokenType.LeftParen)
                    {
                        return ParseDeclarationStatement();
                    }
                    break;
            }
        }

        if (Current.Type == TokenType.Keyword && (Current.Value == "int" || Current.Value == "void"))
        {
            throw new InvalidOperationException($"Function definitions/prototypes are not allowed inside other functions.");
        }


        if (Current.Type == TokenType.LeftBrace)
        {
            return ParseBlockStatement();
        }

        var expression = ParseExpression();
        Eat(TokenType.Semicolon);
        return new ExpressionStatementNode(expression);
    }

    private IfStatementNode ParseIfStatement()
    {
        Eat(TokenType.Keyword);
        Eat(TokenType.LeftParen);
        var condition = ParseExpression();
        Eat(TokenType.RightParen);

        var thenBody = ParseStatement();
        StatementNode? elseBody = null;

        if (Current.Type == TokenType.Keyword && Current.Value == "else")
        {
            Eat(TokenType.Keyword);
            elseBody = ParseStatement();
        }

        return new IfStatementNode(condition, thenBody, elseBody);
    }

    private StatementNode ParseDeclarationStatement()
    {
        Eat(TokenType.Keyword);
        var identifier = Eat(TokenType.Identifier);
        ExpressionNode? initializer = null;

        if (Current.Type == TokenType.Assignment)
        {
            Eat(TokenType.Assignment);
            initializer = ParseExpression();
        }

        Eat(TokenType.Semicolon);
        return new DeclarationStatementNode(identifier, initializer);
    }

    private WhileStatementNode ParseWhileStatement()
    {
        Eat(TokenType.Keyword);
        Eat(TokenType.LeftParen);
        var condition = ParseExpression();
        Eat(TokenType.RightParen);
        var body = ParseStatement();
        return new WhileStatementNode(condition, body);
    }

    private ReturnStatementNode ParseReturnStatement()
    {
        Eat(TokenType.Keyword);
        ExpressionNode? expression = null;
        if (Current.Type != TokenType.Semicolon)
        {
            expression = ParseExpression();
        }
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
            var right = ParseAssignmentExpression();

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
        var left = ParseUnaryExpression();
        while (Current.Type is TokenType.Star or TokenType.Slash)
        {
            var op = Current;
            _position++;
            var right = ParseUnaryExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseUnaryExpression()
    {
        if (Current.Type is TokenType.Minus or TokenType.Plus)
        {
            var op = Current;
            _position++;
            var right = ParseUnaryExpression();
            return new UnaryExpressionNode(op, right);
        }
        return ParseCallExpression();
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
                    do
                    {
                        arguments.Add(ParseExpression());
                    } while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
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

        if (Current.Type == TokenType.HexLiteral)
        {
            var token = Eat(TokenType.HexLiteral);
            var hexString = token.Value.StartsWith("0x") ? token.Value.Substring(2) : token.Value;
            if (int.TryParse(hexString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            {
                return new IntegerLiteralNode(value);
            }
            throw new InvalidOperationException($"Could not parse hex literal: {token.Value}");
        }

        if (Current.Type == TokenType.StringLiteral)
        {
            var token = Eat(TokenType.StringLiteral);
            string label = $"str{_stringLabelCounter++}";
            return new StringLiteralNode(token.Value, label);
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