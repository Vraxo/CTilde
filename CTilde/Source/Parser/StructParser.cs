using System.Collections.Generic;
using System.Linq;

namespace CTilde;

internal class StructParser
{
    private readonly Parser _parser;
    private readonly StatementParser _statementParser;
    private readonly ExpressionParser _expressionParser;
    private readonly FunctionParser _functionParser;

    internal StructParser(Parser parser, StatementParser statementParser, ExpressionParser expressionParser, FunctionParser functionParser)
    {
        _parser = parser;
        _statementParser = statementParser;
        _expressionParser = expressionParser;
        _functionParser = functionParser;
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
        var properties = new List<PropertyDefinitionNode>();

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
                var methodNode = _functionParser.FinishParsingFunction(type, name.Value, structName.Value, currentAccess, isVirtual, isOverride, _parser._currentNamespace, true);
                methods.Add(methodNode);
            }
            else if (_parser.Current.Type == TokenType.LeftBrace)
            {
                var propertyNode = ParsePropertyDefinition(type, name, currentAccess, isVirtual, isOverride);
                properties.Add(propertyNode);
            }
            else
            {
                if (isVirtual || isOverride) _parser.ReportError("Only methods or properties can be marked 'virtual' or 'override'.", startToken);
                members.Add(new MemberVariableNode(isConst, type, name, currentAccess));
                _parser.Eat(TokenType.Semicolon);
            }
        }

        _parser.Eat(TokenType.RightBrace);
        _parser.Eat(TokenType.Semicolon);
        return new StructDefinitionNode(structName.Value, genericParameters, baseStructName, _parser._currentNamespace, members, methods, constructors, destructors, properties);
    }

    private PropertyDefinitionNode ParsePropertyDefinition(TypeNode type, Token name, AccessSpecifier access, bool isVirtual, bool isOverride)
    {
        _parser.Eat(TokenType.LeftBrace);

        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;

        while (_parser.Current.Type != TokenType.RightBrace && _parser.Current.Type != TokenType.Unknown)
        {
            var accessorToken = _parser.Current;
            if (accessorToken.Type != TokenType.Keyword || (accessorToken.Value != "get" && accessorToken.Value != "set"))
            {
                _parser.ReportError("Expected 'get' or 'set' keyword inside property.", accessorToken);
                _parser.AdvancePosition(1); // Skip bad token
                continue;
            }

            _parser.Eat(TokenType.Keyword); // Consume 'get' or 'set'

            StatementNode? body = null;
            if (_parser.Current.Type == TokenType.LeftBrace)
            {
                body = _statementParser.ParseBlockStatement();
            }
            else
            {
                _parser.Eat(TokenType.Semicolon); // Auto-property accessor
            }

            var accessorNode = new PropertyAccessorNode(accessorToken, body);

            if (accessorToken.Value == "get")
            {
                if (getter != null) _parser.ReportError("Property can only have one 'get' accessor.", accessorToken);
                getter = accessorNode;
            }
            else // "set"
            {
                if (setter != null) _parser.ReportError("Property can only have one 'set' accessor.", accessorToken);
                setter = accessorNode;
            }
        }

        _parser.Eat(TokenType.RightBrace);
        // Note: No semicolon after property definition brace

        return new PropertyDefinitionNode(type, name, access, isVirtual, isOverride, getter, setter);
    }


    private ConstructorDeclarationNode ParseConstructor(string ownerStructName, string? baseStructName, AccessSpecifier access)
    {
        var nameToken = _parser.Eat(TokenType.Identifier);
        var parameters = _functionParser.ParseParameterList();

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
}