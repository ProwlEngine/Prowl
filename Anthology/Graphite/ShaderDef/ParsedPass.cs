using System.Collections.Generic;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


public class ParsedPass
{
    public string Name = "";

    public Dictionary<string, string>? Tags = null;

    public required ParsedPassState State;

    public required ParsedShaderSource Source;


    static string ParsePassName(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Name");
        return ParserUtility.QuotedString(ref t);
    }


    static Dictionary<string, string> ParsePassTags(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Tags");
        t.Expect(ShaderToken.OpenBrace);

        Dictionary<string, string> tags = new();

        while (t.Peek().Kind == ShaderToken.String)
        {
            string key = ParserUtility.QuotedString(ref t);
            t.Expect(ShaderToken.Equals);
            string value = ParserUtility.QuotedString(ref t);
            tags[key] = value;
        }

        t.Expect(ShaderToken.CloseBrace);
        return tags;
    }


    // Parses a pass block.
    public static ParsedPass Parse(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Pass");

        // Optional pass index.
        if (t.Peek().Kind is ShaderToken.Number or ShaderToken.Minus)
            ParserUtility.Integer(ref t);

        t.Expect(ShaderToken.OpenBrace);

        string name = "";
        if (ParserUtility.PeekKeyword(ref t, "Name"))
            name = ParsePassName(ref t);

        Dictionary<string, string>? tags = null;
        if (ParserUtility.PeekKeyword(ref t, "Tags"))
            tags = ParsePassTags(ref t);

        ParsedPassState state = ParsedPassState.Parse(ref t);
        ParsedShaderSource source = ParsedShaderSource.Parse(ref t);

        t.Expect(ShaderToken.CloseBrace);

        return new ParsedPass
        {
            Name = name,
            Tags = tags,
            State = state,
            Source = source
        };
    }
}
