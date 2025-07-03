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
    private readonly List<ImportDirectiveNode> _imports = new();

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

    public List<ImportDirectiveNode> GetImports() => _imports;

    public CompilationUnitNode Parse(string filePath)
    {
        var usings = new List<UsingDirectiveNode>();
        var structs = new List<StructDefinitionNode>();
        var functions = new List<FunctionDeclarationNode>();
        var enums = new List<EnumDefinitionNode>();

        while (Current.Type != TokenType.Unknown)
        {
            if (Current.Type == TokenType.Hash)
            {
                var hashKeyword = Peek(1);
                if (hashKeyword.Type == TokenType.Identifier && hashKeyword.Value == "import")
                {
                    _imports.Add(ParseImportDirective());
                }
                else if (hashKeyword.Type == TokenType.Identifier && hashKeyword.Value == "include")
                {
                    ParseIncludeDirective(); // Handle and skip #include
                }
                else
                {
                    throw new InvalidOperationException($"Unexpected directive after #: '{hashKeyword.Value}'");
                }
            }
            else if (Current.Type == TokenType.Keyword && Current.Value == "using")
            {
                usings.Add(ParseUsingDirective());
            }
            else if (Current.Type == TokenType.Keyword && Current.Value == "namespace")
            {
                ParseNamespaceDirective();
            }
            else if (Current.Type == TokenType.Keyword && Current.Value == "struct")
            {
                structs.Add(ParseStructDefinition(functions));
            }
            else if (Current.Type == TokenType.Keyword && Current.Value == "enum")
            {
                enums.Add(ParseEnumDefinition());
            }
            else
            {
                functions.Add(ParseGlobalFunction());
            }
        }

        var unitNode = new CompilationUnitNode(filePath, usings, structs, functions, enums);
        SetParents(unitNode);
        return unitNode;
    }

    private UsingDirectiveNode ParseUsingDirective()
    {
        Eat(TokenType.Keyword); // using
        var firstIdentifier = Eat(TokenType.Identifier);
        string namespaceName;
        string? alias = null;

        if (Current.Type == TokenType.Assignment) // This is 'using alias = namespace;'
        {
            alias = firstIdentifier.Value; // 'rl' in 'using rl = raylib;'
            Eat(TokenType.Assignment);
            namespaceName = Eat(TokenType.Identifier).Value; // 'raylib' in 'using rl = raylib;'
        }
        else // This is 'using namespace;'
        {
            namespaceName = firstIdentifier.Value; // 'raylib' in 'using raylib;'
        }

        Eat(TokenType.Semicolon);
        return new UsingDirectiveNode(namespaceName, alias);
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
        Eat(TokenType.Identifier); // import
        var libNameToken = Eat(TokenType.StringLiteral);
        return new ImportDirectiveNode(libNameToken.Value);
    }

    private void ParseIncludeDirective()
    {
        Eat(TokenType.Hash);
        Eat(TokenType.Identifier); // include
        Eat(TokenType.StringLiteral); // "filename"
        // No AST node for include, as it's handled by the preprocessor
    }

    private EnumDefinitionNode ParseEnumDefinition()
    {
        Eat(TokenType.Keyword); // enum
        var enumName = Eat(TokenType.Identifier);
        Eat(TokenType.LeftBrace);

        var members = new List<EnumMemberNode>();
        int currentValue = 0; // Default enum value starts at 0 and increments

        while (Current.Type != TokenType.RightBrace)
        {
            var memberName = Eat(TokenType.Identifier);
            if (Current.Type == TokenType.Assignment)
            {
                Eat(TokenType.Assignment);
                var valueToken = Eat(TokenType.IntegerLiteral);
                if (!int.TryParse(valueToken.Value, out currentValue))
                {
                    throw new InvalidOperationException($"Invalid integer value for enum member '{memberName.Value}': '{valueToken.Value}'");
                }
            }
            members.Add(new EnumMemberNode(memberName, currentValue));
            currentValue++; // Increment for next default value

            if (Current.Type == TokenType.Comma)
            {
                Eat(TokenType.Comma);
            }
            else if (Current.Type != TokenType.RightBrace)
            {
                throw new InvalidOperationException($"Expected ',' or '}}' after enum member '{memberName.Value}'");
            }
        }

        Eat(TokenType.RightBrace);
        Eat(TokenType.Semicolon);
        return new EnumDefinitionNode(enumName.Value, _currentNamespace, members);
    }

    private StructDefinitionNode ParseStructDefinition(List<FunctionDeclarationNode> programFunctions)
    {
        Eat(TokenType.Keyword); // struct
        var structName = Eat(TokenType.Identifier);

        string? baseStructName = null;
        if (Current.Type == TokenType.Colon)
        {
            Eat(TokenType.Colon);
            baseStructName = Eat(TokenType.Identifier).Value;
        }

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

            bool isConst = false;
            bool isVirtual = false;
            bool isOverride = false;

            if (Current.Type == TokenType.Keyword && Current.Value == "const")
            {
                isConst = true;
                Eat(TokenType.Keyword); // Eat 'const'
            }

            if (Current.Type == TokenType.Keyword && Current.Value == "virtual")
            {
                isVirtual = true;
                Eat(TokenType.Keyword);
            }
            else if (Current.Type == TokenType.Keyword && Current.Value == "override")
            {
                isOverride = true;
                Eat(TokenType.Keyword);
            }

            if (isVirtual && isOverride) throw new InvalidOperationException("A method cannot be both 'virtual' and 'override'.");


            var (type, pointerLevel) = ParseType();
            var name = Eat(TokenType.Identifier);

            if (Current.Type == TokenType.LeftParen)
            {
                // This is a method definition.
                var methodNode = FinishParsingFunction(type, pointerLevel, name.Value, structName.Value, currentAccess, isVirtual, isOverride, _currentNamespace);
                programFunctions.Add(methodNode);
            }
            else
            {
                // This is a member variable.
                if (isVirtual || isOverride) throw new InvalidOperationException("Only methods can be marked 'virtual' or 'override'.");
                members.Add(new MemberVariableNode(isConst, type, pointerLevel, name, currentAccess)); // Pass isConst
                Eat(TokenType.Semicolon);
            }
        }

        Eat(TokenType.RightBrace);
        Eat(TokenType.Semicolon);
        return new StructDefinitionNode(structName.Value, baseStructName, _currentNamespace, members);
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
        // Global functions are implicitly public and never virtual/override
        return FinishParsingFunction(returnType, returnPointerLevel, identifier.Value, null, AccessSpecifier.Public, false, false, _currentNamespace);
    }

    private FunctionDeclarationNode FinishParsingFunction(Token returnType, int returnPointerLevel, string name, string? ownerStructName, AccessSpecifier accessLevel, bool isVirtual, bool isOverride, string? namespaceName)
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

        return new FunctionDeclarationNode(returnType, returnPointerLevel, name, parameters, body, ownerStructName, accessLevel, isVirtual, isOverride, namespaceName);
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
                case "const": // Handle 'const' keyword for declarations
                case "int":
                case "char":
                case "struct":
                    return ParseDeclarationStatement();
            }
        }

        // Disambiguate between a declaration and an expression statement
        bool isDeclaration = false;
        // Check for 'const' keyword, type keyword, or identifier followed by variable name
        if (Current.Type == TokenType.Identifier || (Current.Type == TokenType.Keyword && (Current.Value == "const" || Current.Value == "int" || Current.Value == "char" || Current.Value == "struct")))
        {
            int tempPos = _position; // Save current position for backtracking
            try
            {
                if (Current.Type == TokenType.Keyword && Current.Value == "const")
                {
                    _position++; // Consume 'const'
                }

                Token potentialTypeToken = Current;
                if (potentialTypeToken.Type == TokenType.Identifier || (potentialTypeToken.Type == TokenType.Keyword && (potentialTypeToken.Value == "int" || potentialTypeToken.Value == "char" || potentialTypeToken.Value == "struct")))
                {
                    _position++; // Consume potential type (Identifier or Keyword like 'int')
                    while (Current.Type == TokenType.Star) _position++; // Consume pointers

                    if (Current.Type == TokenType.Identifier) // Next must be the variable name
                    {
                        isDeclaration = true;
                    }
                    else if (potentialTypeToken.Type == TokenType.Identifier && Peek(0).Type == TokenType.DoubleColon && Peek(1).Type == TokenType.Identifier)
                    {
                        // Case for `ns::TypeName varName`
                        _position++; // Consume ::
                        _position++; // Consume TypeName
                        while (Current.Type == TokenType.Star) _position++; // Consume pointers
                        if (Current.Type == TokenType.Identifier) // Next must be variable name
                        {
                            isDeclaration = true;
                        }
                    }
                }
            }
            finally
            {
                _position = tempPos; // Always rewind position
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
        bool isConst = false;
        if (Current.Type == TokenType.Keyword && Current.Value == "const")
        {
            isConst = true;
            Eat(TokenType.Keyword); // Eat 'const'
        }

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
        else if (isConst)
        {
            // const variables must be initialized
            throw new InvalidOperationException($"Constant variable '{identifier.Value}' must be initialized.");
        }
        Eat(TokenType.Semicolon);
        return new DeclarationStatementNode(isConst, typeToken, pointerLevel, identifier, initializer);
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
            else if (Current.Type == TokenType.DoubleColon) // Qualified access
            {
                Eat(TokenType.DoubleColon);
                var member = Eat(TokenType.Identifier);
                expr = new QualifiedAccessExpressionNode(expr, member);
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