using System.Collections.Generic;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// The parsed representation of a shaderdef shader description markdown file.
/// </summary>
public class ParsedShader
{
    /// <summary>
    /// The identifier name of this shader. Required and never null.
    /// </summary>
    public string? Name;

    /// <summary>
    /// Metadata for a fallback shader name that a renderer can use. Empty if none is defined.
    /// </summary>
    public string? Fallback;

    /// <summary>
    /// A list of default properties requested by the markdown for shader resources or uniforms
    /// </summary>
    public ShaderProperty[]? Properties;

    /// <summary>
    /// A list of executable render passes present in the shader.
    /// </summary>
    public ParsedPass[]? Passes;


    // Parses a property block: Properties { ... }
    static ShaderProperty[] ParsePropertiesBlock(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Properties");
        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        List<ShaderProperty> properties = new();
        HashSet<string> names = new();

        while (t.Peek().Kind == ShaderToken.Identifier)
        {
            Token<ShaderToken> nameToken = t.Peek();
            ShaderProperty property = ParsedProperty.Parse(ref t);

            if (!names.Add(property.Name))
                throw Exceptions.Duplicate("property", property.Name, nameToken);

            properties.Add(property);
        }

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);
        return properties.ToArray();
    }


    // The main parser for a shader markup language inspired by ShaderLab.
    // Contains a property block, pass blocks, and a fallback.
    static ParsedShader ParseShader(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Shader");
        string name = ParserUtility.QuotedString(ref t);

        if (string.IsNullOrWhiteSpace(name))
            throw Exceptions.NoName(t.Peek());

        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        ShaderProperty[] properties = [];
        if (ParserUtility.PeekKeyword(ref t, "Properties"))
            properties = ParsePropertiesBlock(ref t);

        List<ParsedPass> passes = new();
        HashSet<string> passNames = new();
        while (ParserUtility.PeekKeyword(ref t, "Pass"))
        {
            Token<ShaderToken> passToken = t.Peek();
            ParsedPass pass = ParsedPass.Parse(ref t);

            // Pass names are optional, so only named passes are checked for collisions.
            if (pass.Name.Length > 0 && !passNames.Add(pass.Name))
                throw Exceptions.Duplicate("pass name", pass.Name, passToken);

            passes.Add(pass);
        }

        if (passes.Count == 0)
            throw Exceptions.NoPasses(t.Peek());

        string fallback = "";
        if (ParserUtility.PeekKeyword(ref t, "Fallback"))
            fallback = ParserUtility.QuotedString(ref t);

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);

        Token<ShaderToken> trailing = t.Peek();
        if (trailing.Kind != ShaderToken.EndOfFile)
            throw Exceptions.TrailingContent(ParserUtility.Text(ref t, trailing), trailing);

        return new ParsedShader
        {
            Name = name,
            Properties = properties,
            Passes = passes.ToArray(),
            Fallback = fallback,
        };
    }


    /// <summary>
    /// Parses a full .shaderdef markdown file.
    /// </summary>
    public static ParsedShader Parse(string source)
    {
        Tokenizer<ShaderToken> tokenizer = ShaderTokenizer.Create(source);
        return ParseShader(ref tokenizer);
    }
}
