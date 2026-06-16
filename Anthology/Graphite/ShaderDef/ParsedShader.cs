using System.Collections.Generic;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


public class ParsedShader
{
    public string? Name;
    public string? Fallback;

    public ShaderProperty[]? Properties;
    public ParsedPass[]? Passes;


    // Parses a property block: Properties { ... }
    static ShaderProperty[] ParsePropertiesBlock(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Properties");
        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        List<ShaderProperty> properties = new();

        while (t.Peek().Kind == ShaderToken.Identifier)
            properties.Add(ParsedProperty.Parse(ref t));

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);
        return properties.ToArray();
    }


    // The main parser for a shader markup language inspired by ShaderLab.
    // Contains a property block, pass blocks, and a fallback.
    static ParsedShader ParseShader(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Shader");
        string name = ParserUtility.QuotedString(ref t);

        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        ShaderProperty[] properties = [];
        if (ParserUtility.PeekKeyword(ref t, "Properties"))
            properties = ParsePropertiesBlock(ref t);

        List<ParsedPass> passes = new();
        while (ParserUtility.PeekKeyword(ref t, "Pass"))
            passes.Add(ParsedPass.Parse(ref t));

        ParserUtility.ExpectKeyword(ref t, "Fallback");
        string fallback = ParserUtility.QuotedString(ref t);

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);

        return new ParsedShader
        {
            Name = name,
            Properties = properties,
            Passes = passes.ToArray(),
            Fallback = fallback,
        };
    }


    public static ParsedShader Parse(string source)
    {
        Tokenizer<ShaderToken> tokenizer = ShaderTokenizer.Create(source);
        return ParseShader(ref tokenizer);
    }
}
