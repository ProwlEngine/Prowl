// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace Prowl.Runtime.Test;

public class GameResourcesTests : IDisposable
{
    private sealed class FakeTexture : EngineObject { }
    private sealed class FakeMesh : EngineObject { }
    private sealed class FakePrefab : EngineObject { }

    private sealed class FakeBackend : AssetBackendBase
    {
        public readonly Dictionary<Guid, EngineObject> Assets = [];
        public readonly List<Guid> Loaded = [];

        protected override EngineObject? LoadFresh(Guid assetId)
        {
            Loaded.Add(assetId);
            if (!Assets.TryGetValue(assetId, out var asset)) return null;
            SetLoaded(assetId, asset);
            return asset;
        }
    }

    private static readonly Guid Grass = Guid.NewGuid();
    private static readonly Guid Hero = Guid.NewGuid();
    private static readonly Guid HeroBody = Guid.NewGuid();

    private readonly FakeBackend _backend = new();
    private readonly AssetBackendBase? _previousBackend = AssetDatabase.Current;

    public GameResourcesTests()
    {
        AssetDatabase.Current = _backend;
        Initialize(
            Entry("Art/Resources/Textures/Grass.png", Grass, new FakeTexture()),
            Entry("Art/Resources/Models/Hero.fbx", Hero, new FakePrefab()),
            Entry("Art/Resources/Models/Hero.fbx", HeroBody, new FakeMesh(), "Body"));
    }

    public void Dispose()
    {
        GameResources.Initialize(null);
        AssetDatabase.Current = _previousBackend;
    }

    private ResourceEntry Entry(string assetPath, Guid guid, EngineObject asset, string? subAsset = null)
    {
        _backend.Assets[guid] = asset;
        return new ResourceEntry(GameResources.GetLoadPath(assetPath, subAsset)!, guid, asset.GetType().AssemblyQualifiedName!);
    }

    private static void Initialize(params ResourceEntry[] entries) => GameResources.Initialize(entries);

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
        Assert.Equal(expected, GameResources.GetLoadPath(assetPath));
    }

    [Theory]
    [InlineData("Art/Textures/Grass.png")]
    [InlineData("Resources.png")]
    [InlineData("Art/Resources")]
    [InlineData("")]
    public void GetLoadPath_IsNullOutsideResourcesFolders(string assetPath)
    {
        Assert.Null(GameResources.GetLoadPath(assetPath));
    }

    [Fact]
    public void GetLoadPath_AppendsTheSubAssetName()
    {
        Assert.Equal("Models/Hero#Body", GameResources.GetLoadPath("Art/Resources/Models/Hero.fbx", "Body"));
    }

    [Theory]
    [InlineData("Textures/Grass")]
    [InlineData("textures/grass")]
    [InlineData("Textures/Grass.png")]
    [InlineData("Art/Resources/Textures/Grass.png")]
    [InlineData("Assets/Art/Resources/Textures/Grass.png")]
    public void Assets_ResolveByLoadPathOrCopiedPath(string path)
    {
        Assert.Equal(Grass, GameResources.GetGuid(path));
        Assert.True(GameResources.Exists(path));
    }

    [Theory]
    [InlineData("Models/Hero#Body")]
    [InlineData("Art/Resources/Models/Hero.fbx#Body")]
    public void SubAssets_ResolveByLoadPathOrCopiedPath(string path)
    {
        Assert.Equal(HeroBody, GameResources.GetGuid(path));
    }

    [Fact]
    public void ParentAndSubAsset_ResolveSeparately()
    {
        Assert.Equal(Hero, GameResources.GetGuid("Art/Resources/Models/Hero.fbx"));
        Assert.Equal(HeroBody, GameResources.GetGuid("Art/Resources/Models/Hero.fbx#Body"));
        Assert.Equal(Guid.Empty, GameResources.GetGuid("Models/Hero#Missing"));
    }

    [Theory]
    [InlineData("Art/Textures/Grass.png")]
    [InlineData("Art/Other/Textures/Grass.png")]
    [InlineData("Art/Resources/Grass.png")]
    [InlineData("")]
    public void PathsThatDoNotMapToAResource_ResolveToNothing(string path)
    {
        Assert.Equal(Guid.Empty, GameResources.GetGuid(path));
        Assert.False(GameResources.Exists(path));
    }

    [Fact]
    public void SharedPath_LoadsTheAssetOfTheRequestedType()
    {
        Guid texture = Guid.NewGuid(), prefab = Guid.NewGuid();
        Initialize(
            Entry("Resources/Enemy.png", texture, new FakeTexture()),
            Entry("Resources/Enemy.prefab", prefab, new FakePrefab()));

        Assert.Same(_backend.Assets[prefab], GameResources.Load<FakePrefab>("Enemy"));
        Assert.Same(_backend.Assets[texture], GameResources.Load<FakeTexture>("Enemy"));
        Assert.Null(GameResources.Load<FakeMesh>("Enemy"));
        Assert.Equal(prefab, GameResources.GetGuid<FakePrefab>("Enemy"));
        Assert.Equal(texture, GameResources.GetGuid("Enemy"));
    }

    [Fact]
    public void SharedPath_OnlyLoadsTheMatchingAsset()
    {
        Guid texture = Guid.NewGuid(), prefab = Guid.NewGuid();
        Initialize(
            Entry("Resources/Enemy.png", texture, new FakeTexture()),
            Entry("Resources/Enemy.prefab", prefab, new FakePrefab()));

        GameResources.Load<FakePrefab>("Enemy");

        Assert.Equal([prefab], _backend.Loaded);
    }

    [Fact]
    public void SharedPathAndType_TheFirstEntryWins()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        Initialize(
            Entry("A/Resources/Icons/Heart.png", first, new FakeTexture()),
            Entry("B/Resources/Icons/Heart.png", second, new FakeTexture()));

        Assert.Same(_backend.Assets[first], GameResources.Load<FakeTexture>("Icons/Heart"));
        Assert.Same(_backend.Assets[first], GameResources.Load<FakeTexture>("B/Resources/Icons/Heart.png"));
    }

    [Fact]
    public void UnresolvedTypeName_IsCheckedOnceLoaded()
    {
        Guid guid = Guid.NewGuid();
        _backend.Assets[guid] = new FakeTexture();
        GameResources.Initialize([new ResourceEntry("Grass", guid, "")]);

        Assert.Null(GameResources.Load<FakePrefab>("Grass"));
        Assert.Same(_backend.Assets[guid], GameResources.Load<FakeTexture>("Grass"));
    }

    [Fact]
    public void LoadAll_ReturnsEveryResourceOfTheType()
    {
        Assert.Equal([_backend.Assets[Grass]], GameResources.LoadAll<FakeTexture>());
        Assert.Equal([_backend.Assets[Grass], _backend.Assets[Hero], _backend.Assets[HeroBody]], GameResources.LoadAll<EngineObject>());
    }

    [Fact]
    public void LoadAll_InAFolderIncludesSubFoldersAndSubAssets()
    {
        Guid heart = Guid.NewGuid(), star = Guid.NewGuid(), starSprite = Guid.NewGuid(), other = Guid.NewGuid();
        Initialize(
            Entry("Resources/Icons/Heart.png", heart, new FakeTexture()),
            Entry("Resources/Icons/Small/Star.png", star, new FakeTexture()),
            Entry("Resources/Icons/Small/Star.png", starSprite, new FakeTexture(), "Star"),
            Entry("Resources/Icons2/Other.png", other, new FakeTexture()));

        var icons = GameResources.LoadAll<FakeTexture>("Icons");

        Assert.Equal([heart, star, starSprite], icons.Select(i => _backend.Assets.First(a => a.Value == i).Key));
        Assert.Equal(3, GameResources.LoadAll<FakeTexture>("Resources/Icons").Count);
    }

    [Fact]
    public void LoadAll_ForAFileReturnsItAndItsSubAssets()
    {
        Assert.Equal([_backend.Assets[Hero], _backend.Assets[HeroBody]], GameResources.LoadAll<EngineObject>("Models/Hero"));
        Assert.Equal([_backend.Assets[HeroBody]], GameResources.LoadAll<FakeMesh>("Art/Resources/Models/Hero.fbx"));
    }
}
