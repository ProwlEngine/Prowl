using System;

using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class ProgramBlockTests
{
    [Fact]
    public void DoesNotTokenizeBracesOrKeywords()
    {
        // Braces, comments, and ShaderDef keywords inside the block are part of the raw body,
        // not interpreted by the outer tokenizer.
        string slang = Parse.Program("""
            GLSLPROGRAM
            struct V { float4 Pass; }
            // Cull Back ZTest Always
            ENDGLSL
            """);

        Assert.Contains("struct V { float4 Pass; }", slang);
        Assert.Contains("// Cull Back ZTest Always", slang);
    }


    [Fact]
    public void MissingEndMarker_Throws()
    {
        ParseException ex = Assert.Throws<ParseException>(() => Parse.Program("""
            GLSLPROGRAM
            float4 vsMain() { return 0; }
            """));

        Assert.Contains("ENDGLSL", ex.Message);
    }
}
