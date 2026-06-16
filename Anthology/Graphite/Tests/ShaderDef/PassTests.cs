using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class PassTests
{
    [Fact]
    public void FullPass_ParsesAllParts()
    {
        ParsedPass pass = Parse.Pass("""
            Pass 0
            {
                Name "Forward"
                Tags { "LightMode" = "ForwardBase" }
                Cull Back
                ShaderSource "Standard.slang" { Vertex "vs" Fragment "fs" }
            }
            """);

        Assert.Equal("Forward", pass.Name);
        Assert.NotNull(pass.Tags);
        Assert.Equal(FaceCullMode.Back, pass.State.CullMode);
        Assert.Equal("Standard.slang", pass.Source.ShaderSourceFile);
    }


    [Fact]
    public void WithoutOptionalParts_UsesDefaults()
    {
        ParsedPass pass = Parse.Pass("""
            Pass
            {
                ShaderSource "s" { Vertex "v" Fragment "f" }
            }
            """);

        Assert.Equal("", pass.Name);
        Assert.Null(pass.Tags);
        Assert.Equal("v", pass.Source.VertexEntrypoint);
    }


    [Fact]
    public void NameOnly_Parsed()
    {
        ParsedPass pass = Parse.Pass("""
            Pass
            {
                Name "ShadowCaster"
                ShaderSource "s" { Vertex "v" Fragment "f" }
            }
            """);

        Assert.Equal("ShadowCaster", pass.Name);
    }
}
