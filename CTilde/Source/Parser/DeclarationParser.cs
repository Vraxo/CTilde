using System.Collections.Generic;
using System.Linq;

namespace CTilde;

internal class DeclarationParser
{
    private readonly Parser _parser;
    private readonly StatementParser _statementParser;
    private readonly ExpressionParser _expressionParser;

    internal DeclarationParser(Parser parser, StatementParser statementParser, ExpressionParser expressionParser)
    {
        _parser = parser;
        _statementParser = statementParser;
        _expressionParser = expressionParser;
    }

    internal UsingDirectiveNode ParseUsingDirective()
    {
        _parser.Eat(TokenType.Keyword); // using
        var firstIdentifier = _parser.Eat(TokenType.Identifier);
        string namespaceName;
        string? alias = null;

        if (_parser.Current.Type == TokenType.Assignment) // This is 'using alias = namespace;'
        {
            alias = firstIdentifier.Value; // 'rl' in 'using rl = raylib;'
            _parser.Eat(TokenType.Assignment);
            namespaceName = _parser.Eat(TokenType.Identifier).Value; // 'raylib' in 'using rl = raylib;'
        }
        else // This is 'using namespace;'
        {
            namespaceName = firstIdentifier.Value; // 'raylib' in 'using raylib;'
        }

        _parser.Eat(TokenType.Semicolon);
        return new UsingDirectiveNode(namespaceName, alias);
    }

    internal void ParseNamespaceDirective()
    {
        _parser.Eat(TokenType.Keyword); // namespace
        var name = _parser.Eat(TokenType.Identifier);
        _parser.Eat(TokenType.Semicolon);
        _parser._currentNamespace = name.Value;
    }

    internal ImportDirectiveNode ParseImportDirective()
    {
        _parser.Eat(TokenType.Hash);
        _parser.Eat(TokenType.Identifier); // import
        var libNameToken = _parser.Eat(TokenType.StringLiteral);
        return new ImportDirectiveNode(libNameToken.Value);
    }

    internal void ParseIncludeDirective()
    {
        _parser.Eat(TokenType.Hash);
        _parser.Eat(TokenType.Identifier); // include
        _parser.Eat(TokenType.StringLiteral); // "filename"
        // No AST node for include, as it's handled by the preprocessor
    }

    internal EnumDefinitionNode ParseEnumDefinition()
    {
        _parser.Eat(TokenType.Keyword); // enum
        var enumName = _parser.Eat(TokenType.Identifier);
        _parser.Eat(TokenType.LeftBrace);

        var members = new List<EnumMemberNode>();
        int currentValue = 0; // Default enum value starts at 0 and increments

        while (_parser.Current.Type != TokenType.RightBrace && _parser.Current.Type != TokenType.Unknown)
        {
            var memberName = _parser.Eat(TokenType.Identifier);
            if (_parser.Current.Type == TokenType.Assignment)
            {
                _parser.Eat(TokenType.Assignment);
                var valueToken = _parser.Eat(TokenType.IntegerLiteral);
                if (!int.TryParse(valueToken.Value, out currentValue))
                {
                    _parser.ReportError($"Invalid integer value for enum member '{memberName.Value}': '{valueToken.Value}'", valueToken);
                }
            }
            members.Add(new EnumMemberNode(memberName, currentValue));
            currentValue++; // Increment for next default value

            if (_parser.Current.Type == TokenType.Comma)
            {
                _parser.Eat(TokenType.Comma);
            }
            else if (_parser.Current.Type != TokenType.RightBrace)
            {
                _parser.ReportError($"Expected ',' or '}}' after enum member '{memberName.Value}'", _parser.Current);
                break;
            }
        }

        _parser.Eat(TokenType.RightBrace);
        _parser.Eat(TokenType.Semicolon);
        return new EnumDefinitionNode(enumName.Value, _parser._currentNamespace, members);
    }

    internal StructDefinitionNode ParseStructDefinition()
    {
        _parser.Eat(TokenType.Keyword); // struct
        var structName = _parser.Eat(TokenType.Identifier);

        var genericParameters = new List<Token>();
        if (_parser.Current.Type == TokenType.LessThan)
        {
            _parser.Eat(TokenType.LessThan);
            do { genericParameters.Add(_parser.Eat(TokenType.Identifier)); }
            while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) != null);
            _parser.Eat(TokenType.GreaterThan);
        }

        string? baseStructName = null;
        if (_parser.Current.Type == TokenType.Colon)
        {
            _parser.Eat(TokenType.Colon);
            baseStructName = _parser.Eat(TokenType.Identifier).Value;
        }

        _parser.Eat(TokenType.LeftBrace);

        var members = new List<MemberVariableNode>();
        var methods = new List<FunctionDeclarationNode>();
        var constructors = new List<ConstructorDeclarationNode>();
        var destructors = new List<DestructorDeclarationNode>();

        var currentAccess = AccessSpecifier.Private;

        while (_parser.Current.Type != TokenType.RightBrace && _parser.Current.Type != TokenType.Unknown)
        {
            if (_parser.Current.Type == TokenType.Keyword && (_parser.Current.Value == "public" || _parser.Current.Value == "private"))
            {
                currentAccess = (_parser.Current.Value == "public") ? AccessSpecifier.Public : AccessSpecifier.Private;
                _parser.Eat(TokenType.Keyword);
                _parser.Eat(TokenType.Colon);
                continue;
            }

            bool isConst = false;
            bool isVirtual = false;
            bool isOverride = false;
            var startToken = _parser.Current;

            if (_parser.Current.Type == TokenType.Keyword && _parser.Current.Value == "const")
            {
                isConst = true;
                _parser.Eat(TokenType.Keyword);
            }

            if (_parser.Current.Type == TokenType.Keyword && _parser.Current.Value == "virtual")
            {
                isVirtual = true;
                _parser.Eat(TokenType.Keyword);
            }
            else if (_parser.Current.Type == TokenType.Keyword && _parser.Current.Value == "override")
            {
                isOverride = true;
                _parser.Eat(TokenType.Keyword);
            }

            if (isVirtual && isOverride) _parser.ReportError("A method cannot be both 'virtual' and 'override'.", startToken);

            if (_parser.Current.Type == TokenType.Tilde)
            {
                destructors.Add(ParseDestructor(structName.Value, currentAccess, isVirtual));
                continue;
            }

            // Check for constructor (e.g. `List(...)` not `List<T>(...)`)
            if (_parser.Current.Type == TokenType.Identifier && _parser.Current.Value == structName.Value && _parser.Peek(1).Type == TokenType.LeftParen)
            {
                if (isVirtual || isOverride || isConst) _parser.ReportError("Constructors cannot be marked 'virtual', 'override', or 'const'.", startToken);
                constructors.Add(ParseConstructor(structName.Value, baseStructName, currentAccess));
                continue;
            }

            var type = _parser.ParseTypeNode();

            Token name;
            if (_parser.Current.Type == TokenType.Keyword && _parser.Current.Value == "operator")
            {
                _parser.Eat(TokenType.Keyword); // operator
                var opToken = _parser.Current;
                _parser.AdvancePosition(1);
                name = new Token(TokenType.Identifier, $"operator_{NameMangler.MangleOperator(opToken.Value)}", opToken.Line, opToken.Column);
            }
            else
            {
                name = _parser.Eat(TokenType.Identifier);
            }

            if (_parser.Current.Type == TokenType.LeftParen)
            {
                var methodNode = FinishParsingFunction(type, name.Value, structName.Value, currentAccess, isVirtual, isOverride, _parser._currentNamespace, true);
                methods.Add(methodNode);
            }
            else
            {
                if (isVirtual || isOverride) _parser.ReportError("Only methods can be marked 'virtual' or 'override'.", startToken);
                members.Add(new MemberVariableNode(isConst, type, name, currentAccess));
                _parser.Eat(TokenType.Semicolon);
            }
        }

        _parser.Eat(TokenType.RightBrace);
        _parser.Eat(TokenType.Semicolon);
        return new StructDefinitionNode(structName.Value, genericParameters, baseStructName, _parser._currentNamespace, members, methods, constructors, destructors);
    }


    private ConstructorDeclarationNode ParseConstructor(string ownerStructName, string? baseStructName, AccessSpecifier access)
    {
        var nameToken = _parser.Eat(TokenType.Identifier);
        var parameters = ParseParameterList(false);

        BaseInitializerNode? baseInitializer = null;
        if (_parser.Current.Type == TokenType.Colon)
        {
            if (baseStructName == null) _parser.ReportError($"Struct '{ownerStructName}' cannot have a base initializer because it does not inherit from another struct.", nameToken);
            _parser.Eat(TokenType.Colon);
            var baseName = _parser.Eat(TokenType.Identifier);
            // No error here, Eat will report if baseName.Value != baseStructName

            _parser.Eat(TokenType.LeftParen);
            var arguments = new List<ExpressionNode>();
            if (_parser.Current.Type != TokenType.RightParen)
            {
                do { arguments.Add(_expressionParser.ParseExpression()); }
                while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) != null);
            }
            _parser.Eat(TokenType.RightParen);
            baseInitializer = new BaseInitializerNode(arguments);
        }

        var body = _statementParser.ParseBlockStatement();
        return new ConstructorDeclarationNode(ownerStructName, _parser._currentNamespace, access, parameters, baseInitializer, body);
    }

    private DestructorDeclarationNode ParseDestructor(string ownerStructName, AccessSpecifier access, bool isVirtual)
    {
        _parser.Eat(TokenType.Tilde);
        var name = _parser.Eat(TokenType.Identifier);
        if (name.Value != ownerStructName) _parser.ReportError($"Destructor name '~{name.Value}' must match struct name '{ownerStructName}'.", name);

        _parser.Eat(TokenType.LeftParen);
        _parser.Eat(TokenType.RightParen);

        var body = _statementParser.ParseBlockStatement();
        return new DestructorDeclarationNode(ownerStructName, _parser._currentNamespace, access, isVirtual, body);
    }


    private List<ParameterNode> ParseParameterList(bool addThisPointer)
    {
        _parser.Eat(TokenType.LeftParen);
        var parameters = new List<ParameterNode>();
        if (_parser.Current.Type != TokenType.RightParen)
        {
            do
            {
                var paramType = _parser.ParseTypeNode();
                var paramName = _parser.Eat(TokenType.Identifier);
                parameters.Add(new ParameterNode(paramType, paramName));
            } while (_parser.Current.Type == TokenType.Comma && _parser.Eat(TokenType.Comma) != null);
        }
        _parser.Eat(TokenType.RightParen);
        return parameters;
    }

    internal FunctionDeclarationNode ParseGlobalFunction()
    {
        var returnType = _parser.ParseTypeNode();
        var identifier = _parser.Eat(TokenType.Identifier);
        return FinishParsingFunction(returnType, identifier.Value, null, AccessSpecifier.Public, false, false, _parser._currentNamespace, false);
    }

    private FunctionDeclarationNode FinishParsingFunction(TypeNode returnType, string name, string? ownerStructName, AccessSpecifier accessLevel, bool isVirtual, bool isOverride, string? namespaceName, bool isMethod)
    {
        var parameters = ParseParameterList(isMethod);

        if (isMethod && ownerStructName != null)
        {
            // The `this` pointer type will be resolved later during semantic analysis.
            // For now, we create a placeholder.
            var thisType = new PointerTypeNode(new SimpleTypeNode(new Token(TokenType.Identifier, ownerStructName, -1, -1)));
            var thisName = new Token(TokenType.Identifier, "this", -1, -1);
            var thisParam = new ParameterNode(thisType, thisName);
            parameters.Insert(0, thisParam);
        }

        StatementNode? body = null;
        if (_parser.Current.Type == TokenType.LeftBrace)
        {
            body = _statementParser.ParseBlockStatement();
        }
        else
        {
            _parser.Eat(TokenType.Semicolon); // For function prototypes
        }

        return new FunctionDeclarationNode(returnType, name, parameters, body, ownerStructName, accessLevel, isVirtual, isOverride, namespaceName);
    }
}