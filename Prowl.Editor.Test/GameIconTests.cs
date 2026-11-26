// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;

using Prowl.Aperture;
using Prowl.Editor.Build;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>The icon files a built game carries: which sizes are made and how each file lays them out.</summary>
public class GameIconTests
{
    private static GameIcon.Source Solid(int size, byte r = 200, byte g = 100, byte b = 50, byte a = 255)
        => new(size, size, TestImages.SolidPixels(size, r, g, b, a));

    [Theory]
    [InlineData(48, new[] { 48, 32, 16 }, new[] { 32, 16 })]
    [InlineData(300, new[] { 256, 128, 64, 48, 32, 16 }, new[] { 256, 128, 64, 32, 16 })]
    [InlineData(1024, new[] { 256, 128, 64, 48, 32, 16 }, new[] { 1024, 512, 256, 128, 64, 32, 16 })]
    [InlineData(100, new[] { 100, 64, 48, 32, 16 }, new[] { 64, 32, 16 })]
    public void Images_NeverEnlargeTheSource(int source, int[] windows, int[] mac)
    {
        Assert.Equal(windows, GameIcon.WindowsImages(Solid(source)).Select(i => i.Size));
        Assert.Equal(mac, GameIcon.MacImages(Solid(source)).Select(i => i.Size));
    }

    [Theory]
    [InlineData(16, 16, true)]
    [InlineData(512, 512, true)]
    [InlineData(15, 15, false)]
    [InlineData(64, 32, false)]
    [InlineData(32, 64, false)]
    public void Problem_AcceptsOnlySquareIconsOfAtLeastSixteen(int width, int height, bool usable)
    {
        string? problem = GameIcon.Problem("Icons/Game.png", width, height);

        Assert.Equal(usable, problem == null);
        if (!usable)
        {
            Assert.Contains("Icons/Game.png", problem);
            Assert.Contains($"{width}x{height}", problem);
        }
    }

    [Fact]
    public void Problem_IsNullWhenNoIconIsSet()
        => Assert.Null(GameIcon.Problem(default(AssetRef<Texture2D>)));

    [Fact]
    public void WindowsImages_KeepTheSourceAtItsOwnSizeAndShrinkTheRest()
    {
        GameIcon.Source source = Solid(48);

        List<GameIcon.IconImage> images = GameIcon.WindowsImages(source);

        Assert.Equal([48, 32, 16], images.Select(i => i.Size));
        Assert.Same(source.Pixels, images[0].Pixels);
        Assert.All(images, i => Assert.Equal(i.Size * i.Size * 4, i.Pixels.Length));
    }

    [Fact]
    public void ToIco_HoldsEachImageAsAPngWithTheSizesInItsDirectory()
    {
        List<GameIcon.IconImage> images = GameIcon.WindowsImages(Solid(300));

        byte[] ico = GameIcon.ToIco(images);

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        Assert.Equal(images.Count, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));

        for (int i = 0; i < images.Count; i++)
        {
            ReadOnlySpan<byte> entry = ico.AsSpan(6 + (16 * i), 16);
            int expectedSide = images[i].Size == 256 ? 0 : images[i].Size;
            Assert.Equal(expectedSide, entry[0]);
            Assert.Equal(expectedSide, entry[1]);
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]));
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));

            int length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            using Aperture.Image decoded = Aperture.Image.Load(ico.AsSpan(offset, length), new DecodeOptions { TargetPixelFormat = PixelFormat.Rgba8 });
            Assert.Equal(images[i].Size, decoded.Width);
            Assert.Equal(images[i].Size, decoded.Height);
        }
    }

    [Fact]
    public void ToIcns_HoldsEachImageAsAPngUnderItsSizeTypeAndTheRetinaOnes()
    {
        List<GameIcon.IconImage> images = GameIcon.MacImages(Solid(1024));

        byte[] icns = GameIcon.ToIcns(images);

        Assert.Equal("icns", Encoding.ASCII.GetString(icns, 0, 4));
        Assert.Equal(icns.Length, BinaryPrimitives.ReadInt32BigEndian(icns.AsSpan(4)));

        (string Type, int Size)[] expected =
        [
            ("ic10", 1024), ("ic09", 512), ("ic08", 256), ("ic07", 128), ("icp6", 64), ("icp5", 32), ("icp4", 16),
            ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512),
        ];
        int position = 8;
        foreach ((string type, int size) in expected)
        {
            Assert.Equal(type, Encoding.ASCII.GetString(icns, position, 4));
            int length = BinaryPrimitives.ReadInt32BigEndian(icns.AsSpan(position + 4));
            using Aperture.Image decoded = Aperture.Image.Load(icns.AsSpan(position + 8, length - 8), new DecodeOptions { TargetPixelFormat = PixelFormat.Rgba8 });
            Assert.Equal(size, decoded.Width);
            position += length;
        }

        Assert.Equal(icns.Length, position);
    }

    [Fact]
    public void ToWindowResource_WritesACountThenEachImagesSizeAndPixels()
    {
        List<GameIcon.IconImage> images = GameIcon.WindowsImages(Solid(48));

        byte[] resource = GameIcon.ToWindowResource(images);

        using BinaryReader reader = new(new MemoryStream(resource));
        Assert.Equal(images.Count, reader.ReadInt32());
        foreach (GameIcon.IconImage image in images)
        {
            Assert.Equal(image.Size, reader.ReadInt32());
            Assert.Equal(image.Size, reader.ReadInt32());
            Assert.Equal(image.Pixels, reader.ReadBytes(image.Size * image.Size * 4));
        }
        Assert.Equal(resource.Length, reader.BaseStream.Position);
    }

    [Fact]
    public void Load_WithNoIconGivesTheProwlLogo()
    {
        GameIcon.Source logo = GameIcon.Load(default);

        Assert.Equal(256, logo.Width);
        Assert.Equal(256, logo.Height);
        Assert.Equal(256 * 256 * 4, logo.Pixels.Length);
        Assert.Contains(Enumerable.Range(0, 256 * 256).Select(i => logo.Pixels[(i * 4) + 3]), a => a > 0);
    }
}

/// <summary>What the build says about an icon texture that is gone, unreadable or the wrong shape.</summary>
public class GameIconProjectTests : EditorTestHarness
{
    [Fact]
    public void Problem_ForAnUnknownTextureSaysItNoLongerExists()
        => Assert.Contains("no longer exists", GameIcon.Problem(new AssetRef<Texture2D>(Guid.NewGuid())));

    [Fact]
    public void Problem_ForATextureDeletedFromDiskSaysItNoLongerExists()
    {
        TestImages.WriteSolidPng(AssetAbsolutePath("Icon.png"), 32, 1, 2, 3);
        Guid guid = Assets.ImportFile("Icon.png");
        File.Delete(AssetAbsolutePath("Icon.png"));

        Assert.Contains("no longer exists", GameIcon.Problem(new AssetRef<Texture2D>(guid)));
        Assert.Throws<InvalidOperationException>(() => GameIcon.Load(new AssetRef<Texture2D>(guid)));
    }

    [Fact]
    public void Problem_ForANonSquareTextureNamesItAndItsSize()
    {
        using (Aperture.Image image = Aperture.Image.FromPixels(new byte[32 * 16 * 4], 32, 16, PixelFormat.Rgba8))
            image.Save(AssetAbsolutePath("Wide.png"));
        Guid guid = Assets.ImportFile("Wide.png");

        string? problem = GameIcon.Problem(new AssetRef<Texture2D>(guid));

        Assert.Contains("Wide.png", problem);
        Assert.Contains("32x16", problem);
    }

    [Fact]
    public void Problem_ForASquareTextureIsNull()
    {
        TestImages.WriteSolidPng(AssetAbsolutePath("Square.png"), 32, 1, 2, 3);
        Guid guid = Assets.ImportFile("Square.png");

        Assert.Null(GameIcon.Problem(new AssetRef<Texture2D>(guid)));
        Assert.Equal(32, GameIcon.Load(new AssetRef<Texture2D>(guid)).Width);
    }

    [Fact]
    public void Problem_ForASpriteInsideATextureNamesTheFileToPick()
    {
        TestImages.WriteSolidPng(AssetAbsolutePath("Atlas.png"), 32, 1, 2, 3);
        Guid texture = Assets.ImportFile("Atlas.png");
        TextureSpriteMeta.Save(texture, new SpriteImportSettings { Mode = SpriteMode.Single });
        Guid sprite = Assets.GetSubAssets(texture)[0].Guid;

        string? problem = GameIcon.Problem(new AssetRef<Texture2D>(sprite));

        Assert.Contains("is inside 'Atlas.png'", problem);
    }
}
