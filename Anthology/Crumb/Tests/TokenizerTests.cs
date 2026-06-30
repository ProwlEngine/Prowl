using System.Collections.Generic;
using Prowl.Crumb;
using Xunit;

namespace Prowl.Crumb.Tests;

public enum TokenKind
{
    EndOfFile = 0,
    Error,
    Identifier,
    Number,
    String,
    Comment,
    Shader,
    SubShader,
    Pass,
    OpenBrace,
    CloseBrace,
    Equals,
    EqualsEquals,
    EqualsEqualsEquals,
    HlslSource,
}

public class TokenizerTests
{
    private static TokenizerRules<TokenKind> ShaderLabRules() => new TokenizerRules<TokenKind>()
        .EndOfFile(TokenKind.EndOfFile)
        .Error(TokenKind.Error)
        .Whitespace(c => c is ' ' or '\t' or '\r' or '\n')
        .Keyword("Shader", TokenKind.Shader)
        .Keyword("SubShader", TokenKind.SubShader)
        .Keyword("Pass", TokenKind.Pass)
        .Symbol("{", TokenKind.OpenBrace)
        .Symbol("}", TokenKind.CloseBrace)
        .Symbol("===", TokenKind.EqualsEqualsEquals)
        .Symbol("==", TokenKind.EqualsEquals)
        .Symbol("=", TokenKind.Equals)
        .Identifier(TokenKind.Identifier)
        .Number(TokenKind.Number)
        .String('"', TokenKind.String)
        .Comment("//", "\n")
        .Comment("/*", "*/")
        .Block("HLSLPROGRAM", "ENDHLSL", TokenKind.HlslSource)
        .Compile();

    private static List<(TokenKind Kind, string Text)> Lex(string source, TokenizerRules<TokenKind> rules)
    {
        var tokenizer = new Tokenizer<TokenKind>(source, rules);
        var result = new List<(TokenKind, string)>();
        while (true)
        {
            var token = tokenizer.Next();
            if (token.Kind == TokenKind.EndOfFile)
                break;
            result.Add((token.Kind, tokenizer.Slice(token).ToString()));
        }
        return result;
    }

    [Fact]
    public void Keywords_Symbols_Identifiers()
    {
        var tokens = Lex("Shader Shaders { }", ShaderLabRules());
        Assert.Equal(TokenKind.Shader, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind); // "Shaders" is not the keyword
        Assert.Equal("Shaders", tokens[1].Text);
        Assert.Equal(TokenKind.OpenBrace, tokens[2].Kind);
        Assert.Equal(TokenKind.CloseBrace, tokens[3].Kind);
    }

    [Fact]
    public void LongestMatch_Symbols()
    {
        var tokens = Lex("= == ===", ShaderLabRules());
        Assert.Equal(TokenKind.Equals, tokens[0].Kind);
        Assert.Equal(TokenKind.EqualsEquals, tokens[1].Kind);
        Assert.Equal(TokenKind.EqualsEqualsEquals, tokens[2].Kind);
    }

    [Fact]
    public void Numbers_AllForms()
    {
        var tokens = Lex("42 3.14 1e10 2.5E-3 0xFF .5 10.", ShaderLabRules());
        Assert.All(tokens, t => Assert.Equal(TokenKind.Number, t.Kind));
        Assert.Equal("42", tokens[0].Text);
        Assert.Equal("3.14", tokens[1].Text);
        Assert.Equal("1e10", tokens[2].Text);
        Assert.Equal("2.5E-3", tokens[3].Text);
        Assert.Equal("0xFF", tokens[4].Text);
        Assert.Equal(".5", tokens[5].Text);
        Assert.Equal("10.", tokens[6].Text);
    }

    [Fact]
    public void Strings_WithEscapes()
    {
        var tokens = Lex("\"hello \\\" world\"", ShaderLabRules());
        Assert.Equal(TokenKind.String, tokens[0].Kind);
        Assert.Equal("\"hello \\\" world\"", tokens[0].Text);
    }

    [Fact]
    public void Comments_AreSkipped()
    {
        var tokens = Lex("Pass // a comment\n Pass /* block */ Pass", ShaderLabRules());
        Assert.Equal(3, tokens.Count);
        Assert.All(tokens, t => Assert.Equal(TokenKind.Pass, t.Kind));
    }

    [Fact]
    public void Block_EmitsOpaqueContent()
    {
        var tokens = Lex("Pass HLSLPROGRAM float4 frag() {} ENDHLSL Pass", ShaderLabRules());
        Assert.Equal(TokenKind.Pass, tokens[0].Kind);
        Assert.Equal(TokenKind.HlslSource, tokens[1].Kind);
        Assert.Equal(" float4 frag() {} ", tokens[1].Text);
        Assert.Equal(TokenKind.Pass, tokens[2].Kind);
    }

    [Fact]
    public void LineAndColumn_Tracking()
    {
        var rules = ShaderLabRules();
        var tokenizer = new Tokenizer<TokenKind>("Pass\n  Pass", rules);
        var first = tokenizer.Next();
        var second = tokenizer.Next();
        Assert.Equal((1, 1), (first.Line, first.Column));
        Assert.Equal((2, 3), (second.Line, second.Column));
    }

    [Fact]
    public void Peek_DoesNotAdvance()
    {
        var rules = ShaderLabRules();
        var tokenizer = new Tokenizer<TokenKind>("Pass {", rules);
        var peek1 = tokenizer.Peek();
        var peek2 = tokenizer.Peek();
        var next = tokenizer.Next();
        Assert.Equal(peek1.Kind, peek2.Kind);
        Assert.Equal(peek1.Start, next.Start);
        Assert.Equal(TokenKind.OpenBrace, tokenizer.Next().Kind);
    }

    [Fact]
    public void MarkReset_Backtracks()
    {
        var rules = ShaderLabRules();
        var tokenizer = new Tokenizer<TokenKind>("Pass Shader", rules);
        var mark = tokenizer.Mark();
        Assert.Equal(TokenKind.Pass, tokenizer.Next().Kind);
        Assert.Equal(TokenKind.Shader, tokenizer.Next().Kind);
        tokenizer.Reset(mark);
        Assert.Equal(TokenKind.Pass, tokenizer.Next().Kind);
    }

    [Fact]
    public void TryConsume_And_Expect()
    {
        var rules = ShaderLabRules();
        var tokenizer = new Tokenizer<TokenKind>("Pass {", rules);
        Assert.True(tokenizer.TryConsume(TokenKind.Pass));
        Assert.False(tokenizer.TryConsume(TokenKind.Pass));
        var brace = tokenizer.Expect(TokenKind.OpenBrace);
        Assert.Equal(TokenKind.OpenBrace, brace.Kind);
    }

    [Fact]
    public void Expect_Throws_WithDiagnostics()
    {
        var rules = ShaderLabRules();
        var tokenizer = new Tokenizer<TokenKind>("Pass", rules);
        UnexpectedTokenException? ex = null;
        try
        {
            tokenizer.Expect(TokenKind.OpenBrace);
        }
        catch (UnexpectedTokenException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal("OpenBrace", ex!.Expected);
        Assert.Equal("Pass", ex.Actual);
        Assert.Equal(1, ex.Line);
    }

    [Fact]
    public void LineComment_Skipped()
    {
        var rules = new TokenizerRules<TokenKind>()
            .EndOfFile(TokenKind.EndOfFile)
            .Error(TokenKind.Error)
            .Whitespace(char.IsWhiteSpace)
            .LineComment("//")
            .Identifier(TokenKind.Identifier)
            .Compile();

        var tokens = Lex("foo // trailing\n bar", rules);
        Assert.Equal(2, tokens.Count);
        Assert.Equal("foo", tokens[0].Text);
        Assert.Equal("bar", tokens[1].Text);
    }

    [Fact]
    public void LineComment_Emitted_ExcludesNewline()
    {
        var rules = new TokenizerRules<TokenKind>()
            .EndOfFile(TokenKind.EndOfFile)
            .Error(TokenKind.Error)
            .Whitespace(char.IsWhiteSpace)
            .LineComment("//", TokenKind.Comment)
            .Identifier(TokenKind.Identifier)
            .Compile();

        var tokens = Lex("foo // a comment\n bar", rules);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(TokenKind.Comment, tokens[1].Kind);
        Assert.Equal("// a comment", tokens[1].Text);
        Assert.Equal(TokenKind.Identifier, tokens[2].Kind);
    }

    [Fact]
    public void EmittedComment_Token()
    {
        var rules = new TokenizerRules<TokenKind>()
            .EndOfFile(TokenKind.EndOfFile)
            .Error(TokenKind.Error)
            .Whitespace(char.IsWhiteSpace)
            .Comment("//", "\n", TokenKind.Comment)
            .Identifier(TokenKind.Identifier)
            .Compile();

        var tokens = Lex("foo // bar\n baz", rules);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(TokenKind.Comment, tokens[1].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[2].Kind);
    }
}
