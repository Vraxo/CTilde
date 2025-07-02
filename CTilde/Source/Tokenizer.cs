using System.Collections.Generic;

namespace CTilde;

public enum TokenType
{
    Keyword,
    Identifier,
    IntegerLiteral,
    Semicolon,
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    Assignment,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Plus,
    Minus,
    Unknown
}

public record Token(TokenType Type, string Value);

public class Tokenizer
{
    private static readonly HashSet<string> Keywords =
    [
        "int",
        "return",
        "while",
        "if",
        "else"
    ];

    public static List<Token> Tokenize(string input)
    {
        List<Token> tokens = [];
        int i = 0;

        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '(':
                    tokens.Add(new(TokenType.LeftParen, "("));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new(TokenType.RightParen, ")"));
                    i++;
                    continue;
                case '{':
                    tokens.Add(new(TokenType.LeftBrace, "{"));
                    i++;
                    continue;
                case '}':
                    tokens.Add(new(TokenType.RightBrace, "}"));
                    i++;
                    continue;
                case ';':
                    tokens.Add(new(TokenType.Semicolon, ";"));
                    i++;
                    continue;
                case '+':
                    tokens.Add(new(TokenType.Plus, "+"));
                    i++;
                    continue;
                case '-':
                    tokens.Add(new(TokenType.Minus, "-"));
                    i++;
                    continue;
                case '=':
                    if (i + 1 < input.Length && input[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenType.Equal, "=="));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Assignment, "="));
                        i++;
                    }
                    continue;
                case '!':
                    if (i + 1 < input.Length && input[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenType.NotEqual, "!="));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Unknown, c.ToString()));
                        i++;
                    }
                    continue;
                case '<':
                    if (i + 1 < input.Length && input[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenType.LessThanOrEqual, "<="));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.LessThan, "<"));
                        i++;
                    }
                    continue;
                case '>':
                    if (i + 1 < input.Length && input[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenType.GreaterThanOrEqual, ">="));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.GreaterThan, ">"));
                        i++;
                    }
                    continue;
            }

            if (char.IsLetter(c))
            {
                int start = i;

                while (i < input.Length && char.IsLetterOrDigit(input[i]))
                {
                    i++;
                }

                string value = input[start..i];

                TokenType type = Keywords.Contains(value)
                    ? TokenType.Keyword
                    : TokenType.Identifier;

                tokens.Add(new(type, value));
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;

                while (i < input.Length && char.IsDigit(input[i]))
                {
                    i++;
                }

                string value = input[start..i];
                tokens.Add(new(TokenType.IntegerLiteral, value));
                continue;
            }

            tokens.Add(new(TokenType.Unknown, c.ToString()));
            i++;
        }

        return tokens;
    }
}