using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


public class ParsedShaderSource
{
    public required string ShaderSourceFile;
    public required string VertexEntrypoint;
    public required string FragmentEntrypoint;


    // Parses: ShaderSource "File" { Vertex "vs" Fragment "fs" }
    public static ParsedShaderSource Parse(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "ShaderSource");
        string shaderSource = ParserUtility.QuotedString(ref t);

        t.Expect(ShaderToken.OpenBrace);

        ParserUtility.ExpectKeyword(ref t, "Vertex");
        string vertexEntrypoint = ParserUtility.QuotedString(ref t);

        ParserUtility.ExpectKeyword(ref t, "Fragment");
        string fragmentEntrypoint = ParserUtility.QuotedString(ref t);

        t.Expect(ShaderToken.CloseBrace);

        return new ParsedShaderSource
        {
            ShaderSourceFile = shaderSource,
            VertexEntrypoint = vertexEntrypoint,
            FragmentEntrypoint = fragmentEntrypoint
        };
    }
}
