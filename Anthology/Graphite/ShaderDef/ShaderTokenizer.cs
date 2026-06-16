using System;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


public static class ShaderTokenizer
{
    // Compiled once and reused across every tokenizer; the rules are immutable after compilation.
    public static readonly TokenizerRules<ShaderToken> Rules = BuildRules();


    // Tokenizes the top-level ShaderLab-inspired syntax of a shader.
    static TokenizerRules<ShaderToken> BuildRules() =>
        new TokenizerRules<ShaderToken>()
            .EndOfFile(ShaderToken.EndOfFile)
            .Error(ShaderToken.Error)

            .Whitespace(char.IsWhiteSpace)
            .LineComment("//")
            .Comment("/*", "*/")

            // Symbols
            .Symbol("{", ShaderToken.OpenBrace)
            .Symbol("}", ShaderToken.CloseBrace)
            .Symbol("(", ShaderToken.OpenParen)
            .Symbol(")", ShaderToken.CloseParen)
            .Symbol("=", ShaderToken.Equals)
            .Symbol(",", ShaderToken.Comma)

            // Crumb's number rule does not consume a leading sign, so a standalone '-' is
            // tokenized as its own symbol and folded back into the value by the numeric parsers.
            .Symbol("-", ShaderToken.Minus)

            .String('"', ShaderToken.String)
            .Number(ShaderToken.Number)
            .Identifier(ShaderToken.Identifier)

            .Compile();


    public static Tokenizer<ShaderToken> Create(ReadOnlySpan<char> source)
        => new(source, Rules);
}
