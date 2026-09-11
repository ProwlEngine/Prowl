// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Recast.Detour;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class NavMeshCollectorTests : RuntimeTestBase
{
    private static NavMeshBuildSettings TestSettings() => new()
    {
        OverrideVoxelSize = true,
        VoxelSize = 0.25f,
        OverrideTileSize = true,
        TileSize = 64,
    };

    [Fact]
    public void BoxCollider_CollectsAndBakesWalkableFloor()
    {
        Scene scene = CreateScene(enable: true);
        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        var box = floor.AddComponent<BoxCollider>();
        box.Size = new Float3(20, 1, 20);
        floor.Transform.Position = new Float3(0, -0.5f, 0); // top surface at y=0

        List<NavMeshGeometrySource> sources = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, TestSettings().EffectiveVoxelSize, NavMeshAreas.Walkable, sources);

        Assert.Single(sources);

        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), sources);
        Assert.NotNull(data);
        Assert.True(data!.HasTiles);

        // Path across the box top must work end to end.
        var world = new NavMeshWorld();
        world.AddNavMeshData(data);
        var path = new NavMeshPath();
        Assert.True(world.CalculatePath(new Float3(-8, 0, -8), new Float3(8, 0, 8), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    [Fact]
    public void Collect_RespectsLayerMask()
    {
        Scene scene = CreateScene(enable: true);
        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(10, 1, 10);
        floor.LayerIndex = 5;

        List<NavMeshGeometrySource> sources = [];
        LayerMask without5 = LayerMask.Everything;
        without5.RemoveLayer(5);
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            without5, 0.25f, NavMeshAreas.Walkable, sources);
        Assert.Empty(sources);

        LayerMask with5 = LayerMask.Everything;
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            with5, 0.25f, NavMeshAreas.Walkable, sources);
        Assert.Single(sources);
    }

    [Fact]
    public void Collect_SkipsDisabledObjects()
    {
        Scene scene = CreateScene(enable: true);
        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(10, 1, 10);
        floor.Enabled = false;

        List<NavMeshGeometrySource> sources = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, 0.25f, NavMeshAreas.Walkable, sources);
        Assert.Empty(sources);
    }

    [Fact]
    public void MeshRenderer_Collects_WithWorldTransform()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Plane");
        scene.Add(go);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(new Float3(10, 0.2f, 10));
        go.Transform.Position = new Float3(100, 0, 100);

        List<NavMeshGeometrySource> sources = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.RenderMeshes,
            LayerMask.Everything, 0.25f, NavMeshAreas.Walkable, sources);
        Assert.Single(sources);

        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), sources);
        Assert.NotNull(data);
        Assert.True(data!.HasTiles);

        // The navmesh must be where the object is, not at the origin.
        var world = new NavMeshWorld();
        world.AddNavMeshData(data);
        Assert.True(world.SamplePosition(new Float3(100, 0.5f, 100), out NavMeshHit hit, 2f, NavMesh.AllAreas));
        Assert.True(System.Math.Abs(hit.Position.X - 100) < 3f);
        Assert.False(world.SamplePosition(new Float3(0, 0, 0), out _, 2f, NavMesh.AllAreas));
    }

    [Fact]
    public void RotatedBoxCollider_BakesRotated()
    {
        Scene scene = CreateScene(enable: true);
        GameObject ramp = CreateGameObject("Floor");
        scene.Add(ramp);
        var box = ramp.AddComponent<BoxCollider>();
        box.Size = new Float3(20, 1, 6);
        // 45° yaw (FromEuler takes degrees): the walkable strip runs diagonally.
        ramp.Transform.Rotation = Quaternion.FromEuler(new Float3(0, 45f, 0));

        List<NavMeshGeometrySource> sources = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, 0.25f, NavMeshAreas.Walkable, sources);
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), sources);
        Assert.NotNull(data);

        var world = new NavMeshWorld();
        world.AddNavMeshData(data!);

        // Center is always on the strip.
        Assert.True(world.SamplePosition(new Float3(0, 1f, 0), out _, 2f, NavMesh.AllAreas));
        // The unrotated +X end is ~6.4 units off the rotated strip's center line: not walkable.
        Assert.False(world.SamplePosition(new Float3(9f, 1f, 0), out _, 2f, NavMesh.AllAreas));
        // Exactly one diagonal lies along the rotated strip (which one depends on yaw handedness).
        bool posDiagonal = world.SamplePosition(new Float3(5f, 1f, 5f), out _, 2f, NavMesh.AllAreas);
        bool negDiagonal = world.SamplePosition(new Float3(5f, 1f, -5f), out _, 2f, NavMesh.AllAreas);
        Assert.True(posDiagonal ^ negDiagonal, $"Expected exactly one diagonal walkable (got +Z:{posDiagonal}, -Z:{negDiagonal}).");
    }

    /// <summary>An agent's own geometry, and geometry on its children, stay out of the bake.</summary>
    [Fact]
    public void Collect_SkipsAgentsAndTheirChildren()
    {
        Scene scene = CreateScene(enable: true);
        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
        floor.Transform.Position = new Float3(0, -0.5f, 0);

        GameObject agent = CreateGameObject("Agent");
        scene.Add(agent);
        agent.Transform.Position = new Float3(0, 1, 0);
        agent.AddComponent<NavMeshAgent>();
        agent.AddComponent<CapsuleCollider>();

        GameObject visual = CreateGameObject("AgentVisual");
        scene.Add(visual);
        visual.SetParent(agent);
        visual.AddComponent<BoxCollider>().Size = new Float3(1, 2, 1);

        List<NavMeshGeometrySource> sources = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, 0.25f, NavMeshAreas.Walkable, sources);

        Assert.Single(sources); // the floor only
    }

    /// <summary>
    /// An agent standing on the floor at bake time must not voxelize as an obstruction, or it
    /// leaves a permanent hole in the navmesh under wherever it stood.
    /// </summary>
    [Fact]
    public void Bake_WithAgentStandingOnFloor_LeavesNoHole()
    {
        Scene scene = CreateScene(enable: true);
        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
        floor.Transform.Position = new Float3(0, -0.5f, 0);

        var standing = new Float3(4, 0, 4);
        GameObject agent = CreateGameObject("Agent");
        scene.Add(agent);
        agent.Transform.Position = standing + new Float3(0, 1, 0);
        agent.AddComponent<NavMeshAgent>();
        var body = agent.AddComponent<BoxCollider>();
        body.Size = new Float3(2, 2, 2);

        List<NavMeshGeometrySource> sources = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, TestSettings().EffectiveVoxelSize, NavMeshAreas.Walkable, sources);
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), sources);
        Assert.NotNull(data);

        var world = new NavMeshWorld();
        world.AddNavMeshData(data!);

        // The floor under the agent is walkable, and a path runs straight through it.
        Assert.True(world.SamplePosition(standing, out NavMeshHit hit, 0.5f, NavMesh.AllAreas));
        Assert.True(Float3.Distance(hit.Position, standing) < 0.5f);

        var path = new NavMeshPath();
        Assert.True(world.CalculatePath(new Float3(-8, 0, -8), new Float3(8, 0, 8), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    private const float TerrainSize = 64f;
    private const float TerrainHeight = 8f;

    /// <summary> A terrain spanning 0..64 on both axes, at whatever the caller's transform says.
    /// <paramref name="normalizedHeight"/> takes world-space X and Z and returns 0..1; flat when
    /// omitted. </summary>
    private TerrainComponent AddTerrain(Scene scene, Func<float, float, float>? normalizedHeight = null,
        float height = TerrainHeight)
    {
        GameObject go = CreateGameObject("Terrain");
        scene.Add(go);

        const int res = 129;
        var data = new TerrainData { Size = TerrainSize, Height = height };
        data.ResizeHeightmap(res);
        if (normalizedHeight != null)
        {
            float cell = TerrainSize / (res - 1);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    data.SetHeight(x, z, normalizedHeight(x * cell, z * cell));
        }

        var terrain = go.AddComponent<TerrainComponent>();
        terrain.Data = data;
        go.AddComponent<TerrainCollider>();
        return terrain;
    }

    /// <summary> One full sine period across the terrain, gentle enough to be walkable throughout. </summary>
    private static float RollingHills(float x, float z)
        => 0.5f + 0.5f * MathF.Sin(x / TerrainSize * MathF.PI * 2f) * MathF.Cos(z / TerrainSize * MathF.PI * 2f);

    private NavMeshWorld BakeTerrain(Scene scene, bool heightDetail = true)
    {
        NavMeshBuildSettings settings = TestSettings();
        settings.BuildHeightDetail = heightDetail;

        List<NavMeshGeometrySource> sources = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, settings.EffectiveVoxelSize, NavMeshAreas.Walkable, sources);
        Assert.Single(sources);

        NavMeshData? data = NavMeshBuilder.Build(settings, sources);
        Assert.NotNull(data);

        var world = new NavMeshWorld();
        world.AddNavMeshData(data!);
        return world;
    }

    /// <summary>
    /// Baking happens with the editor open, where nothing but <see cref="ExecuteAlwaysAttribute"/>
    /// components have had a lifecycle callback. Terrain has to be readable there or every bake
    /// from the editor silently leaves it out.
    /// </summary>
    [Fact]
    public void Terrain_CollectsAndBakesInEditMode()
    {
        using (EditMode())
        {
            Scene scene = CreateScene(enable: true);
            AddTerrain(scene);

            NavMeshWorld world = BakeTerrain(scene);
            Assert.True(world.SamplePosition(new Float3(32, 0, 32), out NavMeshHit hit, 2f, NavMesh.AllAreas));
            Assert.True(Math.Abs(hit.Position.Y) < 1f);
        }
    }

    /// <summary> Heights are terrain-local, so the object's transform is what puts them in the world. </summary>
    [Fact]
    public void Terrain_BakesWhereItsTransformPutsIt()
    {
        Scene scene = CreateScene(enable: true);
        TerrainComponent terrain = AddTerrain(scene);
        terrain.Transform.Position = new Float3(-32, 25, -32);
        terrain.Transform.LocalScale = new Float3(2, 1, 2);

        NavMeshWorld world = BakeTerrain(scene);

        // Scaled to 128 a side from a corner at -32, so the far edge reaches +96.
        Assert.True(world.SamplePosition(new Float3(90, 25, 90), out NavMeshHit hit, 2f, NavMesh.AllAreas));
        Assert.True(Math.Abs(hit.Position.Y - 25) < 1f, $"terrain baked at y={hit.Position.Y}, expected 25");
        Assert.False(world.SamplePosition(new Float3(110, 25, 110), out _, 2f, NavMesh.AllAreas));
    }

    /// <summary>
    /// The navmesh has to track a curved surface, not stretch across it. Polygons carry height
    /// only at their corners, and region growing hands a whole hillside to one polygon, so without
    /// height detail the mesh spans dips as a flat sheet and sinks into rises — measured at nearly
    /// a metre on this terrain, which is gentle. Sampling the whole surface is what catches that:
    /// a spot check would land on a polygon corner and read exactly right.
    /// </summary>
    [Fact]
    public void Terrain_NavMeshTracksTheSurfaceItCovers()
    {
        Scene scene = CreateScene(enable: true);
        AddTerrain(scene, RollingHills);

        NavMeshWorld world = BakeTerrain(scene);

        float worst = 0, deepest = 0; int samples = 0; Float3 worstAt = default;
        for (float z = 4; z < TerrainSize - 4; z += 0.5f)
        {
            for (float x = 4; x < TerrainSize - 4; x += 0.5f)
            {
                float expected = RollingHills(x, z) * TerrainHeight;
                Assert.True(world.SamplePosition(new Float3(x, expected, z), out NavMeshHit hit, 4f, NavMesh.AllAreas),
                    $"no navmesh over ({x}, {z})");
                samples++;

                float error = hit.Position.Y - expected;
                deepest = MathF.Min(deepest, error);
                if (MathF.Abs(error) > worst) { worst = MathF.Abs(error); worstAt = new Float3(x, expected, z); }
            }
        }

        Assert.True(samples > 10000);
        // Voxelization puts the walkable surface at the top of a voxel column, so the mesh rides
        // slightly high everywhere. Contour corners sit on the surface rather than above it
        // (heights come through span connectivity), so between detail samples the mesh may sag
        // a hair below a curved rise — about a voxel height, never more.
        Assert.True(worst < 0.5f, $"worst vertical error {worst:F3} at {worstAt}");
        Assert.True(deepest > -0.15f, $"navmesh sits {-deepest:F3} below the terrain");
    }

    /// <summary>
    /// What the overlay and user tooling read has to track the surface as well. Position queries
    /// and triangulation resolve heights by different routes, so one can be right while the other
    /// still draws each polygon as a flat outline stretched over whatever it covers.
    /// </summary>
    [Fact]
    public void Terrain_TriangulationTracksTheSurfaceItCovers()
    {
        Scene scene = CreateScene(enable: true);
        AddTerrain(scene, RollingHills);

        NavMeshTriangulation tri = BakeTerrain(scene).CalculateTriangulation();

        float worst = 0; int checkedTris = 0; Float3 worstAt = default;
        for (int t = 0; t < tri.Areas.Length; t++)
        {
            // The centroid is the part of a triangle furthest from any vertex the mesh got right.
            Float3 centroid = (tri.Vertices[tri.Indices[t * 3 + 0]]
                + tri.Vertices[tri.Indices[t * 3 + 1]]
                + tri.Vertices[tri.Indices[t * 3 + 2]]) / 3f;
            if (centroid.X < 4 || centroid.X > TerrainSize - 4 || centroid.Z < 4 || centroid.Z > TerrainSize - 4)
                continue;

            checkedTris++;
            float error = MathF.Abs(centroid.Y - RollingHills(centroid.X, centroid.Z) * TerrainHeight);
            if (error > worst) { worst = error; worstAt = centroid; }
        }

        Assert.True(checkedTris > 100);
        Assert.True(worst < 0.5f, $"worst vertical error {worst:F3} at {worstAt} over {checkedTris} triangles");
    }

    /// <summary>
    /// Steep ground is where navmesh generation degenerates, so bake terrain steep enough that
    /// the 45° walk limit carves the surface up (24 units of relief; the walkable slopes reach
    /// ~40°) and hold the mesh to the whole integrity set: no torn vertices, no unstitched tile
    /// portals, no sliver polygons, no near-vertical facets, and a walked surface that is
    /// continuous across polygon edges and stays on the ground it covers.
    /// </summary>
    [Fact]
    public void Terrain_SteepBake_SurfaceIsContinuousGroundedAndSliverFree()
    {
        Scene scene = CreateScene(enable: true);
        AddTerrain(scene, RollingHills, height: 24f);
        AssertBakeIntegrity(BakeTerrain(scene), RollingHills, 24f);
    }

    /// <summary>
    /// A sculpted plateau: flat low ground, a ~37° ramp whose base line meanders the way
    /// hand-sculpted terrain does, sharp creases at base and lip, flat top. The meander is the
    /// point — it puts kinks in the walkable-region borders that a straight crease never makes,
    /// and those kinks are what mint sliver polygons.
    /// </summary>
    [Fact]
    public void Terrain_SculptedPlateauBake_SurfaceIsContinuousGroundedAndSliverFree()
    {
        Scene scene = CreateScene(enable: true);
        AddTerrain(scene, WigglyPlateau, height: 24f);
        AssertBakeIntegrity(BakeTerrain(scene), WigglyPlateau, 24f);
    }

    /// <summary>
    /// A bounded collect has to sample the same grid an unbounded one does. The heights come from
    /// a decimated grid, so sampling the same span at a shifted phase describes a different
    /// surface — a rebuilt tile would then step away from the neighbour it shares a seam with,
    /// which nothing downstream can repair. Coverage matters as much as reduction: the quads
    /// straddling the region's edge have to be there.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(37f)] // rotated: the region's local box is not the world box with swapped axes
    public void Terrain_BoundedCollect_SamplesTheSameGridAndCoversTheRegion(float yawDegrees)
    {
        Scene scene = CreateScene(enable: true);
        TerrainComponent terrain = AddTerrain(scene, RollingHills);
        terrain.Transform.LocalEulerAngles = new Float3(0, yawDegrees, 0);

        // Coarse enough that sampling actually decimates (a stride of 1 samples every cell, and
        // then there is no phase to get wrong): 2 units over 0.5-unit cells strides by 4.
        const float voxelSize = 2f;
        List<NavMeshGeometrySource> whole = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, voxelSize, NavMeshAreas.Walkable, whole);
        Assert.Single(whole);

        // A world-space region well inside the terrain, whatever the yaw is.
        var region = new AABB(new Float3(20, -8, 20), new Float3(28, 16, 28));
        List<NavMeshGeometrySource> bounded = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, voxelSize, NavMeshAreas.Walkable, bounded, region);
        Assert.Single(bounded);

        Assert.True(bounded[0].TriangleCount < whole[0].TriangleCount / 4,
            $"bounded collect took {bounded[0].TriangleCount} of {whole[0].TriangleCount} triangles");

        // Same grid: every sampled vertex is one the unbounded collect produced, exactly.
        HashSet<Float3> wholeVerts = [.. whole[0].Vertices];
        foreach (Float3 v in bounded[0].Vertices)
            Assert.Contains(v, wholeVerts);

        // And the region is covered: the sampled span reaches past it on all four sides.
        Float3 min = Float4x4.TransformPoint(bounded[0].Vertices[0], bounded[0].Transform), max = min;
        foreach (Float3 v in bounded[0].Vertices)
        {
            Float3 world = Float4x4.TransformPoint(v, bounded[0].Transform);
            min = Maths.Min(min, world);
            max = Maths.Max(max, world);
        }
        Assert.True(min.X <= region.Min.X && min.Z <= region.Min.Z && max.X >= region.Max.X && max.Z >= region.Max.Z,
            $"sampled span {min}..{max} does not cover {region.Min}..{region.Max}");
    }

    /// <summary>
    /// Rebuilding one tile must cost what that tile covers, not what the scene holds. Before the
    /// range clip a partial rebuild re-sampled the whole heightmap and re-flattened every triangle
    /// of it — hundreds of milliseconds and tens of megabytes on a real terrain, per carve.
    /// </summary>
    [Fact]
    public void Terrain_OneTileRebuild_DoesNotPayForTheWholeHeightmap()
    {
        Scene scene = CreateScene(enable: true);
        AddTerrain(scene, RollingHills);

        NavMeshBuildSettings settings = TestSettings();
        List<NavMeshGeometrySource> whole = [];
        NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
            LayerMask.Everything, settings.EffectiveVoxelSize, NavMeshAreas.Walkable, whole);
        NavMeshData? data = NavMeshBuilder.Build(settings, whole);
        Assert.NotNull(data);

        var region = new AABB(new Float3(20, -8, 20), new Float3(24, 16, 24));
        long Rebuild()
        {
            List<NavMeshGeometrySource> sources = [];
            NavMeshGeometryCollector.Collect(scene.ActiveObjects, NavMeshCollectGeometry.PhysicsColliders,
                LayerMask.Everything, settings.EffectiveVoxelSize, NavMeshAreas.Walkable, sources, region);
            List<(int X, int Z, List<byte[]> Layers)> tiles =
                NavMeshBuilder.BuildTilesInBounds(data!, sources, region.Min, region.Max);
            Assert.Contains(tiles, t => t.Layers.Count > 0);
            return sources[0].TriangleCount;
        }

        Rebuild(); // warm the pools
        long before = GC.GetAllocatedBytesForCurrentThread();
        long triangles = Rebuild();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // 0.29 MB from 200 triangles as it stands; 4.32 MB from 32768 without the clipping.
        Assert.True(allocated < 1024 * 1024,
            $"one-tile rebuild allocated {allocated / 1024.0 / 1024.0:0.00} MB from {triangles} triangles");
    }

    /// <summary>
    /// End to end, on the geometry the clipping actually matters for: rebuilding part of a baked
    /// terrain against UNCHANGED geometry has to put the surface back exactly where it was, inside
    /// the region and across the seam into the tiles it did not touch. The source-level test above
    /// proves the samples are on the same grid; this proves the tiles built from them agree with
    /// the ones beside them.
    /// </summary>
    [Fact]
    public void Terrain_PartialRebuild_ReproducesTheSameSurface()
    {
        Scene scene = CreateScene(enable: true);
        AddTerrain(scene, RollingHills);

        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);

        // Probe well past the region, so a tile the rebuild only partly covers is included.
        var probes = new List<Float3>();
        for (float z = 10; z <= 44; z += 2)
            for (float x = 10; x <= 44; x += 2)
                probes.Add(new Float3(x, 12, z));

        // A probe landing on a polygon edge can miss the mesh even on a clean bake, so the baseline
        // is whatever was on it — what matters is that the rebuild takes nothing away.
        List<float> before = SampleHeights(scene, probes);
        int onMesh = before.Count(h => !float.IsNaN(h));
        Assert.True(onMesh > probes.Count * 0.9, $"only {onMesh} of {probes.Count} probes were on the baked mesh");

        var region = new AABB(new Float3(22, 0, 22), new Float3(32, 24, 32));
        Assert.True(surface.RebuildTiles(region));

        List<float> after = SampleHeights(scene, probes);
        float worst = 0f;
        Float3 worstAt = default;
        for (int i = 0; i < probes.Count; i++)
        {
            if (float.IsNaN(before[i])) continue;
            Assert.False(float.IsNaN(after[i]), $"the rebuild left no navmesh under {probes[i]}");
            float moved = MathF.Abs(after[i] - before[i]);
            if (moved > worst) { worst = moved; worstAt = probes[i]; }
        }

        Assert.True(worst < 0.005f, $"the rebuilt surface moved {worst:0.000} at {worstAt}");
    }

    /// <summary>Navmesh height directly under each probe, NaN where there is none. A generous
    /// radius is needed for hilly ground and lets a hit snap sideways onto a neighbouring polygon,
    /// so a hole would read as a surface at the wrong height — hence the XZ check.</summary>
    private static List<float> SampleHeights(Scene scene, List<Float3> probes)
    {
        var heights = new List<float>(probes.Count);
        foreach (Float3 p in probes)
        {
            if (scene.Navigation.SamplePosition(p, out NavMeshHit hit, 13f, NavMesh.AllAreas)
                && MathF.Abs((float)(hit.Position.X - p.X)) < 0.5f
                && MathF.Abs((float)(hit.Position.Z - p.Z)) < 0.5f)
                heights.Add((float)hit.Position.Y);
            else
                heights.Add(float.NaN);
        }
        return heights;
    }

    private static float WigglyPlateau(float x, float z)
    {
        float x0 = 24f + 2.5f * MathF.Sin(z * 0.45f) + 1.2f * MathF.Sin(z * 1.3f);
        float t = Math.Clamp((x - x0) / 16f, 0f, 1f);
        return 0.5f * t * t * (3f - 2f * t) + 0.25f * (z / TerrainSize);
    }

    /// <summary>
    /// Every structural guarantee a baked terrain navmesh makes, asserted in one pass:
    /// <list type="bullet">
    /// <item>no two vertices in a tile share an XZ column (a torn vertex splits the mesh into
    /// overlapping sheets);</item>
    /// <item>every tile-border edge carries a link to its neighbouring tile (an unstitched
    /// portal is a wall agents cannot cross);</item>
    /// <item>no facet of the walked surface stands past 60° — the walk limit is 45°, and
    /// anything past 60° is not ground, it is an artifact drawn as a sheet and felt as a pop.
    /// One carve-out: polygons narrower than the sliver-absorption threshold (3 voxels). A few
    /// such strips survive where every union with a neighbour would bend reflex, and across a
    /// strip that thin a single quantization step already reads as 45°+, so their facets are
    /// only bounded (75°) rather than forbidden — a hand-span wedge at a crease, never a
    /// wall;</item>
    /// <item>adjacent polygons agree about the surface along their shared edge — the two sides
    /// read the same height cells, so any disagreement is a tear an agent falls through
    /// visually even when Detour walks it fine. Tile borders are held tighter still: both
    /// tiles describe the seam from the same cells, so they must meet on it, not near it;</item>
    /// <item>the surface sits on the terrain: a little high everywhere (voxelization rides the
    /// top of the cell), never buried, because a buried stretch draws as a hole.</item>
    /// </list>
    /// </summary>
    private void AssertBakeIntegrity(NavMeshWorld world, Func<float, float, float> normalizedHeight, float height)
    {
        DtNavMesh mesh = world.GetInstance()!.NativeNavMesh;

        int tornVerts = 0, unstitchedPortals = 0, steepFacets = 0, steepStripFacets = 0, crackedEdges = 0;
        double worstCrack = 0, highestAbove = 0, deepestBelow = 0, worstSlope = 0;
        double worstCrossTileStep = 0;
        Float3 worstSlopeAt = default, worstCrossTileStepAt = default;

        var detailCache = new Dictionary<(int t, int p), List<Float3[]>>();
        List<Float3[]> Det(int dt, int dp)
        {
            if (!detailCache.TryGetValue((dt, dp), out List<Float3[]>? tris))
                detailCache[(dt, dp)] = tris = DetailTris(mesh.GetTile(dt), dp);
            return tris;
        }

        var polyRings = new Dictionary<(int t, int p), Float3[]>();
        var borderEdges = new List<(Float3 a, Float3 b, int t, int p)>();

        for (int t = 0; t < mesh.GetMaxTiles(); t++)
        {
            DtMeshTile tile = mesh.GetTile(t);
            if (tile?.data?.header == null) continue;

            var columns = new HashSet<(int X, int Z)>();
            for (int v = 0; v < tile.data.header.vertCount; v++)
                if (!columns.Add(((int)MathF.Round(tile.data.verts[v * 3] * 64), (int)MathF.Round(tile.data.verts[v * 3 + 2] * 64))))
                    tornVerts++;

            for (int p = 0; p < tile.data.header.polyCount; p++)
            {
                DtPoly poly = tile.data.polys[p];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION) continue;

                var corners = new Float3[poly.vertCount];
                for (int v = 0; v < poly.vertCount; v++)
                    corners[v] = NavMeshConnection.VertexAt(tile, poly.verts[v]);
                polyRings[(t, p)] = corners;
                for (int v = 0; v < poly.vertCount; v++)
                    if (poly.neis[v] == 0)
                        borderEdges.Add((corners[v], corners[(v + 1) % poly.vertCount], t, p));

                bool narrowStrip = PolyWidthXZ(corners) < 0.75; // the builder's absorption threshold (3 voxels)
                foreach (Float3[] tri in Det(t, p))
                {
                    Float3 n = Float3.Cross(tri[1] - tri[0], tri[2] - tri[0]);
                    double len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                    double slope = len < 1e-9 ? 90 : Math.Acos(Math.Clamp(Math.Abs(n.Y / len), 0, 1)) * 180.0 / Math.PI;
                    if (slope > worstSlope && !narrowStrip)
                    {
                        worstSlope = slope;
                        worstSlopeAt = (tri[0] + tri[1] + tri[2]) / 3f;
                    }
                    if (slope > 60.0 && !narrowStrip)
                        steepFacets++;
                    if (slope > 75.0 && narrowStrip)
                        steepStripFacets++;

                    // The surface between the vertices is where errors hide; vertices alone
                    // always read within quantization.
                    Float3[] samples =
                    [
                        (tri[0] + tri[1] + tri[2]) / 3f,
                        (tri[0] + tri[1]) / 2f,
                        (tri[1] + tri[2]) / 2f,
                        (tri[2] + tri[0]) / 2f,
                    ];
                    foreach (Float3 s in samples)
                    {
                        double dev = s.Y - normalizedHeight((float)s.X, (float)s.Z) * height;
                        highestAbove = Math.Max(highestAbove, dev);
                        deepestBelow = Math.Min(deepestBelow, dev);
                    }
                }

                for (int j = 0; j < poly.vertCount; j++)
                {
                    Float3 a = corners[j], b = corners[(j + 1) % poly.vertCount];
                    int nei = poly.neis[j];

                    if ((nei & DtDetour.DT_EXT_LINK) != 0)
                    {
                        bool linked = false;
                        for (int l = poly.firstLink; l != DtDetour.DT_NULL_LINK; l = tile.links[l].next)
                        {
                            if (tile.links[l].edge != j || tile.links[l].refs == 0) continue;
                            linked = true;

                            // The far side reads its own tile's layer, whose heights can
                            // quantize a step or two apart from this one's.
                            DtDetour.DecodePolyId(tile.links[l].refs, out _, out int nt, out int np);
                            if (mesh.GetTile(nt)?.data?.header == null) continue;
                            double crossGap = 0;
                            for (int k = 1; k < 8; k++)
                            {
                                double u = k / 8.0;
                                double x = a.X + (b.X - a.X) * u, z = a.Z + (b.Z - a.Z) * u;
                                double? hp = DetailHeightAt(Det(t, p), x, z, 0.05);
                                double? hq = DetailHeightAt(Det(nt, np), x, z, 0.05);
                                if (hp is double vp && hq is double vq)
                                    crossGap = Math.Max(crossGap, Math.Abs(vp - vq));
                            }
                            if (crossGap > worstCrossTileStep)
                            {
                                worstCrossTileStep = crossGap;
                                worstCrossTileStepAt = new Float3(
                                    (a.X + b.X) / 2f, (a.Y + b.Y) / 2f, (a.Z + b.Z) / 2f);
                            }
                        }
                        if (!linked) unstitchedPortals++;
                        continue;
                    }

                    // Interior edge, visited once per pair: both polygons' detail surfaces must
                    // tell the same story along it.
                    if (nei == 0 || nei - 1 <= p) continue;
                    int q = nei - 1;

                    double gap = 0;
                    for (int k = 1; k < 8; k++)
                    {
                        double u = k / 8.0;
                        double x = a.X + (b.X - a.X) * u, z = a.Z + (b.Z - a.Z) * u;
                        double? hp = DetailHeightAt(Det(t, p), x, z);
                        double? hq = DetailHeightAt(Det(t, q), x, z);
                        if (hp is double vp && hq is double vq)
                            gap = Math.Max(gap, Math.Abs(vp - vq));
                    }

                    worstCrack = Math.Max(worstCrack, gap);
                    if (gap > 0.1)
                        crackedEdges++;
                }
            }
        }

        // Two polygons covering the same ground: the crack checks compare along shared edges,
        // so surfaces crossing without sharing one would slip past them. A detail triangle's
        // centroid strictly inside another polygon's footprint is coverage claimed twice.
        int overlapSamples = 0;
        Float3 overlapAt = default;
        foreach (((int t, int p) ka, Float3[] _) in polyRings)
        {
            foreach (Float3[] tri in Det(ka.t, ka.p))
            {
                Float3 c = (tri[0] + tri[1] + tri[2]) / 3f;
                foreach (((int t, int p) kb, Float3[] ringB) in polyRings)
                {
                    if (kb == ka || !InsideXZ(ringB, c.X, c.Z, 0.02)) continue;
                    overlapSamples++;
                    overlapAt = c;
                    break;
                }
            }
        }

        // Detached slits: two border edges facing each other across a thin gap at similar
        // height — a hair-wide hole between polygons that should abut, drawn as a dark tear.
        // Border chains meeting at a corner are one hole's outline and do not count.
        int slitSamples = 0;
        Float3 slitAt = default;
        foreach ((Float3 a, Float3 b, int t, int p) ea in borderEdges)
        {
            for (int k = 1; k < 4 && slitSamples == 0; k++)
            {
                Float3 m = ea.a + (ea.b - ea.a) * (k / 4f);
                foreach ((Float3 a, Float3 b, int t, int p) eb in borderEdges)
                {
                    if ((eb.t == ea.t && eb.p == ea.p)
                        || SamePointXZ(ea.a, eb.a) || SamePointXZ(ea.a, eb.b)
                        || SamePointXZ(ea.b, eb.a) || SamePointXZ(ea.b, eb.b))
                        continue;

                    (double d, double dy, double u) = PointToSegment(eb.a, eb.b, m);
                    if (d > 0.005 && d < 0.35 && dy < 0.6 && u > 0.05 && u < 0.95)
                    {
                        slitSamples++;
                        slitAt = m;
                        break;
                    }
                }
            }
        }

        // Tile seams close exactly, not nearly. Both tiles read the same cells and describe the
        // seam the same way, so a residual step means one of them built its seam from something
        // the other did not have. A fraction of a voxel is enough: it draws as a dark hairline
        // at a grazing angle.
        Assert.True(worstCrossTileStep < 0.005,
            $"tile surfaces meet {worstCrossTileStep:F3} apart at a seam (worst at {worstCrossTileStepAt})");

        // The outlines the debug view draws must lie on the surface it draws them around. The
        // detail bends every polygon edge between its corners, so a chord corner to corner
        // leaves the surface and reads as a dark seam under it — a defect that exists only in
        // the drawing, which is the worst kind to chase.
        NavMeshTriangulation triangulation = NavMeshTriangulation.FromNavMesh(mesh);
        int floatingEdges = 0;
        double worstFloatingEdge = 0;
        Float3 floatingEdgeAt = default;
        foreach (NavMeshEdge edge in triangulation.Edges)
        {
            Float3 mid = (edge.A + edge.B) / 2f;
            double best = double.MaxValue;
            foreach ((_, List<Float3[]> tris) in detailCache)
            {
                double? surface = DetailHeightAt(tris, mid.X, mid.Z);
                if (surface.HasValue)
                    best = Math.Min(best, Math.Abs(surface.Value - mid.Y));
            }

            if (best == double.MaxValue || best <= 0.02) continue;
            floatingEdges++;
            if (best > worstFloatingEdge)
            {
                worstFloatingEdge = best;
                floatingEdgeAt = mid;
            }
        }

        Assert.True(floatingEdges == 0, $"{floatingEdges} drawn outline segments leave the surface (worst {worstFloatingEdge:F2} at {floatingEdgeAt})");
        Assert.True(tornVerts == 0, $"{tornVerts} torn vertices (same XZ column twice in one tile layer)");
        Assert.True(unstitchedPortals == 0, $"{unstitchedPortals} tile-border edges without a link to the neighbouring tile");
        Assert.True(overlapSamples == 0, $"{overlapSamples} detail triangles sit inside another polygon's footprint (e.g. at {overlapAt})");
        Assert.True(slitSamples == 0, $"hair-wide holes between polygons that should abut (e.g. at {slitAt})");
        Assert.True(steepFacets == 0, $"{steepFacets} facets steeper than 60° on ground that never exceeds ~40° (worst {worstSlope:F1}° at {worstSlopeAt})");
        Assert.True(steepStripFacets == 0, $"{steepStripFacets} facets steeper than 75° inside sub-absorption-width strips");
        Assert.True(crackedEdges == 0, $"{crackedEdges} shared edges where adjacent detail surfaces disagree by more than 0.1 (worst {worstCrack:F2})");
        // Measured on these terrains: rides up to ~+0.7 high (hull corners carry the max of
        // their 2x2 cell neighbourhood plus quantization), dips no lower than chord sag between
        // detail samples. Growth past these bounds is a defect, not drift.
        Assert.True(highestAbove < 0.9, $"surface floats {highestAbove:F2} above the terrain");
        Assert.True(deepestBelow > -0.35, $"surface buried {-deepestBelow:F2} below the terrain");
    }

    /// <summary>The walked surface of one polygon: its height-detail triangles, or the corner
    /// fan Detour falls back to when a tile carries no detail. Reads the tile directly, per
    /// polygon, rather than through <see cref="NavMeshTriangulation"/>, which flattens every
    /// polygon into one buffer. Both decode the same convention — a detail index below the
    /// polygon's vertex count means a corner, above it means a vertex the detail added — so a
    /// change to that convention lands on both.</summary>
    private static List<Float3[]> DetailTris(DtMeshTile tile, int p)
    {
        DtPoly poly = tile.data.polys[p];
        var corners = new Float3[poly.vertCount];
        for (int v = 0; v < poly.vertCount; v++)
            corners[v] = NavMeshConnection.VertexAt(tile, poly.verts[v]);

        List<Float3[]> tris = [];
        if (tile.data.detailMeshes == null)
        {
            for (int v = 2; v < poly.vertCount; v++)
                tris.Add([corners[0], corners[v - 1], corners[v]]);
            return tris;
        }

        DtPolyDetail det = tile.data.detailMeshes[p];
        Float3 VertOf(int idx)
        {
            if (idx < poly.vertCount) return corners[idx];
            int i = (det.vertBase + (idx - poly.vertCount)) * 3;
            return new Float3(tile.data.detailVerts[i], tile.data.detailVerts[i + 1], tile.data.detailVerts[i + 2]);
        }
        for (int d = 0; d < det.triCount; d++)
        {
            int i = (det.triBase + d) * 4;
            tris.Add([VertOf(tile.data.detailTris[i]), VertOf(tile.data.detailTris[i + 1]), VertOf(tile.data.detailTris[i + 2])]);
        }
        return tris;
    }

    /// <summary>Whether an XZ point sits strictly inside a convex ring, at least
    /// <paramref name="shrink"/> in from every edge — points on shared edges and corners
    /// therefore do not count.</summary>
    private static bool InsideXZ(Float3[] ring, double x, double z, double shrink)
    {
        int n = ring.Length;
        double sign = 0;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double ex = ring[i].X - ring[j].X, ez = ring[i].Z - ring[j].Z;
            double len = Math.Sqrt(ex * ex + ez * ez);
            if (len < 1e-9) continue;
            double d = (ex * (z - ring[j].Z) - ez * (x - ring[j].X)) / len;
            if (sign == 0 && Math.Abs(d) > 1e-9) sign = Math.Sign(d);
            if (sign != 0 && d * sign < shrink) return false;
        }
        return sign != 0;
    }

    private static bool SamePointXZ(Float3 a, Float3 b)
        => Math.Abs(a.X - b.X) < 1e-3 && Math.Abs(a.Z - b.Z) < 1e-3;

    /// <summary>XZ distance from a point to a segment, the height difference at the closest
    /// spot, and how far along the segment it lies.</summary>
    private static (double d, double dy, double u) PointToSegment(Float3 a, Float3 b, Float3 p)
    {
        double abx = b.X - a.X, abz = b.Z - a.Z;
        double len2 = abx * abx + abz * abz;
        double u = len2 > 1e-12 ? Math.Clamp(((p.X - a.X) * abx + (p.Z - a.Z) * abz) / len2, 0, 1) : 0;
        double x = a.X + u * abx, z = a.Z + u * abz, y = a.Y + u * (b.Y - a.Y);
        double dx = x - p.X, dz = z - p.Z;
        return (Math.Sqrt(dx * dx + dz * dz), Math.Abs(y - p.Y), u);
    }

    /// <summary>A polygon's XZ footprint width: twice its area over its longest edge — the
    /// width of the strip it covers, the same measure sliver absorption uses.</summary>
    private static double PolyWidthXZ(Float3[] corners)
    {
        double area2 = 0, maxEdgeSq = 0;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            area2 += corners[j].X * corners[i].Z - corners[i].X * corners[j].Z;
            double dx = corners[i].X - corners[j].X, dz = corners[i].Z - corners[j].Z;
            maxEdgeSq = Math.Max(maxEdgeSq, dx * dx + dz * dz);
        }
        return Math.Abs(area2) / Math.Max(1e-9, Math.Sqrt(maxEdgeSq));
    }

    /// <summary>Height of a polygon's walked surface at an XZ point, from whichever of its
    /// triangles covers it; null when the point is outside them all. Points exactly on a
    /// triangle edge sit on the boundary of two, so a little slack keeps the lookup from
    /// falling between them.</summary>
    private static double? DetailHeightAt(List<Float3[]> tris, double x, double z, double slackLimit = 0.02)
    {
        double? best = null;
        double bestSlack = double.MaxValue;
        foreach (Float3[] t in tris)
        {
            double d = (t[1].Z - t[2].Z) * (t[0].X - t[2].X) + (t[2].X - t[1].X) * (t[0].Z - t[2].Z);
            if (Math.Abs(d) < 1e-12) continue;
            double wa = ((t[1].Z - t[2].Z) * (x - t[2].X) + (t[2].X - t[1].X) * (z - t[2].Z)) / d;
            double wb = ((t[2].Z - t[0].Z) * (x - t[2].X) + (t[0].X - t[2].X) * (z - t[2].Z)) / d;
            double wc = 1 - wa - wb;
            double slack = -Math.Min(wa, Math.Min(wb, wc));
            if (slack < bestSlack)
            {
                bestSlack = slack;
                best = wa * t[0].Y + wb * t[1].Y + wc * t[2].Y;
            }
        }
        return bestSlack < slackLimit ? best : null;
    }


    /// <summary>
    /// Turning height detail off has to actually skip it — the setting exists to buy back the
    /// build time it costs on every tile, including every obstacle carve, for scenes whose ground
    /// is flat enough that polygon corners already describe it. Asserted on the vertices
    /// themselves: a coarse bake's surface is made of polygon corners and nothing else, while a
    /// detailed bake of curved ground must have added some.
    /// </summary>
    [Fact]
    public void Terrain_WithoutHeightDetail_KeepsTheCoarsePolygons()
    {
        Scene scene = CreateScene(enable: true);
        AddTerrain(scene, RollingHills);
        NavMeshTriangulation detailed = BakeTerrain(scene).CalculateTriangulation();

        Scene coarseScene = CreateScene(enable: true);
        AddTerrain(coarseScene, RollingHills);
        NavMeshTriangulation coarse = BakeTerrain(coarseScene, heightDetail: false).CalculateTriangulation();

        Assert.True(Array.TrueForAll(coarse.IsPolygonCorner, c => c),
            "the coarse bake carries height-detail vertices despite BuildHeightDetail being off");
        Assert.Contains(false, detailed.IsPolygonCorner);
    }
}
