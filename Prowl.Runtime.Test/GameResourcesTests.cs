// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Xunit;

namespace Prowl.Runtime.Test;

public class GameResourcesTests : IDisposable
{
    private static readonly Guid Grass = Guid.NewGuid();
    private static readonly Guid Hero = Guid.NewGuid();
    private static readonly Guid HeroBody = Guid.NewGuid();

    public GameResourcesTests()
    {
        GameResources.Initialize(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            [GameResources.GetLoadPath("Art/Resources/Textures/Grass.png")!] = Grass,
            [GameResources.GetLoadPath("Art/Resources/Models/Hero.fbx")!] = Hero,
            [GameResources.GetLoadPath("Art/Resources/Models/Hero.fbx", "Body")!] = HeroBody,
        });
    }

    public void Dispose() => GameResources.Initialize(null!);

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
}
