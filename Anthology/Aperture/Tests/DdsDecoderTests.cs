// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Tests;

/// <summary>
/// Tests for the BVH importer.
/// </summary>
public sealed class DdsDecoderTests
{
    [Fact]
    public void LoadRgbaF16_HasExpectedProperties()
    {
        // Arrange / Act
        var image = Image.Load(TestFiles.Dds("RgbaF16.dds"));

        // Assert
        Assert.Equal(ImageFormat.Dds, image.Format);
        Assert.Single(image.Frames);
        Assert.Equal(240, image.Width);
        Assert.Equal(135, image.Height);
        Assert.Equal(PixelFormat.RgbaF16, image.PixelFormat);
        Assert.False(image.IsAnimated);

        // ImageFrame properties
        Assert.Equal(240, image.RootFrame.Width);
        Assert.Equal(135, image.RootFrame.Height);
        Assert.Equal(1920, image.RootFrame.Stride);
        Assert.Equal(PixelFormat.RgbaF16, image.RootFrame.PixelFormat);
        Assert.Equal(0, image.RootFrame.OffsetX);
        Assert.Equal(0, image.RootFrame.OffsetY);
        Assert.Equal(TimeSpan.Zero, image.RootFrame.Delay);
        Assert.Equal(FrameDisposal.None, image.RootFrame.Disposal);
        Assert.Equal(FrameBlend.Source, image.RootFrame.Blend);
        Assert.Equal(0, image.RootFrame.MipLevel);
        Assert.Equal(0, image.RootFrame.ArraySlice);
        Assert.Equal(259200, image.RootFrame.ByteLength);

        // ImageInfo properties
        Assert.Equal(ImageFormat.Dds, image.Info.Format);
        Assert.Equal(240, image.Info.Width);
        Assert.Equal(135, image.Info.Height);
        Assert.Equal(16, image.Info.BitsPerChannel);
        Assert.Equal(4, image.Info.Channels);
        Assert.True(image.Info.HasAlpha);
        Assert.True(image.Info.IsHdr);
        Assert.Equal(ColorModel.Rgb, image.Info.ColorModel);
        Assert.Equal(PixelFormat.RgbaF16, image.Info.PreferredPixelFormat);
        Assert.Equal(1, image.Info.FrameCount);
        Assert.False(image.Info.IsAnimated);
        Assert.Equal(1, image.Info.MipmapCount);
        Assert.Equal(ExifOrientation.Unspecified, image.Info.Orientation);
        Assert.Equal(0, image.Info.HorizontalDpi);
        Assert.Equal(0, image.Info.VerticalDpi);
        Assert.Equal(RawVariant.None, image.Info.RawVariant);
        Assert.Equal(0, image.Info.DisplayWidth);
        Assert.Equal(0, image.Info.DisplayHeight);
        Assert.Equal("A16B16G16R16F", image.Info.Compression);
        Assert.Equal(240 * 135, image.Info.PixelCount);
    }

    [Fact]
    public void LoadRgbaF16_HasExpectedContent()
    {
        // Arrange
        var image = Image.Load(TestFiles.Dds("RgbaF16.dds"));

        // Act
        var pixels = image.RootFrame.GetRowAs<Half>(y: 84);

        // Assert
        Assert.Equal((Half)19.66f, pixels[9 * 4 + 0]);  // R
        Assert.Equal((Half)19.62f, pixels[9 * 4 + 1]);  // G
        Assert.Equal((Half)19.62f, pixels[9 * 4 + 2]);  // B
        Assert.Equal((Half)1f, pixels[9 * 4 + 3]);      // A
    }

    [Fact]
    public void LoadRgbaF16_HandlesConversionToRgba8()
    {
        // Arrange
        var image = Image.Load(TestFiles.Dds("RgbaF16.dds"), new DecodeOptions { TargetPixelFormat = PixelFormat.Rgba8 });

        // Act
        var pixels = image.RootFrame.GetRow(y: 84);

        // Assert
        Assert.Equal(61, pixels[0]);    // R
        Assert.Equal(0, pixels[1]);     // G
        Assert.Equal(0, pixels[2]);     // B
        Assert.Equal(255, pixels[3]);   // A
    }

    [Fact]
    public void LoadBC6U_HasExpectedProperties()
    {
        // Arrange / Act
        var image = Image.Load(TestFiles.Dds("BC6U.dds"));

        // Assert
        Assert.Equal(ImageFormat.Dds, image.Format);
        Assert.Single(image.Frames);
        Assert.Equal(240, image.Width);
        Assert.Equal(135, image.Height);
        Assert.Equal(PixelFormat.RgbF16, image.PixelFormat);

        // ImageInfo properties
        Assert.Equal(16, image.Info.BitsPerChannel);
        Assert.Equal(3, image.Info.Channels);
        Assert.False(image.Info.HasAlpha);
        Assert.True(image.Info.IsHdr);
        Assert.Equal("BC6H", image.Info.Compression);
    }

    [Fact]
    public void LoadBC6U_HasExpectedContent()
    {
        // Arrange
        var image = Image.Load(TestFiles.Dds("BC6U.dds"));

        // Act
        var pixels = image.RootFrame.GetRowAs<Half>(y: 84);

        // Assert
        Assert.Equal((Half)19.75f, pixels[9 * 3 + 0]);  // R
        Assert.Equal((Half)19.62f, pixels[9 * 3 + 1]);  // G
        Assert.Equal((Half)19.62f, pixels[9 * 3 + 2]);  // B
    }

    [Fact]
    public void LoadRgb8_HasExpectedProperties()
    {
        // Arrange / Act
        var image = Image.Load(TestFiles.Dds("Rgb8.dds"));

        // Assert
        Assert.Equal(ImageFormat.Dds, image.Format);
        Assert.Single(image.Frames);
        Assert.Equal(159, image.Width);
        Assert.Equal(166, image.Height);
        Assert.Equal(PixelFormat.Rgba8, image.PixelFormat);

        // ImageInfo properties
        Assert.Equal(8, image.Info.BitsPerChannel);
        Assert.Equal(4, image.Info.Channels);
        Assert.True(image.Info.HasAlpha);
        Assert.False(image.Info.IsHdr);
        Assert.Equal("R8G8B8A8", image.Info.Compression);
    }

    [Fact]
    public void LoadRgb8_HasExpectedContent()
    {
        // Arrange
        var image = Image.Load(TestFiles.Dds("Rgb8.dds"));

        // Act
        var row = image.RootFrame.GetRow(y: 119);

        // Assert
        Assert.Equal(0, row[12 * 4 + 0]);   // R
        Assert.Equal(0, row[12 * 4 + 1]);   // G
        Assert.Equal(0, row[12 * 4 + 2]);   // B
        Assert.Equal(0, row[12 * 4 + 3]);   // A

        Assert.Equal(113, row[26 * 4 + 0]); // R
        Assert.Equal(88, row[26 * 4 + 1]);  // G
        Assert.Equal(66, row[26 * 4 + 2]);  // B
        Assert.Equal(216, row[26 * 4 + 3]); // A

        Assert.Equal(254, row[44 * 4 + 0]); // R
        Assert.Equal(233, row[44 * 4 + 1]); // G
        Assert.Equal(91, row[44 * 4 + 2]);  // B
        Assert.Equal(255, row[44 * 4 + 3]); // A
    }

    [Fact]
    public void LoadBC7Mips_HasExpectedProperties()
    {
        // Arrange / Act
        var image = Image.Load(TestFiles.Dds("BC7-mips.dds"), new DecodeOptions { DecodeMipmaps = true });

        // Assert
        Assert.Equal(ImageFormat.Dds, image.Format);
        Assert.Equal(159, image.Width);
        Assert.Equal(166, image.Height);
        Assert.Equal(PixelFormat.Rgba8, image.PixelFormat);

        // Frames
        Assert.Equal(159, image.Frames[0].Width);
        Assert.Equal(166, image.Frames[0].Height);
        Assert.Equal(79, image.Frames[1].Width);
        Assert.Equal(83, image.Frames[1].Height);
        Assert.Equal(39, image.Frames[2].Width);
        Assert.Equal(41, image.Frames[2].Height);
        Assert.Equal(19, image.Frames[3].Width);
        Assert.Equal(20, image.Frames[3].Height);
        Assert.Equal(1, image.Frames[7].Width);
        Assert.Equal(1, image.Frames[7].Height);

        // ImageInfo properties
        Assert.Equal(8, image.Info.BitsPerChannel);
        Assert.Equal(4, image.Info.Channels);
        Assert.True(image.Info.HasAlpha);
        Assert.False(image.Info.IsHdr);
        Assert.Equal(8, image.Info.MipmapCount);
        Assert.Equal(8, image.Info.FrameCount);
        Assert.Equal("BC7", image.Info.Compression);
    }

    [Fact]
    public void LoadBC7Mips_HasExpectedContent()
    {
        // Arrange
        var image = Image.Load(TestFiles.Dds("BC7-mips.dds"));

        // Act
        var row = image.RootFrame.GetRow(y: 119);

        // Assert
        Assert.Equal(0, row[12 * 4 + 0]);   // R
        Assert.Equal(0, row[12 * 4 + 1]);   // G
        Assert.Equal(0, row[12 * 4 + 2]);   // B
        Assert.Equal(0, row[12 * 4 + 3]);   // A

        Assert.Equal(113, row[26 * 4 + 0]); // R
        Assert.Equal(77, row[26 * 4 + 1]);  // G
        Assert.Equal(67, row[26 * 4 + 2]);  // B
        Assert.Equal(255, row[26 * 4 + 3]); // A

        Assert.Equal(255, row[44 * 4 + 0]); // R
        Assert.Equal(232, row[44 * 4 + 1]); // G
        Assert.Equal(91, row[44 * 4 + 2]);  // B
        Assert.Equal(255, row[44 * 4 + 3]); // A

        // BC7 supports partial transparency.
        row = image.RootFrame.GetRow(y: 40);
        Assert.Equal(255, row[43 * 4 + 0]); // R
        Assert.Equal(253, row[43 * 4 + 1]); // G
        Assert.Equal(251, row[43 * 4 + 2]);  // B
        Assert.Equal(113, row[43 * 4 + 3]); // A
    }

    [Fact]
    public void LoadBC1_HasExpectedProperties()
    {
        // Arrange / Act
        var image = Image.Load(TestFiles.Dds("BC1.dds"));

        // Assert
        Assert.Equal(ImageFormat.Dds, image.Format);
        Assert.Single(image.Frames);
        Assert.Equal(159, image.Width);
        Assert.Equal(166, image.Height);
        Assert.Equal(PixelFormat.Rgba8, image.PixelFormat);

        // ImageInfo properties
        Assert.Equal(8, image.Info.BitsPerChannel);
        Assert.Equal(4, image.Info.Channels);
        Assert.True(image.Info.HasAlpha);
        Assert.False(image.Info.IsHdr);
        Assert.Equal(1, image.Info.MipmapCount);
        Assert.Equal(1, image.Info.FrameCount);
        Assert.Equal("BC1", image.Info.Compression);
    }

    [Fact]
    public void LoadBC1_HasExpectedContent()
    {
        // Arrange
        var image = Image.Load(TestFiles.Dds("BC1.dds"));

        // Act
        var row = image.RootFrame.GetRow(y: 119);

        // Assert
        Assert.Equal(0, row[12 * 4 + 0]);   // R
        Assert.Equal(0, row[12 * 4 + 1]);   // G
        Assert.Equal(0, row[12 * 4 + 2]);   // B
        Assert.Equal(0, row[12 * 4 + 3]);   // A

        Assert.Equal(107, row[26 * 4 + 0]); // R
        Assert.Equal(77, row[26 * 4 + 1]);  // G
        Assert.Equal(58, row[26 * 4 + 2]);  // B
        Assert.Equal(255, row[26 * 4 + 3]); // A

        Assert.Equal(255, row[44 * 4 + 0]); // R
        Assert.Equal(231, row[44 * 4 + 1]); // G
        Assert.Equal(90, row[44 * 4 + 2]);  // B
        Assert.Equal(255, row[44 * 4 + 3]); // A
    }

    [Fact]
    public void LoadArray_HasExpectedProperties()
    {
        // Arrange / Act
        var image = Image.Load(TestFiles.Dds("Array.dds"), new DecodeOptions { DecodeAllFrames = true });

        // Assert
        Assert.Equal(ImageFormat.Dds, image.Format);
        Assert.Equal(62, image.Width);
        Assert.Equal(103, image.Height);
        Assert.Equal(PixelFormat.Rgba8, image.PixelFormat);

        // The DDS file has 3 array images.
        Assert.Collection(image.Frames,
            f => Assert.Equal(62, f.Width),
            f => Assert.Equal(62, f.Width),
            f => Assert.Equal(62, f.Width));

        // ImageInfo properties
        Assert.Equal(8, image.Info.BitsPerChannel);
        Assert.Equal(4, image.Info.Channels);
        Assert.True(image.Info.HasAlpha);
        Assert.False(image.Info.IsHdr);
        Assert.Equal(1, image.Info.MipmapCount);
        Assert.Equal(3, image.Info.FrameCount);
        Assert.Equal("R8G8B8A8", image.Info.Compression);
    }
}
