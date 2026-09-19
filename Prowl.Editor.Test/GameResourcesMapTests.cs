// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Build;
using Prowl.Editor.Importers;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The Resources map the editor and a build publish covers sub assets, answers to the path the project
/// panel copies, and never reaches outside a Resources folder.
/// </summary>
public class GameResourcesMapTests : EditorTestHarness
{
    private (Guid Texture, Guid Sprite, string SpriteName) ImportSpriteTexture(string relativePath)
    {
        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        TestImages.WriteSolidPng(abs, 4, 10, 200, 90, 255);

        Guid texture = Assets.ImportFile(relativePath);
        Assert.NotEqual(Guid.Empty, texture);

        TextureSpriteMeta.Save(texture, new SpriteImportSettings { Mode = SpriteMode.Single });
        var subs = Assets.GetSubAssets(texture);
        Assert.True(subs.Length > 0, "Expected a Sprite sub asset after enabling Sprite mode.");
        return (texture, subs[0].Guid, subs[0].Name);
    }

    [Fact]
    public void EditorMap_ResolvesAssetsAndSubAssetsInsideResources()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        var (texture, sprite, spriteName) = ImportSpriteTexture("Art/Resources/Icons/Heart.png");
        Assets.RefreshResourcesMap();

        Assert.Equal(texture, GameResources.GetGuid("Icons/Heart"));
        Assert.Equal(sprite, GameResources.GetGuid($"Icons/Heart#{spriteName}"));

        // Exactly what "Copy Path" puts on the clipboard for the asset and its sub asset.
        Assert.Equal(texture, GameResources.GetGuid("Art/Resources/Icons/Heart.png"));
        Assert.Equal(sprite, GameResources.GetGuid($"Art/Resources/Icons/Heart.png#{spriteName}"));
    }

    [Fact]
    public void EditorMap_IgnoresAssetsOutsideResources()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        var (_, _, spriteName) = ImportSpriteTexture("Art/Icons/Heart.png");
        Assets.RefreshResourcesMap();

        Assert.Equal(Guid.Empty, GameResources.GetGuid("Art/Icons/Heart.png"));
        Assert.Equal(Guid.Empty, GameResources.GetGuid($"Art/Icons/Heart.png#{spriteName}"));
        Assert.Equal(Guid.Empty, GameResources.GetGuid("Art/Icons/Heart"));
        Assert.Equal(Guid.Empty, GameResources.GetGuid("Icons/Heart"));
    }

    [Fact]
    public void BuildMap_IncludesSubAssetsOfResources()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        var (texture, sprite, spriteName) = ImportSpriteTexture("Resources/Icons/Heart.png");
        ImportSpriteTexture("Art/Icons/Other.png");

        var collected = AssetCollector.Collect(Assets, [], dependenciesOnly: true);

        Assert.Equal(texture, collected.ResourcesMap["Icons/Heart"]);
        Assert.Equal(sprite, collected.ResourcesMap[$"Icons/Heart#{spriteName}"]);
        Assert.Contains(sprite, collected.AllAssets);
        Assert.DoesNotContain(collected.ResourcesMap.Keys, k => k.StartsWith("Art/", StringComparison.OrdinalIgnoreCase) || k.Contains("Other"));
    }
}
