namespace CTilde;

public enum TokenType
{
    Unknown,
    Keyword,
    Enum,
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
    Dot,
    Hash,
    Assignment,
    Plus,
    Minus,
    Star,
    Slash,

    DoubleEquals,
    NotEquals,
    LessThan,
    GreaterThan,

    Ampersand,
    Arrow,
    Colon,
    DoubleColon,
    Tilde
}