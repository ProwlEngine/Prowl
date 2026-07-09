using Prowl.Crumb;

using Prowl.Graphite.ShaderDef.Compiler;

namespace Prowl.Graphite.ShaderDef.Tests;


// Centralizes the ref-struct tokenizer boilerplate so each component can be driven in isolation
// straight from a source string.
internal static class Parse
{
    public static ShaderProperty Property(string source) => ShaderParser.ParseProperty(source);


    public static PassState State(string source) => ShaderParser.ParsePassState(source);


    public static string Slang(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return ParserUtility.SlangProgram(ref t);
    }


    public static ShaderPass Pass(string source) => ShaderParser.ParsePass(source);


    public static ShaderDefinition Shader(string source) => ShaderParser.Parse(source);
}
