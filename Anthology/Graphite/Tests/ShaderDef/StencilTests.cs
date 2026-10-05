using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


// Stencil is reached through a render-state command, so it is driven via ShaderParser.ParsePassState.
public class StencilTests
{
    [Fact]
    public void Block_EnablesStencilTest()
    {
        PassState s = Parse.State("""Stencil { Ref 3 }""");

        Assert.True(s.DepthStencil.StencilTestEnabled);
        Assert.True(s.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyLessEqual).StencilTestEnabled);
    }


    [Fact]
    public void ReadAndWriteMask_Set()
    {
        PassState s = Parse.State("""
            Stencil
            {
                ReadMask 15
                WriteMask 7
            }
            """);

        Assert.Equal(15, s.DepthStencil.StencilReadMask);
        Assert.Equal(7, s.DepthStencil.StencilWriteMask);
    }


    [Fact]
    public void Comp_SetsBothFaces()
    {
        PassState s = Parse.State("""Stencil { Comp Equal }""");

        Assert.Equal(ComparisonKind.Equal, s.DepthStencil.StencilFront.Comparison);
        Assert.Equal(ComparisonKind.Equal, s.DepthStencil.StencilBack.Comparison);
    }


    [Fact]
    public void CompFrontAndCompBack_SetIndividualFaces()
    {
        PassState s = Parse.State("""
            Stencil
            {
                CompFront Less
                CompBack Greater
            }
            """);

        Assert.Equal(ComparisonKind.Less, s.DepthStencil.StencilFront.Comparison);
        Assert.Equal(ComparisonKind.Greater, s.DepthStencil.StencilBack.Comparison);
    }


    [Fact]
    public void PassFailZFail_SetBothFaces()
    {
        PassState s = Parse.State("""
            Stencil
            {
                Pass Replace
                Fail Keep
                ZFail Invert
            }
            """);

        Assert.Equal(StencilOperation.Replace, s.DepthStencil.StencilFront.Pass);
        Assert.Equal(StencilOperation.Replace, s.DepthStencil.StencilBack.Pass);
        Assert.Equal(StencilOperation.Keep, s.DepthStencil.StencilFront.Fail);
        Assert.Equal(StencilOperation.Keep, s.DepthStencil.StencilBack.Fail);
        Assert.Equal(StencilOperation.Invert, s.DepthStencil.StencilFront.DepthFail);
        Assert.Equal(StencilOperation.Invert, s.DepthStencil.StencilBack.DepthFail);
    }


    [Fact]
    public void FrontBackVariants_SetIndividualFaces()
    {
        PassState s = Parse.State("""
            Stencil
            {
                PassFront Replace
                PassBack Keep
                FailFront Zero
                FailBack Invert
                ZFailFront IncrementAndClamp
                ZFailBack DecrementAndClamp
            }
            """);

        Assert.Equal(StencilOperation.Replace, s.DepthStencil.StencilFront.Pass);
        Assert.Equal(StencilOperation.Keep, s.DepthStencil.StencilBack.Pass);
        Assert.Equal(StencilOperation.Zero, s.DepthStencil.StencilFront.Fail);
        Assert.Equal(StencilOperation.Invert, s.DepthStencil.StencilBack.Fail);
        Assert.Equal(StencilOperation.IncrementAndClamp, s.DepthStencil.StencilFront.DepthFail);
        Assert.Equal(StencilOperation.DecrementAndClamp, s.DepthStencil.StencilBack.DepthFail);
    }


    [Fact]
    public void EmptyBlock_OnlyEnablesStencilTest()
    {
        PassState s = Parse.State("""Stencil { }""");

        Assert.Equal(PassStateFields.StencilTest, s.Set);
    }


    [Fact]
    public void UnknownStencilCommand_Throws()
    {
        ParseException ex = Assert.Throws<ParseException>(
            () => Parse.State("""Stencil { Reff 3 }"""));

        Assert.Contains("Unknown command", ex.Message);
        Assert.Contains("Reff", ex.Message);
    }
}
