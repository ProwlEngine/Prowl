using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

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
                GLSLPROGRAM
                float4 vsMain() : SV_Position { return 0; }
                ENDGLSL
            }
            """);

        Assert.Equal("Forward", pass.Name);
        Assert.NotNull(pass.Tags);
        Assert.Equal(FaceCullMode.Back, pass.State.Raster.CullMode);
        Assert.Equal("float4 vsMain() : SV_Position { return 0; }", pass.Source);
    }



    [Fact]
    public void MisspelledCommand_ThrowsUnknownCommand()
    {
        ParseException ex = Assert.Throws<ParseException>(() => Parse.Pass("""
            Pass
            {
                Culll Back
                GLSLPROGRAM void main() {} ENDGLSL
            }
            """));

        Assert.Contains("Unknown command", ex.Message);
        Assert.Contains("Culll", ex.Message);
    }


    [Fact]
    public void MissingProgram_ThrowsDomainMessage()
    {
        ParseException ex = Assert.Throws<ParseException>(() => Parse.Pass("""
            Pass
            {
                Cull Back
            }
            """));

        Assert.Contains("GLSLPROGRAM", ex.Message);
    }
}
