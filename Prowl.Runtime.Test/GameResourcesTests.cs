// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace Prowl.Runtime.Test;

public class GameResourcesTests : IDisposable
{
    private sealed class FakeTexture : Asset { }
    private sealed class FakeMesh : Asset { }
    private sealed class FakePrefab : Asset { }

    private static readonly Guid Grass = Guid.NewGuid();
    private static readonly Guid Hero = Guid.NewGuid();
    private static readonly Guid HeroBody = Guid.NewGuid();

    private readonly MemoryAssetBackend _backend = new();
    private readonly AssetBackend? _previousBackend = AssetDatabase.Backend;

    public GameResourcesTests()
    {
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = _backend;
        Initialize(
            Entry("Art/Resources/Textures/Grass.png", Grass, new FakeTexture()),
            Entry("Art/Resources/Models/Hero.fbx", Hero, new FakePrefab()),
            Entry("Art/Resources/Models/Hero.fbx", HeroBody, new FakeMesh(), "Body"));
    }

    public void Dispose()
    {
        AssetDatabase.Backend = _previousBackend;
        AssetDatabase.ClearForTests();
    }

    private ResourceEntry Entry(string assetPath, Guid guid, Asset asset, string? subAsset = null)
    {
        _backend.Add(guid, asset, assetPath);
        return new ResourceEntry(AssetDatabase.GetLoadPath(assetPath, subAsset)!, guid, asset.GetType().AssemblyQualifiedName!);
    }

    private void Initialize(params ResourceEntry[] entries) => _backend.ResourceEntries = entries;

    private static Guid IdOf(Asset? asset) => asset is null ? Guid.Empty : asset.AssetID;

    [Theory]
    [InlineData("Resources/Grass.png", "Grass")]
    [InlineData("Art/Resources/Textures/Grass.png", "Textures/Grass")]
    [InlineData(@"Art\Resources\Textures\Grass.png", "Textures/Grass")]
    [InlineData("Resources/UI/Resources/Icons/Heart.png", "Icons/Heart")]
    [InlineData("Resources/Data.v2/Config.json", "Data.v2/Config")]
    [InlineData("Resources/Data.v2/Config", "Data.v2/Config")]
    [InlineData("Resources/Folder/Resources.png", "Folder/Resources")]
    public void GetLoadPath_IsThePathBelowTheNearestResourcesFolder(string assetPath, string expected)
    {
        Assert.Equal(expected, AssetDatabase.GetLoadPath(assetPath));
    }

    [Theory]
    [InlineData("Art/Textures/Grass.png")]
    [InlineData("Resources.png")]
    [InlineData("Art/Resources")]
    [InlineData("")]
    public void GetLoadPath_IsNullOutsideResourcesFolders(string assetPath)
    {
        Assert.Null(AssetDatabase.GetLoadPath(assetPath));
    }

    [Fact]
    public void GetLoadPath_AppendsTheSubAssetName()
    {
        Assert.Equal("Models/Hero#Body", AssetDatabase.GetLoadPath("Art/Resources/Models/Hero.fbx", "Body"));
    }

    [Theory]
    [InlineData("Textures/Grass")]
    [InlineData("textures/grass")]
    [InlineData("Textures/Grass.png")]
    [InlineData("Art/Resources/Textures/Grass.png")]
    [InlineData("Assets/Art/Resources/Textures/Grass.png")]
    public void Assets_ResolveByLoadPathOrCopiedPath(string path)
    {
        Assert.Equal(Grass, AssetDatabase.FindResourceGuid<Asset>(path));
        Assert.Equal(Grass, IdOf(AssetDatabase.FindResource<FakeTexture>(path)));
    }

    [Theory]
    [InlineData("Models/Hero#Body")]
    [InlineData("Art/Resources/Models/Hero.fbx#Body")]
    public void SubAssets_ResolveByLoadPathOrCopiedPath(string path)
    {
        Assert.Equal(HeroBody, AssetDatabase.FindResourceGuid<Asset>(path));
    }

    [Fact]
    public void ParentAndSubAsset_ResolveSeparately()
    {
        Assert.Equal(Hero, AssetDatabase.FindResourceGuid<Asset>("Art/Resources/Models/Hero.fbx"));
        Assert.Equal(HeroBody, AssetDatabase.FindResourceGuid<Asset>("Art/Resources/Models/Hero.fbx#Body"));
        Assert.Equal(Guid.Empty, AssetDatabase.FindResourceGuid<Asset>("Models/Hero#Missing"));
    }

    [Theory]
    [InlineData("Art/Textures/Grass.png")]
    [InlineData("Art/Other/Textures/Grass.png")]
    [InlineData("Art/Resources/Grass.png")]
    [InlineData("")]
    public void PathsThatDoNotMapToAResource_ResolveToNothing(string path)
    {
        Assert.Equal(Guid.Empty, AssetDatabase.FindResourceGuid<Asset>(path));
        Assert.Null(AssetDatabase.FindResource<Asset>(path));
    }

    [Fact]
    public void SharedPath_LoadsTheAssetOfTheRequestedType()
    {
        Guid texture = Guid.NewGuid(), prefab = Guid.NewGuid();
        Initialize(
            Entry("Resources/Enemy.png", texture, new FakeTexture()),
            Entry("Resources/Enemy.prefab", prefab, new FakePrefab()));

        Assert.Equal(prefab, IdOf(AssetDatabase.FindResource<FakePrefab>("Enemy")));
        Assert.Equal(texture, IdOf(AssetDatabase.FindResource<FakeTexture>("Enemy")));
        Assert.Null(AssetDatabase.FindResource<FakeMesh>("Enemy"));
        Assert.Equal(prefab, AssetDatabase.FindResourceGuid<FakePrefab>("Enemy"));
        Assert.Equal(texture, AssetDatabase.FindResourceGuid<Asset>("Enemy"));
    }

    [Fact]
    public void SharedPath_OnlyLoadsTheMatchingAsset()
    {
        Guid texture = Guid.NewGuid(), prefab = Guid.NewGuid();
        Initialize(
            Entry("Resources/Enemy.png", texture, new FakeTexture()),
            Entry("Resources/Enemy.prefab", prefab, new FakePrefab()));

        AssetDatabase.FindResource<FakePrefab>("Enemy");

        Assert.Equal([prefab], _backend.Reads.Keys);
    }

    [Fact]
    public void SharedPathAndType_TheFirstEntryWins()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        Initialize(
            Entry("A/Resources/Icons/Heart.png", first, new FakeTexture()),
            Entry("B/Resources/Icons/Heart.png", second, new FakeTexture()));

        Assert.Equal(first, IdOf(AssetDatabase.FindResource<FakeTexture>("Icons/Heart")));
        Assert.Equal(first, IdOf(AssetDatabase.FindResource<FakeTexture>("B/Resources/Icons/Heart.png")));
    }

    [Fact]
    public void UnresolvedTypeName_IsCheckedAgainstTheAsset()
    {
        Guid guid = Guid.NewGuid();
        _backend.Add(guid, new FakeTexture(), "Resources/Grass.png");
        Initialize(new ResourceEntry("Grass", guid, ""));

        Assert.Null(AssetDatabase.FindResource<FakePrefab>("Grass"));
        Assert.Equal(guid, IdOf(AssetDatabase.FindResource<FakeTexture>("Grass")));
    }

    [Fact]
    public void FindAll_ReturnsEveryResourceOfTheType_Loaded()
    {
        List<FakeTexture> textures = AssetDatabase.FindAllResources<FakeTexture>();

        Assert.Equal([Grass], textures.Select(IdOf));
        Assert.All(textures, t => Assert.True(t.IsLoaded));
        Assert.Equal([Grass, Hero, HeroBody], AssetDatabase.FindAllResources<Asset>().Select(IdOf));
    }

    [Fact]
    public void FindAll_InAFolderIncludesSubFoldersAndSubAssets()
    {
        Guid heart = Guid.NewGuid(), star = Guid.NewGuid(), starSprite = Guid.NewGuid(), other = Guid.NewGuid();
        Initialize(
            Entry("Resources/Icons/Heart.png", heart, new FakeTexture()),
            Entry("Resources/Icons/Small/Star.png", star, new FakeTexture()),
            Entry("Resources/Icons/Small/Star.png", starSprite, new FakeTexture(), "Star"),
            Entry("Resources/Icons2/Other.png", other, new FakeTexture()));

        Assert.Equal([heart, star, starSprite], AssetDatabase.FindAllResources<FakeTexture>("Icons").Select(IdOf));
        Assert.Equal(3, AssetDatabase.FindAllResources<FakeTexture>("Resources/Icons").Count);
    }

    [Fact]
    public void FindAll_ForAFileReturnsItAndItsSubAssets()
    {
        Assert.Equal([Hero, HeroBody], AssetDatabase.FindAllResources<Asset>("Models/Hero").Select(IdOf));
        Assert.Equal([HeroBody], AssetDatabase.FindAllResources<FakeMesh>("Art/Resources/Models/Hero.fbx").Select(IdOf));
    }
}
