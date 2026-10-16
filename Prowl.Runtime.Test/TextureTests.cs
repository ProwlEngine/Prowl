// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

using GFilter = Prowl.Graphite.FilterMode;
using GAddress = Prowl.Graphite.SamplerAddressMode;

namespace Prowl.Runtime.Test;

public class TextureTests
{
    [Theory]
    [InlineData(FilterMode.Point, GFilter.Point, GFilter.Point)]
    [InlineData(FilterMode.Bilinear, GFilter.Linear, GFilter.Point)]
    [InlineData(FilterMode.Trilinear, GFilter.Linear, GFilter.Linear)]
    public void Sampler_FilterMapsOntoGraphite(FilterMode filter, GFilter texel, GFilter mip)
    {
        Graphite.SamplerDescription sampler = GraphicsFormats.ToSampler(filter, WrapMode.Repeat, WrapMode.Repeat, WrapMode.Repeat, 5, 8);
        Assert.Equal(texel, sampler.MinFilter);
        Assert.Equal(texel, sampler.MagFilter);
        Assert.Equal(mip, sampler.MipFilter);
        Assert.Equal(4f, sampler.MaximumLod);
        Assert.Equal(filter == FilterMode.Point ? 0u : 8u, sampler.MaximumAnisotropy);
    }

    [Fact]
    public void Sampler_WrapIsPerAxis()
    {
        Graphite.SamplerDescription sampler = GraphicsFormats.ToSampler(FilterMode.Bilinear, WrapMode.Clamp, WrapMode.Mirror, WrapMode.Border, 1,
            border: TextureBorderColor.OpaqueWhite);
        Assert.Equal(GAddress.Clamp, sampler.AddressModeU);
        Assert.Equal(GAddress.Mirror, sampler.AddressModeV);
        Assert.Equal(GAddress.Border, sampler.AddressModeW);
        Assert.Equal(Graphite.SamplerBorderColor.OpaqueWhite, sampler.BorderColor);
        Assert.Equal(0f, sampler.MaximumLod);
    }

    [Fact]
    public void Srgb_OnlyAppliesToRgba8()
    {
        Assert.Equal(Graphite.PixelFormat.R8_G8_B8_A8_UNorm_SRgb, GraphicsFormats.ToPixelFormat(TextureFormat.RGBA8, true));
        Assert.True(new Texture2D(4, 4, TextureFormat.RGBA8, srgb: true).Srgb);
        Assert.False(new Texture2D(4, 4, TextureFormat.RGBA16Float, srgb: true).Srgb);
    }

    [Fact]
    public void Texture2D_RoundTripKeepsSettingsAndMips()
    {
        var texture = new Texture2D(8, 4, TextureFormat.RGBA8, mipmaps: true, srgb: true)
        {
            Filter = FilterMode.Point,
            WrapU = WrapMode.Clamp,
            WrapV = WrapMode.Mirror,
            Anisotropy = 4,
        };
        Assert.Equal(4u, texture.MipCount);

        EchoObject saved = Serializer.Serialize(texture);
        Assert.Equal(4, saved["Mips"].Count);
        Assert.Equal(8 * 4 * 4, saved["Mips"][0].ByteArrayValue.Length);
        Assert.Equal(4, saved["Mips"][3].ByteArrayValue.Length);

        Texture2D loaded = Serializer.Deserialize<Texture2D>(saved)!;
        Assert.Equal(4u, loaded.MipCount);
        Assert.True(loaded.Srgb);
        Assert.Equal(FilterMode.Point, loaded.Filter);
        Assert.Equal(WrapMode.Clamp, loaded.WrapU);
        Assert.Equal(WrapMode.Mirror, loaded.WrapV);
        Assert.Equal(4u, loaded.Anisotropy);
    }

    [Fact]
    public void GenerateMipmaps_GivesAFullChain()
    {
        var texture = new Texture2D(16, 4);
        Assert.Equal(1u, texture.MipCount);
        texture.GenerateMipmaps();
        Assert.Equal(5u, texture.MipCount);
        texture.RecreateImage(2, 2);
        Assert.Equal(2u, texture.MipCount);
    }

    [Theory]
    [InlineData("Textures/Bricks Normal.png", false)]
    [InlineData("Textures/Worn Panel Surface.png", false)]
    [InlineData("Textures/Bricks Height.png", false)]
    [InlineData("Textures/Circuit Emission.png", true)]
    [InlineData("Textures/Checker.png", true)]
    public void RuntimeImporter_ColourUnlessTheNameNamesData(string path, bool colour)
        => Assert.Equal(colour, !TextureRuntimeImporter.NamesData(path));
}
