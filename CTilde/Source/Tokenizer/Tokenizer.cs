using System.Collections.Generic;
using System.Text;

namespace CTilde;

public class Tokenizer
{
    private static readonly HashSet<string> Keywords =
    [
        "int",
        "void",
        "return",
        "while",
        "if",
        "else",
        "struct",
        "char",
        "public",
        "private",
        "namespace",
        "using",
        "const",
        "enum",
        "virtual",
        "override",
        "new",
        "delete",
        "operator" // NEW
    ];

    public static List<Token> Tokenize(string input)
    {
        List<Token> tokens = [];
        int i = 0;
        int line = 1;
        int column = 1;

        while (i < input.Length)
        {
            var startColumn = column;
            char c = input[i];

            if (c == '\n')
            {
                line++;
                column = 1;
                i++;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                i++;
                column++;
                continue;
            }

            if (c == '/' && i + 1 < input.Length && input[i + 1] == '/')
            {
                while (i < input.Length && input[i] != '\n')
                {
                    i++;
                }
                // Let the main loop handle the newline character and line/column update
                continue;
            }

            if (c == '"')
            {
                i++;
                column++;
                var sb = new StringBuilder();
                while (i < input.Length && input[i] != '"')
                {
                    char current = input[i];
                    if (current == '\\' && i + 1 < input.Length)
                    {
                        i++;
                        column++;
                        switch (input[i])
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            default: sb.Append('\\'); sb.Append(input[i]); break;
                        }
                    }
                    else sb.Append(current);
                    i++;
                    column++;
                }
                if (i < input.Length)
                {
                    i++;
                    column++;
                }
                tokens.Add(new Token(TokenType.StringLiteral, sb.ToString(), line, startColumn));
                continue;
            }

            switch (c)
            {
                case '~': tokens.Add(new(TokenType.Tilde, "~", line, startColumn)); i++; column++; continue;
                case '#': tokens.Add(new(TokenType.Hash, "#", line, startColumn)); i++; column++; continue;
                case '.': tokens.Add(new(TokenType.Dot, ".", line, startColumn)); i++; column++; continue;
                case ':':
                    if (i + 1 < input.Length && input[i + 1] == ':') { tokens.Add(new(TokenType.DoubleColon, "::", line, startColumn)); i += 2; column += 2; }
                    else { tokens.Add(new(TokenType.Colon, ":", line, startColumn)); i++; column++; }
                    continue;
                case '(': tokens.Add(new(TokenType.LeftParen, "(", line, startColumn)); i++; column++; continue;
                case ')': tokens.Add(new(TokenType.RightParen, ")", line, startColumn)); i++; column++; continue;
                case '{': tokens.Add(new(TokenType.LeftBrace, "{", line, startColumn)); i++; column++; continue;
                case '}': tokens.Add(new(TokenType.RightBrace, "}", line, startColumn)); i++; column++; continue;
                case ';': tokens.Add(new(TokenType.Semicolon, ";", line, startColumn)); i++; column++; continue;
                case ',': tokens.Add(new(TokenType.Comma, ",", line, startColumn)); i++; column++; continue;
                case '+': tokens.Add(new(TokenType.Plus, "+", line, startColumn)); i++; column++; continue;
                case '-':
                    if (i + 1 < input.Length && input[i + 1] == '>') { tokens.Add(new(TokenType.Arrow, "->", line, startColumn)); i += 2; column += 2; }
                    else { tokens.Add(new(TokenType.Minus, "-", line, startColumn)); i++; column++; }
                    continue;
                case '*': tokens.Add(new(TokenType.Star, "*", line, startColumn)); i++; column++; continue;
                case '/': tokens.Add(new(TokenType.Slash, "/", line, startColumn)); i++; column++; continue;
                case '&': tokens.Add(new(TokenType.Ampersand, "&", line, startColumn)); i++; column++; continue;
                case '<': tokens.Add(new(TokenType.LessThan, "<", line, startColumn)); i++; column++; continue;
                case '>': tokens.Add(new(TokenType.GreaterThan, ">", line, startColumn)); i++; column++; continue;
                case '=':
                    if (i + 1 < input.Length && input[i + 1] == '=') { tokens.Add(new(TokenType.DoubleEquals, "==", line, startColumn)); i += 2; column += 2; }
                    else { tokens.Add(new(TokenType.Assignment, "=", line, startColumn)); i++; column++; }
                    continue;
                case '!':
                    if (i + 1 < input.Length && input[i + 1] == '=') { tokens.Add(new(TokenType.NotEquals, "!=", line, startColumn)); i += 2; column += 2; }
                    else { tokens.Add(new(TokenType.Unknown, c.ToString(), line, startColumn)); i++; column++; }
                    continue;
            }

            if (c == '0' && i + 1 < input.Length && (input[i + 1] == 'x' || input[i + 1] == 'X'))
            {
                int start = i;
                i += 2;
                while (i < input.Length && "0123456789abcdefABCDEF".Contains(input[i])) i++;
                var literalValue = input.Substring(start, i - start);
                tokens.Add(new Token(TokenType.HexLiteral, literalValue, line, startColumn));
                column += literalValue.Length;
                continue;
            }
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < input.Length && char.IsDigit(input[i])) i++;
                string value = input.Substring(start, i - start);
                tokens.Add(new(TokenType.IntegerLiteral, value, line, startColumn));
                column += value.Length;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_')) i++;
                string value = input.Substring(start, i - start);
                tokens.Add(new(Keywords.Contains(value) ? TokenType.Keyword : TokenType.Identifier, value, line, startColumn));
                column += value.Length;
                continue;
            }
            tokens.Add(new(TokenType.Unknown, c.ToString(), line, startColumn));
            i++;
            column++;
        }
        tokens.Add(new(TokenType.Unknown, string.Empty, line, column)); // EOF token
        return tokens;
    }
}