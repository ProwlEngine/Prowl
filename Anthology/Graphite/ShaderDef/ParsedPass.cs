using System.Collections.Generic;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// A parsed pass that encapsulates render state, identification metadata, and source shader files.
/// </summary>
public class ParsedPass
{
    /// <summary>
    /// The name of this pass, or blank if no name was defined.
    /// </summary>
    public string Name = "";

    /// <summary>
    /// The tag key-value pairs for this pass, defined in source as a list of: <code>{ "Key" = "Value" "Key2" = "Value2" }</code>
    /// </summary>
    public Dictionary<string, string>? Tags = null;

    /// <summary>
    /// The pass state, encapsulating rasterizer settings, blend, depth, stencil, and more.
    /// </summary>
    public required ParsedPassState State;

    /// <summary>
    /// The raw Slang source embedded between SLANGPROGRAM and ENDSLANG. Slang derives its own
    /// entrypoints, so no explicit vertex/fragment stages are declared here.
    /// </summary>
    public required string InlineSlang;


    static string ParsePassName(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Name");
        return ParserUtility.QuotedString(ref t);
    }


    static Dictionary<string, string> ParsePassTags(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Tags");
        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        Dictionary<string, string> tags = new();

        while (t.Peek().Kind == ShaderToken.String)
        {
            Token<ShaderToken> keyToken = t.Peek();
            string key = ParserUtility.QuotedString(ref t);

            if (tags.ContainsKey(key))
                throw Exceptions.Duplicate("tag key", key, keyToken);

            ParserUtility.Expect(ref t, ShaderToken.Equals);
            string value = ParserUtility.QuotedString(ref t);
            tags[key] = value;
        }

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);
        return tags;
    }


    /// <summary>
    /// Parses a pass block.
    /// </summary>
    public static ParsedPass Parse(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return Parse(ref t);
    }


    internal static ParsedPass Parse(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Pass");

        // Optional pass index.
        if (t.Peek().Kind is ShaderToken.Number or ShaderToken.Minus)
            ParserUtility.Integer(ref t);

        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        string name = "";
        if (ParserUtility.PeekKeyword(ref t, "Name"))
            name = ParsePassName(ref t);

        Dictionary<string, string>? tags = null;
        if (ParserUtility.PeekKeyword(ref t, "Tags"))
            tags = ParsePassTags(ref t);

        ParsedPassState state = ParsedPassState.Parse(ref t);

        // State parsing stops at the first identifier it doesn't recognize. A SLANGPROGRAM block is
        // a block token, not an identifier, so a leftover identifier here is a misspelled command.
        Token<ShaderToken> afterState = t.Peek();
        if (afterState.Kind == ShaderToken.Identifier)
            throw Exceptions.UnknownCommand(ParserUtility.Text(ref t, afterState), afterState);

        string inlineSlang = ParserUtility.SlangProgram(ref t);

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);

        return new ParsedPass
        {
            Name = name,
            Tags = tags,
            State = state,
            InlineSlang = inlineSlang
        };
    }
}
