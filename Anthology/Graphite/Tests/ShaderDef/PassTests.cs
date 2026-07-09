using Xunit;

using Prowl.Graphite.ShaderDef.Compiler;

namespace Prowl.Graphite.ShaderDef.Tests;


public class PassTests
{
    [Fact]
    public void FullPass_ParsesAllParts()
    {
        ShaderPass pass = Parse.Pass("""
            Pass 0
            {
                Name "Forward"
                Tags { "LightMode" = "ForwardBase" }
                Cull Back
                SLANGPROGRAM
                float4 vsMain() : SV_Position { return 0; }
                ENDSLANG
            }
            """);

        Assert.Equal("Forward", pass.Name);
        Assert.NotNull(pass.Tags);
        Assert.Equal(FaceCullMode.Back, pass.State.CullMode);
        Assert.Equal("float4 vsMain() : SV_Position { return 0; }", pass.InlineSlang);
    }


    [Fact]
    public void WithoutOptionalParts_UsesDefaults()
    {
        ShaderPass pass = Parse.Pass("""
            Pass
            {
                SLANGPROGRAM
                void main() {}
                ENDSLANG
            }
            """);

        Assert.Equal("", pass.Name);
        Assert.Null(pass.Tags);
        Assert.Equal("void main() {}", pass.InlineSlang);
    }


    [Fact]
    public void NameOnly_Parsed()
    {
        ShaderPass pass = Parse.Pass("""
            Pass
            {
                Name "ShadowCaster"
                SLANGPROGRAM
                void main() {}
                ENDSLANG
            }
            """);

        Assert.Equal("ShadowCaster", pass.Name);
    }


    [Fact]
    public void MisspelledCommand_ThrowsUnknownCommand()
    {
        ParseException ex = Assert.Throws<ParseException>(() => Parse.Pass("""
            Pass
            {
                Culll Back
                SLANGPROGRAM void main() {} ENDSLANG
            }
            """));

        Assert.Contains("Unknown command", ex.Message);
        Assert.Contains("Culll", ex.Message);
    }


    [Fact]
    public void MissingSlangProgram_ThrowsDomainMessage()
    {
        ParseException ex = Assert.Throws<ParseException>(() => Parse.Pass("""
            Pass
            {
                Cull Back
            }
            """));

        Assert.Contains("SLANGPROGRAM", ex.Message);
    }
}
