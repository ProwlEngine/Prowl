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


    private string KeyedAxis(int key, string axis) => _pass.GetVariant(key).Keywords.First(k => k.Name == axis).Value;


    [Fact]
    public void Axes_ExposesBoundAxesInOrder()
    {
        Assert.Equal(2, _pass.Axes.Count);
        Assert.Equal("SKINNED", _pass.Axes[0].Name);
        Assert.Equal("ALPHA_MODE", _pass.Axes[1].Name);
        Assert.True(_pass.Axes[1].IsEnum);
    }


    [Fact]
    public void GetKey_SetsKnownAndSkipsUnknown()
    {
        int key = _pass.GetKey([K("SKINNED", "true"), K("NOT_AN_AXIS", "true"), K("ALPHA_MODE", "Cutout")]);

        Assert.Equal("true", KeyedAxis(key, "SKINNED"));
        Assert.Equal("Cutout", KeyedAxis(key, "ALPHA_MODE"));
    }


    [Fact]
    public void GetKey_EmptyOrOnlyUnknown_IsZero()
    {
        Assert.Equal(0, _pass.GetKey(ReadOnlySpan<Keyword>.Empty));
        Assert.Equal(0, _pass.GetKey([K("NOT_AN_AXIS", "true")]));
        Assert.Equal("false", KeyedAxis(0, "SKINNED"));
        Assert.Equal("Opaque", KeyedAxis(0, "ALPHA_MODE"));
    }


    [Fact]
    public void GetKey_CollapsedAxisKeyword_IsSkipped()
    {
        int key = _pass.GetKey([K("ANISOTROPIC", "true"), K("SKINNED", "true")]);

        Assert.DoesNotContain(_pass.Axes, a => a.Name == "ANISOTROPIC");
        Assert.Equal("true", KeyedAxis(key, "SKINNED"));
        Assert.Equal("Opaque", KeyedAxis(key, "ALPHA_MODE"));
        Assert.DoesNotContain(_pass.GetVariant(key).Keywords, k => k.Name == "ANISOTROPIC");
    }


    [Fact]
    public void GetKey_LaterEntryWinsWithinSpan()
    {
        int key = _pass.GetKey([K("ALPHA_MODE", "Cutout"), K("ALPHA_MODE", "Transparent")]);

        Assert.Equal("Transparent", KeyedAxis(key, "ALPHA_MODE"));
    }


    [Fact]
    public void GetKey_UnknownValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => _pass.GetKey([K("SKINNED", "True")]));
    }


    [Fact]
    public void GetKey_DoesNotMutateOtherKeys()
    {
        int first = _pass.GetKey([K("SKINNED", "true")]);
        int second = _pass.GetKey([K("ALPHA_MODE", "Cutout")]);

        Assert.Equal("true", KeyedAxis(first, "SKINNED"));
        Assert.Equal("Opaque", KeyedAxis(first, "ALPHA_MODE"));
        Assert.Equal("false", KeyedAxis(second, "SKINNED"));
        Assert.Equal("Cutout", KeyedAxis(second, "ALPHA_MODE"));
    }
}
