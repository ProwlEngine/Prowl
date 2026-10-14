namespace Prowl.Prism.Glsl;

public enum TokenKind
{
    End,
    Identifier,   // keywords included; the parser distinguishes them by text
    IntConst,
    UintConst,
    Int64Const,    // an 'l'-suffixed literal
    Uint64Const,   // a 'ul'/'lu'-suffixed literal
    FloatConst,
    DoubleConst,   // an 'lf'-suffixed literal
    HalfConst,     // an 'hf'-suffixed literal (float16_t)
    BoolConst,
    StringConst,   // a "..."-delimited literal (debugPrintfEXT format string)

    // punctuation
    LParen, RParen, LBracket, RBracket, LBrace, RBrace,
    Semicolon, Comma, Dot, Colon, Question,
    Hash,         // '#', only meaningful to the preprocessor

    // operators (Text holds the exact lexeme, e.g. "+=", "<<")
    Operator,
}

/// <summary>A lexed token. <see cref="Text"/> is the source lexeme (identifier name,
/// numeric text, or operator like "&amp;&amp;").</summary>
public readonly record struct Token(TokenKind Kind, string Text, int Line, int Column)
{
    public bool Is(TokenKind kind) => Kind == kind;
    public bool Is(string op) => Text == op;
    public override string ToString() => $"{Kind}('{Text}') {Line}:{Column}";
}
