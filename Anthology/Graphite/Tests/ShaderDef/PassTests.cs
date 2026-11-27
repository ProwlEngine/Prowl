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
                Stencil { Ref 1 Comp Equal }
                GLSLPROGRAM
                float4 vsMain() : SV_Position { return 0; }
                ENDGLSL
            }
            """);

        Assert.Equal("Forward", pass.Name);
        Assert.NotNull(pass.Tags);
        Assert.Equal(FaceCullMode.Back, pass.State.Raster.CullMode);
        Assert.Equal("float4 vsMain() : SV_Position { return 0; }", pass.Source);

        DepthStencilStateDescription stencil = pass.State.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyGreaterEqual);
        Assert.Equal(255, stencil.StencilReadMask);
        Assert.Equal(255, stencil.StencilWriteMask);
    }
}
