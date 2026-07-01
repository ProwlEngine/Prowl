using System;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class PassStateTests
{
    [Fact]
    public void Empty_LeavesEverythingUnset()
    {
        ParsedPassState s = Parse.State("");

        Assert.Null(s.CullMode);
        Assert.Null(s.DepthFunc);
        Assert.Null(s.WriteMask);
        Assert.Null(s.EnableBlend);
    }


    [Theory]
    [InlineData("Back", FaceCullMode.Back)]
    [InlineData("Front", FaceCullMode.Front)]
    [InlineData("Off", FaceCullMode.None)]
    public void Cull_SetsCullMode(string value, FaceCullMode expected)
    {
        Assert.Equal(expected, Parse.State($"Cull {value}").CullMode);
    }


    [Theory]
    [InlineData("Never", ComparisonKind.Never)]
    [InlineData("Less", ComparisonKind.Less)]
    [InlineData("LessEqual", ComparisonKind.LessEqual)]
    [InlineData("Greater", ComparisonKind.Greater)]
    [InlineData("Always", ComparisonKind.Always)]
    public void ZTest_SetsDepthFunc(string value, ComparisonKind expected)
    {
        Assert.Equal(expected, Parse.State($"ZTest {value}").DepthFunc);
    }


    [Theory]
    [InlineData("On", true)]
    [InlineData("Off", false)]
    public void ZWrite_SetsDepthWriteMask(string value, bool expected)
    {
        Assert.Equal(expected, Parse.State($"ZWrite {value}").DepthWriteMask);
    }


    [Theory]
    [InlineData("On", false)]   // ZClip On  -> depth clamping disabled
    [InlineData("Off", true)]   // ZClip Off -> depth clamping enabled
    public void ZClip_SetsDepthClampInverted(string value, bool expected)
    {
        Assert.Equal(expected, Parse.State($"ZClip {value}").EnableDepthClamp);
    }


    [Fact]
    public void Blend_SetsAllFourFactors()
    {
        ParsedPassState s = Parse.State("Blend SourceAlpha InverseSourceAlpha");

        Assert.Equal(BlendFactor.SourceAlpha, s.BlendSrcRgb);
        Assert.Equal(BlendFactor.SourceAlpha, s.BlendSrcAlpha);
        Assert.Equal(BlendFactor.InverseSourceAlpha, s.BlendDstRgb);
        Assert.Equal(BlendFactor.InverseSourceAlpha, s.BlendDstAlpha);
    }


    [Fact]
    public void BlendRGB_SetsOnlyRgbFactors()
    {
        ParsedPassState s = Parse.State("BlendRGB One Zero");

        Assert.Equal(BlendFactor.One, s.BlendSrcRgb);
        Assert.Equal(BlendFactor.Zero, s.BlendDstRgb);
        Assert.Null(s.BlendSrcAlpha);
        Assert.Null(s.BlendDstAlpha);
    }


    [Fact]
    public void BlendAlpha_SetsOnlyAlphaFactors()
    {
        ParsedPassState s = Parse.State("BlendAlpha One Zero");

        Assert.Equal(BlendFactor.One, s.BlendSrcAlpha);
        Assert.Equal(BlendFactor.Zero, s.BlendDstAlpha);
        Assert.Null(s.BlendSrcRgb);
        Assert.Null(s.BlendDstRgb);
    }


    [Theory]
    [InlineData("Add", BlendFunction.Add)]
    [InlineData("Subtract", BlendFunction.Subtract)]
    [InlineData("Maximum", BlendFunction.Maximum)]
    public void BlendOp_SetsBothBlendFunctions(string value, BlendFunction expected)
    {
        ParsedPassState s = Parse.State($"BlendOp {value}");

        Assert.Equal(expected, s.BlendFunctionRgb);
        Assert.Equal(expected, s.BlendFunctionAlpha);
    }


    [Theory]
    [InlineData("R", ColorWriteMask.Red)]
    [InlineData("G", ColorWriteMask.Green)]
    [InlineData("B", ColorWriteMask.Blue)]
    [InlineData("A", ColorWriteMask.Alpha)]
    [InlineData("RG", ColorWriteMask.Red | ColorWriteMask.Green)]
    [InlineData("RB", ColorWriteMask.Red | ColorWriteMask.Blue)]
    [InlineData("RA", ColorWriteMask.Red | ColorWriteMask.Alpha)]
    [InlineData("GB", ColorWriteMask.Green | ColorWriteMask.Blue)]
    [InlineData("GA", ColorWriteMask.Green | ColorWriteMask.Alpha)]
    [InlineData("BA", ColorWriteMask.Blue | ColorWriteMask.Alpha)]
    [InlineData("RGB", ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue)]
    [InlineData("RGBA", ColorWriteMask.All)]
    public void ColorMask_ParsesChannels(string mask, ColorWriteMask expected)
    {
        Assert.Equal(expected, Parse.State($"ColorMask {mask}").WriteMask);
    }


    [Fact]
    public void ColorMask_InvalidChannel_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Parse.State("ColorMask RGBX"));
    }


    [Theory]
    [InlineData("On", true)]
    [InlineData("Off", false)]
    public void AlphaToMask_Sets(string value, bool expected)
    {
        Assert.Equal(expected, Parse.State($"AlphaToMask {value}").AlphaToMask);
    }


    [Fact]
    public void Offset_SetsFillAndNegativeValues()
    {
        ParsedPassState s = Parse.State("Offset -1 -2");

        Assert.True(s.EnablePolygonOffsetFill);
        Assert.Equal(-1f, s.PolygonOffsetFactor);
        Assert.Equal(-2f, s.PolygonOffsetUnits);
    }


    [Fact]
    public void MultipleCommands_Combine()
    {
        ParsedPassState s = Parse.State("""
            Cull Front
            ZWrite Off
            ZTest Greater
            """);

        Assert.Equal(FaceCullMode.Front, s.CullMode);
        Assert.False(s.DepthWriteMask);
        Assert.Equal(ComparisonKind.Greater, s.DepthFunc);
    }


    [Fact]
    public void StopsAtUnknownIdentifier()
    {
        // "Banana" is not a render-state command, so parsing stops there and Cull is captured.
        ParsedPassState s = Parse.State("Cull Back Banana");

        Assert.Equal(FaceCullMode.Back, s.CullMode);
    }


    [Fact]
    public void UnknownEnumValue_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Parse.State("ZTest Baloney"));
    }


    [Fact]
    public void InvalidNumber_ReportsLineAndColumn()
    {
        // A hex literal is a valid Number token but not a valid float, and it sits on line 2,
        // so the diagnostic must point there rather than line 1.
        ParseException ex = Assert.Throws<ParseException>(() => Parse.State("""
            Cull Back
            Offset 0xFF 0
            """));

        Assert.Equal(2, ex.Line);
        Assert.Contains("number", ex.Message);
    }
}
