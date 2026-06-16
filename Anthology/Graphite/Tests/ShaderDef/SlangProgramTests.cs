using System;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class SlangProgramTests
{
    [Fact]
    public void CapturesBodyVerbatim()
    {
        string slang = Parse.Slang("""
            SLANGPROGRAM
            float4 vsMain() : SV_Position { return 0; }
            ENDSLANG
            """);

        Assert.Equal("float4 vsMain() : SV_Position { return 0; }", slang);
    }


    [Fact]
    public void DoesNotTokenizeBracesOrKeywords()
    {
        // Braces, comments, and ShaderDef keywords inside the block are part of the raw body,
        // not interpreted by the outer tokenizer.
        string slang = Parse.Slang("""
            SLANGPROGRAM
            struct V { float4 Pass; }
            // Cull Back ZTest Always
            ENDSLANG
            """);

        Assert.Contains("struct V { float4 Pass; }", slang);
        Assert.Contains("// Cull Back ZTest Always", slang);
    }


    [Fact]
    public void MissingEndMarker_Throws()
    {
        ParseException ex = Assert.Throws<ParseException>(() => Parse.Slang("""
            SLANGPROGRAM
            float4 vsMain() { return 0; }
            """));

        Assert.Contains("ENDSLANG", ex.Message);
    }


    [Fact]
    public void MarkersAreCaseSensitive()
    {
        // Lowercase markers are not recognized, so no SlangProgram token is produced.
        Assert.ThrowsAny<Exception>(() => Parse.Slang("""
            slangprogram
            float4 vsMain() { return 0; }
            endslang
            """));
    }
}
