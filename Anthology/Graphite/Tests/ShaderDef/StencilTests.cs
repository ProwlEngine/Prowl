using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


// Stencil is reached through a render-state command, so it is driven via ParsedPassState.Parse.
public class StencilTests
{
    [Fact]
    public void Ref_SetsFrontAndBack()
    {
        ParsedPassState s = Parse.State("""Stencil { Ref 3 }""");

        Assert.Equal(3, s.StencilFrontRef);
        Assert.Equal(3, s.StencilBackRef);
    }


    [Fact]
    public void ReadAndWriteMask_SetBothFaces()
    {
        ParsedPassState s = Parse.State("""
            Stencil
            {
                ReadMask 15
                WriteMask 7
            }
            """);

        Assert.Equal(15u, s.StencilFrontReadMask);
        Assert.Equal(15u, s.StencilBackReadMask);
        Assert.Equal(7u, s.StencilFrontWriteMask);
        Assert.Equal(7u, s.StencilBackWriteMask);
    }


    [Fact]
    public void Comp_SetsBothFaces()
    {
        ParsedPassState s = Parse.State("""Stencil { Comp Equal }""");

        Assert.Equal(ComparisonKind.Equal, s.StencilFrontFunc);
        Assert.Equal(ComparisonKind.Equal, s.StencilBackFunc);
    }


    [Fact]
    public void CompFrontAndCompBack_SetIndividualFaces()
    {
        ParsedPassState s = Parse.State("""
            Stencil
            {
                CompFront Less
                CompBack Greater
            }
            """);

        Assert.Equal(ComparisonKind.Less, s.StencilFrontFunc);
        Assert.Equal(ComparisonKind.Greater, s.StencilBackFunc);
    }


    [Fact]
    public void PassFailZFail_SetBothFaces()
    {
        ParsedPassState s = Parse.State("""
            Stencil
            {
                Pass Replace
                Fail Keep
                ZFail Invert
            }
            """);

        Assert.Equal(StencilOperation.Replace, s.StencilFrontPassOp);
        Assert.Equal(StencilOperation.Replace, s.StencilBackPassOp);
        Assert.Equal(StencilOperation.Keep, s.StencilFrontFailOp);
        Assert.Equal(StencilOperation.Keep, s.StencilBackFailOp);
        Assert.Equal(StencilOperation.Invert, s.StencilFrontDepthFailOp);
        Assert.Equal(StencilOperation.Invert, s.StencilBackDepthFailOp);
    }


    [Fact]
    public void FrontBackVariants_SetIndividualFaces()
    {
        ParsedPassState s = Parse.State("""
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

        Assert.Equal(StencilOperation.Replace, s.StencilFrontPassOp);
        Assert.Equal(StencilOperation.Keep, s.StencilBackPassOp);
        Assert.Equal(StencilOperation.Zero, s.StencilFrontFailOp);
        Assert.Equal(StencilOperation.Invert, s.StencilBackFailOp);
        Assert.Equal(StencilOperation.IncrementAndClamp, s.StencilFrontDepthFailOp);
        Assert.Equal(StencilOperation.DecrementAndClamp, s.StencilBackDepthFailOp);
    }


    [Fact]
    public void EmptyBlock_LeavesUnset()
    {
        ParsedPassState s = Parse.State("""Stencil { }""");

        Assert.Null(s.StencilFrontRef);
        Assert.Null(s.StencilFrontFunc);
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
