namespace Prowl.Graphite.ShaderDef;


public enum ShaderToken
{
    // Crumb requires explicit end-of-file and error kinds.
    EndOfFile,
    Error,

    // Simple token types
    Identifier,
    String,
    Number,

    // Character token types
    OpenBrace,
    CloseBrace,
    OpenParen,
    CloseParen,
    Comma,
    Equals,
    Minus,
}
