// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Tests;

/// <summary>
/// Tests for the <see cref="TextureEncoder"/> class.
/// </summary>
public sealed class TextureEncoderTests
{
    [Fact]
    public void CanEncode_IsTrueForBlockFormats()
    {
        // BC1,3,4,5,7 are supported.
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc1Unorm));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc1UnormSrgb));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc2Unorm));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc2UnormSrgb));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc3Unorm));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc3UnormSrgb));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc4Unorm));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc5Unorm));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc7Unorm));
        Assert.True(TextureEncoder.CanEncode(TextureFormat.Bc7UnormSrgb));

        // Not supported yet.
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc6hSf16));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Bc6hUf16));

        // Not block formats.
        Assert.False(TextureEncoder.CanEncode(TextureFormat.R8G8B8A8Unorm));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.R8Unorm));
        Assert.False(TextureEncoder.CanEncode(TextureFormat.Unknown));
    }

    [Fact]
    public void Encode_Bc1_HasExpectedError()
    {
        // Arrange
        var image = Image.Load(TestFiles.Dds("Rgba8.dds"));
        ReadOnlySpan<byte> source = image.RootFrame.AsSpan<byte>();

        // Act
        byte[] fast = RoundTrip(image.RootFrame.AsSpan<byte>(), image.Width, image.Height, TextureEncodeQuality.Fast);
        byte[] balanced = RoundTrip(image.RootFrame.AsSpan<byte>(), image.Width, image.Height, TextureEncodeQuality.Balanced);
        byte[] thorough = RoundTrip(image.RootFrame.AsSpan<byte>(), image.Width, image.Height, TextureEncodeQuality.Thorough);

        // Assert
        Assert.Equal(PixelFormat.Rgba8, image.PixelFormat);

        // Note: the error decreases with increasing quality, as expected.
        Assert.Equal(650816224, SquaredError(source, fast));
        Assert.Equal(650738558, SquaredError(source, balanced));
        Assert.Equal(650729207, SquaredError(source, thorough));
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
        byte[] decoded = RoundTrip4x4(source, TextureEncodeQuality.Balanced);

        // Assert
        Assert.Equal(176568, SquaredError(source, decoded));
    }

    [Theory]
    [InlineData(1, 1, 8)]
    [InlineData(4, 4, 8)]
    [InlineData(5, 4, 16)]
    [InlineData(4, 5, 16)]
    [InlineData(9, 7, 48)]
    public void Encode_Bc1_RoundsSizeUpToWholeBlocks(int width, int height, int expected)
    {
        byte[] encoded = TextureEncoder.Encode(new byte[width * height * 4], width, height, TextureFormat.Bc1Unorm);

        Assert.Equal(expected, encoded.Length);
    }

    [Fact]
    public void Encode_Bc1_SolidColoursAreWithinOneStep()
    {
        for (int value = 0; value < 256; value++)
        {
            byte v = (byte)value;
            (byte R, byte G, byte B)[] colours = [(v, v, v), (v, 0, 0), (0, v, 0), (0, 0, v)];

            foreach (var (r, g, b) in colours)
            {
                byte[] source = Block(_ => (r, g, b, 255));

                byte[] decoded = RoundTrip4x4(source);

                AssertClose(source, decoded, 1);
            }
        }
    }

    [Fact]
    public void Encode_Bc1_TwoExtremeColoursAreLossless()
    {
        // Every channel is 0 or 255, which RGB565 stores exactly.
        for (int a = 0; a < 8; a++)
        {
            for (int b = a + 1; b < 8; b++)
            {
                byte[] source = Block(i =>
                {
                    int c = ((i % 4) + (i / 4)) % 2 == 0 ? a : b;
                    return ((byte)((c & 4) != 0 ? 255 : 0), (byte)((c & 2) != 0 ? 255 : 0),
                        (byte)((c & 1) != 0 ? 255 : 0), 255);
                });

                foreach (TextureEncodeQuality quality in Enum.GetValues<TextureEncodeQuality>())
                    Assert.Equal(source, RoundTrip4x4(source, quality));
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Encode_Bc1_GreyRampIsClose(int blockIndex)
    {
        byte[] source = Block(i =>
        {
            byte v = (byte)(((blockIndex * 4) + (i % 4)) * 17);
            return (v, v, v, 255);
        });

        AssertClose(source, RoundTrip4x4(source), 4);
    }

    [Fact]
    public void Encode_Bc1_FullyTransparentBlockHasExpectedBytes()
    {
        byte[] source = Block(i => ((byte)(i * 16), 100, 200, 0));

        byte[] encoded = TextureEncoder.Encode(source, 4, 4, TextureFormat.Bc1Unorm);

        Assert.Equal(new byte[] { 0, 0, 0, 0, 255, 255, 255, 255 }, encoded);
    }

    [Fact]
    public void Encode_Bc1_CutsAlphaBelow128()
    {
        byte[] source = Block(i => (255, 255, 255, (byte)(i < 8 ? 127 : 128)));

        byte[] decoded = RoundTrip4x4(source);

        for (int i = 0; i < 16; i++)
            Assert.Equal(i < 8 ? 0 : 255, decoded[(i * 4) + 3]);
    }

    [Fact]
    public void Encode_Bc1_MixedBlockUsesThreeColourMode()
    {
        byte[] source = Block(i => i % 2 == 0 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)255, (byte)0, (byte)0));

        byte[] encoded = TextureEncoder.Encode(source, 4, 4, TextureFormat.Bc1Unorm);
        byte[] decoded = TextureDecoder.Decode(encoded, 4, 4, TextureFormat.Bc1Unorm);

        // Three colour mode is selected by the first endpoint not exceeding the second.
        Assert.True(BinaryPrimitives.ReadUInt16LittleEndian(encoded)
            <= BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(2)));

        for (int i = 0; i < 16; i++)
        {
            if (i % 2 == 0)
                Assert.Equal(source.AsSpan(i * 4, 4).ToArray(), decoded.AsSpan(i * 4, 4).ToArray());
            else
                Assert.Equal(0, decoded[(i * 4) + 3]);
        }
    }

    [Fact]
    public void Encode_Bc1_IgnoresColourOfTransparentPixels()
    {
        byte[] first = Block(i => ((byte)(i * 15), (byte)(255 - (i * 9)), 40, (byte)(i % 3 == 0 ? 0 : 255)));
        byte[] second = (byte[])first.Clone();
        for (int i = 0; i < 16; i += 3)
        {
            second[i * 4] = 7;
            second[(i * 4) + 1] = 201;
            second[(i * 4) + 2] = 255;
        }

        Assert.Equal(
            TextureEncoder.Encode(first, 4, 4, TextureFormat.Bc1Unorm),
            TextureEncoder.Encode(second, 4, 4, TextureFormat.Bc1Unorm));
    }

    [Fact]
    public void Encode_Bc1_OpaqueInputStaysOpaque()
    {
        byte[] source = RandomPixels(64, 64, opaque: true);

        byte[] encoded = TextureEncoder.Encode(source, 64, 64, TextureFormat.Bc1Unorm);
        byte[] decoded = TextureDecoder.Decode(encoded, 64, 64, TextureFormat.Bc1Unorm);

        for (int i = 3; i < decoded.Length; i += 4)
            Assert.Equal(255, decoded[i]);
    }

    [Fact]
    public void Encode_Bc1_BalancedIsNoWorseThanFast()
    {
        // Balanced starts with the same passes as Fast and only accepts improvements.
        byte[] source = RandomPixels(64, 64, opaque: true);

        long fast = SquaredError(source, RoundTrip(source, 64, 64, TextureEncodeQuality.Fast));
        long balanced = SquaredError(source, RoundTrip(source, 64, 64, TextureEncodeQuality.Balanced));

        Assert.True(balanced <= fast, $"Balanced {balanced}, Fast {fast}");
    }

    [Fact]
    public void Encode_Bc1_RepeatsEdgePixelsInPartialBlocks()
    {
        byte[] source = RandomPixels(5, 7, opaque: false);
        byte[] padded = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int from = ((Math.Min(y, 6) * 5) + Math.Min(x, 4)) * 4;
                source.AsSpan(from, 4).CopyTo(padded.AsSpan(((y * 8) + x) * 4));
            }
        }

        Assert.Equal(
            TextureEncoder.Encode(padded, 8, 8, TextureFormat.Bc1Unorm),
            TextureEncoder.Encode(source, 5, 7, TextureFormat.Bc1Unorm));
    }

    [Fact]
    public void Encode_Bc1_SkipsRowPadding()
    {
        const int width = 8, height = 8, rowBytes = width * 4, stride = rowBytes + 12;
        byte[] tight = RandomPixels(width, height, opaque: false);
        byte[] padded = new byte[stride * height];
        padded.AsSpan().Fill(0xCD);
        for (int y = 0; y < height; y++)
            tight.AsSpan(y * rowBytes, rowBytes).CopyTo(padded.AsSpan(y * stride));

        byte[] expected = TextureEncoder.Encode(tight, width, height, TextureFormat.Bc1Unorm);
        byte[] actual = new byte[expected.Length];
        TextureEncoder.Encode(padded, width, height, stride, TextureFormat.Bc1Unorm, actual);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Encode_Bc1_OutputDoesNotDependOnThreadCount()
    {
        byte[] source = RandomPixels(64, 64, opaque: false);

        byte[] single = TextureEncoder.Encode(source, 64, 64, TextureFormat.Bc1Unorm,
            new TextureEncodeOptions { MaxDegreeOfParallelism = 1 });
        byte[] parallel = TextureEncoder.Encode(source, 64, 64, TextureFormat.Bc1Unorm);

        Assert.Equal(single, parallel);
    }

    [Fact]
    public void Encode_Bc1_SrgbAndUnormProduceTheSameBytes()
    {
        byte[] source = RandomPixels(16, 16, opaque: false);

        Assert.Equal(
            TextureEncoder.Encode(source, 16, 16, TextureFormat.Bc1Unorm),
            TextureEncoder.Encode(source, 16, 16, TextureFormat.Bc1UnormSrgb));
    }

    [Fact]
    public void Encode_Bc7_ErrorDecreasesWithQuality()
    {
        var image = Image.Load(TestFiles.Dds("Rgba8.dds"));
        ReadOnlySpan<byte> source = image.RootFrame.AsSpan<byte>();

        byte[] fast = RoundTrip(source, image.Width, image.Height, TextureEncodeQuality.Fast, TextureFormat.Bc7Unorm);
        byte[] balanced = RoundTrip(source, image.Width, image.Height, TextureEncodeQuality.Balanced, TextureFormat.Bc7Unorm);
        byte[] thorough = RoundTrip(source, image.Width, image.Height, TextureEncodeQuality.Thorough, TextureFormat.Bc7Unorm);

        // Note: the error decreases with increasing quality, as expected.
        Assert.Equal(1885700, SquaredError(source, fast));
        Assert.Equal(1346155, SquaredError(source, balanced));
        Assert.Equal(1328731, SquaredError(source, thorough));
    }

    [Fact]
    public void Encode_Bc7_SolidColoursAreWithinOneStep()
    {
        for (int value = 0; value < 256; value += 3)
        {
            byte v = (byte)value;
            (byte R, byte G, byte B, byte A)[] colours = [(v, v, v, 255), (v, 0, 0, 255), (0, v, 0, v), (10, 200, v, (byte)(255 - v))];

            foreach (var (r, g, b, a) in colours)
            {
                byte[] source = Block(_ => (r, g, b, a));

                foreach (TextureEncodeQuality quality in Enum.GetValues<TextureEncodeQuality>())
                    AssertClose(source, RoundTrip(source, 4, 4, quality, TextureFormat.Bc7Unorm), 1);
            }
        }
    }

    [Fact]
    public void Encode_Bc7_TwoExtremeColoursAreLossless()
    {
        for (int a = 0; a < 16; a++)
        {
            for (int b = a + 1; b < 16; b++)
            {
                byte[] source = Block(i =>
                {
                    int c = ((i % 4) + (i / 4)) % 2 == 0 ? a : b;
                    return ((byte)((c & 8) != 0 ? 255 : 0), (byte)((c & 4) != 0 ? 255 : 0),
                        (byte)((c & 2) != 0 ? 255 : 0), (byte)((c & 1) != 0 ? 255 : 0));
                });

                foreach (TextureEncodeQuality quality in Enum.GetValues<TextureEncodeQuality>())
                    Assert.Equal(source, RoundTrip(source, 4, 4, quality, TextureFormat.Bc7Unorm));
            }
        }
    }

    [Fact]
    public void Encode_Bc7_KeepsSmoothAlpha()
    {
        byte[] source = Block(i => ((byte)(i * 16), (byte)(255 - (i * 16)), 128, (byte)(i * 17)));

        foreach (TextureEncodeQuality quality in Enum.GetValues<TextureEncodeQuality>())
            AssertClose(source, RoundTrip(source, 4, 4, quality, TextureFormat.Bc7Unorm), 6);
    }

    [Fact]
    public void Encode_Bc7_RandomPixelsBeatBc3()
    {
        byte[] source = RandomPixels(64, 64, opaque: false);

        long bc7 = SquaredError(source, RoundTrip(source, 64, 64, TextureEncodeQuality.Balanced, TextureFormat.Bc7Unorm));
        long bc3 = SquaredError(source, RoundTrip(source, 64, 64, TextureEncodeQuality.Balanced, TextureFormat.Bc3Unorm));

        Assert.True(bc7 < bc3, $"BC7 {bc7}, BC3 {bc3}");
    }

    [Fact]
    public void Encode_Bc7_OutputDoesNotDependOnThreadCount()
    {
        byte[] source = RandomPixels(64, 64, opaque: false);

        byte[] single = TextureEncoder.Encode(source, 64, 64, TextureFormat.Bc7Unorm,
            new TextureEncodeOptions { MaxDegreeOfParallelism = 1 });
        byte[] parallel = TextureEncoder.Encode(source, 64, 64, TextureFormat.Bc7Unorm);

        Assert.Equal(single, parallel);
    }

    // Returns a 4x4 block of pixels filled with the given color.
    private static byte[] Block(Func<int, (byte R, byte G, byte B, byte A)> pixel)
    {
        byte[] block = new byte[16 * 4];
        for (int i = 0; i < 16; i++)
            (block[i * 4], block[(i * 4) + 1], block[(i * 4) + 2], block[(i * 4) + 3]) = pixel(i);

        return block;
    }

    // Returns a 4x4 block of pixels filled random colors.
    private static byte[] RandomPixels(int width, int height, bool opaque)
    {
        byte[] pixels = new byte[width * height * 4];
        new Random(1234).NextBytes(pixels);

        if (opaque)
        {
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        }

        return pixels;
    }

    // Encodes then decodes a 4x4 block of pixels.
    private static byte[] RoundTrip4x4(ReadOnlySpan<byte> source, TextureEncodeQuality quality = TextureEncodeQuality.Balanced)
        => RoundTrip(source, 4, 4, quality);

    // Encodes then decodes a block of pixels of the given size.
    private static byte[] RoundTrip(ReadOnlySpan<byte> source, int width, int height, TextureEncodeQuality quality,
                                    TextureFormat format = TextureFormat.Bc1Unorm)
    {
        byte[] encoded = TextureEncoder.Encode(source, width, height, format,
            new TextureEncodeOptions { Quality = quality });

        return TextureDecoder.Decode(encoded, width, height, format);
    }

    // Asserts that two images are within a certain tolerance.
    private static void AssertClose(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, int tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.InRange(actual[i], Math.Max(0, expected[i] - tolerance), Math.Min(255, expected[i] + tolerance));
    }

    // Computes the error metric for two images by summing the square of the differences.
    private static long SquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        long total = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int difference = expected[i] - actual[i];
            total += difference * difference;
        }

        return total;
    }
}
