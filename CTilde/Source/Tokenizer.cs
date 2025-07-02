using System.Collections.Generic;
using System.Text;

namespace CTilde;

public enum TokenType
{
    Keyword,
    Identifier,
    IntegerLiteral,
    HexLiteral,
    StringLiteral,
    Semicolon,
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    Comma,
    Hash,
    Assignment,
    Unknown,
    Plus,
    Minus,
    Star,
    Slash,
    DoubleEquals,
    NotEquals,
    LessThan,
    GreaterThan
}

public record Token(TokenType Type, string Value);

public class Tokenizer
{
    private static readonly HashSet<string> Keywords =
    [
        "int",
        "void",
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

            if (c == '/' && i + 1 < input.Length && input[i + 1] == '/')
            {
                while (i < input.Length && input[i] != '\n') i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < input.Length && input[i] != '"')
                {
                    char current = input[i];
                    if (current == '\\' && i + 1 < input.Length)
                    {
                        i++;
                        switch (input[i])
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            default:
                                sb.Append('\\');
                                sb.Append(input[i]);
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(current);
                    }
                    i++;
                }

                if (i < input.Length) i++;

                tokens.Add(new Token(TokenType.StringLiteral, sb.ToString()));
                continue;
            }

            switch (c)
            {
                case '#': tokens.Add(new(TokenType.Hash, "#")); i++; continue;
                case '(': tokens.Add(new(TokenType.LeftParen, "(")); i++; continue;
                case ')': tokens.Add(new(TokenType.RightParen, ")")); i++; continue;
                case '{': tokens.Add(new(TokenType.LeftBrace, "{")); i++; continue;
                case '}': tokens.Add(new(TokenType.RightBrace, "}")); i++; continue;
                case ';': tokens.Add(new(TokenType.Semicolon, ";")); i++; continue;
                case ',': tokens.Add(new(TokenType.Comma, ",")); i++; continue;
                case '+': tokens.Add(new(TokenType.Plus, "+")); i++; continue;
                case '-': tokens.Add(new(TokenType.Minus, "-")); i++; continue;
                case '*': tokens.Add(new(TokenType.Star, "*")); i++; continue;
                case '/': tokens.Add(new(TokenType.Slash, "/")); i++; continue;
                case '<': tokens.Add(new(TokenType.LessThan, "<")); i++; continue;
                case '>': tokens.Add(new(TokenType.GreaterThan, ">")); i++; continue;
                case '=':
                    if (i + 1 < input.Length && input[i + 1] == '=') { tokens.Add(new(TokenType.DoubleEquals, "==")); i += 2; }
                    else { tokens.Add(new(TokenType.Assignment, "=")); i++; }
                    continue;
                case '!':
                    if (i + 1 < input.Length && input[i + 1] == '=') { tokens.Add(new(TokenType.NotEquals, "!=")); i += 2; }
                    else { tokens.Add(new(TokenType.Unknown, c.ToString())); i++; }
                    continue;
            }

            if (c == '0' && i + 1 < input.Length && (input[i + 1] == 'x' || input[i + 1] == 'X'))
            {
                int start = i;
                i += 2; // Skip '0x'
                while (i < input.Length && "0123456789abcdefABCDEF".Contains(input[i]))
                {
                    i++;
                }
                tokens.Add(new Token(TokenType.HexLiteral, input.Substring(start, i - start)));
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;
                while (i < input.Length && char.IsDigit(input[i])) i++;
                string value = input.Substring(start, i - start);
                tokens.Add(new(TokenType.IntegerLiteral, value));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_')) i++;
                string value = input.Substring(start, i - start);
                TokenType type = Keywords.Contains(value) ? TokenType.Keyword : TokenType.Identifier;
                tokens.Add(new(type, value));
                continue;
            }

            tokens.Add(new(TokenType.Unknown, c.ToString()));
            i++;
        }
        tokens.Add(new(TokenType.Unknown, string.Empty)); // EOF token
        return tokens;
    }
}