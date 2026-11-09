// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;

using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Lightmapping;
using Prowl.Editor.Navigation;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>One bake over every loaded scene, with each scene keeping its own output and its own dirty mark.</summary>
public class MultiSceneBakeTests : EditorTestHarness
{
    public MultiSceneBakeTests() => Core.Undo.ClearAll();

    // A scene with a static cube, and optionally probes and a baked sun.
    private Scene Author(string path, Float3 at, bool probes = false, bool sun = false)
    {
        var world = new SceneInstance(SceneMode.Edit);
        GameObject cube = world.Create("Cube");
        cube.IsStatic = true;
        cube.Transform.Position = at;
        MeshRenderer renderer = cube.AddComponent<MeshRenderer>();
        renderer.Mesh = AssetDatabase.Get<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Cube));
        renderer.Material = AssetDatabase.Get<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
        if (probes)
            world.Create("Probes").AddComponent<LightProbeGroup>().ProbePositions =
                [at, at + new Float3(2, 0, 0), at + new Float3(0, 2, 0), at + new Float3(0, 0, 2), at + new Float3(2, 2, 2)];
        if (sun)
        {
            DirectionalLight light = world.Create("Sun").AddComponent<DirectionalLight>();
            light.BakeMode = LightBakeMode.Baked;
            light.Transform.LocalEulerAngles = new Float3(50, 30, 0);
        }
        return AssetDatabase.Get<Scene>(CreateSceneAsset(world, path))!;
    }

    private static (OpenScene A, OpenScene B) OpenBoth(Scene a, Scene b)
    {
        OpenScene first = EditorSceneManager.Open(a, force: true);
        OpenScene second = EditorSceneManager.OpenAdditive(b);
        SceneInstance.ProcessPendingLoad();
        return (first, second);
    }

    private static LightmapBakeSettings Quick() => new() { AtlasSize = 64, TexelsPerUnit = 4, Samples = 1, Bounces = 0, ProbeSamples = 8 };

    private static void BakeToEnd(LightmapBakeService service)
    {
        var clock = Stopwatch.StartNew();
        while (service.IsBaking && clock.Elapsed.TotalSeconds < 120)
        {
            service.Poll();
            Thread.Sleep(10);
        }
        Assert.False(service.IsBaking, "The bake did not finish.");
    }

    [Fact]
    public void OneBake_GivesEachLoadedSceneItsOwnPagesPlacementsAndProbes_AndSavesNothing()
    {
        Scene aAsset = Author("A.scene", Float3.Zero, sun: true);
        Scene bAsset = Author("B.scene", new Float3(4, 0, 0), probes: true);
        string aOnDisk = File.ReadAllText(AssetAbsolutePath("A.scene"));
        (OpenScene a, OpenScene b) = OpenBoth(aAsset, bAsset);

        var service = new LightmapBakeService();
        Assert.True(service.Start(EditorSceneManager.EditWorld, Quick()));
        BakeToEnd(service);

        SceneLighting aLighting = a.Section!.Lighting;
        SceneLighting bLighting = b.Section!.Lighting;
        Assert.True(aLighting.HasLightmaps);
        Assert.True(bLighting.HasLightmaps);
        Assert.Contains(a.Section.Roots.Single(r => r.Name == "Cube").Identifier, aLighting.Placements.Keys);
        Assert.Single(bLighting.Placements);
        Assert.True(Directory.Exists(AssetAbsolutePath("A_lightmaps")));
        Assert.True(Directory.Exists(AssetAbsolutePath("B_lightmaps")));

        Assert.False(aLighting.HasProbes);
        Assert.Equal(5, bLighting.ProbePositions.Length);
        Assert.NotEmpty(bLighting.ProbeTetrahedra);

        Assert.True(a.IsDirty);
        Assert.True(b.IsDirty);
        Assert.Equal(aOnDisk, File.ReadAllText(AssetAbsolutePath("A.scene")));
    }

    [Fact]
    public void ABakeReadyDuringPlay_WaitsForPlayToEnd()
    {
        (OpenScene a, _) = OpenBoth(Author("A.scene", Float3.Zero, sun: true), Author("B.scene", new Float3(4, 0, 0)));
        var service = new LightmapBakeService();
        Assert.True(service.Start(EditorSceneManager.EditWorld, Quick()));

        using (EnterPlayMode())
        {
            Assert.False(service.Start(EditorSceneManager.EditWorld, Quick()));
            var clock = Stopwatch.StartNew();
            while (service.Progress < 1f && clock.Elapsed.TotalSeconds < 120)
            {
                service.Poll();
                Thread.Sleep(10);
            }
            service.Poll();
            Assert.True(service.IsBaking);
            Assert.False(a.Section!.Lighting.HasLightmaps);
        }

        BakeToEnd(service);
        Assert.True(a.Section!.Lighting.HasLightmaps);
        Assert.True(a.IsDirty);
    }

    [Fact]
    public void AnUntitledScene_BlocksTheBake_AndAnUnloadedOneKeepsItsData()
    {
        (OpenScene a, OpenScene b) = OpenBoth(Author("A.scene", Float3.Zero), Author("B.scene", new Float3(4, 0, 0)));
        OpenScene untitled = EditorSceneManager.New(additive: true);
        SceneInstance.ProcessPendingLoad();

        var service = new LightmapBakeService();
        Assert.Same(untitled, LightmapBakeService.UntitledScene(EditorSceneManager.EditWorld));
        Assert.False(service.Start(EditorSceneManager.EditWorld, Quick()));

        EditorSceneManager.Remove(untitled, force: true);
        EditorSceneManager.Unload(b);
        SceneInstance.ProcessPendingLoad();
        Assert.True(service.Start(EditorSceneManager.EditWorld, Quick()));
        BakeToEnd(service);

        Assert.True(a.Section!.Lighting.HasLightmaps);
        Assert.False(Directory.Exists(AssetAbsolutePath("B_lightmaps")));
        Assert.False(b.IsDirty);
    }

    [Fact]
    public void ANavMeshSurface_StoresBesideItsOwnScene()
    {
        (OpenScene _, OpenScene b) = OpenBoth(Author("A.scene", Float3.Zero), Author("Levels/B.scene", new Float3(4, 0, 0)));
        NavMeshSurface surface = b.Section!.Roots.First().AddComponent<NavMeshSurface>();

        Assert.StartsWith("Levels/B_navmesh/", NavMeshBakeService.BakePath(surface));
    }

    [Fact]
    public void AReflectionProbeBake_MarksItsOwnScene()
    {
        (OpenScene a, OpenScene b) = OpenBoth(Author("A.scene", Float3.Zero), Author("B.scene", new Float3(4, 0, 0)));
        ReflectionProbe probe = b.Section!.Roots.First().AddComponent<ReflectionProbe>();
        a.Dirty = false;
        b.Dirty = false;

        probe.SetBaked(new ReflectionProbe.BakedCubemap { Size = 1, Mips = 1 });

        Assert.True(b.IsDirty);
        Assert.False(a.IsDirty);
    }
}
