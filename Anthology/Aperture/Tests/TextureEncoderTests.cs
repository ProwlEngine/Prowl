// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Tests;

/// <summary>
/// Tests for the <see cref="TextureEncoder"/> class.
/// </summary>
public sealed class TextureEncoderTests
{
    [Fact]
    public void CanEncode_IsTrueForBlockFormats()
    {
        // BC1,3,4,5 are supported.
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc1Unorm));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc1UnormSrgb));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc2Unorm));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc2UnormSrgb));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc3Unorm));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc3UnormSrgb));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc4Unorm));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc5Unorm));

        // Not supported yet.
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc6hSf16));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc6hUf16));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc7Unorm));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc7UnormSrgb));

        // Not block formats.
        Assert.False(TextureEncoder.CanEncode(TextureFormat.R8G8B8A8Unorm));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.R8Unorm));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Unknown));
    }

    [Fact]
    public void Encode_Bc1_HasExpectedError()
    {
        // Arrange
        var image = Image.Load(TestFiles.Dds("Rgb8.dds"));
        ReadOnlySpan<byte> source = image.RootFrame.AsSpan<byte>();

        // Act
        byte[] encoded = TextureEncoder.Encode(image.RootFrame, TextureFormat.Bc1UnormSrgb);
        byte[] decoded = TextureDecoder.Decode(encoded, image.Width, image.Height, TextureFormat.Bc1UnormSrgb);

        // Assert
        Assert.Equal(PixelFormat.Rgba8, image.PixelFormat);
        Assert.Equal(source.Length, decoded.Length);

        int error = 0;
        for (int i = 0; i < source.Length; i ++)
            error += int.Abs(source[i] - decoded[i]);
        Assert.Equal(2966582, error);
    }

    [Fact]
    public void Encode_Bc1_FindsAxisAcrossCorrelatedChannels()
    {
        // This test is a regression test for a specific bug in Bc1BlockEncoder.PowerIteration().
        // Red alternates by column; green and blue change together by row.
        byte[] source = new byte[4 * 4 * 4];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int at = ((y * 4) + x) * 4;
                source[at] = (byte)(210 * (x & 1));
                source[at + 1] = source[at + 2] = (byte)(200 * (y >> 1));
                source[at + 3] = 255;
            }
        }

        // Act
        byte[] encoded = TextureEncoder.Encode(source, 4, 4, TextureFormat.Bc1UnormSrgb);
        byte[] decoded = TextureDecoder.Decode(encoded, 4, 4, TextureFormat.Bc1UnormSrgb);


        // Assert
        Assert.Equal(source.Length, decoded.Length);
        int error = 0;
        for (int i = 0; i < source.Length; i++)
            error += int.Abs(source[i] - decoded[i]);
        Assert.Equal(1720, error);
    }
}
