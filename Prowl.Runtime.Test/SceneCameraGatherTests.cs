// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Guards <see cref="SceneInstance.GatherActiveCameras"/>, which replaced a per-object
/// <c>GetComponentsInChildren</c> recursion in Scene.Render. The camera list must return exactly what
/// the old recursive-plus-Distinct expression returned, for every hierarchy shape.
/// </summary>
public class SceneCameraGatherTests : RuntimeTestBase
{
    /// <summary>The exact expression Scene.Render used before the camera list replaced it.</summary>
    private static List<Camera> LegacyGather(SceneInstance scene)
    {
        var cameras = scene.Objects.Where(o => o.EnabledInHierarchy)
            .SelectMany(x => x.GetComponentsInChildren<Camera>())
            .Distinct()
            .ToList();
        cameras.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        return cameras;
    }

    private static void AssertMatchesLegacy(SceneInstance scene)
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
        SceneInstance scene = CreateScene(enable: true);
        Assert.Empty(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void NestedCamera_IsCountedExactlyOnce()
    {
        // The bug the old Distinct() existed to paper over: a camera nested under N active
        // ancestors was collected N+1 times by the recursion.
        SceneInstance scene = CreateScene(enable: true);
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
        SceneInstance scene = CreateScene(enable: true);
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
        SceneInstance scene = CreateScene(enable: true);
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
        SceneInstance scene = CreateScene(enable: true);
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
        SceneInstance scene = CreateScene(enable: true);
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
    public void DisabledCameraComponent_IsSkipped()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Cam");
        Camera cam = go.AddComponent<Camera>();
        scene.Add(go);
        cam.Enabled = false;

        Assert.Empty(scene.GatherActiveCameras());
    }

    [Fact]
    public void ReenabledCamera_IsFoundOnce()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Cam");
        Camera cam = go.AddComponent<Camera>();
        scene.Add(go);

        cam.Enabled = false;
        cam.Enabled = true;
        go.Enabled = false;
        go.Enabled = true;

        Assert.Single(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void CameraAddedToAnObjectInTheScene_IsFound()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Cam");
        scene.Add(go);

        go.AddComponent<Camera>();

        Assert.Single(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void RemovedCamera_IsGone()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Cam");
        Camera cam = go.AddComponent<Camera>();
        scene.Add(go);

        go.RemoveComponent(cam);

        Assert.Empty(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    [Fact]
    public void CameraOnAnObjectRemovedFromTheScene_IsGone()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Cam");
        go.AddComponent<Camera>();
        scene.Add(go);

        scene.Remove(go);

        Assert.Empty(scene.GatherActiveCameras());
        AssertMatchesLegacy(scene);
    }

    // A dormant world's registries are empty, and fill as it activates.
    [Fact]
    public void CameraInADormantWorld_IsListedOnceItActivates()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Cam");
        go.AddComponent<Camera>();
        scene.Add(go);

        Assert.Empty(scene.GatherActiveCameras());
        scene.Activate();
        Assert.Single(scene.GatherActiveCameras());
    }

    [Fact]
    public void PersistentCamera_StaysListedAcrossALoad_AndADisabledOneOnceEnabled()
    {
        SceneInstance main = SceneInstance.Main;
        GameObject keeper = main.Create("Keeper");
        keeper.AddComponent<Camera>();
        GameObject idle = main.Create("Idle");
        Camera idleCam = idle.AddComponent<Camera>();
        idleCam.Enabled = false;
        keeper.Persistent = true;
        idle.Persistent = true;

        main.ReplaceContent(new SceneContent(), "Next", null);
        Assert.Single(main.GatherActiveCameras());

        idleCam.Enabled = true;
        Assert.Equal(2, main.GatherActiveCameras().Count);
    }

    private sealed class MainCameraReader : Component
    {
        public Camera? Seen;

        public override void OnEnable() => Seen = Camera.Main;
    }

    // A scene enables its objects in order, so a script ahead of the camera must still find it.
    [Fact]
    public void Main_IsFoundByAnOnEnableThatRunsBeforeTheCameras()
    {
        SceneInstance scene = CreateScene();
        GameObject first = CreateGameObject("First");
        MainCameraReader reader = first.AddComponent<MainCameraReader>();
        GameObject second = CreateGameObject("Second");
        Camera cam = second.AddComponent<Camera>();
        scene.Add(first);
        scene.Add(second);

        scene.Activate();

        Assert.Same(cam, reader.Seen);
    }

    [Fact]
    public void GatherCost_DoesNotScaleWithSceneSize()
    {
        // The regression this guards: Scene.Render's camera gather used to allocate O(sum of subtree
        // sizes) worth of LINQ/iterator garbage EVERY frame - ~6 MB on a 10k-object tilemap, which
        // dominated the engine's gen0 churn. The camera list allocates only the result, so cost must
        // stay flat as the scene grows.
        static long MeasureGather(SceneInstance scene)
        {
            scene.GatherActiveCameras(); // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
                scene.GatherActiveCameras();
            return (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
        }

        SceneInstance small = BuildTileMap(5, 5);     // 31 objects
        SceneInstance large = BuildTileMap(60, 60);   // 3661 objects

        long smallBytes = MeasureGather(small);
        long largeBytes = MeasureGather(large);

        // A 118x bigger scene must not cost meaningfully more. Allow generous slack for list growth.
        Assert.True(largeBytes <= smallBytes + 256,
            $"Camera gather scaled with scene size: {smallBytes} B at 31 objects vs {largeBytes} B at 3661 objects. " +
            "Something reintroduced a per-object walk.");
    }

    private SceneInstance BuildTileMap(int rows, int cols)
    {
        SceneInstance scene = CreateScene(enable: true);
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
    public void FindMainCamera_SkipsDisabledCamerasAndEditorHelpers()
    {
        SceneInstance scene = CreateScene(enable: true);
        GameObject game = CreateGameObject("Game");
        Camera expected = game.AddComponent<Camera>();

        GameObject disabled = CreateGameObject("Disabled");
        Camera off = disabled.AddComponent<Camera>();
        off.Depth = 10;

        GameObject helper = CreateGameObject("SceneViewCamera");
        helper.HideFlags = HideFlags.HideAndDontSave;
        helper.AddComponent<Camera>().Depth = 20;

        scene.Add(game);
        scene.Add(disabled);
        scene.Add(helper);
        off.Enabled = false;

        Assert.Same(expected, scene.MainCamera);
    }

    [Fact]
    public void Main_ReadsTheCurrentScene_AndForgetsADestroyedCamera()
    {
        SceneInstance scene = CreateScene();
        Bind(scene);

        GameObject first = CreateGameObject("First");
        Camera firstCam = first.AddComponent<Camera>();
        firstCam.Depth = 5;
        GameObject second = CreateGameObject("Second");
        Camera secondCam = second.AddComponent<Camera>();
        scene.Add(first);
        scene.Add(second);

        Assert.Same(firstCam, Camera.Main);

        first.Dispose();

        Assert.Same(secondCam, Camera.Main);
    }

    [Fact]
    public void MixedHierarchy_MatchesLegacyExactly()
    {
        SceneInstance scene = CreateScene(enable: true);

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
