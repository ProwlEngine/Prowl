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
    public void Srgb_OnlyAppliesToColourFormats()
    {
        Assert.Equal(Graphite.PixelFormat.R8_G8_B8_A8_UNorm_SRgb, GraphicsFormats.ToPixelFormat(TextureFormat.RGBA8, true));
        Assert.Equal(Graphite.PixelFormat.BC1_Rgba_UNorm_SRgb, GraphicsFormats.ToPixelFormat(TextureFormat.BC1, true));
        Assert.Equal(Graphite.PixelFormat.ASTC_6x6_UNorm_SRgb, GraphicsFormats.ToPixelFormat(TextureFormat.ASTC6x6, true));
        Assert.True(new Texture2D(4, 4, TextureFormat.RGBA8, srgb: true).Srgb);
        Assert.True(new Texture2D(4, 4, TextureFormat.BC3, srgb: true).Srgb);
        Assert.False(new Texture2D(4, 4, TextureFormat.RGBA16Float, srgb: true).Srgb);
        Assert.False(new Texture2D(4, 4, TextureFormat.BC5, srgb: true).Srgb);
    }

    [Theory]
    [InlineData(TextureFormat.RGBA8, 13u, 7u, 13 * 7 * 4)]
    [InlineData(TextureFormat.BC1, 13u, 7u, 4 * 2 * 8)]
    [InlineData(TextureFormat.BC5, 1u, 1u, 16)]
    [InlineData(TextureFormat.ASTC6x6, 13u, 7u, 3 * 2 * 16)]
    [InlineData(TextureFormat.ASTC12x12, 1u, 1u, 16)]
    public void DataSize_CountsPartialEdgeBlocks(TextureFormat format, uint width, uint height, long expected)
        => Assert.Equal(expected, GraphicsFormats.DataSize(format, width, height));

    [Fact]
    public void CompressedImage_KeepsItsFormatAndEveryMipThroughSerialization()
    {
        byte[] pixels = new byte[37 * 21 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7);
        using Aperture.Image image = Aperture.Image.FromPixels(pixels, 37, 21, Aperture.PixelFormat.Rgba8);

        Texture2D texture = Texture2D.FromImage(image, mipmaps: true, srgb: true, TextureFormat.ASTC6x6, Aperture.TextureEncodeQuality.Fast);
        Assert.Equal(TextureFormat.ASTC6x6, texture.Format);
        Assert.True(texture.Srgb);
        Assert.Equal(6u, texture.MipCount);
        Assert.Equal(FilterMode.Trilinear, texture.Filter);

        EchoObject saved = Serializer.Serialize(texture);
        Assert.Equal(6, saved["Mips"].Count);
        Assert.Equal(7 * 4 * 16, saved["Mips"][0].ByteArrayValue.Length);
        Assert.Equal(16, saved["Mips"][5].ByteArrayValue.Length);

        Texture2D loaded = Serializer.Deserialize<Texture2D>(saved)!;
        Assert.Equal(TextureFormat.ASTC6x6, loaded.Format);
        Assert.Equal(6u, loaded.MipCount);
    }

    [Fact]
    public void CompressedSetData_MustStartAndEndOnABlock()
    {
        var texture = new Texture2D(10, 8, TextureFormat.BC1);
        Assert.Throws<System.ArgumentException>(() => texture.SetData(new byte[64], 2, 0, 4, 4));
        Assert.Throws<System.ArgumentException>(() => texture.SetData(new byte[64], 0, 0, 3, 4));
        texture.SetData(new byte[16], 8, 4, 2, 4);
    }

    [Fact]
    public void CompressedFormats_CannotBeWrittenOrRenderedInto()
    {
        Assert.Throws<System.ArgumentException>(() => new Texture2D(8, 8, TextureFormat.BC3, randomWrite: true));
        Assert.Throws<System.ArgumentException>(() => new RenderTexture(8, 8, false, [TextureFormat.ASTC4x4]));
    }

    [Fact]
    public void SingleChannelTextures_ReadBackAsGrey()
    {
        byte[]? rgba = new Texture2D(4, 2, TextureFormat.R8).GetMipRgba8();
        Assert.NotNull(rgba);
        Assert.Equal(4 * 2 * 4, rgba!.Length);
        Assert.Equal(255, rgba[3]);
    }

    [Fact]
    public void CompressedTexture_RefusesToFilterItsOwnMips()
        => Assert.Throws<System.InvalidOperationException>(() => new Texture2D(8, 8, TextureFormat.BC1).GenerateMipmaps());

    [Fact]
    public void NormalMapMips_StayUnitLength()
    {
        byte[] pixels = new byte[2 * 2 * 4];
        byte[][] normals = [[255, 128, 128], [128, 255, 128], [0, 128, 128], [128, 0, 128]];
        for (int i = 0; i < 4; i++) { pixels[i * 4] = normals[i][0]; pixels[(i * 4) + 1] = normals[i][1]; pixels[(i * 4) + 2] = normals[i][2]; pixels[(i * 4) + 3] = 255; }

        byte[] top = TextureProcessing.BuildMipChain(pixels, 2, 2, new MipChainOptions(false, NormalMap: true))[1];
        float x = (top[0] / 127.5f) - 1, y = (top[1] / 127.5f) - 1, z = (top[2] / 127.5f) - 1;
        Assert.InRange(System.MathF.Sqrt((x * x) + (y * y) + (z * z)), 0.98f, 1.02f);
        Assert.InRange(z, 0.98f, 1f);
    }

    [Fact]
    public void NormalsFromHeight_TiltAwayFromRisingGround()
    {
        byte[] pixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                pixels[((y * 8) + x) * 4] = pixels[(((y * 8) + x) * 4) + 1] = pixels[(((y * 8) + x) * 4) + 2] = (byte)(x * 20);

        TextureProcessing.NormalsFromHeight(pixels, 8, 8, 0.5f, smooth: true);

        int middle = ((4 * 8) + 4) * 4;
        Assert.True(pixels[middle] < 128, "Height rising to the right tilts the normal to the left.");
        Assert.InRange(pixels[middle + 1], 126, 130);
    }

    [Fact]
    public void PreservedCoverage_KeepsTheAlphaTestedAreaInEveryMip()
    {
        // A checker of half opaque, half clear texels: plain averaging leaves every mip at half alpha, under a 0.6 cutoff.
        byte[] pixels = new byte[8 * 8 * 4];
        for (int i = 0; i < 64; i++) pixels[(i * 4) + 3] = (byte)((((i % 8) + (i / 8)) & 1) == 0 ? 255 : 0);

        var plain = TextureProcessing.BuildMipChain((byte[])pixels.Clone(), 8, 8, new MipChainOptions(false));
        var kept = TextureProcessing.BuildMipChain(pixels, 8, 8, new MipChainOptions(false, CoverageCutoff: 0.6f));

        Assert.True(plain[1][3] < 153);
        Assert.True(kept[1][3] >= 153);
    }

    [Fact]
    public void DilateTransparent_BleedsColourWithoutTouchingAlpha()
    {
        byte[] pixels = new byte[3 * 1 * 4];
        pixels[0] = 200; pixels[1] = 100; pixels[2] = 50; pixels[3] = 255;
        TextureProcessing.DilateTransparent(pixels, 3, 1);

        Assert.Equal(200, pixels[8]);
        Assert.Equal(0, pixels[11]);
    }

    [Fact]
    public void FitMaxSize_HalvesUntilBothSidesFit()
    {
        int width = 100, height = 30;
        byte[] fitted = TextureProcessing.FitMaxSize(new byte[100 * 30 * 4], ref width, ref height, 32, srgb: false);
        Assert.Equal((25, 7), (width, height));
        Assert.Equal(25 * 7 * 4, fitted.Length);
    }

    [Fact]
    public void SingleChannelMipChain_StoresRed()
    {
        byte[] pixels = [10, 1, 2, 30, 20, 1, 2, 40, 30, 1, 2, 50, 40, 1, 2, 60];
        TextureProcessing.ToSingleChannel(pixels, fromAlpha: true);
        Texture2D texture = Texture2D.FromMipChain([pixels], 2, 2, false, TextureFormat.R8);
        Assert.Equal(TextureFormat.R8, texture.Format);
        Assert.Equal(30, pixels[0]);
        Assert.Equal(255, pixels[3]);
    }

    [Fact]
    public void Downsample_AveragesSrgbAsLight()
    {
        byte[] pixels = [0, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0, 255, 255, 255, 255];
        int width = 2, height = 2;
        byte[] colour = TextureProcessing.Downsample(pixels, ref width, ref height, srgb: true);
        Assert.Equal((1, 1), (width, height));
        Assert.Equal(188, colour[0]);
        Assert.Equal(128, colour[3]);

        width = height = 2;
        Assert.Equal(128, TextureProcessing.Downsample(pixels, ref width, ref height, srgb: false)[0]);
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
