using System;
using Prowl.Crumb;

enum TokenKind
{
    EndOfFile = 0,
    Error,
    Identifier,
    Number,
    String,
    Shader,
    SubShader,
    Pass,
    Properties,
    OpenBrace,
    CloseBrace,
    Equals,
    HlslSource,
}

class Program
{
    static void Main()
    {
        var rules = new TokenizerRules<TokenKind>()
            .EndOfFile(TokenKind.EndOfFile)
            .Error(TokenKind.Error)
            .Whitespace(c => c is ' ' or '\t' or '\r' or '\n')
            .Keyword("Properties", TokenKind.Properties)
            .Keyword("Shader", TokenKind.Shader)
            .Keyword("SubShader", TokenKind.SubShader)
            .Keyword("Pass", TokenKind.Pass)
            .Symbol("{", TokenKind.OpenBrace)
            .Symbol("}", TokenKind.CloseBrace)
            .Symbol("=", TokenKind.Equals)
            .Identifier(TokenKind.Identifier)
            .Number(TokenKind.Number)
            .String('"', TokenKind.String)
            .Comment("//", "\n")
            .Comment("/*", "*/")
            .Block("HLSLPROGRAM", "ENDHLSL", TokenKind.HlslSource);

        const string source =
"""
Shader "Examples/CopiedFromUnityDocs"
{
    Properties
    {
        // Change this value in the Material Inspector to affect the value of the Offset command
        _OffsetUnitScale ("Offset unit scale", Integer) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM

#pragma bla bla
HLSL code goes here
something float4x4 SV_TARGET APPDATA v2f vert

            ENDHLSL
        }
    }
}
""";

        var tokenizer = new Tokenizer<TokenKind>(source, rules);

        while (true)
        {
            var token = tokenizer.Next();
            var text = tokenizer.Slice(token).ToString().Replace("\n", "\\n");
            Console.WriteLine($"{token.Line,3}:{token.Column,-3} {token.Kind,-12} '{text}'");

            if (token.Kind == TokenKind.EndOfFile)
                break;
        }
    }
}
