using System;
using System.Linq;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class PassKeywordTests : IDisposable
{
    private const string Source = """
        Shader "Test/Keywords"
        {
            Pass
            {
                SLANGPROGRAM
                void main() {}
                ENDSLANG
            }
        }
        """;

    private static readonly VariantSpace[] s_axes =
    [
        new("SKINNED", "bool", ["false", "true"]),
        new("ALPHA_MODE", "AlphaMode", ["Opaque", "Cutout", "Transparent"], true),
    ];

    private readonly GraphicsDevice _device;
    private readonly ShaderPass _pass;


    public PassKeywordTests()
    {
        _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(false));

        ShaderDefinition definition = Parse.Shader(Source);
        _pass = definition.Passes![0];

        Variant[] variants = VariantCombos.Generate(s_axes).Select(combo => new Variant(combo, [])).ToArray();
        definition.Create(_device, new ShaderSnapshot { Passes = [new PassSnapshot { Axes = s_axes, Variants = variants }] });
    }


    public void Dispose()
    {
        _device.Dispose();
    }


    private static Keyword K(string name, string value) => new(name, value);


    private string Active(string axis) => _pass.ActiveVariant.Keywords.First(k => k.Name == axis).Value;


    [Fact]
    public void Axes_ExposesBoundAxesInOrder()
    {
        Assert.Equal(2, _pass.Axes.Count);
        Assert.Equal("SKINNED", _pass.Axes[0].Name);
        Assert.Equal("ALPHA_MODE", _pass.Axes[1].Name);
        Assert.True(_pass.Axes[1].IsEnum);
    }


    [Fact]
    public void Axes_BeforeCreate_Throws()
    {
        ShaderPass pass = Parse.Shader(Source).Passes![0];
        Assert.Throws<InvalidOperationException>(() => pass.Axes);
    }


    [Fact]
    public void ApplyKeywords_SetsKnownAndSkipsUnknown()
    {
        int applied = _pass.ApplyKeywords([K("SKINNED", "true"), K("NOT_AN_AXIS", "true"), K("ALPHA_MODE", "Cutout")]);

        Assert.Equal(2, applied);
        Assert.Equal("true", Active("SKINNED"));
        Assert.Equal("Cutout", Active("ALPHA_MODE"));
    }


    [Fact]
    public void ApplyKeywords_OnlyUnknown_LeavesStateUntouched()
    {
        _pass.ApplyKeywords([K("SKINNED", "true")]);
        int applied = _pass.ApplyKeywords([K("NOT_AN_AXIS", "true")]);

        Assert.Equal(0, applied);
        Assert.Equal("true", Active("SKINNED"));
    }


    [Fact]
    public void ApplyKeywords_LaterEntryWinsWithinSpan()
    {
        _pass.ApplyKeywords([K("ALPHA_MODE", "Cutout"), K("ALPHA_MODE", "Transparent")]);

        Assert.Equal("Transparent", Active("ALPHA_MODE"));
    }


    [Fact]
    public void ApplyKeywords_EmptySpan_IsNoOp()
    {
        _pass.ApplyKeywords([K("SKINNED", "true")]);
        Assert.Equal(0, _pass.ApplyKeywords(ReadOnlySpan<Keyword>.Empty));
        Assert.Equal("true", Active("SKINNED"));
    }


    [Fact]
    public void ResetKeywords_ReturnsToComboZero()
    {
        _pass.ApplyKeywords([K("SKINNED", "true"), K("ALPHA_MODE", "Transparent")]);
        _pass.ResetKeywords();

        Assert.Equal("false", Active("SKINNED"));
        Assert.Equal("Opaque", Active("ALPHA_MODE"));
    }


    [Fact]
    public void ResetThenApply_DoesNotLeakPreviousKeywords()
    {
        _pass.ApplyKeywords([K("SKINNED", "true"), K("ALPHA_MODE", "Transparent")]);
        _pass.ResetKeywords();
        _pass.ApplyKeywords([K("ALPHA_MODE", "Cutout")]);

        Assert.Equal("false", Active("SKINNED"));
        Assert.Equal("Cutout", Active("ALPHA_MODE"));
    }


    [Fact]
    public void ResetKeywords_MatchesTrySetKeywordSelection()
    {
        _pass.ResetKeywords();
        Variant fromReset = _pass.ActiveVariant;

        _pass.ApplyKeywords([K("SKINNED", "true")]);
        Assert.True(_pass.TrySetKeyword(K("SKINNED", "false")));

        Assert.Same(fromReset, _pass.ActiveVariant);
    }
}
