using System;
using System.Reflection;

using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class PassStateTests
{
    [Fact]
    public void Empty_LeavesEverythingUnset()
    {
        PassState s = Parse.State("");

        Assert.Equal(PassStateFields.None, s.Set);
    }


    [Theory]
    [InlineData("Back", FaceCullMode.Back)]
    [InlineData("Front", FaceCullMode.Front)]
    [InlineData("Off", FaceCullMode.None)]
    public void Cull_SetsCullMode(string value, FaceCullMode expected)
    {
        Assert.Equal(expected, Parse.State($"Cull {value}").Raster.CullMode);
    }


    [Theory]
    [InlineData("Never", ComparisonKind.Never)]
    [InlineData("Less", ComparisonKind.Less)]
    [InlineData("LessEqual", ComparisonKind.LessEqual)]
    [InlineData("Greater", ComparisonKind.Greater)]
    [InlineData("Always", ComparisonKind.Always)]
    public void ZTest_SetsDepthFunc(string value, ComparisonKind expected)
    {
        Assert.Equal(expected, Parse.State($"ZTest {value}").DepthStencil.DepthComparison);
    }


    [Theory]
    [InlineData("On", true)]
    [InlineData("Off", false)]
    public void ZWrite_SetsDepthWriteMask(string value, bool expected)
    {
        Assert.Equal(expected, Parse.State($"ZWrite {value}").DepthStencil.DepthWriteEnabled);
    }


    [Theory]
    [InlineData("On", true)]
    [InlineData("Off", false)]
    public void ZClip_SetsDepthClip(string value, bool expected)
    {
        Assert.Equal(expected, Parse.State($"ZClip {value}").Raster.DepthClipEnabled);
    }


    [Fact]
    public void Blend_SetsAllFourFactors()
    {
        PassState s = Parse.State("Blend SourceAlpha InverseSourceAlpha");

        Assert.Equal(BlendFactor.SourceAlpha, s.Blend.SourceColorFactor);
        Assert.Equal(BlendFactor.SourceAlpha, s.Blend.SourceAlphaFactor);
        Assert.Equal(BlendFactor.InverseSourceAlpha, s.Blend.DestinationColorFactor);
        Assert.Equal(BlendFactor.InverseSourceAlpha, s.Blend.DestinationAlphaFactor);
    }


    [Fact]
    public void BlendRGB_SetsOnlyRgbFactors()
    {
        PassState s = Parse.State("BlendRGB One Zero");

        Assert.Equal(BlendFactor.One, s.Blend.SourceColorFactor);
        Assert.Equal(BlendFactor.Zero, s.Blend.DestinationColorFactor);
        Assert.False(s.Set.HasFlag(PassStateFields.SourceAlphaFactor));
        Assert.False(s.Set.HasFlag(PassStateFields.DestinationAlphaFactor));
    }


    [Fact]
    public void BlendAlpha_SetsOnlyAlphaFactors()
    {
        PassState s = Parse.State("BlendAlpha One Zero");

        Assert.Equal(BlendFactor.One, s.Blend.SourceAlphaFactor);
        Assert.Equal(BlendFactor.Zero, s.Blend.DestinationAlphaFactor);
        Assert.False(s.Set.HasFlag(PassStateFields.SourceColorFactor));
        Assert.False(s.Set.HasFlag(PassStateFields.DestinationColorFactor));
    }


    [Theory]
    [InlineData("Add", BlendFunction.Add)]
    [InlineData("Subtract", BlendFunction.Subtract)]
    [InlineData("Maximum", BlendFunction.Maximum)]
    public void BlendOp_SetsBothBlendFunctions(string value, BlendFunction expected)
    {
        PassState s = Parse.State($"BlendOp {value}");

        Assert.Equal(expected, s.Blend.ColorFunction);
        Assert.Equal(expected, s.Blend.AlphaFunction);
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
        Assert.Equal(expected, Parse.State($"ColorMask {mask}").Blend.ColorWriteMask);
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
        Assert.Equal(expected, Parse.State($"AlphaToMask {value}").AlphaToCoverage);
    }


    [Fact]
    public void Offset_SetsFillAndNegativeValues()
    {
        PassState s = Parse.State("Offset -1 -2");

        Assert.True(s.Raster.DepthBiasEnabled);
        Assert.Equal(-1f, s.Raster.DepthBiasSlopeFactor);
        Assert.Equal(-2f, s.Raster.DepthBiasConstantFactor);
    }


    [Fact]
    public void Offset_AppliesDepthBiasToRasterizerState()
    {
        PassState s = Parse.State("Offset -1 -1");

        RasterizerStateDescription r = s.ToRasterizerState(RasterizerStateDescription.Default);

        Assert.True(r.DepthBiasEnabled);
        Assert.Equal(-1f, r.DepthBiasSlopeFactor);
        Assert.Equal(-1f, r.DepthBiasConstantFactor);
    }


    [Fact]
    public void NoOffset_LeavesDepthBiasOff()
    {
        RasterizerStateDescription r = Parse.State("").ToRasterizerState(RasterizerStateDescription.Default);

        Assert.False(r.DepthBiasEnabled);
        Assert.Equal(RasterizerStateDescription.Default, r);
    }


    [Fact]
    public void MultipleCommands_Combine()
    {
        PassState s = Parse.State("""
            Cull Front
            ZWrite Off
            ZTest Greater
            """);

        Assert.Equal(FaceCullMode.Front, s.Raster.CullMode);
        Assert.False(s.DepthStencil.DepthWriteEnabled);
        Assert.Equal(ComparisonKind.Greater, s.DepthStencil.DepthComparison);
    }


    [Fact]
    public void StopsAtUnknownIdentifier()
    {
        // "Banana" is not a render-state command, so parsing stops there and Cull is captured.
        PassState s = Parse.State("Cull Back Banana");

        Assert.Equal(FaceCullMode.Back, s.Raster.CullMode);
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


    [Fact]
    public void Equals_SameFields_AreEqualWithSameHash()
    {
        PassState a = Parse.State("Cull Front\nZWrite Off\nBlend SourceAlpha InverseSourceAlpha");
        PassState b = Parse.State("Cull Front\nZWrite Off\nBlend SourceAlpha InverseSourceAlpha");

        Assert.NotSame(a, b);
        Assert.Equal(a, b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }


    [Fact]
    public void Equals_EmptyStates_AreEqual()
    {
        Assert.Equal(new PassState(), new PassState());
        Assert.Equal(new PassState().GetHashCode(), new PassState().GetHashCode());
    }


    [Fact]
    public void Equals_UnsetVersusSet_AreNotEqual()
    {
        Assert.NotEqual(new PassState(), Parse.State("Cull Back"));
        Assert.NotEqual(Parse.State("Cull Back"), Parse.State("Cull Off"));
        Assert.NotEqual(Parse.State("ZTest Less"), Parse.State("ZTest Always"));
        Assert.NotEqual(Parse.State("ColorMask RGB"), Parse.State("ColorMask RGBA"));
        Assert.NotEqual(Parse.State("Stencil { Ref 1 }"), Parse.State("Stencil { Ref 2 }"));
    }


    [Fact]
    public void Equals_Null_IsFalse()
    {
        Assert.False(new PassState().Equals(null));
    }


    [Fact]
    public void Equals_IgnoresValuesOutsideTheSetMask()
    {
        PassState a = new() { Set = PassStateFields.CullMode, Raster = new() { CullMode = FaceCullMode.Front, DepthBiasEnabled = true } };
        PassState b = new() { Set = PassStateFields.CullMode, Raster = new() { CullMode = FaceCullMode.Front }, Blend = new() };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }


    [Fact]
    public void EveryFlag_IsCopiedByApplyAndCompared()
    {
        PassState source = new()
        {
            Raster = (RasterizerStateDescription)Fill(typeof(RasterizerStateDescription)),
            DepthStencil = (DepthStencilStateDescription)Fill(typeof(DepthStencilStateDescription)),
            Blend = (BlendAttachmentDescription)Fill(typeof(BlendAttachmentDescription)),
            AlphaToCoverage = true,
        };

        foreach (PassStateFields flag in Enum.GetValues<PassStateFields>())
        {
            if (flag == PassStateFields.None)
                continue;

            source.Set = flag;
            PassState applied = new PassState().Apply(source);
            PassState flagOnly = new() { Set = flag };

            Assert.False(flagOnly.Equals(applied), $"{flag} is not copied by Apply or not compared by Equals.");
        }
    }


    [Fact]
    public void Apply_SetsUnionOfBothMasks()
    {
        PassState combined = Parse.State("Cull Off").Apply(Parse.State("ZWrite Off"));

        Assert.Equal(PassStateFields.CullMode | PassStateFields.DepthWrite, combined.Set);
    }


    [Fact]
    public void Apply_ResultEqualsHandBuiltState()
    {
        PassState combined = Parse.State("Cull Off").Apply(Parse.State("Cull Back\nZWrite Off"));

        Assert.Equal(Parse.State("Cull Off\nZWrite Off"), combined);
    }


    private static object Fill(Type type)
    {
        if (type == typeof(bool))
            return true;
        if (type == typeof(float))
            return 1.5f;
        if (type == typeof(byte))
            return (byte)7;
        if (type == typeof(uint))
            return 7u;
        if (type.IsEnum)
        {
            foreach (object value in Enum.GetValues(type))
            {
                if (Convert.ToInt64(value) != 0)
                    return value;
            }
        }
        if (type.IsValueType && !type.IsEnum)
        {
            object instance = Activator.CreateInstance(type)!;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(instance, Fill(field.FieldType));
            return instance;
        }

        throw new NotSupportedException(type.Name);
    }
}
