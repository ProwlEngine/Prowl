// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using ImageMagick;

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A component that references another asset, for dependency-tracking tests.</summary>
public sealed class AssetRefComponent : MonoBehaviour
{
    public AssetRef<Scene> Ref;
}

/// <summary>An importer that always throws, to test that one bad asset can't abort a whole scan/import batch.</summary>
[ImporterFor(".throwtest")]
public sealed class ThrowingTestImporter : AssetImporter
{
    public override int Version => 1;
    public override EchoObject? DefaultSettings() => throw new InvalidOperationException("Simulated importer failure.");
    public override bool Import(ImportContext ctx) => throw new InvalidOperationException("Simulated importer failure.");
}

/// <summary>
/// Thorough tests for the editor asset database: create/import/resolve, GUID stability across
/// re-open, the metadata cache, move/delete/rename, queries, dependency tracking, idle-timeout
/// eviction, locking, sub-asset family behavior, and edge cases.
/// </summary>
public class AssetDatabaseTests : EditorTestHarness
{
    public AssetDatabaseTests()
    {
        AssetDatabase.ClearForTests();
    }

    private Guid CreateScene(string path) => CreateSceneAsset(new Scene(), path);

    #region Create / Import / Resolve

    [Fact]
    public void CreateAsset_WritesFileAndMeta()
    {
        CreateScene("S.scene");
        Assert.True(File.Exists(AssetAbsolutePath("S.scene")));
        Assert.True(File.Exists(AssetAbsolutePath("S.scene.meta")));
    }

    [Fact]
    public void CreateAsset_AssignsGuidAndPathToInstance()
    {
        var scene = new Scene();
        Assets.CreateAsset(scene, "S.scene");

        Assert.NotEqual(Guid.Empty, scene.AssetID);
        Assert.Equal("S.scene", scene.AssetPath);
    }

    [Fact]
    public void CreateAsset_ResolvableByGuid()
    {
        Guid g = CreateScene("S.scene");
        Assert.IsType<Scene>(Assets.Get(g));
    }

    [Fact]
    public void CreateAsset_IndexedByPath_RoundTrips()
    {
        Guid g = CreateScene("S.scene");
        Assert.Equal(g, Assets.PathToGuid("S.scene"));
        Assert.Equal("S.scene", Assets.GuidToPath(g));
    }

    [Fact]
    public void CreateAsset_NestedFolders_AreCreated()
    {
        Guid g = CreateScene("A/B/C/Deep.scene");
        Assert.NotEqual(Guid.Empty, g);
        Assert.True(File.Exists(AssetAbsolutePath("A/B/C/Deep.scene")));
    }

    [Fact]
    public void CreateAsset_ShowsInEntryListings()
    {
        CreateScene("S.scene");
        Assert.Contains(Assets.GetAllAssetPaths(), p => p == "S.scene");
        Assert.Contains(Assets.GetAllEntries(), e => e.Path == "S.scene");
    }

    [Fact]
    public void ImportFile_NonExistent_ReturnsEmpty() => Assert.Equal(Guid.Empty, Assets.ImportFile("nope.scene"));

    [Fact]
    public void ImportFile_Existing_ReimportsInPlace_PreservesGuid()
    {
        Guid g = CreateScene("S.scene");
        Guid again = Assets.ImportFile("S.scene");
        Assert.Equal(g, again);
    }

    // Copying an asset together with its .meta (VCS/user duplicate) must yield a distinct GUID for
    // the copy - two files can't share one GUID (or one becomes invisible to the database).
    [Fact]
    public void CopiedAssetWithMeta_GetsDistinctGuid_OnReopen()
    {
        CreateScene("Original.scene");
        File.Copy(AssetAbsolutePath("Original.scene"), AssetAbsolutePath("Copy.scene"));
        File.Copy(AssetAbsolutePath("Original.scene.meta"), AssetAbsolutePath("Copy.scene.meta"));

        ReopenDatabase();

        Guid g1 = Assets.PathToGuid("Original.scene");
        Guid g2 = Assets.PathToGuid("Copy.scene");

        Assert.NotEqual(Guid.Empty, g1);
        Assert.NotEqual(Guid.Empty, g2);
        Assert.NotEqual(g1, g2);
    }

    #endregion

    #region Get / GetCached

    [Fact]
    public void Get_EmptyGuid_ReturnsNull() => Assert.Null(Assets.Get(Guid.Empty));

    [Fact]
    public void Get_UnknownGuid_ReturnsNull() => Assert.Null(Assets.Get(Guid.NewGuid()));

    [Fact]
    public void Get_CachesInstance()
    {
        var scene = new Scene();
        Assets.CreateAsset(scene, "S.scene");

        var a = Assets.Get(scene.AssetID);
        var b = Assets.Get(scene.AssetID);

        Assert.NotNull(a);
        Assert.Same(a, b); // repeated Get returns the same cached instance
    }

    [Fact]
    public void GetCached_NullUntilLoaded_ThenCached()
    {
        Guid g = CreateScene("S.scene");
        ReopenDatabase(); // fresh db: nothing deserialized yet

        Assert.Null(Assets.GetCached(g));
        var loaded = Assets.Get(g);
        Assert.NotNull(loaded);
        Assert.Same(loaded, Assets.GetCached(g));
    }

    [Fact]
    public void GetLoadedAssets_DoesNotCountAsActivity()
    {
        Guid g = CreateScene("S.scene");
        AssetDatabase.TryGetLastTouched(g, out var before);

        _ = Assets.GetLoadedAssets().ToList();

        AssetDatabase.TryGetLastTouched(g, out var after);
        Assert.Equal(before, after);
    }

    #endregion

    #region Path / Guid Queries

    [Fact]
    public void PathToGuid_Unknown_ReturnsEmpty() => Assert.Equal(Guid.Empty, Assets.PathToGuid("nope.scene"));

    [Fact]
    public void GuidToPath_Unknown_ReturnsNull() => Assert.Null(Assets.GuidToPath(Guid.NewGuid()));

    [Fact]
    public void PathToGuid_IsCaseInsensitive()
    {
        Guid g = CreateScene("Folder/S.scene");
        Assert.Equal(g, Assets.PathToGuid("folder/s.scene"));
    }

    [Fact]
    public void GetEntry_ByGuidAndByPath()
    {
        Guid g = CreateScene("S.scene");
        Assert.NotNull(Assets.GetEntry(g));
        Assert.Equal(g, Assets.GetEntry("S.scene")!.Guid);
    }

    #endregion

    #region GUID Stability Across Reopen

    [Fact]
    public void Guid_IsStable_AcrossDatabaseReopen()
    {
        Guid g = CreateScene("S.scene");
        ReopenDatabase();
        Assert.Equal(g, Assets.PathToGuid("S.scene"));
    }

    [Fact]
    public void Reopen_PicksUpFileAddedOutOfBand()
    {
        // Write an asset straight to disk (not through the database), then reopen.
        File.WriteAllText(AssetAbsolutePath("Extra.scene"),
            Serializer.Serialize(typeof(object), new Scene()).WriteToString());

        ReopenDatabase();

        Guid g = Assets.PathToGuid("Extra.scene");
        Assert.NotEqual(Guid.Empty, g);
        Assert.NotNull(Assets.Get(g));
    }

    [Fact]
    public void Reopen_DropsFileDeletedOutOfBand()
    {
        Guid g = CreateScene("S.scene");
        File.Delete(AssetAbsolutePath("S.scene"));
        File.Delete(AssetAbsolutePath("S.scene.meta"));

        ReopenDatabase();

        Assert.Equal(Guid.Empty, Assets.PathToGuid("S.scene"));
        Assert.Null(Assets.GetEntry(g));
    }

    [Fact]
    public void DeletingMetaFile_MintsNewGuid_OnReopen()
    {
        // The .meta is the source of truth for an asset's stable identity. Losing it (e.g. not
        // committed to version control) means the asset gets a fresh GUID and references break.
        Guid original = CreateScene("S.scene");
        File.Delete(AssetAbsolutePath("S.scene.meta"));

        ReopenDatabase();

        Guid regenerated = Assets.PathToGuid("S.scene");
        Assert.NotEqual(Guid.Empty, regenerated);
        Assert.NotEqual(original, regenerated);
    }

    #endregion

    #region Move / Delete / Save / Reimport

    [Fact]
    public void MoveAsset_PreservesGuid_MovesFileAndMeta()
    {
        Guid g = CreateScene("S.scene");

        bool ok = Assets.MoveAsset("S.scene", "Sub/Moved.scene");

        Assert.True(ok);
        Assert.Equal(g, Assets.PathToGuid("Sub/Moved.scene"));
        Assert.Equal(Guid.Empty, Assets.PathToGuid("S.scene"));
        Assert.False(File.Exists(AssetAbsolutePath("S.scene")));
        Assert.True(File.Exists(AssetAbsolutePath("Sub/Moved.scene")));
        Assert.True(File.Exists(AssetAbsolutePath("Sub/Moved.scene.meta")));
    }

    [Fact]
    public void MoveAsset_ToOccupiedPath_Fails()
    {
        CreateScene("A.scene");
        CreateScene("B.scene");
        Assert.False(Assets.MoveAsset("A.scene", "B.scene"));
    }

    [Fact]
    public void MoveAsset_NonExistent_Fails() => Assert.False(Assets.MoveAsset("nope.scene", "x.scene"));

    // On a case-insensitive filesystem (Windows/macOS default), a case-only rename's target path
    // File.Exists-matches the SOURCE file itself, so it must not be treated as "already occupied by
    // a different file" - the user is fixing casing, not colliding with something else.
    [Fact]
    public void MoveAsset_CaseOnlyRename_Succeeds()
    {
        CreateScene("Texture.scene");
        bool ok = Assets.MoveAsset("Texture.scene", "texture.scene");
        Assert.True(ok, "A case-only rename must be allowed, not treated as a name collision.");
        Assert.Equal("texture.scene", Assets.GetEntry(Assets.PathToGuid("texture.scene"))?.Path);
    }

    // NormalizePath only swaps backslashes for forward slashes - it never rejects ".." segments or
    // rooted paths, so a relative path escaping Assets/ lands wherever Path.Combine/the OS resolves it.
    [Fact]
    public void CreateAsset_PathTraversal_IsRejected()
    {
        string escapedPath = Path.GetFullPath(Path.Combine(Project.AssetsPath, "../../../Escaped.scene"));
        try
        {
            Assets.CreateAsset(new Scene(), "../../../Escaped.scene");

            Assert.False(File.Exists(escapedPath),
                "CreateAsset must not be able to write outside the Assets folder via '..' segments.");
        }
        finally
        {
            File.Delete(escapedPath);
            File.Delete(escapedPath + ".meta");
        }
    }

    [Fact]
    public void MoveFolder_PreservesGuids_RemapsPaths()
    {
        Guid g = CreateScene("Old/S.scene");

        bool ok = Assets.MoveFolder("Old", "New");

        Assert.True(ok);
        Assert.Equal(g, Assets.PathToGuid("New/S.scene"));
        Assert.True(File.Exists(AssetAbsolutePath("New/S.scene")));
    }

    [Fact]
    public void DeleteAsset_RemovesFileMetaIndexAndCache()
    {
        Guid g = CreateScene("S.scene");

        Assets.DeleteAsset("S.scene");

        Assert.False(File.Exists(AssetAbsolutePath("S.scene")));
        Assert.False(File.Exists(AssetAbsolutePath("S.scene.meta")));
        Assert.Equal(Guid.Empty, Assets.PathToGuid("S.scene"));
        Assert.Null(Assets.Get(g));
    }

    [Fact]
    public void SaveAsset_PersistsChangesToDisk()
    {
        var scene = new Scene();
        Guid g = CreateSceneAsset(scene, "S.scene");
        scene.Add(new GameObject("Added"));

        Assets.SaveAsset(scene);
        ReopenDatabase();

        var loaded = Assets.Get(g) as Scene;
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Count);
        Assert.Equal("Added", loaded.AllObjects.First().Name);
    }

    [Fact]
    public void Reimport_PicksUpOnDiskEdit_AndReplacesInstance()
    {
        Guid g = CreateScene("S.scene");
        var before = Assets.Get(g);

        // Overwrite the file with a scene that has an object.
        var edited = new Scene();
        edited.Add(new GameObject("X"));
        File.WriteAllText(AssetAbsolutePath("S.scene"),
            Serializer.Serialize(typeof(object), edited).WriteToString());

        Assets.Reimport(g);
        var after = Assets.Get(g) as Scene;

        Assert.NotNull(after);
        Assert.NotSame(before, after);
        Assert.Equal(1, after!.Count);
    }

    #endregion

    #region Queries

    [Fact]
    public void FindAssetsOfType_FiltersByType()
    {
        CreateScene("S.scene");
        CreatePrefabAsset(new GameObject("P"), "P.prefab");

        var scenes = Assets.FindAssetsOfType<Scene>().ToList();

        Assert.Single(scenes);
        Assert.Equal("S.scene", scenes[0].Path);
    }

    #endregion

    #region Import Batch Resilience

    // ScanAssets/ImportDirty call importer.DefaultSettings()/Import() with no try/catch around most of
    // it, so one throwing importer aborts the entire scan - every other asset (including ones already
    // successfully imported before restart) never finishes reconciling, and Initialize() itself throws.
    [Fact]
    public void OneImporterThrowing_DoesNotAbortWholeScan()
    {
        CreateScene("Good.scene");
        File.WriteAllText(AssetAbsolutePath("Bad.throwtest"), "junk");

        var ex = Record.Exception(() => ReopenDatabase());

        Assert.Null(ex);
        // The real guarantee under test: Good.scene must have actually finished reconciling, not
        // merely "reopening didn't throw" - a scan that silently gives up after the bad asset would
        // also pass a no-exception-only check.
        Assert.NotEqual(Guid.Empty, Assets.PathToGuid("Good.scene"));
        Assert.NotNull(Assets.Get(Assets.PathToGuid("Good.scene")));
    }

    #endregion

    #region Folder Index Cache

    // CreateAsset never calls InvalidateFolderIndex (unlike DeleteAsset/MoveAsset/MoveFolder), so once
    // the folder cache has been built, a newly created asset is invisible to GetFolderFiles until some
    // other operation happens to mark it dirty again.
    [Fact]
    public void CreateAsset_IsVisibleInFolderIndex()
    {
        CreateScene("A.scene");
        Assets.GetFolderFiles(""); // build the folder index cache

        CreateScene("B.scene");

        var files = Assets.GetFolderFiles("");
        Assert.Contains(files, f => f.Name == "B.scene");
    }

    // BuildFolderIndex keys folders without a trailing slash, but GetFolderFiles/GetSubFolders only
    // swap backslashes (NormalizePath) and never trim one - so a caller passing a trailing slash
    // (MoveFolder does its own TrimEnd('/') before calling in, but nothing enforces that elsewhere)
    // misses the cached entry entirely.
    [Fact]
    public void GetFolderFiles_TrailingSlash_StillFindsFiles()
    {
        CreateScene("Sub/S.scene");
        var files = Assets.GetFolderFiles("Sub/");
        Assert.Contains(files, f => f.Name == "S.scene");
    }

    #endregion

    #region Dependencies

    [Fact]
    public void Prefab_TracksAssetReferenceDependency()
    {
        Guid sceneGuid = CreateScene("Referenced.scene");

        var go = new GameObject("Holder");
        go.AddComponent<AssetRefComponent>().Ref = new AssetRef<Scene>(sceneGuid);
        Guid prefabGuid = CreatePrefabAsset(go, "Holder.prefab");

        var entry = Assets.GetEntry(prefabGuid);
        Assert.NotNull(entry);
        Assert.Contains(sceneGuid, entry!.Dependencies);
    }

    // PrefabUtility.CompareField builds each override's Value via Serializer.Serialize(fieldType, val)
    // with no tracking context, so an AssetRef GUID living only inside an override blob (not mirrored
    // in the live component field) never reaches ctx.Dependencies during serialize. PrefabImporter's
    // raw-echo walk catches it anyway by also checking "AssetID", but SceneImporter's walk only checks
    // "PrefabAssetId".
    [Fact]
    public void Scene_TracksAssetRefInsidePrefabOverride()
    {
        Guid orphanGuid = CreateScene("Orphaned.scene");

        var go = new GameObject("Holder");
        go.AddComponent<AssetRefComponent>(); // live Ref field stays Guid.Empty
        go.PrefabAssetId = Guid.NewGuid();
        go.PrefabOverrides.Add(new PropertyOverride
        {
            Path = "AssetRefComponent.Ref",
            Value = Serializer.Serialize(typeof(AssetRef<Scene>), new AssetRef<Scene>(orphanGuid))
        });

        var scene = new Scene();
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        var entry = Assets.GetEntry(sceneGuid);
        Assert.NotNull(entry);
        Assert.Contains(orphanGuid, entry!.Dependencies);
    }

    // AudioSource used to serialize the resolved AudioClip inline (Serializer.Serialize(Clip, ctx))
    // instead of its AssetRef<AudioClip> field, so the clip's GUID never reached ctx.Dependencies and
    // a DependenciesOnly build would ship a scene with no audio.
    [Fact]
    public void Scene_TracksAudioSourceClipDependency()
    {
        var clip = new AudioClip([1, 2, 3, 4]) { AssetID = Guid.NewGuid() };

        var go = new GameObject("Holder");
        go.AddComponent<AudioSource>().Clip = clip;

        var scene = new Scene();
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        var entry = Assets.GetEntry(sceneGuid);
        Assert.NotNull(entry);
        Assert.Contains(clip.AssetID, entry!.Dependencies);
    }

    #endregion

    #region Idle Eviction

    [Fact]
    public void IdleAsset_IsDisposedAndEvicted_AfterSweep()
    {
        Guid guid = CreateScene("Idle.scene");
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
        Guid guid = CreateScene("Fresh.scene");
        var loaded = Assets.GetLoadedAsset(guid); // touches it - not idle

        Assets.ForceIdleSweep();

        Assert.False(loaded.IsDisposed);
        Assert.Same(loaded, Assets.Get(guid));
    }

    [Fact]
    public void AssetRefEquality_HoldsAcrossEvictionAndReload()
    {
        Guid guid = CreateScene("EqScene.scene");

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

    [Fact]
    public void ReloadCount_IncrementsEachTimeAssetIsReloadedFromDisk()
    {
        Guid guid = CreateScene("Reload.scene");
        int before = Assets.GetReloadCount(guid);

        AssetDatabase.ForceIdle(guid);
        Assets.ForceIdleSweep();
        Assets.Get(guid); // reload from disk

        Assert.Equal(before + 1, Assets.GetReloadCount(guid));
    }

    [Fact]
    public void ForceIdleSweep_UpdatesLastSweepUtc()
    {
        // A fresh backend has never swept, so this must start far in the past - otherwise the
        // "recent" assertion below would trivially pass even if ForceIdleSweep did nothing.
        Assert.True((DateTime.UtcNow - Assets.LastSweepUtc).TotalDays > 1);

        Assets.ForceIdleSweep();

        Assert.True((DateTime.UtcNow - Assets.LastSweepUtc).TotalSeconds < 2);
    }

    // TickIdleSweep is meant to be called every frame - MaybeSweepIdle's own gate must make repeated
    // calls cheap by only actually scanning once per IdleSweepInterval. ForceIdleSweep just reset that
    // gate, so an immediately-following TickIdleSweep must be a no-op even for an idle asset.
    [Fact]
    public void TickIdleSweep_RespectsInterval_NoOpRightAfterAFullSweep()
    {
        Guid guid = CreateScene("Tick.scene");
        Assets.ForceIdleSweep(); // resets the interval gate

        AssetDatabase.ForceIdle(guid);
        Assets.TickIdleSweep();

        Assert.NotNull(Assets.GetLoadedAsset(guid));
    }

    #endregion

    #region Locking

    [Fact]
    public void LockPermanent_PreventsIdleEviction_UntilUnlocked()
    {
        Guid guid = CreateScene("LockedPermanent.scene");
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
    public void LockPermanent_Twice_IsIdempotent_SingleUnlockReleasesIt()
    {
        Guid guid = CreateScene("DoubleLock.scene");
        AssetDatabase.LockPermanent(guid);
        AssetDatabase.LockPermanent(guid);

        AssetDatabase.Unlock(guid);

        Assert.False(AssetDatabase.IsLocked(guid));
    }

    [Fact]
    public void Unlock_WhenNotLocked_IsNoOp()
    {
        Guid guid = CreateScene("NeverLocked.scene");
        var ex = Record.Exception(() => AssetDatabase.Unlock(guid));

        Assert.Null(ex);
        Assert.False(AssetDatabase.IsLocked(guid));
    }

    [Fact]
    public void LockToScene_PreventsIdleEviction_ThenReleasesOnSceneDispose()
    {
        Guid guid = CreateScene("LockedToScene.scene");
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

    [Fact]
    public void LockToScene_TwoScenes_OneDisposing_DoesNotReleaseTheOthersLock()
    {
        Guid guid = CreateScene("SharedLock.scene");
        var sceneA = new Scene();
        var sceneB = new Scene();
        AssetDatabase.LockToScene(guid, sceneA);
        AssetDatabase.LockToScene(guid, sceneB);

        sceneA.Dispose();
        Assert.True(AssetDatabase.IsLocked(guid)); // sceneB still holds it

        sceneB.Dispose();
        Assert.False(AssetDatabase.IsLocked(guid));
    }

    #endregion

    #region Sub-Asset Family Behavior

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

    #endregion

    #region Edge Cases

    [Fact]
    public void UnknownExtension_IsTrackedButNotResolvable()
    {
        // DefaultImporter tracks the file but produces no runtime asset.
        File.WriteAllText(AssetAbsolutePath("notes.xyz"), "hello");
        ReopenDatabase();

        Guid g = Assets.PathToGuid("notes.xyz");
        Assert.NotEqual(Guid.Empty, g);
        Assert.Null(Assets.Get(g));
    }

    #endregion
}
