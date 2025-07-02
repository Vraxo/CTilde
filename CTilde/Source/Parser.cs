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
    private string? _currentNamespace;

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
            else if (Current.Type == TokenType.Keyword && Current.Value == "namespace")
            {
                ParseNamespaceDirective();
            }
            else if (Current.Type == TokenType.Keyword && Current.Value == "struct")
            {
                structs.Add(ParseStructDefinition(functions));
            }
            else
            {
                functions.Add(ParseGlobalFunction());
            }
        }

        var programNode = new ProgramNode(imports, structs, functions);
        SetParents(programNode);
        return programNode;
    }

    private void ParseNamespaceDirective()
    {
        Eat(TokenType.Keyword); // namespace
        var name = Eat(TokenType.Identifier);
        Eat(TokenType.Semicolon);
        _currentNamespace = name.Value;
    }

    private ImportDirectiveNode ParseImportDirective()
    {
        Eat(TokenType.Hash);
        var keyword = Eat(TokenType.Identifier);
        if (keyword.Value != "import") throw new InvalidOperationException($"Expected 'import' after '#' but got '{keyword.Value}'");
        var libNameToken = Eat(TokenType.StringLiteral);
        return new ImportDirectiveNode(libNameToken.Value);
    }

    private StructDefinitionNode ParseStructDefinition(List<FunctionDeclarationNode> programFunctions)
    {
        Eat(TokenType.Keyword); // struct
        var structName = Eat(TokenType.Identifier);
        Eat(TokenType.LeftBrace);

        var members = new List<MemberVariableNode>();
        var currentAccess = AccessSpecifier.Private; // Default for struct is private

        while (Current.Type != TokenType.RightBrace)
        {
            if (Current.Type == TokenType.Keyword && (Current.Value == "public" || Current.Value == "private"))
            {
                currentAccess = (Current.Value == "public") ? AccessSpecifier.Public : AccessSpecifier.Private;
                Eat(TokenType.Keyword);
                Eat(TokenType.Colon);
                continue;
            }

            var (type, pointerLevel) = ParseType();
            var name = Eat(TokenType.Identifier);

            if (Current.Type == TokenType.LeftParen)
            {
                // This is a method definition.
                var methodNode = FinishParsingFunction(type, pointerLevel, name.Value, structName.Value, currentAccess, _currentNamespace);
                programFunctions.Add(methodNode);
            }
            else
            {
                // This is a member variable.
                members.Add(new MemberVariableNode(type, pointerLevel, name, currentAccess));
                Eat(TokenType.Semicolon);
            }
        }

        Eat(TokenType.RightBrace);
        Eat(TokenType.Semicolon);
        return new StructDefinitionNode(structName.Value, _currentNamespace, members);
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
        string? namespaceQualifier = null;

        if (Current.Type == TokenType.Identifier && Peek(1).Type == TokenType.DoubleColon)
        {
            namespaceQualifier = Eat(TokenType.Identifier).Value;
            Eat(TokenType.DoubleColon);
        }

        if (Current.Type == TokenType.Keyword && Current.Value == "struct")
        {
            Eat(TokenType.Keyword); // struct
            typeToken = Eat(TokenType.Identifier);
        }
        else
        {
            typeToken = Current;
            _position++;
        }

        // Only qualify if it's an identifier (a UDT), not a keyword (int, char)
        if (typeToken.Type == TokenType.Identifier)
        {
            if (namespaceQualifier != null)
            {
                typeToken = new Token(typeToken.Type, $"{namespaceQualifier}::{typeToken.Value}");
            }
            else if (_currentNamespace != null)
            {
                // Automatically qualify unqualified types with current namespace, unless it's a call to a global function
                var nextToken = Current;
                if (nextToken.Type != TokenType.LeftParen) // Simple heuristic: if not a call, it's likely a type
                {
                    typeToken = new Token(typeToken.Type, $"{_currentNamespace}::{typeToken.Value}");
                }
            }
        }

        int pointerLevel = 0;
        while (Current.Type == TokenType.Star)
        {
            Eat(TokenType.Star);
            pointerLevel++;
        }
        return (typeToken, pointerLevel);
    }

    private FunctionDeclarationNode ParseGlobalFunction()
    {
        var (returnType, returnPointerLevel) = ParseType();
        var identifier = Eat(TokenType.Identifier);
        // Global functions are implicitly public
        return FinishParsingFunction(returnType, returnPointerLevel, identifier.Value, null, AccessSpecifier.Public, _currentNamespace);
    }

    private FunctionDeclarationNode FinishParsingFunction(Token returnType, int returnPointerLevel, string name, string? ownerStructName, AccessSpecifier accessLevel, string? namespaceName)
    {
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

        // If this is a method, prepend the implicit 'this' pointer to the parameter list.
        if (ownerStructName != null)
        {
            var thisTypeTokenValue = namespaceName != null ? $"{namespaceName}::{ownerStructName}" : ownerStructName;
            var thisParam = new ParameterNode(new Token(TokenType.Identifier, thisTypeTokenValue), 1, new Token(TokenType.Identifier, "this"));
            parameters.Insert(0, thisParam);
        }

        StatementNode? body = null;
        if (Current.Type == TokenType.LeftBrace)
        {
            body = ParseBlockStatement();
        }
        else
        {
            Eat(TokenType.Semicolon); // For function prototypes
        }

        return new FunctionDeclarationNode(returnType, returnPointerLevel, name, parameters, body, ownerStructName, accessLevel, namespaceName);
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

        // Disambiguate between a declaration and an expression statement
        bool isDeclaration = false;
        if (Current.Type == TokenType.Identifier)
        {
            var lookahead1 = Peek(1).Type;
            if (lookahead1 == TokenType.Identifier || lookahead1 == TokenType.Star)
            {
                // Case: `TypeName varName;` or `TypeName* pVar;`
                isDeclaration = true;
            }
            else if (lookahead1 == TokenType.DoubleColon && Peek(2).Type == TokenType.Identifier)
            {
                // Case: `ns::TypeName ...`
                var lookahead3 = Peek(3).Type;
                if (lookahead3 == TokenType.Identifier || lookahead3 == TokenType.Star)
                {
                    // `ns::TypeName varName;` or `ns::TypeName* pVar;`
                    isDeclaration = true;
                }
            }
        }

        if (isDeclaration)
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
        return ParsePostfixExpression();
    }

    private ExpressionNode ParsePostfixExpression()
    {
        var expr = ParsePrimaryExpression();

        while (true)
        {
            if (Current.Type == TokenType.LeftParen) // Call
            {
                Eat(TokenType.LeftParen);
                var arguments = new List<ExpressionNode>();
                if (Current.Type != TokenType.RightParen)
                {
                    do { arguments.Add(ParseExpression()); }
                    while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
                }
                Eat(TokenType.RightParen);
                expr = new CallExpressionNode(expr, arguments);
            }
            else if (Current.Type is TokenType.Dot or TokenType.Arrow) // Member access
            {
                var op = Current;
                _position++;
                var member = Eat(TokenType.Identifier);
                expr = new MemberAccessExpressionNode(expr, op, member);
            }
            else
            {
                break;
            }
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
        if (Current.Type == TokenType.Identifier && Peek(1).Type == TokenType.DoubleColon)
        {
            var ns = Eat(TokenType.Identifier);
            Eat(TokenType.DoubleColon);
            var member = Eat(TokenType.Identifier);
            return new QualifiedAccessExpressionNode(ns, member);
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