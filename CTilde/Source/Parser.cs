using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
        throw new InvalidOperationException($"Expected token {expectedType} but got {currentToken.Type} ('{currentToken.Value}') at position {_position}");
    }

    public ProgramNode Parse()
    {
        var imports = new List<ImportDirectiveNode>();
        var structs = new List<StructDefinitionNode>();
        var functions = new List<FunctionDeclarationNode>();

        while (Current.Type != TokenType.Unknown)
        {
            if (Current.Type == TokenType.Hash)
            {
                imports.Add(ParseImportDirective());
            }
            // `struct Identifier {` is a struct definition
            else if (Current.Type == TokenType.Keyword && Current.Value == "struct" && Peek(2).Type == TokenType.LeftBrace)
            {
                structs.Add(ParseStructDefinition());
            }
            // Otherwise, assume it's a function declaration or a global variable (not supported)
            else
            {
                functions.Add(ParseFunction());
            }
        }

        var programNode = new ProgramNode(imports, structs, functions);
        SetParents(programNode);
        return programNode;
    }

    private ImportDirectiveNode ParseImportDirective()
    {
        Eat(TokenType.Hash);
        var keyword = Eat(TokenType.Identifier);
        if (keyword.Value != "import") throw new InvalidOperationException($"Expected 'import' after '#' but got '{keyword.Value}'");
        var libNameToken = Eat(TokenType.StringLiteral);
        return new ImportDirectiveNode(libNameToken.Value);
    }

    private StructDefinitionNode ParseStructDefinition()
    {
        Eat(TokenType.Keyword); // struct
        var name = Eat(TokenType.Identifier);
        Eat(TokenType.LeftBrace);

        var members = new List<ParameterNode>();
        while (Current.Type != TokenType.RightBrace)
        {
            var (memberType, pointerLevel) = ParseType();
            var memberName = Eat(TokenType.Identifier);
            members.Add(new ParameterNode(memberType, pointerLevel, memberName));
            Eat(TokenType.Semicolon);
        }

        Eat(TokenType.RightBrace);
        Eat(TokenType.Semicolon);
        return new StructDefinitionNode(name.Value, members);
    }

    private void SetParents(AstNode node)
    {
        foreach (var property in node.GetType().GetProperties())
        {
            if (property.CanWrite && property.Name == "Parent") continue;
            if (property.GetValue(node) is AstNode child)
            {
                var parentProp = child.GetType().GetProperty("Parent");
                if (parentProp != null && parentProp.CanWrite) parentProp.SetValue(child, node);
                SetParents(child);
            }
            else if (property.GetValue(node) is IEnumerable<AstNode> children)
            {
                foreach (var c in children.ToList()) // ToList to avoid mutation issues
                {
                    var parentProp = c.GetType().GetProperty("Parent");
                    if (parentProp != null && parentProp.CanWrite) parentProp.SetValue(c, node);
                    SetParents(c);
                }
            }
        }
    }

    private (Token type, int pointerLevel) ParseType()
    {
        Token typeToken;
        if (Current.Type == TokenType.Keyword && Current.Value == "struct")
        {
            Eat(TokenType.Keyword); // struct
            typeToken = Eat(TokenType.Identifier);
        }
        else
        {
            // A type is a keyword (int, char) or an identifier (struct name)
            typeToken = Current;
            _position++;
        }

        int pointerLevel = 0;
        while (Current.Type == TokenType.Star)
        {
            Eat(TokenType.Star);
            pointerLevel++;
        }
        return (typeToken, pointerLevel);
    }

    private FunctionDeclarationNode ParseFunction()
    {
        var (returnType, returnPointerLevel) = ParseType();
        var identifier = Eat(TokenType.Identifier);
        Eat(TokenType.LeftParen);

        var parameters = new List<ParameterNode>();
        if (Current.Type != TokenType.RightParen)
        {
            do
            {
                var (paramType, paramPointerLevel) = ParseType();
                var paramName = Eat(TokenType.Identifier);
                parameters.Add(new ParameterNode(paramType, paramPointerLevel, paramName));
            } while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
        }

        Eat(TokenType.RightParen);

        StatementNode? body = null;
        if (Current.Type == TokenType.LeftBrace) body = ParseBlockStatement();
        else Eat(TokenType.Semicolon);

        return new FunctionDeclarationNode(returnType, returnPointerLevel, identifier.Value, parameters, body);
    }

    private BlockStatementNode ParseBlockStatement()
    {
        Eat(TokenType.LeftBrace);
        var statements = new List<StatementNode>();
        while (Current.Type != TokenType.RightBrace) statements.Add(ParseStatement());
        Eat(TokenType.RightBrace);
        return new BlockStatementNode(statements);
    }

    private StatementNode ParseStatement()
    {
        if (Current.Type == TokenType.Keyword)
        {
            switch (Current.Value)
            {
                case "return": return ParseReturnStatement();
                case "if": return ParseIfStatement();
                case "while": return ParseWhileStatement();
                case "int":
                case "char":
                case "struct":
                    return ParseDeclarationStatement();
            }
        }

        // `TypeName varName;` is a declaration.
        if (Current.Type == TokenType.Identifier && (Peek(1).Type == TokenType.Identifier || Peek(1).Type == TokenType.Star))
        {
            return ParseDeclarationStatement();
        }

        if (Current.Type == TokenType.LeftBrace) return ParseBlockStatement();

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
        var (typeToken, pointerLevel) = ParseType();
        var identifier = Eat(TokenType.Identifier);
        ExpressionNode? initializer = null;
        if (Current.Type == TokenType.Assignment)
        {
            Eat(TokenType.Assignment);
            if (Current.Type == TokenType.LeftBrace)
            {
                initializer = ParseInitializerListExpression();
            }
            else
            {
                initializer = ParseExpression();
            }
        }
        Eat(TokenType.Semicolon);
        return new DeclarationStatementNode(typeToken, pointerLevel, identifier, initializer);
    }

    private ExpressionNode ParseInitializerListExpression()
    {
        Eat(TokenType.LeftBrace);
        var values = new List<ExpressionNode>();
        if (Current.Type != TokenType.RightBrace)
        {
            do
            {
                values.Add(ParseExpression());
            } while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
        }
        Eat(TokenType.RightBrace);
        return new InitializerListExpressionNode(values);
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
        if (Current.Type != TokenType.Semicolon) expression = ParseExpression();
        Eat(TokenType.Semicolon);
        return new ReturnStatementNode(expression);
    }

    private ExpressionNode ParseExpression() => ParseAssignmentExpression();

    private ExpressionNode ParseAssignmentExpression()
    {
        var left = ParseEqualityExpression();
        if (Current.Type == TokenType.Assignment)
        {
            Eat(TokenType.Assignment);
            var right = ParseAssignmentExpression();
            if (left is VariableExpressionNode or MemberAccessExpressionNode or UnaryExpressionNode) return new AssignmentExpressionNode(left, right);
            throw new InvalidOperationException($"Invalid assignment target: {left.GetType().Name}");
        }
        return left;
    }

    private ExpressionNode ParseEqualityExpression()
    {
        var left = ParseRelationalExpression();
        while (Current.Type is TokenType.DoubleEquals or TokenType.NotEquals)
        {
            var op = Current; _position++;
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
            var op = Current; _position++;
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
            var op = Current; _position++;
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
            var op = Current; _position++;
            var right = ParseUnaryExpression();
            left = new BinaryExpressionNode(left, op, right);
        }
        return left;
    }

    private ExpressionNode ParseUnaryExpression()
    {
        if (Current.Type is TokenType.Minus or TokenType.Plus or TokenType.Star or TokenType.Ampersand)
        {
            var op = Current; _position++;
            return new UnaryExpressionNode(op, ParseUnaryExpression());
        }
        return ParseMemberAccessExpression();
    }

    private ExpressionNode ParseMemberAccessExpression()
    {
        var left = ParseCallExpression();
        while (Current.Type == TokenType.Dot)
        {
            Eat(TokenType.Dot);
            var member = Eat(TokenType.Identifier);
            left = new MemberAccessExpressionNode(left, member);
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
                    do { arguments.Add(ParseExpression()); }
                    while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
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
            if (int.TryParse(token.Value, out int v)) return new IntegerLiteralNode(v);
            throw new InvalidOperationException($"Could not parse int: {token.Value}");
        }
        if (Current.Type == TokenType.HexLiteral)
        {
            var token = Eat(TokenType.HexLiteral);
            var hex = token.Value.StartsWith("0x") ? token.Value.Substring(2) : token.Value;
            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return new IntegerLiteralNode(v);
            throw new InvalidOperationException($"Could not parse hex: {token.Value}");
        }
        if (Current.Type == TokenType.StringLiteral)
        {
            var token = Eat(TokenType.StringLiteral);
            return new StringLiteralNode(token.Value, $"str{_stringLabelCounter++}");
        }
        if (Current.Type == TokenType.Identifier) return new VariableExpressionNode(Eat(TokenType.Identifier));
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