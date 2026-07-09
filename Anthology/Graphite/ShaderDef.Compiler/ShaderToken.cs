namespace Prowl.Graphite.ShaderDef.Compiler;


internal enum ShaderToken
{
    // Crumb requires explicit end-of-file and error kinds.
    EndOfFile,
    Error,

    // Simple token types
    Identifier,
    String,
    Number,

    // Raw embedded Slang source captured between SLANGPROGRAM and ENDSLANG.
    SlangProgram,

    // Character token types
    OpenBrace,
    CloseBrace,
    OpenParen,
    CloseParen,
    Comma,
    Equals,
    Minus,
}
