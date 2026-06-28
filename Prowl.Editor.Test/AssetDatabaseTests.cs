// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A component that references another asset, for dependency-tracking tests.</summary>
public sealed class AssetRefComponent : MonoBehaviour
{
    public AssetRef<Scene> Ref;
}

/// <summary>
/// Thorough tests for the editor asset database: create/import/resolve, GUID stability across
/// re-open, the metadata cache, move/delete/rename, queries, dependency tracking, and edge cases.
/// </summary>
public class AssetDatabaseTests : EditorTestHarness
{
    private Guid CreateScene(string path) => CreateSceneAsset(new Scene(), path);

    // ---------------------------------------------------------------------
    // CreateAsset / resolve / index
    // ---------------------------------------------------------------------

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

    // ---------------------------------------------------------------------
    // Get / GetCached edge cases
    // ---------------------------------------------------------------------

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

    // ---------------------------------------------------------------------
    // Path / guid query edge cases
    // ---------------------------------------------------------------------

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

    // ---------------------------------------------------------------------
    // ImportFile
    // ---------------------------------------------------------------------

    [Fact]
    public void ImportFile_NonExistent_ReturnsEmpty() => Assert.Equal(Guid.Empty, Assets.ImportFile("nope.scene"));

    [Fact]
    public void ImportFile_Existing_ReimportsInPlace_PreservesGuid()
    {
        Guid g = CreateScene("S.scene");
        Guid again = Assets.ImportFile("S.scene");
        Assert.Equal(g, again);
    }

    // ---------------------------------------------------------------------
    // GUID stability across re-open (meta files + metadata cache)
    // ---------------------------------------------------------------------

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

    // ---------------------------------------------------------------------
    // Move / delete / save / reimport
    // ---------------------------------------------------------------------

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

    // ---------------------------------------------------------------------
    // Queries
    // ---------------------------------------------------------------------

    [Fact]
    public void FindAssetsOfType_FiltersByType()
    {
        CreateScene("S.scene");
        CreatePrefabAsset(new GameObject("P"), "P.prefab");

        var scenes = Assets.FindAssetsOfType<Scene>().ToList();

        Assert.Single(scenes);
        Assert.Equal("S.scene", scenes[0].Path);
    }

    // ---------------------------------------------------------------------
    // Dependencies
    // ---------------------------------------------------------------------

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

    // ---------------------------------------------------------------------
    // Edge cases
    // ---------------------------------------------------------------------

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
}
