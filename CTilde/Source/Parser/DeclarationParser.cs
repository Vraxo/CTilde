using System.Collections.Generic;
using System.Linq;

namespace CTilde;

internal class DeclarationParser
{
    private readonly Parser _parser;
    private readonly StructParser _structParser;
    private readonly FunctionParser _functionParser;

    internal DeclarationParser(Parser parser, StructParser structParser, FunctionParser functionParser)
    {
        _parser = parser;
        _structParser = structParser;
        _functionParser = functionParser;
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
        return _structParser.ParseStructDefinition();
    }

    internal FunctionDeclarationNode ParseGlobalFunction()
    {
        return _functionParser.ParseGlobalFunction();
    }
}