namespace Prowl.Graphite.ShaderDef;


internal enum ShaderToken
{
    // Crumb requires explicit end-of-file and error kinds.
    EndOfFile,
    Error,

    // Simple token types
    Identifier,
    String,
    Number,

    // Raw embedded GLSL source captured between GLSLPROGRAM and ENDGLSL.
    GlslProgram,

    // Character token types
    OpenBrace,
    CloseBrace,
    OpenParen,
    CloseParen,
    OpenBracket,
    CloseBracket,
    Comma,
    Equals,
    Minus,
}
