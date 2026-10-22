// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

public class TextureImportFormatTests
{
    [Theory]
    [InlineData(TextureImportType.Default, false, TextureFormat.BC1)]
    [InlineData(TextureImportType.Default, true, TextureFormat.BC3)]
    [InlineData(TextureImportType.Sprite, true, TextureFormat.BC3)]
    [InlineData(TextureImportType.Cookie, false, TextureFormat.BC1)]
    [InlineData(TextureImportType.NormalMap, false, TextureFormat.BC5)]
    [InlineData(TextureImportType.SingleChannel, false, TextureFormat.BC4)]
    [InlineData(TextureImportType.LookupTable, false, TextureFormat.RGBA8)]
    public void Automatic_PicksFromTypeAndAlpha(TextureImportType type, bool alpha, TextureFormat expected)
        => Assert.Equal(expected, TextureImporter.FormatFor(TextureImportFormat.Automatic, TextureCompression.Balanced, type, alpha));

    [Theory]
    [InlineData(TextureImportType.Default, TextureFormat.RGBA8)]
    [InlineData(TextureImportType.SingleChannel, TextureFormat.R8)]
    public void NoCompression_StoresUncompressed(TextureImportType type, TextureFormat expected)
        => Assert.Equal(expected, TextureImporter.FormatFor(TextureImportFormat.ASTC4x4, TextureCompression.None, type, false));

    [Fact]
    public void AnExplicitFormatIsKept()
        => Assert.Equal(TextureFormat.ASTC8x8, TextureImporter.FormatFor(TextureImportFormat.ASTC8x8, TextureCompression.Fast, TextureImportType.NormalMap, false));

    [Fact]
    public void NewTexturesDefaultToBalancedAutomaticCompressionAt2048()
    {
        EchoObject settings = new TextureImporter().DefaultSettings()!;
        TextureImportSettings s = TextureImportSettings.Read(settings, "Bricks.png");
        Assert.Equal(TextureCompression.Balanced, s.Compression);
        Assert.Equal(TextureImportFormat.Automatic, s.Format);
        Assert.Equal(2048, s.MaxSize);
    }

    [Theory]
    [InlineData(TextureImportType.Default, true, true, WrapMode.Repeat, false)]
    [InlineData(TextureImportType.NormalMap, false, true, WrapMode.Repeat, false)]
    [InlineData(TextureImportType.Sprite, true, false, WrapMode.Clamp, true)]
    [InlineData(TextureImportType.Cookie, true, false, WrapMode.Clamp, false)]
    [InlineData(TextureImportType.SingleChannel, false, true, WrapMode.Repeat, false)]
    [InlineData(TextureImportType.LookupTable, false, false, WrapMode.Clamp, false)]
    public void UntouchedSettingsFollowTheType(TextureImportType type, bool srgb, bool mipmaps, WrapMode wrap, bool bleed)
    {
        EchoObject settings = EchoObject.NewCompound();
        settings[TextureImportSettings.Keys.Type] = new EchoObject((int)type);
        TextureImportSettings s = TextureImportSettings.Read(settings, "Texture.png");

        Assert.Equal(srgb, s.Srgb);
        Assert.Equal(mipmaps, s.Mipmaps);
        Assert.Equal(mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear, s.Filter);
        Assert.Equal(wrap, s.WrapU);
        Assert.Equal(bleed, s.AlphaIsTransparency);
    }

    [Fact]
    public void TheFileNameSuggestsNormalMapsAndLinearData()
    {
        Assert.Equal(TextureImportType.NormalMap, TextureImportSettings.Read(null, "Bricks Normal.png").Type);
        TextureImportSettings mask = TextureImportSettings.Read(null, "Panel Mask.png");
        Assert.Equal(TextureImportType.Default, mask.Type);
        Assert.False(mask.Srgb);
    }

    [Fact]
    public void TheRetiredMaskTypeReadsAsLinearDefault()
    {
        EchoObject settings = EchoObject.NewCompound();
        settings[TextureImportSettings.Keys.Type] = new EchoObject(2);
        TextureImportSettings s = TextureImportSettings.Read(settings, "Texture.png");
        Assert.Equal(TextureImportType.Default, s.Type);
        Assert.False(s.Srgb);
    }

    [Theory]
    [InlineData(TextureImportType.LookupTable)]
    [InlineData(TextureImportType.Lightmap)]
    public void ExactTypesAreNeverResizedOrCompressedByDefault(TextureImportType type)
    {
        EchoObject settings = EchoObject.NewCompound();
        settings[TextureImportSettings.Keys.Type] = new EchoObject((int)type);
        TextureImportSettings s = TextureImportSettings.Read(settings, "Lightmap-0.png");
        Assert.Equal(16384, s.MaxSize);
        Assert.Equal(TextureCompression.None, s.Compression);
        Assert.Equal(WrapMode.Clamp, s.WrapU);
        Assert.Equal(TextureFormat.RGBA8, TextureImporter.FormatFor(TextureImportFormat.Automatic, TextureCompression.Balanced, type, true));
    }

    [Fact]
    public void SpriteSheetsDefaultToNoCompression_SingleSpritesStayCompressed()
    {
        EchoObject sheet = SpriteSettings(SpriteMode.Multiple);
        EchoObject single = SpriteSettings(SpriteMode.Single);
        Assert.Equal(TextureCompression.None, TextureImportSettings.Read(sheet, "Sheet.png").Compression);
        Assert.Equal(TextureCompression.Balanced, TextureImportSettings.Read(single, "Icon.png").Compression);
    }

    [Fact]
    public void AnOldMetaSlicingSpritesWithoutAType_IsASprite()
    {
        EchoObject settings = EchoObject.NewCompound();
        EchoObject sprite = EchoObject.NewCompound();
        sprite["Mode"] = new EchoObject(2L);
        settings["sprite"] = sprite;
        Assert.Equal(TextureImportType.Sprite, TextureImportSettings.Read(settings, "Sheet.png").Type);
    }

    [Fact]
    public void ASpriteTextureSavedWithoutAMode_KeepsMakingASprite()
    {
        EchoObject settings = SpriteSettings(SpriteMode.None);
        Assert.Equal(SpriteMode.Single, TextureSpriteMeta.ReadFrom(settings).Mode);

        TextureSpriteMeta.WriteInto(settings, TextureSpriteMeta.ReadFrom(settings));
        Assert.Equal((int)TextureImportType.Sprite, settings[TextureImportSettings.Keys.Type].IntValue);
    }

    [Fact]
    public void SlicesAreAuthoredInSourcePixels_AndKeepTheirWorldSizeWhenDownscaled()
    {
        var texture = new Texture2D(64, 32);
        var settings = new SpriteImportSettings { Mode = SpriteMode.Multiple, PixelsPerUnit = 100f };
        settings.Slices.Add(new SpriteSliceData { Id = System.Guid.NewGuid(), Name = "a", Rect = new SpriteRect(16, 16, 32, 32), Border = new Prowl.Vector.Float4(4, 8, 4, 8) });

        Sprite sprite = Assert.Single(SpriteBuilder.Build(texture, settings, 128, 64, null)).sprite;

        Assert.Equal((8, 8, 16, 16), (sprite.Rect.X, sprite.Rect.Y, sprite.Rect.Width, sprite.Rect.Height));
        Assert.Equal(50f, sprite.PixelsPerUnit);
        Assert.Equal(2f, sprite.Border.X);
        Assert.Equal(4f, sprite.Border.Y);
    }

    [Fact]
    public void ASingleSpriteAlwaysCoversTheWholeTexture()
    {
        var texture = new Texture2D(32, 32);
        var settings = new SpriteImportSettings { Mode = SpriteMode.Single };
        settings.Slices.Add(new SpriteSliceData { Id = System.Guid.NewGuid(), Name = "stale", Rect = new SpriteRect(0, 0, 8, 8) });

        Sprite sprite = Assert.Single(SpriteBuilder.Build(texture, settings, 64, 64, null)).sprite;

        Assert.Equal((0, 0, 32, 32), (sprite.Rect.X, sprite.Rect.Y, sprite.Rect.Width, sprite.Rect.Height));
        Assert.Equal("stale", sprite.Name);
    }

    private static EchoObject SpriteSettings(SpriteMode mode)
    {
        EchoObject settings = EchoObject.NewCompound();
        settings[TextureImportSettings.Keys.Type] = new EchoObject((int)TextureImportType.Sprite);
        EchoObject sprite = EchoObject.NewCompound();
        sprite["Mode"] = new EchoObject((long)mode);
        settings["sprite"] = sprite;
        return settings;
    }
}
