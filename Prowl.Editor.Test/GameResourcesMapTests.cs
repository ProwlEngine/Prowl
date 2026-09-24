// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Build;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

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

        Assert.Contains(collected.ResourcesMap, r => r.LoadPath == "Icons/Heart" && r.Guid == texture);
        Assert.Contains(collected.ResourcesMap, r => r.LoadPath == $"Icons/Heart#{spriteName}" && r.Guid == sprite);
        Assert.Contains(sprite, collected.AllAssets);
        Assert.DoesNotContain(collected.ResourcesMap, r => r.LoadPath.StartsWith("Art/", StringComparison.OrdinalIgnoreCase) || r.LoadPath.Contains("Other"));
    }

    [Fact]
    public void Maps_RecordTheAssetType()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        var (texture, sprite, spriteName) = ImportSpriteTexture("Resources/Icons/Heart.png");
        Assets.RefreshResourcesMap();

        Assert.Equal(texture, GameResources.GetGuid<Texture2D>("Icons/Heart"));
        Assert.Equal(sprite, GameResources.GetGuid<Sprite>($"Icons/Heart#{spriteName}"));
        Assert.Equal(Guid.Empty, GameResources.GetGuid<Sprite>("Icons/Heart"));

        var collected = AssetCollector.Collect(Assets, [], dependenciesOnly: true);
        Assert.Contains(collected.ResourcesMap, r => r.Guid == sprite && RuntimeUtils.ResolveType(r.TypeName) == typeof(Sprite));
    }

    [Fact]
    public void SharedLoadPath_ResolvesToTheFirstAssetPath()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        var (second, _, _) = ImportSpriteTexture("B/Resources/Icons/Heart.png");
        var (first, _, _) = ImportSpriteTexture("A/Resources/Icons/Heart.png");
        Assets.RefreshResourcesMap();

        Assert.Equal(first, GameResources.GetGuid<Texture2D>("Icons/Heart"));
        Assert.Equal(first, GameResources.GetGuid<Texture2D>("B/Resources/Icons/Heart.png"));

        var collected = AssetCollector.Collect(Assets, [], dependenciesOnly: true);
        var hearts = collected.ResourcesMap.Where(r => r.LoadPath == "Icons/Heart").Select(r => r.Guid).ToList();
        Assert.Equal([first, second], hearts);
    }
}
