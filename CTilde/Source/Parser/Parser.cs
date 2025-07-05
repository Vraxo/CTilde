using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde;

public class Parser
{
    internal readonly List<Token> _tokens;
    internal int _position;
    internal string? _currentNamespace;
    internal readonly List<ImportDirectiveNode> _imports = new();
    private string _filePath = "";

    public List<Diagnostic> Diagnostics { get; } = new();

    private readonly ExpressionParser _expressionParser;
    private readonly StatementParser _statementParser;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _position = 0;
        _expressionParser = new ExpressionParser(this);
        _statementParser = new StatementParser(this, _expressionParser);
    }

    internal Token Current => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];
    internal Token Previous => _position > 0 ? _tokens[_position - 1] : _tokens[0];
    internal Token Peek(int offset) => _position + offset < _tokens.Count ? _tokens[_position + offset] : _tokens[^1];

    internal void ReportError(string message, Token token)
    {
        Diagnostics.Add(new Diagnostic(_filePath, message, token.Line, token.Column));
    }

    internal void ReportErrorAfter(string message, Token previousToken)
    {
        var line = previousToken.Line;
        var col = previousToken.Column + previousToken.Value.Length;
        Diagnostics.Add(new Diagnostic(_filePath, message, line, col));
    }

    internal Token Eat(TokenType expectedType)
    {
        var currentToken = Current;
        if (currentToken.Type == expectedType)
        {
            _position++;
            return currentToken;
        }

        string message = $"Expected '{expectedType}' but got '{currentToken.Type}' ('{currentToken.Value}')";

        // Heuristic: If we expect a statement/block terminator, the error is likely at the end of the previous construct.
        if (expectedType is TokenType.Semicolon or TokenType.RightBrace or TokenType.RightParen)
        {
            // Report the error at the position immediately *after* the last successfully consumed token.
            ReportErrorAfter(message, Previous);
        }
        else
        {
            ReportError(message, currentToken);
        }

        return new Token(expectedType, string.Empty, currentToken.Line, currentToken.Column); // Return a dummy token
    }

    internal void AdvancePosition(int amount) => _position += amount;

    public List<ImportDirectiveNode> GetImports() => _imports;

    public CompilationUnitNode Parse(string filePath)
    {
        _filePath = filePath;
        var usings = new List<UsingDirectiveNode>();
        var structs = new List<StructDefinitionNode>();
        var functions = new List<FunctionDeclarationNode>();
        var enums = new List<EnumDefinitionNode>();

        while (Current.Type != TokenType.Unknown)
        {
            try
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
                        ReportError($"Unexpected directive after '#': '{hashKeyword.Value}'", hashKeyword);
                        AdvancePosition(2); // Skip '#' and the bad identifier
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
                    structs.Add(ParseStructDefinition());
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
            catch (Exception) // Catch potential cascading failures from bad tokens
            {
                // Synchronize to the next likely statement start to continue parsing
                while (Current.Type != TokenType.Semicolon && Current.Type != TokenType.RightBrace && Current.Type != TokenType.Unknown)
                {
                    AdvancePosition(1);
                }
                // Also consume the synchronizing token
                if (Current.Type != TokenType.Unknown) AdvancePosition(1);
            }
        }

        var unitNode = new CompilationUnitNode(filePath, usings, structs, functions, enums);
        SetParents(unitNode, null);
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

        while (Current.Type != TokenType.RightBrace && Current.Type != TokenType.Unknown)
        {
            var memberName = Eat(TokenType.Identifier);
            if (Current.Type == TokenType.Assignment)
            {
                Eat(TokenType.Assignment);
                var valueToken = Eat(TokenType.IntegerLiteral);
                if (!int.TryParse(valueToken.Value, out currentValue))
                {
                    ReportError($"Invalid integer value for enum member '{memberName.Value}': '{valueToken.Value}'", valueToken);
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
                ReportError($"Expected ',' or '}}' after enum member '{memberName.Value}'", Current);
                break;
            }
        }

        Eat(TokenType.RightBrace);
        Eat(TokenType.Semicolon);
        return new EnumDefinitionNode(enumName.Value, _currentNamespace, members);
    }

    private StructDefinitionNode ParseStructDefinition()
    {
        Eat(TokenType.Keyword); // struct
        var structName = Eat(TokenType.Identifier);

        var genericParameters = new List<Token>();
        if (Current.Type == TokenType.LessThan)
        {
            Eat(TokenType.LessThan);
            do { genericParameters.Add(Eat(TokenType.Identifier)); }
            while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
            Eat(TokenType.GreaterThan);
        }

        string? baseStructName = null;
        if (Current.Type == TokenType.Colon)
        {
            Eat(TokenType.Colon);
            baseStructName = Eat(TokenType.Identifier).Value;
        }

        Eat(TokenType.LeftBrace);

        var members = new List<MemberVariableNode>();
        var methods = new List<FunctionDeclarationNode>();
        var constructors = new List<ConstructorDeclarationNode>();
        var destructors = new List<DestructorDeclarationNode>();

        var currentAccess = AccessSpecifier.Private;

        while (Current.Type != TokenType.RightBrace && Current.Type != TokenType.Unknown)
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
            var startToken = Current;

            if (Current.Type == TokenType.Keyword && Current.Value == "const")
            {
                isConst = true;
                Eat(TokenType.Keyword);
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

            if (isVirtual && isOverride) ReportError("A method cannot be both 'virtual' and 'override'.", startToken);

            if (Current.Type == TokenType.Tilde)
            {
                destructors.Add(ParseDestructor(structName.Value, currentAccess, isVirtual));
                continue;
            }

            // Check for constructor (e.g. `List(...)` not `List<T>(...)`)
            if (Current.Type == TokenType.Identifier && Current.Value == structName.Value && Peek(1).Type == TokenType.LeftParen)
            {
                if (isVirtual || isOverride || isConst) ReportError("Constructors cannot be marked 'virtual', 'override', or 'const'.", startToken);
                constructors.Add(ParseConstructor(structName.Value, baseStructName, currentAccess));
                continue;
            }

            var type = ParseTypeNode();

            Token name;
            if (Current.Type == TokenType.Keyword && Current.Value == "operator")
            {
                Eat(TokenType.Keyword); // operator
                var opToken = Current;
                _position++;
                name = new Token(TokenType.Identifier, $"operator_{NameMangler.MangleOperator(opToken.Value)}", opToken.Line, opToken.Column);
            }
            else
            {
                name = Eat(TokenType.Identifier);
            }

            if (Current.Type == TokenType.LeftParen)
            {
                var methodNode = FinishParsingFunction(type, name.Value, structName.Value, currentAccess, isVirtual, isOverride, _currentNamespace, true);
                methods.Add(methodNode);
            }
            else
            {
                if (isVirtual || isOverride) ReportError("Only methods can be marked 'virtual' or 'override'.", startToken);
                members.Add(new MemberVariableNode(isConst, type, name, currentAccess));
                Eat(TokenType.Semicolon);
            }
        }

        Eat(TokenType.RightBrace);
        Eat(TokenType.Semicolon);
        return new StructDefinitionNode(structName.Value, genericParameters, baseStructName, _currentNamespace, members, methods, constructors, destructors);
    }


    private ConstructorDeclarationNode ParseConstructor(string ownerStructName, string? baseStructName, AccessSpecifier access)
    {
        var nameToken = Eat(TokenType.Identifier);
        var parameters = ParseParameterList(false);

        BaseInitializerNode? baseInitializer = null;
        if (Current.Type == TokenType.Colon)
        {
            if (baseStructName == null) ReportError($"Struct '{ownerStructName}' cannot have a base initializer because it does not inherit from another struct.", nameToken);
            Eat(TokenType.Colon);
            var baseName = Eat(TokenType.Identifier);
            // No error here, Eat will report if baseName.Value != baseStructName

            Eat(TokenType.LeftParen);
            var arguments = new List<ExpressionNode>();
            if (Current.Type != TokenType.RightParen)
            {
                do { arguments.Add(_expressionParser.ParseExpression()); }
                while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
            }
            Eat(TokenType.RightParen);
            baseInitializer = new BaseInitializerNode(arguments);
        }

        var body = _statementParser.ParseBlockStatement();
        return new ConstructorDeclarationNode(ownerStructName, _currentNamespace, access, parameters, baseInitializer, body);
    }

    private DestructorDeclarationNode ParseDestructor(string ownerStructName, AccessSpecifier access, bool isVirtual)
    {
        Eat(TokenType.Tilde);
        var name = Eat(TokenType.Identifier);
        if (name.Value != ownerStructName) ReportError($"Destructor name '~{name.Value}' must match struct name '{ownerStructName}'.", name);

        Eat(TokenType.LeftParen);
        Eat(TokenType.RightParen);

        var body = _statementParser.ParseBlockStatement();
        return new DestructorDeclarationNode(ownerStructName, _currentNamespace, access, isVirtual, body);
    }


    public void SetParents(AstNode node, AstNode? parent)
    {
        node.Parent = parent;
        foreach (var property in node.GetType().GetProperties())
        {
            if (property.CanWrite && property.Name == "Parent") continue;
            if (property.GetValue(node) is AstNode child)
            {
                SetParents(child, node);
            }
            else if (property.GetValue(node) is IEnumerable<AstNode> children)
            {
                foreach (var c in children.ToList()) // ToList to avoid mutation issues
                {
                    SetParents(c, node);
                }
            }
        }
    }

    internal TypeNode ParseTypeNode()
    {
        Token baseTypeToken;
        var current = Current;

        // 1. Parse the base name (which could be qualified)
        if (current.Type == TokenType.Keyword && current.Value == "struct")
        {
            Eat(TokenType.Keyword);
            baseTypeToken = Eat(TokenType.Identifier);
        }
        else if (current.Type == TokenType.Keyword && (current.Value is "int" or "char" or "void"))
        {
            baseTypeToken = Eat(TokenType.Keyword);
        }
        else if (current.Type == TokenType.Identifier)
        {
            baseTypeToken = Eat(TokenType.Identifier);
            // Check for `::`
            if (Current.Type == TokenType.DoubleColon)
            {
                Eat(TokenType.DoubleColon);
                var memberName = Eat(TokenType.Identifier);
                baseTypeToken = new Token(TokenType.Identifier, $"{baseTypeToken.Value}::{memberName.Value}", baseTypeToken.Line, baseTypeToken.Column);
            }
        }
        else
        {
            ReportError($"Expected a type name but found '{current.Type}' ('{current.Value}').", current);
            AdvancePosition(1); // Consume the bad token to prevent infinite loop
            return new SimpleTypeNode(new Token(TokenType.Identifier, "unknown", current.Line, current.Column));
        }

        // 2. Parse optional generic arguments
        TypeNode typeNode;
        if (Current.Type == TokenType.LessThan)
        {
            Eat(TokenType.LessThan);
            var typeArgs = new List<TypeNode>();
            do { typeArgs.Add(ParseTypeNode()); }
            while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
            Eat(TokenType.GreaterThan);
            typeNode = new GenericInstantiationTypeNode(baseTypeToken, typeArgs);
        }
        else
        {
            typeNode = new SimpleTypeNode(baseTypeToken);
        }

        // 3. Parse optional pointers
        while (Current.Type == TokenType.Star)
        {
            Eat(TokenType.Star);
            typeNode = new PointerTypeNode(typeNode);
        }
        return typeNode;
    }


    private List<ParameterNode> ParseParameterList(bool addThisPointer)
    {
        Eat(TokenType.LeftParen);
        var parameters = new List<ParameterNode>();
        if (Current.Type != TokenType.RightParen)
        {
            do
            {
                var paramType = ParseTypeNode();
                var paramName = Eat(TokenType.Identifier);
                parameters.Add(new ParameterNode(paramType, paramName));
            } while (Current.Type == TokenType.Comma && Eat(TokenType.Comma) != null);
        }
        Eat(TokenType.RightParen);
        return parameters;
    }

    private FunctionDeclarationNode ParseGlobalFunction()
    {
        var returnType = ParseTypeNode();
        var identifier = Eat(TokenType.Identifier);
        return FinishParsingFunction(returnType, identifier.Value, null, AccessSpecifier.Public, false, false, _currentNamespace, false);
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
        if (Current.Type == TokenType.LeftBrace)
        {
            body = _statementParser.ParseBlockStatement();
        }
        else
        {
            Eat(TokenType.Semicolon); // For function prototypes
        }

        return new FunctionDeclarationNode(returnType, name, parameters, body, ownerStructName, accessLevel, isVirtual, isOverride, namespaceName);
    }
}