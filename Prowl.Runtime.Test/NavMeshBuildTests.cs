// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Recast.Detour;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class NavMeshBuildTests
{
    /// <summary>
    /// A 20x20 quad at y=0, wound counter-clockwise when viewed from above (+Y normal),
    /// which is what Recast considers up-facing/walkable.
    /// </summary>
    private static NavMeshGeometrySource FlatQuad(float size = 20f)
    {
        Float3[] verts =
        [
            new(0, 0, 0),
            new(0, 0, size),
            new(size, 0, size),
            new(size, 0, 0),
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];
        return new NavMeshGeometrySource(verts, indices, Float4x4.Identity);
    }

    /// <summary>A 10x10 up-facing plane centred on X, offset along Z. Two of these with the
    /// default tile grid (64 voxels ≈ 10.67 units) sit either side of a tile boundary, which is
    /// what the link-rationing tests need.</summary>
    private static NavMeshGeometrySource Plane(float zOffset) => new(
        [new(-5, 0, -5 + zOffset), new(-5, 0, 5 + zOffset), new(5, 0, 5 + zOffset), new(5, 0, -5 + zOffset)],
        [0, 1, 2, 0, 2, 3], Float4x4.Identity);

    private static NavMeshBuildSettings TestSettings() => new()
    {
        // Coarse voxels + small tiles keep the test fast.
        OverrideVoxelSize = true,
        VoxelSize = 0.25f,
        OverrideTileSize = true,
        TileSize = 64,
    };

    /// <summary>
    /// The winding gate: Recast derives walkability from triangle face normals, so a total
    /// winding/handedness mismatch fails as "the bake produced nothing" rather than an error.
    /// This test existing and passing is what proves the coordinate conventions line up.
    /// </summary>
    [Fact]
    public void FlatQuad_UpFacingWinding_ProducesWalkablePolys()
    {
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [FlatQuad()]);

        Assert.NotNull(data);
        Assert.True(data!.HasTiles, "Expected at least one non-empty tile from a flat walkable quad.");

        DtNavMesh navMesh = data.CreateTileCache(1).GetNavMesh();
        int polyCount = 0;
        for (int i = 0; i < navMesh.GetMaxTiles(); i++)
        {
            DtMeshTile tile = navMesh.GetTile(i);
            if (tile?.data?.header != null)
                polyCount += tile.data.header.polyCount;
        }

        Assert.True(polyCount > 0, "Navmesh instantiated but contains no polygons.");
    }

    /// <summary>The inverse gate: a downward-facing quad must produce nothing walkable — and
    /// "nothing walkable" is a null return, never an empty NavMeshData (an empty one registers
    /// nowhere and draws nothing, silently).</summary>
    [Fact]
    public void FlatQuad_DownFacingWinding_ReturnsNull()
    {
        NavMeshGeometrySource quad = FlatQuad();
        // Reverse winding: normals point -Y, nothing is walkable.
        (quad.Indices[1], quad.Indices[2]) = (quad.Indices[2], quad.Indices[1]);
        (quad.Indices[4], quad.Indices[5]) = (quad.Indices[5], quad.Indices[4]);

        Assert.Null(NavMeshBuilder.Build(TestSettings(), [quad]));
    }

    /// <summary>
    /// Explicit world bounds: a bake whose geometry covers a corner of a much larger world
    /// must size its bounds and tile grid from the supplied extent, not the geometry —
    /// otherwise later partial rebuilds outside the initial geometry are silently discarded
    /// (the destructible-map case: one open spawn cavern in a sealed map).
    /// </summary>
    [Fact]
    public void Build_WithWorldBounds_SizesGridFromBounds()
    {
        // A 10x10 quad in the corner of a declared 100x100 world.
        var bounds = new AABB(new Float3(0, -1, 0), new Float3(100, 1, 100));
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [FlatQuad(10f)], worldBounds: bounds);

        Assert.NotNull(data);
        Assert.Equal(0, data!.BoundsMin.X, 3);
        Assert.Equal(0, data.BoundsMin.Z, 3);
        Assert.Equal(100, data.BoundsMax.X, 3);
        Assert.Equal(100, data.BoundsMax.Z, 3);
        // Y unions with geometry rather than trusting the declared bounds alone.
        Assert.True(data.BoundsMin.Y <= 0f && data.BoundsMax.Y >= 0f);

        // Tile capacity must span the declared world: 100/16 per axis => 7x7 = 49 tiles minimum.
        Assert.True(data.MaxTiles >= 49, $"MaxTiles must cover the declared bounds, got {data.MaxTiles}.");
    }

    [Fact]
    public void Build_WithNoGeometry_ReturnsNull()
    {
        Assert.Null(NavMeshBuilder.Build(TestSettings(), []));
    }

    [Fact]
    public void Build_AppliesDefaultAreaToPolys()
    {
        const int area = 4;
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [FlatQuad()], defaultArea: area);
        Assert.NotNull(data);

        DtNavMesh navMesh = data!.CreateTileCache(1).GetNavMesh();
        for (int i = 0; i < navMesh.GetMaxTiles(); i++)
        {
            DtMeshTile tile = navMesh.GetTile(i);
            if (tile?.data?.header == null) continue;
            for (int p = 0; p < tile.data.header.polyCount; p++)
                Assert.Equal(area, NavMeshAreas.FromDetourArea(tile.data.polys[p].GetArea()));
        }
    }

    /// <summary>
    /// Two quads separated by a gap wider than the agent: same tile grid, but paths must not
    /// cross the gap. Locks in that disconnected geometry stays disconnected.
    /// </summary>
    [Fact]
    public void SeparatedQuads_BothBake()
    {
        NavMeshGeometrySource left = FlatQuad(10f);
        NavMeshGeometrySource right = FlatQuad(10f);
        right.Transform = Float4x4.CreateTranslation(new Float3(20f, 0, 0));

        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [left, right]);
        Assert.NotNull(data);
        Assert.True(data!.HasTiles);
    }

    [Fact]
    public void NavMeshData_EchoRoundTrip_PreservesTilesAndSettings()
    {
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [FlatQuad()], defaultArea: 3);
        Assert.NotNull(data);
        Assert.True(data!.HasTiles);

        // The serialize → deserialize path every .navmesh asset takes.
        EchoObject echo = Serializer.Serialize(data);
        NavMeshData? loaded = Serializer.Deserialize<NavMeshData>(echo);

        Assert.NotNull(loaded);
        Assert.Equal(data.CacheLayers.Count, loaded!.CacheLayers.Count);
        for (int i = 0; i < data.CacheLayers.Count; i++)
        {
            Assert.Equal(data.CacheLayers[i].X, loaded.CacheLayers[i].X);
            Assert.Equal(data.CacheLayers[i].Z, loaded.CacheLayers[i].Z);
            Assert.Equal(data.CacheLayers[i].Data, loaded.CacheLayers[i].Data);
        }

        Assert.Equal(data.Settings.AgentRadius, loaded.Settings.AgentRadius);
        Assert.Equal(data.TileWorldSize, loaded.TileWorldSize);
        Assert.Equal(data.MaxTiles, loaded.MaxTiles);
        Assert.Equal(data.MaxPolys, loaded.MaxPolys);
        Assert.Equal(data.Origin, loaded.Origin);

        // The reloaded asset must instantiate a working navmesh.
        DtNavMesh navMesh = loaded.CreateTileCache(1).GetNavMesh();
        int polyCount = 0;
        for (int i = 0; i < navMesh.GetMaxTiles(); i++)
        {
            DtMeshTile tile = navMesh.GetTile(i);
            if (tile?.data?.header != null)
                polyCount += tile.data.header.polyCount;
        }
        Assert.True(polyCount > 0);
    }

    /// <summary>Two coplanar adjacent quads as separate sources with different areas.
    /// Left: x 0..10 (Walkable), right: x 10..20 (area 3).</summary>
    private static (NavMeshGeometrySource left, NavMeshGeometrySource right) TwoAreaFloor()
    {
        Float3[] leftVerts = [new(0, 0, 0), new(0, 0, 20), new(10, 0, 20), new(10, 0, 0)];
        Float3[] rightVerts = [new(10, 0, 0), new(10, 0, 20), new(20, 0, 20), new(20, 0, 0)];
        int[] indices = [0, 1, 2, 0, 2, 3];
        return (
            new NavMeshGeometrySource(leftVerts, indices, Float4x4.Identity, NavMeshAreas.Walkable),
            new NavMeshGeometrySource(rightVerts, indices, Float4x4.Identity, area: 3));
    }

    private static void AssertTwoAreaSemantics(NavMeshWorld world)
    {
        // Each side samples as its own area (Mask is the area's bit).
        Assert.True(world.SamplePosition(new Float3(4, 0.2f, 10), out NavMeshHit leftHit, 0.5f, NavMesh.AllAreas));
        Assert.Equal(1 << NavMeshAreas.Walkable, leftHit.Mask);
        Assert.True(world.SamplePosition(new Float3(16, 0.2f, 10), out NavMeshHit rightHit, 0.5f, NavMesh.AllAreas));
        Assert.Equal(1 << 3, rightHit.Mask);

        // The area boundary must remain traversable — different areas are neighbours,
        // not walls. A rubble border must never become invisible geometry.
        var path = new NavMeshPath();
        Assert.True(world.CalculatePath(new Float3(4, 0, 10), new Float3(16, 0, 10), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);

        // Excluding area 3 makes the right side unreachable (partial path at best).
        int maskWithout3 = ~(1 << 3);
        world.CalculatePath(new Float3(4, 0, 10), new Float3(16, 0, 10), maskWithout3, path);
        Assert.NotEqual(NavMeshPathStatus.PathComplete, path.Status);
    }

    /// <summary>The per-source-area gate: NavMeshGeometrySource.Area must survive the bake
    /// into Detour poly areas, on the correct side, without breaking adjacency.</summary>
    [Fact]
    public void Build_HonorsPerSourceAreas()
    {
        (NavMeshGeometrySource left, NavMeshGeometrySource right) = TwoAreaFloor();
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [left, right]);
        Assert.NotNull(data);

        var world = new NavMeshWorld();
        world.AddNavMeshData(data!);

        // The triangulation must contain both areas.
        NavMeshTriangulation tri = world.CalculateTriangulation();
        var areas = new HashSet<int>(tri.Areas);
        Assert.Contains(NavMeshAreas.Walkable, areas);
        Assert.Contains(3, areas);

        AssertTwoAreaSemantics(world);
    }

    /// <summary>Per-source areas must also survive the partial-rebuild path (the drill path
    /// builds its provider separately).</summary>
    [Fact]
    public void BuildTilesInBounds_HonorsPerSourceAreas()
    {
        // Bake the whole floor uniform first...
        (NavMeshGeometrySource left, NavMeshGeometrySource right) = TwoAreaFloor();
        NavMeshGeometrySource uniformRight = right;
        uniformRight.Area = NavMeshAreas.Walkable;
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [left, uniformRight]);
        Assert.NotNull(data);

        var world = new NavMeshWorld();
        NavMeshInstance? instance = world.AddNavMeshData(data!);
        Assert.NotNull(instance);

        // ...then rebuild with the right half as area 3 (rubble appearing after a drill).
        List<(int X, int Z, List<byte[]> Layers)> rebuilt = NavMeshBuilder.BuildTilesInBounds(
            data!, [left, right], new Float3(0, -1, 0), new Float3(20, 1, 20));
        Assert.NotEmpty(rebuilt);

        SwapRebuiltTiles(world, instance!, rebuilt);
        AssertTwoAreaSemantics(world);
    }

    /// <summary>The tile swap NavMeshSurface.ApplyRebuiltTiles performs, without a surface: drop
    /// each affected tile's layers (and the navmesh tiles the cache built from them), add the
    /// regenerated blobs, then re-contour.</summary>
    private static void SwapRebuiltTiles(NavMeshWorld world, NavMeshInstance instance,
        List<(int X, int Z, List<byte[]> Layers)> rebuilt)
    {
        world.MutateTileCache(instance, cache =>
        {
            DtNavMesh navMesh = cache.GetNavMesh();
            var added = new List<long>();
            foreach ((int x, int z, List<byte[]> blobs) in rebuilt)
            {
                foreach (long tileRef in cache.GetTilesAt(x, z))
                {
                    var header = cache.GetTileByRef(tileRef)?.header;
                    if (header != null)
                    {
                        long navRef = navMesh.GetTileRefAt(header.tx, header.ty, header.tlayer);
                        if (navRef != 0) navMesh.RemoveTile(navRef);
                    }
                    cache.RemoveTile(tileRef);
                }
                foreach (byte[] blob in blobs)
                {
                    long tileRef = cache.AddTile(blob, 0);
                    if (tileRef != 0) added.Add(tileRef);
                }
            }
            foreach (long tileRef in added)
                cache.BuildNavMeshTile(tileRef);
        });
    }

    /// <summary>Higher area cost must bias route choice: with a cheap detour available around
    /// an expensive strip, the path avoids the strip; with uniform costs it goes straight.</summary>
    [Fact]
    public void AreaCosts_BiasPathSelection()
    {
        // 30x30 floor; a full-height strip (x 12..18) of area 4 crosses the middle.
        Float3[] leftVerts = [new(0, 0, 0), new(0, 0, 30), new(12, 0, 30), new(12, 0, 0)];
        Float3[] stripVerts = [new(12, 0, 0), new(12, 0, 30), new(18, 0, 30), new(18, 0, 0)];
        Float3[] rightVerts = [new(18, 0, 0), new(18, 0, 30), new(30, 0, 30), new(30, 0, 0)];
        int[] indices = [0, 1, 2, 0, 2, 3];
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(),
        [
            new NavMeshGeometrySource(leftVerts, indices, Float4x4.Identity, NavMeshAreas.Walkable),
            new NavMeshGeometrySource(stripVerts, indices, Float4x4.Identity, area: 4),
            new NavMeshGeometrySource(rightVerts, indices, Float4x4.Identity, NavMeshAreas.Walkable),
        ]);
        Assert.NotNull(data);
        var world = new NavMeshWorld();
        world.AddNavMeshData(data!);

        // The strip spans the full floor, so it cannot be avoided — but a filter that prices
        // area 4 highly must still cross it (cost biases, never blocks).
        var expensive = new NavMeshQueryFilter();
        expensive.SetAreaCost(4, 10f);
        var path = new NavMeshPath();
        Assert.True(world.CalculatePath(new Float3(5, 0, 15), new Float3(25, 0, 15), expensive, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    /// <summary>
    /// Tiles carry no detail mesh — heights come from the polygon planes, which is exact on flat
    /// geometry. Locks in that such a bake builds, serializes, round-trips, and answers queries.
    /// </summary>
    [Fact]
    public void Build_WithoutDetailMesh_BakesAndQueries()
    {
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [FlatQuad()]);
        Assert.NotNull(data);
        Assert.True(data!.HasTiles);

        // Serialized tiles with no detail mesh must round-trip and instantiate.
        EchoObject echo = Serializer.Serialize(data);
        NavMeshData? loaded = Serializer.Deserialize<NavMeshData>(echo);
        Assert.NotNull(loaded);

        var world = new NavMeshWorld();
        Assert.NotNull(world.AddNavMeshData(loaded!));

        // Queries work; heights come from the polygon planes (exact on a flat floor).
        var path = new NavMeshPath();
        Assert.True(world.CalculatePath(new Float3(2, 0, 2), new Float3(18, 0, 18), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
        Assert.True(world.SamplePosition(new Float3(10, 0.5f, 10), out NavMeshHit hit, 1f, NavMesh.AllAreas));
        Assert.True(System.Math.Abs(hit.Position.Y) < 0.3f, $"Height should come from the poly plane, got y={hit.Position.Y:0.00}.");
    }

    /// <summary>Same input twice must produce byte-identical tiles (single-threaded build).</summary>
    [Fact]
    public void Build_IsDeterministic_SingleThreaded()
    {
        NavMeshData? a = NavMeshBuilder.Build(TestSettings(), [FlatQuad()]);
        NavMeshData? b = NavMeshBuilder.Build(TestSettings(), [FlatQuad()]);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a!.CacheLayers.Count, b!.CacheLayers.Count);
        for (int i = 0; i < a.CacheLayers.Count; i++)
        {
            Assert.Equal(a.CacheLayers[i].X, b.CacheLayers[i].X);
            Assert.Equal(a.CacheLayers[i].Z, b.CacheLayers[i].Z);
            Assert.Equal(a.CacheLayers[i].Data, b.CacheLayers[i].Data);
        }
    }

    /// <summary>
    /// A threaded bake produces byte-identical tiles to a serial one. Every surface bake is
    /// threaded, and workers build through their own reusable scratch — so a partition carrying
    /// state between the tiles it builds, or writing results out of order, would show up here.
    /// </summary>
    [Fact]
    public void Build_Threaded_MatchesSingleThreaded()
    {
        // 40x40 spans several tiles, so the work actually partitions across threads.
        NavMeshData? serial = NavMeshBuilder.Build(TestSettings(), [FlatQuad(40f)], threads: 1);
        NavMeshData? threaded = NavMeshBuilder.Build(TestSettings(), [FlatQuad(40f)], threads: 4);

        Assert.NotNull(serial);
        Assert.NotNull(threaded);
        Assert.True(serial!.CacheLayers.Count > 1, "Test geometry must span more than one tile.");
        Assert.Equal(serial.CacheLayers.Count, threaded!.CacheLayers.Count);
        for (int i = 0; i < serial.CacheLayers.Count; i++)
        {
            Assert.Equal(serial.CacheLayers[i].X, threaded.CacheLayers[i].X);
            Assert.Equal(serial.CacheLayers[i].Z, threaded.CacheLayers[i].Z);
            Assert.Equal(serial.CacheLayers[i].Data, threaded.CacheLayers[i].Data);
        }
    }

    /// <summary>
    /// A baked asset triangulates without being registered with any scene or world, which is what
    /// lets the editor draw the surface overlay outside play mode where nothing registers it.
    /// </summary>
    [Fact]
    public void NavMeshData_CalculateTriangulation_WorksWithoutRegistration()
    {
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [FlatQuad()]);
        Assert.NotNull(data);

        NavMeshTriangulation tri = data!.CalculateTriangulation();

        Assert.NotEmpty(tri.Vertices);
        Assert.NotEmpty(tri.Indices);
        Assert.Equal(tri.Indices.Length / 3, tri.Areas.Length);
        Assert.All(tri.Areas, a => Assert.Equal(NavMeshAreas.Walkable, a));
        // Indices stay inside the vertex array — a fan-triangulation slip would blow past it.
        Assert.All(tri.Indices, i => Assert.InRange(i, 0, tri.Vertices.Length - 1));
    }

    /// <summary>
    /// Links whose endpoints fall in DIFFERENT tiles have to bake AND instantiate, however many
    /// of them cross the same boundary. Detour sizes a tile's link pool when the tile is built
    /// and does not fully budget connections that leave it, so past a handful the pool overflows
    /// and AddTile throws IndexOutOfRange — at load, on an asset that baked and saved cleanly.
    /// The trigger is the count crossing one boundary, not the width: a single Width=5 link and
    /// five separate zero-width links failed identically. The tile builder now rations them, so
    /// the excess is dropped with a warning rather than taking the whole navmesh down.
    /// </summary>
    [Theory]
    [InlineData(1, 0f)]
    [InlineData(1, 5f)]     // one wide link: expands to 5 parallel connections
    [InlineData(5, 0f)]     // five separate narrow links across the same boundary
    [InlineData(40, 0f)]
    [InlineData(8, 12f)]    // both at once
    public void Build_LinksAcrossTileBoundary_Instantiate(int linkCount, float width)
    {
        var links = new List<NavMeshLinkSource>();
        for (int i = 0; i < linkCount; i++)
        {
            float x = linkCount == 1 ? 0f : -3f + i * (6f / (linkCount - 1));
            links.Add(new NavMeshLinkSource(new Float3(x, 0, 3.92f), new Float3(x, 0, 6.92f),
                width, bidirectional: true, NavMeshAreas.Jump, userId: i + 1));
        }

        NavMeshData? data = NavMeshBuilder.Build(new NavMeshBuildSettings(),
            [Plane(0f), Plane(11.08f)], links: links);
        Assert.NotNull(data);
        Assert.Equal(linkCount, data!.Links.Count);

        var world = new NavMeshWorld();
        NavMeshInstance? instance = world.AddNavMeshData(data);
        Assert.NotNull(instance);
        // Rationing may drop the excess, but never all of them — the route must survive.
        Assert.True(instance!.ContainsLinkId(1), "The first link must reach the live navmesh.");

        var path = new NavMeshPath();
        Assert.True(world.CalculatePath(new Float3(0, 0, -3), new Float3(0, 0, 14), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    /// <summary>
    /// A tile's link pool is spent by connections ARRIVING from its eight neighbours as well as by
    /// its own, and Detour budgets nothing for arrivals — so a destination can be swamped by
    /// sources that are individually modest. This drives every neighbour at one destination at once.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void Build_LinksArrivingFromEveryNeighbour_Instantiate(int perNeighbour)
    {
        NavMeshData? data = NavMeshBuilder.Build(new NavMeshBuildSettings(), [FlatQuad(40f)]);
        Assert.NotNull(data);

        float ts = data!.TileWorldSize;
        Float3 o = data.Origin;
        Float3 Centre(int tx, int tz) => new(o.X + (tx + 0.5f) * ts, 0, o.Z + (tz + 0.5f) * ts);

        Float3 destination = Centre(2, 2);
        int id = 1;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                Float3 source = Centre(2 + dx, 2 + dz);
                for (int k = 0; k < perNeighbour; k++)
                    data.Links.Add(NavMeshData.NavMeshLinkEntry.From(new NavMeshLinkSource(
                        new Float3(source.X + k * 0.4f, 0, source.Z), destination,
                        width: 0f, bidirectional: true, NavMeshAreas.Jump, userId: id++)));
            }

        var world = new NavMeshWorld();
        NavMeshInstance? instance = world.AddNavMeshData(data);
        Assert.NotNull(instance);

        for (int link = 1; link < id; link++)
            Assert.True(instance!.ContainsLinkId(link), $"Link {link} must reach the live navmesh.");
    }

    /// <summary>
    /// Links crowding one tile boundary all cross, however many there are and however wide.
    /// Detour sizes a tile's link pool from the connections stored in the tile and budgets nothing
    /// for those arriving from neighbours, so a crowded boundary is where the pool runs out.
    /// </summary>
    [Theory]
    [InlineData(1, 5f)]    // one wide link
    [InlineData(1, 20f)]
    [InlineData(4, 0f)]
    [InlineData(6, 0f)]
    [InlineData(12, 0f)]
    public void Build_LinksCrowdingATileBoundary_AllCross(int linkCount, float width)
    {
        var links = new List<NavMeshLinkSource>();
        for (int i = 0; i < linkCount; i++)
        {
            float x = linkCount == 1 ? 0f : -3f + i * (6f / (linkCount - 1));
            links.Add(new NavMeshLinkSource(new Float3(x, 0, 3.92f), new Float3(x, 0, 6.92f),
                width, bidirectional: true, NavMeshAreas.Jump, userId: i + 1));
        }

        NavMeshData? data = NavMeshBuilder.Build(new NavMeshBuildSettings(),
            [Plane(0f), Plane(11.08f)], links: links);
        Assert.NotNull(data);

        var warnings = new List<string>();
        void Capture(string message, DebugStackTrace? trace, LogSeverity severity)
        {
            if (severity == LogSeverity.Warning && message.Contains("NavMeshLink")) warnings.Add(message);
        }

        Debug.OnLog += Capture;
        try
        {
            var world = new NavMeshWorld();
            NavMeshInstance? instance = world.AddNavMeshData(data!);
            Assert.NotNull(instance);

            int inMesh = 0;
            for (int i = 1; i <= linkCount; i++)
                if (instance!.ContainsLinkId(i)) inMesh++;
            Assert.Equal(linkCount, inMesh);
            Assert.Empty(warnings);
        }
        finally
        {
            Debug.OnLog -= Capture;
        }
    }

    /// <summary>
    /// Min Region Area (Unity's) culls islands too small to be worth standing on. The layers a
    /// carving bake stores are partitioned at runtime, so the cull has to happen at bake time
    /// or not at all — this pins that it happens, and that turning it off keeps the island.
    /// </summary>
    [Theory]
    [InlineData(20f, false)]
    [InlineData(0f, true)]
    public void Build_MinRegionArea_CullsSmallIslands(float minRegionArea, bool islandSurvives)
    {
        NavMeshBuildSettings settings = TestSettings();
        settings.MinRegionArea = minRegionArea;

        // A 4x4 platform floating well above the floor, far enough inside one tile that it is
        // not exempted as a border region. Erosion leaves ~6 units² of it — under the 20 above.
        NavMeshData? data = NavMeshBuilder.Build(settings, [FlatQuad(), Platform(3f, 3f, 4f, 2f)]);

        Assert.NotNull(data);
        NavMeshTriangulation tri = data!.CalculateTriangulation();

        bool island = false;
        foreach (Float3 v in tri.Vertices)
            if (v.Y > 1.5f) island = true;

        Assert.Equal(islandSurvives, island);
        // The floor is far too big to cull either way.
        Assert.Contains(tri.Vertices, v => v.Y < 1.5f);
    }

    /// <summary>An up-facing quad of <paramref name="size"/> at height <paramref name="y"/>.</summary>
    private static NavMeshGeometrySource Platform(float minX, float minZ, float size, float y)
    {
        Float3[] verts =
        [
            new(minX, y, minZ),
            new(minX, y, minZ + size),
            new(minX + size, y, minZ + size),
            new(minX + size, y, minZ),
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];
        return new NavMeshGeometrySource(verts, indices, Float4x4.Identity);
    }
}
