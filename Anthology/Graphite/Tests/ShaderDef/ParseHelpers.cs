using Prowl.Crumb;

namespace Prowl.Graphite.ShaderDef.Tests;


// Centralizes the ref-struct tokenizer boilerplate so each component can be driven in isolation
// straight from a source string.
internal static class Parse
{
    public static ShaderProperty Property(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return ParsedProperty.Parse(ref t);
    }


    public static ParsedPassState State(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return ParsedPassState.Parse(ref t);
    }


    public static string Slang(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return ParserUtility.SlangProgram(ref t);
    }


    public static ParsedPass Pass(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return ParsedPass.Parse(ref t);
    }


    public static ParsedShader Shader(string source) => ParsedShader.Parse(source);
}
