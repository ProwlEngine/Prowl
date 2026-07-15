// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using ImageMagick;

using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Regression tests for the asset database's idle-timeout eviction model: assets stay resolvable as
/// long as something touches them, and are evicted deterministically via AssetDatabase.ForceIdle +
/// EditorAssetDatabase.ForceIdleSweep instead of GC.Collect().
/// </summary>
public class AssetUnloadingTests : EditorTestHarness
{
    public AssetUnloadingTests()
    {
        AssetDatabase.ClearForTests();
    }

    [Fact]
    public void IdleAsset_IsDisposedAndEvicted_AfterSweep()
    {
        Guid guid = CreateSceneAsset(new Scene(), "Idle.scene");
        var loaded = Assets.GetLoadedAsset(guid);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsDisposed);

        AssetDatabase.ForceIdle(guid);
        Assets.ForceIdleSweep();

        Assert.True(loaded.IsDisposed);
        Assert.Null(Assets.GetLoadedAsset(guid));
    }

    [Fact]
    public void RecentlyTouchedAsset_SurvivesSweep_AndResolvesToSameInstance()
    {
        Guid guid = CreateSceneAsset(new Scene(), "Fresh.scene");
        var loaded = Assets.GetLoadedAsset(guid); // touches it - not idle

        Assets.ForceIdleSweep();

        Assert.False(loaded.IsDisposed);
        Assert.Same(loaded, Assets.Get(guid));
    }

    [Fact]
    public void AssetRefEquality_HoldsAcrossEvictionAndReload()
    {
        Guid guid = CreateSceneAsset(new Scene(), "EqScene.scene");

        var refFirst = new AssetRef<Scene>(guid);
        refFirst.EnsureLoaded();
        Assert.NotNull(refFirst.Res);

        AssetDatabase.ForceIdle(guid);
        Assets.ForceIdleSweep();
        Assert.Null(Assets.GetLoadedAsset(guid)); // sanity: really evicted

        var refSecond = new AssetRef<Scene>(guid);
        refSecond.EnsureLoaded();
        Assert.NotNull(refSecond.Res);

        // A fresh, never-resolved AssetRef for the same GUID compares equal via AssetID alone,
        // regardless of which instance (before/after eviction) either side has cached.
        var refA = new AssetRef<Scene>(guid);
        Assert.True(refA == refSecond);
        Assert.Equal(refA.GetHashCode(), refSecond.GetHashCode());
    }

    private (Guid texGuid, Guid spriteGuid) CreateTextureWithSprite(string path)
    {
        string pngPath = AssetAbsolutePath(path);
        using (var image = new MagickImage(new MagickColor(1, 2, 3, 255), 4, 4))
        {
            image.Format = MagickFormat.Png;
            image.Write(pngPath);
        }
        Guid texGuid = Assets.ImportFile(path);
        Assert.NotEqual(Guid.Empty, texGuid);

        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.Single });
        Guid spriteGuid = Assets.GetSubAssets(texGuid)[0].Guid;
        Assert.NotNull(Assets.GetLoadedAsset(spriteGuid));
        return (texGuid, spriteGuid);
    }

    [Fact]
    public void SubAsset_ReloadsAfterEviction_WhenParentReimports()
    {
        var (texGuid, spriteGuid) = CreateTextureWithSprite("SubAssetReload.png");

        AssetDatabase.ForceIdle(texGuid);
        AssetDatabase.ForceIdle(spriteGuid);
        Assets.ForceIdleSweep();
        Assert.Null(Assets.GetLoadedAsset(spriteGuid)); // sanity: the sub-asset really was evicted

        Assets.Reimport(texGuid);

        Assert.NotNull(Assets.GetLoadedAsset(spriteGuid));
    }

    // A sub-asset never loads except as a side effect of loading its parent, so it can't be
    // independently idle-evicted either - ResolveFamily routes its touch/idle checks to the parent.
    [Fact]
    public void SubAssetAndParent_AreEvictedTogether_NeverPartially()
    {
        var (texGuid, spriteGuid) = CreateTextureWithSprite("SubAssetFamilyEviction.png");

        // Forcing just the sub-asset idle must resolve to the shared family entry.
        AssetDatabase.ForceIdle(spriteGuid);
        Assets.ForceIdleSweep();

        Assert.Null(Assets.GetLoadedAsset(texGuid));    // parent evicted too
        Assert.Null(Assets.GetLoadedAsset(spriteGuid)); // sub evicted
    }

    [Fact]
    public void TouchingSubAsset_KeepsWholeFamilyAlive()
    {
        var (texGuid, spriteGuid) = CreateTextureWithSprite("SubAssetFamilyTouch.png");
        AssetDatabase.ForceIdle(texGuid); // baseline: whole family idle

        Assets.Get(spriteGuid); // touching only the sub-asset...

        Assets.ForceIdleSweep();

        Assert.NotNull(Assets.GetLoadedAsset(texGuid));    // ...keeps the parent alive too
        Assert.NotNull(Assets.GetLoadedAsset(spriteGuid));
    }

    [Fact]
    public void LockingSubAsset_LocksTheWholeFamily()
    {
        var (texGuid, spriteGuid) = CreateTextureWithSprite("SubAssetFamilyLock.png");

        AssetDatabase.LockPermanent(spriteGuid);
        Assert.True(AssetDatabase.IsLocked(texGuid)); // locking the sub locked the parent too

        AssetDatabase.ForceIdle(texGuid);
        Assets.ForceIdleSweep();
        Assert.NotNull(Assets.GetLoadedAsset(texGuid));
        Assert.NotNull(Assets.GetLoadedAsset(spriteGuid));

        AssetDatabase.Unlock(spriteGuid); // unlocking via the sub releases the same family lock
        AssetDatabase.ForceIdle(texGuid);
        Assets.ForceIdleSweep();
        Assert.Null(Assets.GetLoadedAsset(texGuid));
    }

    [Fact]
    public void LockPermanent_PreventsIdleEviction_UntilUnlocked()
    {
        Guid guid = CreateSceneAsset(new Scene(), "LockedPermanent.scene");
        AssetDatabase.LockPermanent(guid);

        AssetDatabase.ForceIdle(guid);
        Assets.ForceIdleSweep();

        Assert.NotNull(Assets.GetLoadedAsset(guid)); // locked - the sweep must have skipped it

        AssetDatabase.Unlock(guid);
        AssetDatabase.ForceIdle(guid);
        Assets.ForceIdleSweep();

        Assert.Null(Assets.GetLoadedAsset(guid));
    }

    [Fact]
    public void LockToScene_PreventsIdleEviction_ThenReleasesOnSceneDispose()
    {
        Guid guid = CreateSceneAsset(new Scene(), "LockedToScene.scene");
        var owningScene = new Scene();
        AssetDatabase.LockToScene(guid, owningScene);

        AssetDatabase.ForceIdle(guid);
        Assets.ForceIdleSweep();

        Assert.NotNull(Assets.GetLoadedAsset(guid)); // locked - the sweep must have skipped it

        owningScene.Dispose(); // releases the lock (see AssetDatabase.ReleaseSceneLocks)
        AssetDatabase.ForceIdle(guid);
        Assets.ForceIdleSweep();

        Assert.Null(Assets.GetLoadedAsset(guid));
    }
}
