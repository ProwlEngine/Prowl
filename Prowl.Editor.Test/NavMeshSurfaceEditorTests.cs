// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Exercises the asset-persistence half of baking a <see cref="NavMeshSurface"/>: writing the baked
/// <c>.navmesh</c> to disk with a proper <c>.meta</c>, and the surface's <see cref="AssetRef{NavMesh}"/>
/// surviving a scene save, a database reopen (closing and reopening the editor), and a scene reload.
/// Deliberately does not go through <c>NavMeshSurfaceEditor</c>'s Bake button itself, since that reads
/// <c>EditorSceneManager.CurrentScenePath</c>, UI-level state this headless harness does not drive;
/// it does exactly what that button does underneath, on the asset database directly.
/// </summary>
public class NavMeshSurfaceEditorTests : EditorTestHarness
{
    private static Mesh CreateGroundQuad()
    {
        return new Mesh
        {
            Vertices = [new(-5, 0, -5), new(5, 0, -5), new(5, 0, 5), new(-5, 0, 5)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    [Fact]
    public void Bake_WritesNavMeshAssetWithMetaFile()
    {
        var scene = new Scene();
        var ground = new GameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateGroundQuad();
        scene.Add(ground);

        var surfaceGo = new GameObject("Surface");
        NavMeshSurface surface = surfaceGo.AddComponent<NavMeshSurface>();
        scene.Add(surfaceGo);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface.LayerMask);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);

        string relativePath = "Nav.navmesh";
        File.WriteAllText(AssetAbsolutePath(relativePath), Serializer.Serialize(typeof(object), asset).WriteToString());
        Guid guid = Assets.ImportFile(relativePath);

        Assert.NotEqual(Guid.Empty, guid);
        Assert.True(File.Exists(AssetAbsolutePath(relativePath)));
        Assert.True(File.Exists(AssetAbsolutePath(relativePath) + ".meta"));
    }

    [Fact]
    public void SurfaceReference_SurvivesSceneSaveAndReload()
    {
        // Registers the NavMesh importer/CreateAssetMenu type before anything gets imported, so the
        // .meta files written below record the right importer rather than a not-yet-scanned fallback.
        EditorRegistries.Initialize();

        var scene = new Scene();
        var ground = new GameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateGroundQuad();
        scene.Add(ground);

        var surfaceGo = new GameObject("Surface");
        NavMeshSurface surface = surfaceGo.AddComponent<NavMeshSurface>();
        scene.Add(surfaceGo);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface.LayerMask);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var navMeshAsset = new NavMesh();
        navMeshAsset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        File.WriteAllText(AssetAbsolutePath("Nav.navmesh"), Serializer.Serialize(typeof(object), navMeshAsset).WriteToString());
        Guid navMeshGuid = Assets.ImportFile("Nav.navmesh");
        Assert.NotEqual(Guid.Empty, navMeshGuid);

        surface.NavMeshAsset = new AssetRef<NavMesh>(navMeshGuid);

        Guid sceneGuid = CreateSceneAsset(scene, "Main.scene");

        // Close and reopen the editor's asset database, then reload the scene from disk - the same
        // round trip a real "save, quit, reopen the project" does.
        ReopenDatabase();
        var reloadedScene = AssetDatabase.Get(sceneGuid) as Scene;
        Assert.NotNull(reloadedScene);

        GameObject? reloadedSurfaceGo = reloadedScene!.AllObjects.FirstOrDefault(g => g.Name == "Surface");
        Assert.NotNull(reloadedSurfaceGo);

        NavMeshSurface? reloadedSurface = reloadedSurfaceGo!.GetComponent<NavMeshSurface>();
        Assert.NotNull(reloadedSurface);

        // The point of this test: the surface's reference to its baked NavMesh - just the GUID, not
        // the loaded object - comes back intact after a full close/reopen/reload cycle.
        Assert.Equal(navMeshGuid, reloadedSurface!.NavMeshAsset.AssetID);
    }
}
