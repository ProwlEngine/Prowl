// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.


using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>A component that names a scene lazily, for soft dependency tests.</summary>
public sealed class AssetRefComponent : Component
{
    public AssetRef<SceneAsset> Ref;
}

/// <summary>A component that holds a material in a plain field, for hard dependency tests.</summary>
public sealed class MaterialHolder : Component
{
    public Material? Material;
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
/// The editor asset database: create/import/resolve, GUID stability across re-open, move/delete/rename, queries,
/// dependency tracking, and what the new model promises about identity: one object per GUID that reimports, saves,
/// deletes and renames all act on in place.
/// </summary>
public class AssetDatabaseTests : EditorTestHarness
{
    private Guid CreateScene(string path) => CreateSceneAsset(new Scene(), path);

    private Material CreateMaterial(string path, float value = 1f)
    {
        var material = new Material();
        material.SetFloat("_Value", value);
        Assets.CreateAsset(material, path);
        return material;
    }

    private void WriteMaterialFile(string path, float value)
    {
        var material = new Material();
        material.SetFloat("_Value", value);
        File.WriteAllText(AssetAbsolutePath(path), Serializer.Serialize(typeof(object), material).WriteToString());
    }

    #region Create / Import / Resolve

    [Fact]
    public void CreateScene_WritesFileAndMeta()
    {
        CreateScene("S.scene");
        Assert.True(File.Exists(AssetAbsolutePath("S.scene")));
        Assert.True(File.Exists(AssetAbsolutePath("S.scene.meta")));
    }

    [Fact]
    public void CreateAsset_MakesTheCallersObjectTheAsset()
    {
        Material material = CreateMaterial("M.mat");

        Assert.NotEqual(Guid.Empty, material.AssetID);
        Assert.Equal("M.mat", material.AssetPath);
        Assert.Same(material, AssetDatabase.Get(material.AssetID));
        Assert.True(material.IsLoaded);
    }

    [Fact]
    public void AScene_ImportsAsItsStoredForm()
    {
        Guid g = CreateScene("S.scene");
        Assert.IsType<SceneAsset>(AssetDatabase.Load<Asset>(g));
    }

    [Fact]
    public void CreateScene_IndexedByPath_RoundTrips()
    {
        Guid g = CreateScene("S.scene");
        Assert.Equal(g, Assets.PathToGuid("S.scene"));
        Assert.Equal("S.scene", Assets.GuidToPath(g));
    }

    [Fact]
    public void CreateScene_NestedFolders_AreCreated()
    {
        Guid g = CreateScene("A/B/C/Deep.scene");
        Assert.NotEqual(Guid.Empty, g);
        Assert.True(File.Exists(AssetAbsolutePath("A/B/C/Deep.scene")));
    }

    [Fact]
    public void CreateScene_ShowsInEntryListings()
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

    #region Identity

    [Fact]
    public void Get_EmptyGuid_ReturnsNull() => Assert.Null(AssetDatabase.Get(Guid.Empty));

    [Fact]
    public void Get_UnknownGuid_ReturnsNothingUntyped_AndAMissingAssetTyped()
    {
        Guid unknown = Guid.NewGuid();

        Assert.Null(AssetDatabase.Get(unknown));
        SceneAsset? missing = AssetDatabase.Get<SceneAsset>(unknown);
        Assert.NotNull(missing);
        Assert.True(missing!.IsMissing);
        Assert.Equal(unknown, missing.AssetID);
    }

    [Fact]
    public void Get_ReturnsOneObjectPerGuid_WithoutLoadingIt()
    {
        Guid g = CreateScene("S.scene");
        ReopenDatabase();
        AssetDatabase.ClearForTests();

        Asset? a = AssetDatabase.Get(g);
        Asset? b = AssetDatabase.Get(g);

        Assert.NotNull(a);
        Assert.Same(a, b);
        Assert.Equal(AssetState.Unloaded, a!.State);

        a.Load();
        Assert.True(a.IsLoaded);
        Assert.Same(a, AssetDatabase.Get(g));
    }

    [Fact]
    public void ADatabaseAsset_CannotBeDisposed()
    {
        Material material = CreateMaterial("M.mat");
        Assert.Throws<InvalidOperationException>(material.Dispose);
    }

    [Fact]
    public void AStub_ReadsBackAsTheSameObject()
    {
        Material material = CreateMaterial("M.mat");
        var holder = new GameObject("Holder");
        holder.AddComponent<MaterialHolder>().Material = material;

        EchoObject echo = Serializer.Serialize(typeof(object), holder);
        var copy = Serializer.Deserialize<GameObject>(echo)!;

        Assert.Same(material, copy.GetComponent<MaterialHolder>()!.Material);
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
        File.WriteAllText(AssetAbsolutePath("Extra.scene"),
            Serializer.Serialize(typeof(object), new Scene()).WriteToString());

        ReopenDatabase();

        Guid g = Assets.PathToGuid("Extra.scene");
        Assert.NotEqual(Guid.Empty, g);
        Assert.NotNull(AssetDatabase.Load<SceneAsset>(g));
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
    public void MoveAsset_KeepsTheObject_AndRenamesIt_EvenUnloaded()
    {
        Guid g = CreateScene("S.scene");
        Asset asset = AssetDatabase.Get(g)!;

        Assert.True(Assets.MoveAsset("S.scene", "Sub/Renamed.scene"));

        Assert.Same(asset, AssetDatabase.Get(g));
        Assert.Equal("Sub/Renamed.scene", asset.AssetPath);
        Assert.Equal("Renamed", asset.Name);
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

    [Fact]
    public void CreateAsset_PathTraversal_IsRejected()
    {
        string escapedPath = Path.GetFullPath(Path.Combine(Project.AssetsPath, "../../../Escaped.mat"));
        try
        {
            Assets.CreateAsset(new Material(), "../../../Escaped.mat");

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
        Assert.Equal("New/S.scene", AssetDatabase.Get(g)!.AssetPath);
    }

    // A moved script keeps its content and timestamp, so nothing else asks for a recompile - but the
    // generated csproj still points at the old path and the owning assembly may have changed.
    [Fact]
    public void MoveAsset_Script_RequestsRecompile()
    {
        WriteScript("Moved.cs", "public class Moved { }");
        Assets.Refresh();
        Projects.Scripting.ScriptAssemblyManager.RecompilePending = false;

        Assert.True(Assets.MoveAsset("Moved.cs", "Sub/Moved.cs"));

        Assert.True(Projects.Scripting.ScriptAssemblyManager.RecompilePending);
    }

    [Fact]
    public void MoveFolder_WithScripts_RequestsRecompile()
    {
        WriteScript("Old/Moved.cs", "public class Moved { }");
        Assets.Refresh();
        Projects.Scripting.ScriptAssemblyManager.RecompilePending = false;

        Assert.True(Assets.MoveFolder("Old", "New"));

        Assert.True(Projects.Scripting.ScriptAssemblyManager.RecompilePending);
    }

    [Fact]
    public void DeleteAsset_RemovesFileMetaAndIndex_AndLeavesTheObjectMissing()
    {
        Guid g = CreateScene("S.scene");
        Asset asset = AssetDatabase.Load<Asset>(g)!;

        Assets.DeleteAsset("S.scene");

        Assert.False(File.Exists(AssetAbsolutePath("S.scene")));
        Assert.False(File.Exists(AssetAbsolutePath("S.scene.meta")));
        Assert.Equal(Guid.Empty, Assets.PathToGuid("S.scene"));
        Assert.Same(asset, AssetDatabase.Get(g));
        Assert.True(asset.IsMissing);
        Assert.Equal(g, asset.AssetID);
    }

    [Fact]
    public void ADeletedAssetRestored_ComesBackInTheSameObject()
    {
        Material material = CreateMaterial("M.mat", 3f);
        string file = File.ReadAllText(AssetAbsolutePath("M.mat"));
        string meta = File.ReadAllText(AssetAbsolutePath("M.mat.meta"));

        Assets.DeleteAsset("M.mat");
        Assert.True(material.IsMissing);

        File.WriteAllText(AssetAbsolutePath("M.mat"), file);
        File.WriteAllText(AssetAbsolutePath("M.mat.meta"), meta);
        Assets.Refresh();

        Assert.Same(material, AssetDatabase.Load<Material>(material.AssetID));
        Assert.True(material.IsLoaded);
        Assert.Equal(3f, material._properties.GetFloat("_Value"));
    }

    [Fact]
    public void SaveAsset_RefillsTheSameObject_AndWritesTheFile()
    {
        Material material = CreateMaterial("M.mat");
        material.SetFloat("_Value", 7f);
        int version = material.ContentVersion;

        Assets.SaveAsset(material);

        Assert.Same(material, AssetDatabase.Get(material.AssetID));
        Assert.True(material.ContentVersion > version);
        Assert.Equal(7f, material._properties.GetFloat("_Value"));

        ReopenDatabase();
        AssetDatabase.ClearForTests();
        Assert.Equal(7f, AssetDatabase.Load<Material>(material.AssetID)!._properties.GetFloat("_Value"));
    }

    [Fact]
    public void Reimport_PicksUpOnDiskEdit_InTheSameObject()
    {
        Material material = CreateMaterial("M.mat", 1f);
        int version = material.ContentVersion;

        WriteMaterialFile("M.mat", 5f);
        Assets.Reimport(material.AssetID);

        Assert.Same(material, AssetDatabase.Get(material.AssetID));
        Assert.True(material.ContentVersion > version);
        Assert.Equal(5f, material._properties.GetFloat("_Value"));
    }

    [Fact]
    public void Reimport_OfAnUnloadedAsset_LoadsTheNewContentLater()
    {
        Guid g = CreateMaterial("M.mat", 1f).AssetID;
        ReopenDatabase();
        AssetDatabase.ClearForTests();
        Material shell = AssetDatabase.Get<Material>(g)!;

        WriteMaterialFile("M.mat", 9f);
        Assets.Reimport(g);

        Assert.Equal(AssetState.Unloaded, shell.State);
        shell.Load();
        Assert.Equal(9f, shell._properties.GetFloat("_Value"));
    }

    [Fact]
    public void RevertToSaved_PutsTheObjectBack_InPlace()
    {
        Material material = CreateMaterial("M.mat", 2f);
        material.SetFloat("_Value", 8f);

        Assets.RevertToSaved(material);

        Assert.Equal(2f, material._properties.GetFloat("_Value"));
    }

    #endregion

    #region Residency

    [Fact]
    public void AnUnusedAsset_IsUnloaded_AndReloadsInTheSameObject()
    {
        Material material = CreateMaterial("M.mat", 4f);

        AssetDatabase.UnloadUnused();

        Assert.Equal(AssetState.Unloaded, material.State);
        material.Load();
        Assert.Same(material, AssetDatabase.Get(material.AssetID));
        Assert.Equal(4f, material._properties.GetFloat("_Value"));
    }

    [Fact]
    public void AHeldAsset_StaysLoaded_UntilReleased()
    {
        Material material = CreateMaterial("M.mat");
        object owner = new();

        AssetDatabase.Hold(material, owner);
        AssetDatabase.UnloadUnused();
        Assert.True(material.IsLoaded);

        AssetDatabase.Release(material, owner);
        AssetDatabase.UnloadUnused();
        Assert.False(material.IsLoaded);
    }

    [Fact]
    public void AnAssetALiveSceneUses_StaysLoaded()
    {
        Material material = CreateMaterial("M.mat");
        var scene = new Scene();
        try
        {
            var go = new GameObject("Holder");
            scene.Add(go);
            go.AddComponent<MaterialHolder>().Material = material;

            AssetDatabase.UnloadUnused();

            Assert.True(material.IsLoaded);
        }
        finally
        {
            scene.Dispose();
        }
    }

    #endregion

    #region Queries

    [Fact]
    public void FindAssetsOfType_FiltersByType()
    {
        CreateScene("S.scene");
        CreatePrefabAsset(new GameObject("P"), "P.prefab");

        var scenes = Assets.FindAssetsOfType<SceneAsset>().ToList();

        Assert.Single(scenes);
        Assert.Equal("S.scene", scenes[0].Path);
    }

    #endregion

    #region Import Batch Resilience

    [Fact]
    public void OneImporterThrowing_DoesNotAbortWholeScan()
    {
        CreateScene("Good.scene");
        File.WriteAllText(AssetAbsolutePath("Bad.throwtest"), "junk");

        var ex = Record.Exception(() => ReopenDatabase());

        Assert.Null(ex);
        Assert.NotEqual(Guid.Empty, Assets.PathToGuid("Good.scene"));
        Assert.NotNull(AssetDatabase.Load<SceneAsset>(Assets.PathToGuid("Good.scene")));
    }

    #endregion

    #region Folder Index Cache

    [Fact]
    public void CreateAsset_IsVisibleInFolderIndex()
    {
        CreateMaterial("A.mat");
        Assets.GetFolderFiles(""); // build the folder index cache

        CreateMaterial("B.mat");

        var files = Assets.GetFolderFiles("");
        Assert.Contains(files, f => f.Name == "B.mat");
    }

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
    public void Prefab_TracksAHardDependency_ThroughAPlainField()
    {
        Material material = CreateMaterial("M.mat");

        var go = new GameObject("Holder");
        go.AddComponent<MaterialHolder>().Material = material;
        Guid prefabGuid = CreatePrefabAsset(go, "Holder.prefab");

        var entry = Assets.GetEntry(prefabGuid)!;
        Assert.Contains(material.AssetID, entry.Dependencies);
        Assert.DoesNotContain(material.AssetID, entry.SoftDependencies);
    }

    [Fact]
    public void Prefab_TracksASoftDependency_ThroughAnAssetRef()
    {
        Guid sceneGuid = CreateScene("Referenced.scene");

        var go = new GameObject("Holder");
        go.AddComponent<AssetRefComponent>().Ref = new AssetRef<SceneAsset>(sceneGuid);
        Guid prefabGuid = CreatePrefabAsset(go, "Holder.prefab");

        var entry = Assets.GetEntry(prefabGuid)!;
        Assert.Contains(sceneGuid, entry.SoftDependencies);
        Assert.DoesNotContain(sceneGuid, entry.Dependencies);
    }

    // An override blob is serialized ahead of time with no tracking context, so only the walk of the
    // stored tree can find what it references.
    [Fact]
    public void Scene_TracksAnAssetInsideAPrefabOverride()
    {
        Material material = CreateMaterial("M.mat");

        var go = new GameObject("Holder");
        go.AddComponent<MaterialHolder>();
        go.PrefabAssetId = Guid.NewGuid();
        go.PrefabOverrides.Add(new PropertyOverride
        {
            Path = "MaterialHolder.Material",
            Value = Serializer.Serialize(typeof(Material), material, new SerializationContext { RootByReference = true })
        });

        var scene = new Scene();
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        Assert.Contains(material.AssetID, Assets.GetEntry(sceneGuid)!.Dependencies);
    }

    // A player never reads which prefab an instance came from, so a prefab only instanced in scenes does not ship.
    [Fact]
    public void Scene_RecordsItsPrefabInstances_AsEditorEdges()
    {
        Guid prefabGuid = CreatePrefabAsset(new GameObject("P"), "P.prefab");
        var instance = GameObject.Instantiate(GetPrefab(prefabGuid)!)!;
        var scene = instance.Scene!;
        scene.Remove(instance);

        var holder = new Scene();
        holder.Add(instance);
        Guid sceneGuid = CreateSceneAsset(holder, "Main.scene");

        var entry = Assets.GetEntry(sceneGuid)!;
        Assert.Contains(prefabGuid, entry.EditorDependencies);
        Assert.DoesNotContain(prefabGuid, entry.Dependencies);
        Assert.DoesNotContain(prefabGuid, Assets.Dependencies.GetDependencies(sceneGuid));
    }

    #endregion

    #region Sub-Assets

    private (Guid texGuid, Guid spriteGuid) CreateTextureWithSprite(string path)
    {
        string pngPath = AssetAbsolutePath(path);
        TestImages.WriteSolidPng(pngPath, 4, 1, 2, 3);
        Guid texGuid = Assets.ImportFile(path);
        Assert.NotEqual(Guid.Empty, texGuid);

        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.Single });
        Guid spriteGuid = Assets.GetSubAssets(texGuid)[0].Guid;
        return (texGuid, spriteGuid);
    }

    // A sub-asset has its own cache, so it loads, unloads and refills on its own.
    [Fact]
    public void ASubAsset_LoadsOnItsOwn_AndRefillsOnReimport()
    {
        var (texGuid, spriteGuid) = CreateTextureWithSprite("Sprite.png");

        Sprite sprite = AssetDatabase.Load<Sprite>(spriteGuid)!;
        Assert.True(sprite.IsLoaded);
        int version = sprite.ContentVersion;

        Assets.Reimport(texGuid);

        Assert.Same(sprite, AssetDatabase.Get(spriteGuid));
        Assert.True(sprite.ContentVersion > version);
        Assert.Same(AssetDatabase.Get(texGuid), sprite.Texture);
    }

    [Fact]
    public void ASubAssetAReimportDrops_IsMissing()
    {
        var (texGuid, spriteGuid) = CreateTextureWithSprite("Sprite.png");
        Sprite sprite = AssetDatabase.Load<Sprite>(spriteGuid)!;

        TextureSpriteMeta.Save(texGuid, new SpriteImportSettings { Mode = SpriteMode.None });

        Assert.True(sprite.IsMissing);
        Assert.Same(sprite, AssetDatabase.Get(spriteGuid));
    }

    #endregion

    #region Saving

    [Fact]
    public void SavingATexture_NeverWritesOverItsImage()
    {
        TestImages.WriteSolidPng(AssetAbsolutePath("Grass.png"), 4, 1, 2, 3);
        Guid guid = Assets.ImportFile("Grass.png");
        byte[] image = File.ReadAllBytes(AssetAbsolutePath("Grass.png"));
        var texture = AssetDatabase.Load<Texture2D>(guid)!;

        Assert.False(Assets.SaveAsset(texture));
        Assert.Equal(image, File.ReadAllBytes(AssetAbsolutePath("Grass.png")));
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
        Assert.Null(AssetDatabase.Get(g));
    }

    /// <summary>
    /// A .navmesh is written and read as binary Echo. Its payload is compressed voxelization
    /// blobs, which as text become base64 - bigger, slower to parse, and no more readable. A
    /// text one does not parse as binary, so it fails the import outright rather than loading
    /// as something wrong; rebaking is the migration.
    /// </summary>
    [Fact]
    public void NavMesh_RoundTripsAsBinary_AndRejectsText()
    {
        NavMeshData? baked = NavMeshBuilder.Build(new NavMeshBuildSettings(), [FlatQuad(20f)]);
        Assert.NotNull(baked);
        EchoObject echo = Serializer.Serialize(typeof(object), baked!);

        echo.WriteToBinary(new FileInfo(AssetAbsolutePath("Baked.navmesh")));
        Guid guid = Assets.ImportFile("Baked.navmesh");
        Assert.NotEqual(Guid.Empty, guid);

        var loaded = AssetDatabase.Load<NavMeshData>(guid);
        Assert.NotNull(loaded);
        Assert.Equal(baked!.CacheLayers.Count, loaded!.CacheLayers.Count);

        File.WriteAllText(AssetAbsolutePath("Legacy.navmesh"), echo.WriteToString());
        Assert.Null(AssetDatabase.Get(Assets.ImportFile("Legacy.navmesh")));
    }

    /// <summary>A rebake goes over the asset the surface references, so renaming the file does not
    /// leave the next bake writing a second asset beside it.</summary>
    [Fact]
    public void NavMeshBake_TargetsTheAssignedAssetWhateverItIsNamed()
    {
        NavMeshData? baked = NavMeshBuilder.Build(new NavMeshBuildSettings(), [FlatQuad(20f)]);
        Assert.NotNull(baked);
        Serializer.Serialize(typeof(object), baked!).WriteToBinary(new FileInfo(AssetAbsolutePath("Renamed By User.navmesh")));
        Guid guid = Assets.ImportFile("Renamed By User.navmesh");
        Assert.NotEqual(Guid.Empty, guid);

        var go = new GameObject("Surface");
        var surface = go.AddComponent<NavMeshSurface>();

        // No asset yet: the first bake picks a name from the scene and agent type.
        Assert.EndsWith(".navmesh", Navigation.NavMeshBakeService.BakePath(surface));
        Assert.DoesNotContain("Renamed By User", Navigation.NavMeshBakeService.BakePath(surface));

        surface.NavMeshData = AssetDatabase.Get<NavMeshData>(guid);

        Assert.Equal("Renamed By User.navmesh", Navigation.NavMeshBakeService.BakePath(surface));
    }

    /// <summary>A duplicated surface shares its original's asset reference, and rebaking it must not
    /// overwrite the file the original still uses.</summary>
    [Fact]
    public void NavMeshBake_DuplicatedSurface_GetsItsOwnFile()
    {
        NavMeshData? baked = NavMeshBuilder.Build(new NavMeshBuildSettings(), [FlatQuad(20f)]);
        Assert.NotNull(baked);
        Serializer.Serialize(typeof(object), baked!).WriteToBinary(new FileInfo(AssetAbsolutePath("Shared.navmesh")));
        Guid guid = Assets.ImportFile("Shared.navmesh");
        Assert.NotEqual(Guid.Empty, guid);

        var scene = new Scene();
        try
        {
            var original = new GameObject("Original");
            scene.Add(original);
            var originalSurface = original.AddComponent<NavMeshSurface>();
            originalSurface.NavMeshData = AssetDatabase.Get<NavMeshData>(guid);

            var duplicate = new GameObject("Duplicate");
            scene.Add(duplicate);
            var duplicateSurface = duplicate.AddComponent<NavMeshSurface>();
            duplicateSurface.NavMeshData = AssetDatabase.Get<NavMeshData>(guid);

            Assert.NotEqual("Shared.navmesh", Navigation.NavMeshBakeService.BakePath(duplicateSurface));

            // Once nothing else references it, the surface bakes over its own file again.
            duplicateSurface.NavMeshData = null;
            Assert.Equal("Shared.navmesh", Navigation.NavMeshBakeService.BakePath(originalSurface));
        }
        finally
        {
            scene.Dispose();
        }
    }

    [Fact]
    public void NavMeshBake_RunsInTheBackground_ThenSavesAndAssignsTheAsset()
    {
        var scene = new Scene();
        scene.Enable();
        try
        {
            var floor = new GameObject("Floor");
            scene.Add(floor);
            floor.AddComponent<BoxCollider>().Size = new Prowl.Vector.Float3(20, 1, 20);
            floor.Transform.Position = new Prowl.Vector.Float3(0, -0.5f, 0);

            var go = new GameObject("Surface");
            scene.Add(go);
            var surface = go.AddComponent<NavMeshSurface>();
            surface.UseGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildOverrides.OverrideVoxelSize = true;
            surface.BuildOverrides.VoxelSize = 0.25f;

            var bake = Navigation.NavMeshBakeService.Instance;
            Assert.True(bake.Start(surface));
            Assert.Same(surface, bake.TargetSurface);

            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (bake.IsBaking && timeout.Elapsed < TimeSpan.FromSeconds(60))
            {
                bake.Poll();
                System.Threading.Thread.Sleep(10);
            }

            Assert.Equal("Done", bake.Status);
            Assert.NotNull(surface.NavMeshData);
            Assert.NotEqual(Guid.Empty, surface.NavMeshData!.AssetID);
            Assert.StartsWith("Scene_navmesh/", Navigation.NavMeshBakeService.BakePath(surface));
            Assert.NotNull(surface.Instance);
        }
        finally
        {
            scene.Dispose();
        }
    }

    private static NavMeshGeometrySource FlatQuad(float size)
    {
        Prowl.Vector.Float3[] verts = [new(0, 0, 0), new(0, 0, size), new(size, 0, size), new(size, 0, 0)];
        return new NavMeshGeometrySource(verts, [0, 1, 2, 0, 2, 3], Prowl.Vector.Float4x4.Identity);
    }

    #endregion
}
