// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Guards <see cref="Scene.GatherActiveCameras"/>, which replaced a per-object
/// <c>GetComponentsInChildren</c> recursion in Scene.Render. The flat scan must return exactly what
/// the old recursive-plus-Distinct expression returned, for every hierarchy shape.
/// </summary>
public class SceneCameraGatherTests : RuntimeTestBase
{
    /// <summary>The exact expression Scene.Render used before the flat scan replaced it.</summary>
    private static List<Camera> LegacyGather(Scene scene)
    {
        var cameras = scene.ActiveObjects
            .SelectMany(x => x.GetComponentsInChildren<Camera>())
            .Distinct()
            .ToList();
        cameras.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        return cameras;
    }

    private static void AssertMatchesLegacy(Scene scene)
    {
        List<Camera> expected = LegacyGather(scene);
        List<Camera> actual = scene.GatherActiveCameras();

        Assert.Equal(expected.Count, actual.Count);
        // Same set, and same depth ordering.
        Assert.Equal(expected.OrderBy(c => c.InstanceID), actual.OrderBy(c => c.InstanceID));
        Assert.Equal(expected.Select(c => c.Depth), actual.Select(c => c.Depth));
    }

    [Fact]
    public void EmptyScene_ReturnsNoCameras()
    {
        Scene scene = CreateScene(enable: true);
        Assert.Empty(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void RootCamera_IsFound()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Cam");
        go.AddComponent<Camera>();
        scene.Add(go);

        Assert.Single(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void NestedCamera_IsCountedExactlyOnce()
    {
        // The bug the old Distinct() existed to paper over: a camera nested under N active
        // ancestors was collected N+1 times by the recursion.
        Scene scene = CreateScene(enable: true);
        GameObject root = CreateGameObject("Root");
        GameObject mid = CreateGameObject("Mid");
        GameObject leaf = CreateGameObject("Leaf");

        mid.SetParent(root);
        leaf.SetParent(mid);
        leaf.AddComponent<Camera>();
        scene.Add(root);

        Assert.Single(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void DisabledGameObject_IsSkipped()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Cam");
        go.AddComponent<Camera>();
        scene.Add(go);
        go.Enabled = false;

        Assert.Empty(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void CameraUnderDisabledParent_IsSkipped()
    {
        Scene scene = CreateScene(enable: true);
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);
        child.AddComponent<Camera>();
        scene.Add(parent);

        parent.Enabled = false;

        Assert.Empty(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void MultipleCamerasOnOneGameObject_AreAllReturned()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Rig");
        go.AddComponent<Camera>();
        go.AddComponent<Camera>();
        scene.Add(go);

        Assert.Equal(2, scene.GatherActiveCameras().Count);
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void CamerasAreSortedByDepth()
    {
        Scene scene = CreateScene(enable: true);
        GameObject a = CreateGameObject("A");
        GameObject b = CreateGameObject("B");
        GameObject c = CreateGameObject("C");

        a.AddComponent<Camera>().Depth = 5;
        b.AddComponent<Camera>().Depth = -3;
        c.AddComponent<Camera>().Depth = 1;

        scene.Add(a);
        scene.Add(b);
        scene.Add(c);

        List<Camera> cameras = scene.GatherActiveCameras();
        Assert.Equal([-3, 1, 5], cameras.Select(x => x.Depth));
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void DisabledCameraComponent_IsStillReturned()
    {
        // Preserved from the old expression: GetComponentsInChildren ignores the component's own
        // Enabled flag, so Camera.Enabled = false did NOT exclude it here. Camera.Render is what
        // honours it downstream. Locking this in so the rewrite doesn't silently change behaviour.
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Cam");
        Camera cam = go.AddComponent<Camera>();
        scene.Add(go);
        cam.Enabled = false;

        Assert.Single(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void GatherCost_DoesNotScaleWithSceneSize()
    {
        // The regression this guards: Scene.Render's camera gather used to allocate O(sum of subtree
        // sizes) worth of LINQ/iterator garbage EVERY frame - ~6 MB on a 10k-object tilemap, which
        // dominated the engine's gen0 churn. A flat scan allocates only the result list, so cost must
        // stay flat as the scene grows.
        static long MeasureGather(Scene scene)
        {
            scene.GatherActiveCameras(); // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
                scene.GatherActiveCameras();
            return (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
        }

        Scene small = BuildTileMap(5, 5);     // 31 objects
        Scene large = BuildTileMap(60, 60);   // 3661 objects

        long smallBytes = MeasureGather(small);
        long largeBytes = MeasureGather(large);

        // A 118x bigger scene must not cost meaningfully more. Allow generous slack for list growth.
        Assert.True(largeBytes <= smallBytes + 256,
            $"Camera gather scaled with scene size: {smallBytes} B at 31 objects vs {largeBytes} B at 3661 objects. " +
            "Something reintroduced a per-object walk.");
    }

    private Scene BuildTileMap(int rows, int cols)
    {
        Scene scene = CreateScene(enable: true);
        GameObject map = CreateGameObject("Map");

        for (int r = 0; r < rows; r++)
        {
            var row = new GameObject($"Row{r}");
            for (int c = 0; c < cols; c++)
                new GameObject($"Tile{r}_{c}").SetParent(row);
            row.SetParent(map);
        }

        GameObject cam = CreateGameObject("MainCamera");
        cam.AddComponent<Camera>();

        scene.Add(map);
        scene.Add(cam);
        return scene;
    }

    [Fact]
    public void MixedHierarchy_MatchesLegacyExactly()
    {
        Scene scene = CreateScene(enable: true);

        GameObject map = CreateGameObject("Map");
        for (int r = 0; r < 5; r++)
        {
            var row = new GameObject($"Row{r}");
            for (int t = 0; t < 5; t++)
            {
                var tile = new GameObject($"Tile{r}_{t}");
                if (t == 2)
                    tile.AddComponent<Camera>().Depth = r;
                tile.SetParent(row);
            }
            row.SetParent(map);
        }

        GameObject loose = CreateGameObject("LooseCam");
        loose.AddComponent<Camera>().Depth = 99;

        scene.Add(map);
        scene.Add(loose);

        Assert.Equal(6, scene.GatherActiveCameras().Count);
        AssertMatchesLegacy(scene);
    }
}
