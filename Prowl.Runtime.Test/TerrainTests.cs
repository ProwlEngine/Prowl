// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Every terrain test lives here: physics raycasts, the terrain data over its Graphite package, and the collider.

using System;
using System.Collections.Generic;
using System.Linq;

using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

using Xunit;

using TerrainBrush = Prowl.Graphite.Rendering.TerrainBrush;

namespace Prowl.Runtime.Test;

/// <summary>Every terrain test: the physics raycast and the terrain data over its Graphite package.</summary>
public class TerrainTests
{
    #region Physics: heightmap raycasts

    // Grid traversal is exercised against a stub height provider, so the raycast can be tested
    // without a TerrainData asset or a scene behind it.

    /// <summary>
    /// A flat 8x8 grid of unit cells at a fixed local height, placed by an arbitrary transform so the
    /// rotated case can be exercised. Heights are terrain-local, exactly as TerrainCollider reports them.
    /// </summary>
    private sealed class FlatTerrain : ITerrainHeightProvider
    {
        private readonly float _height;
        private readonly (int X, int Z)? _hole;

        public FlatTerrain(float height, (int X, int Z)? hole = null, Float4x4? localToWorld = null)
        {
            _height = height;
            _hole = hole;
            LocalToWorld = localToWorld ?? Float4x4.Identity;
            Float4x4.Invert(LocalToWorld, out Float4x4 inverse);
            WorldToLocal = inverse;
        }

        public int Width => 8;
        public int Height => 8;
        public float CellSize => 1.0f;

        public Float4x4 LocalToWorld { get; }
        public Float4x4 WorldToLocal { get; }

        public JBoundingBox WorldBounds
        {
            get
            {
                Float3 min = new(float.MaxValue), max = new(float.MinValue);
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Float3(
                        (i & 1) == 0 ? 0f : 7f,
                        (i & 2) == 0 ? _height - 1f : _height + 1f,
                        (i & 4) == 0 ? 0f : 7f);

                    Float3 world = Float4x4.TransformPoint(corner, LocalToWorld);
                    min = Maths.Min(min, world);
                    max = Maths.Max(max, world);
                }

                return new JBoundingBox(new JVector(min.X, min.Y, min.Z), new JVector(max.X, max.Y, max.Z));
            }
        }

        public bool TryGetHeight(int x, int z, out float height)
        {
            height = _height;
            return x >= 0 && x < Width && z >= 0 && z < Height;
        }

        public bool IsValidCell(int x, int z) => x >= 0 && x < Width - 1 && z >= 0 && z < Height - 1;

        public bool IsCellHole(int x, int z) => _hole.HasValue && _hole.Value.X == x && _hole.Value.Z == z;
    }

    // A straight-down ray has no XZ component, so the grid walk has nothing to step through. That case
    // used to bail out and report a miss, which broke the most common terrain query there is: the
    // ground height under a point, a character's grounding check, a vehicle's suspension ray.
    [Fact]
    public void VerticalRay_HitsTheCellBelowIt()
    {
        var proxy = new TerrainHeightmapProxy(new FlatTerrain(2.0f));

        bool hit = proxy.RayCast(new JVector(3.25f, 10.0f, 3.75f), new JVector(0, -1, 0), out JVector normal, out float lambda);

        Assert.True(hit, "a ray dropped straight onto terrain should hit it");
        Assert.Equal(8.0, lambda, 3); // from y=10 down to y=2
        Assert.True(normal.Y > 0, "a flat terrain should report an upward normal");
    }

    [Fact]
    public void VerticalRay_MissesWhereTheTerrainHasAHole()
    {
        var proxy = new TerrainHeightmapProxy(new FlatTerrain(2.0f, hole: (3, 3)));

        Assert.False(proxy.RayCast(new JVector(3.25f, 10.0f, 3.75f), new JVector(0, -1, 0), out _, out _));
        Assert.True(proxy.RayCast(new JVector(4.25f, 10.0f, 4.75f), new JVector(0, -1, 0), out _, out _));
    }

    [Fact]
    public void VerticalRay_OutsideTheGrid_Misses()
    {
        var proxy = new TerrainHeightmapProxy(new FlatTerrain(2.0f));

        Assert.False(proxy.RayCast(new JVector(50.0f, 10.0f, 50.0f), new JVector(0, -1, 0), out _, out _));
    }

    [Fact]
    public void AngledRay_StillTraversesTheGrid()
    {
        var proxy = new TerrainHeightmapProxy(new FlatTerrain(2.0f));

        // Starts above one corner and descends across the grid, so it must walk several cells first.
        // Descends across a couple of cells before reaching the surface, and stays off the cell
        // diagonal, since a ray running exactly along it misses both of a cell's triangles.
        Float3 d = Float3.Normalize(new Float3(1, -8, 3));
        bool hit = proxy.RayCast(new JVector(0.25f, 6.0f, 0.75f), new JVector(d.X, d.Y, d.Z), out _, out float lambda);

        Assert.True(hit, "an angled ray should still find the ground");
        Assert.True(lambda > 0);
    }

    // Terrain has to support arbitrary rotation, so the grid walk happens in terrain-local space and
    // the ray is brought into it. A terrain stood on its side is hit by a ray along world -X.
    [Fact]
    public void RotatedTerrain_IsHitAlongItsOwnUpAxis()
    {
        // Rotated 90 degrees about Z, so the terrain's local +Y points along world -X.
        Float4x4 rotated = Float4x4.CreateTRS(Float3.Zero, Quaternion.AxisAngle(Float3.UnitZ, Maths.PI * 0.5f), Float3.One);
        var proxy = new TerrainHeightmapProxy(new FlatTerrain(2.0f, localToWorld: rotated));

        // Local (3.5, 10, 3.5) is where a downward ray would start; put it through the same transform.
        Float3 start = Float4x4.TransformPoint(new Float3(3.25f, 10.0f, 3.75f), rotated);
        Float3 dir = Float4x4.TransformNormal(new Float3(0, -1, 0), rotated);

        bool hit = proxy.RayCast(new JVector(start.X, start.Y, start.Z), new JVector(dir.X, dir.Y, dir.Z),
            out JVector normal, out float lambda);

        Assert.True(hit, "a rotated terrain must still be hit along its own up axis");
        Assert.Equal(8.0, lambda, 3); // the same distance as the unrotated case

        // The surface normal should have rotated with the terrain: local +Y becomes world -X.
        Assert.Equal(-1.0, normal.X, 2);
    }

    [Fact]
    public void RotatedTerrain_IsMissedByAWorldVerticalRay()
    {
        Float4x4 rotated = Float4x4.CreateTRS(Float3.Zero, Quaternion.AxisAngle(Float3.UnitZ, Maths.PI * 0.5f), Float3.One);
        var proxy = new TerrainHeightmapProxy(new FlatTerrain(2.0f, localToWorld: rotated));

        // Straight down in world space is now along the terrain's surface, not into it.
        Assert.False(proxy.RayCast(new JVector(-2.0f, 50.0f, 3.5f), new JVector(0, -1, 0), out _, out _));
    }

    #endregion

    #region Data

    private const int kRes = 33;

    /// <summary>A 32 metre terrain of 33 samples a side, 100 metres tall, whose height rises with U from 0 to 1.</summary>
    private static TerrainData RampTerrain()
    {
        var data = new TerrainData { Size = 32f, Height = 100f };
        data.ResizeHeightmap(kRes);
        float[] heights = new float[kRes * kRes];
        for (int z = 0; z < kRes; z++)
            for (int x = 0; x < kRes; x++)
                heights[z * kRes + x] = x / (float)(kRes - 1);
        data.SetHeights(heights);
        return data;
    }

    private static TerrainData Small()
    {
        var data = new TerrainData { Size = 32f, Height = 100f };
        data.ResizeHeightmap(kRes);
        return data;
    }

    [Fact]
    public void InterpolatedHeightFollowsTheSamples()
    {
        using TerrainData data = RampTerrain();
        for (float u = 0.1f; u <= 0.9f; u += 0.05f)
            Assert.Equal(u * data.Height, data.GetInterpolatedHeight(u, 0.5f), 1);
        Assert.Equal(50f, data.GetHeight(16, 3), 2);
    }

    [Fact]
    public void HeightsRoundTripThroughThePackage()
    {
        using TerrainData data = RampTerrain();
        float[] heights = data.GetHeights();
        for (int x = 0; x < kRes; x++)
            Assert.Equal(x / (float)(kRes - 1), heights[5 * kRes + x], 1f / 65535f);
    }

    [Fact]
    public void ResizingResamplesTheHeights()
    {
        using TerrainData data = RampTerrain();
        data.ResizeHeightmap(65);
        Assert.Equal(65, data.HeightmapResolution);
        Assert.Equal(0.5f, data.Spacing, 4);
        for (float u = 0.1f; u <= 0.9f; u += 0.1f)
            Assert.Equal(u * data.Height, data.GetInterpolatedHeight(u, 0.5f), 1);
    }

    [Fact]
    public void ChangingTheHeightScalesTheGround()
    {
        using TerrainData data = RampTerrain();
        data.Height = 50f;
        Assert.Equal(25f, data.GetInterpolatedHeight(0.5f, 0.5f), 1);
    }

    [Fact]
    public void RaisingLiftsTheGroundUnderTheBrushOnly()
    {
        using TerrainData data = Small();
        int before = data.Version;
        data.Edit(TerrainBrush.Raise, new Float2(16, 16), 4f, 5f);
        Assert.Equal(5f, data.GetHeight(16, 16), 2);
        Assert.Equal(0f, data.GetHeight(2, 2));
        Assert.NotEqual(before, data.Version);
    }

    [Fact]
    public void FlattenPullsTowardItsHeightAboveTheBase()
    {
        using TerrainData data = Small();
        data.Edit(TerrainBrush.Flatten, new Float2(16, 16), 4f, 1f, 30f);
        Assert.Equal(30f, data.GetHeight(16, 16), 1);
    }

    [Fact]
    public void HolesCutAndFillUnderTheBrush()
    {
        using TerrainData data = Small();
        data.Edit(TerrainBrush.Hole, new Float2(16, 16), 2f, 1f);
        Assert.True(data.IsCellHole(15, 15));
        Assert.True(data.IsHoleAt(15.5f / 32f, 15.5f / 32f));
        Assert.False(data.IsCellHole(2, 2));

        data.Edit(TerrainBrush.Hole, new Float2(16, 16), 2f, -1f);
        Assert.False(data.IsCellHole(15, 15));
    }

    [Fact]
    public void PaintPutsItsLayerUnderTheBrush()
    {
        using TerrainData data = Small();
        data.Edit(TerrainBrush.Paint, new Float2(16, 16), 4f, 1f, layer: 2);
        Assert.Equal(1f, data.GetSplat(16, 16, 2), 2);
        Assert.Equal(0f, data.GetSplat(16, 16, 0), 2);
        Assert.Equal(1f, data.GetSplat(2, 2, 0), 2);
    }

    [Fact]
    public void DetailBrushGrowsAndThinsItsPrototype()
    {
        using TerrainData data = Small();
        data.Edit(TerrainBrush.Detail, new Float2(16, 16), 4f, 1f);
        Assert.Equal(1f, data.GetDetailDensity(0, 16, 16), 2);
        data.Edit(TerrainBrush.Detail, new Float2(16, 16), 4f, -0.5f);
        Assert.Equal(0.5f, data.GetDetailDensity(0, 16, 16), 1);
        Assert.Equal(0f, data.GetDetailDensity(0, 2, 2));
    }

    [Fact]
    public void AddingALayerKeepsThePaint()
    {
        using TerrainData data = Small();
        data.Edit(TerrainBrush.Paint, new Float2(16, 16), 4f, 1f, layer: 3);
        data.AddLayer(new TerrainLayer());
        Assert.Equal(5, data.LayerCount);
        Assert.Equal(1f, data.GetSplat(16, 16, 3), 2);
        Assert.Equal(1f, data.GetSplat(2, 2, 0), 2);
        Assert.Equal(0f, data.GetSplat(2, 2, 4), 2);
    }

    [Fact]
    public void RemovingALayerMovesTheLaterLayersDown()
    {
        using TerrainData data = Small();
        data.Edit(TerrainBrush.Paint, new Float2(16, 16), 4f, 1f, layer: 2);
        data.RemoveLayer(1);
        Assert.Equal(3, data.LayerCount);
        Assert.Equal(1f, data.GetSplat(16, 16, 1), 2);
    }

    [Fact]
    public void OneLayerLeftPaintsEverythingWithIt()
    {
        using TerrainData data = Small();
        data.RemoveLayer(3);
        data.RemoveLayer(2);
        data.RemoveLayer(1);
        Assert.Equal(1, data.LayerCount);
        Assert.Equal(1f, data.GetSplat(16, 16, 0));
        data.AddLayer(new TerrainLayer());
        Assert.Equal(1f, data.GetSplat(16, 16, 0), 2);
        Assert.Equal(0f, data.GetSplat(16, 16, 1), 2);
    }

    [Fact]
    public void AddingAndRemovingDetailPrototypesKeepsTheDensities()
    {
        using TerrainData data = Small();
        data.AddDetailPrototype(new DetailPrototype());
        data.Edit(TerrainBrush.Detail, new Float2(16, 16), 4f, 1f, layer: 1);
        data.RemoveDetailPrototype(0);
        Assert.Single(data.DetailPrototypes);
        Assert.Equal(1f, data.GetDetailDensity(0, 16, 16), 2);
    }

    [Fact]
    public void TreesKeepWhereTheyWerePlaced()
    {
        using TerrainData data = Small();
        data.TreePrototypes.Add(new TreePrototype());
        data.AddTree(new TreeInstance { Position = new Float2(0.25f, 0.75f), Rotation = 1f, WidthScale = 2f, HeightScale = 3f, Tint = Color.Red });
        TreeInstance tree = Assert.Single(data.Trees);
        Assert.Equal(0.25f, tree.Position.X, 4);
        Assert.Equal(0.75f, tree.Position.Y, 4);
        Assert.Equal((1f, 2f, 3f), (tree.Rotation, tree.WidthScale, tree.HeightScale));
        Assert.Equal(Color.Red, tree.Tint);
        Assert.Equal(1, data.RemoveTrees(new Float2(0.25f, 0.75f), 0.01f));
        Assert.Empty(data.Trees);
    }

    [Fact]
    public void RemovingATreePrototypeRemapsTheTreesAfterIt()
    {
        using TerrainData data = Small();
        data.TreePrototypes.AddRange([new TreePrototype(), new TreePrototype(), new TreePrototype()]);
        data.SetTrees([new TreeInstance { PrototypeIndex = 0 }, new TreeInstance { PrototypeIndex = 1 }, new TreeInstance { PrototypeIndex = 2 }]);

        data.RemoveTreePrototype(1);

        Assert.Equal(2, data.TreePrototypes.Count);
        Assert.Equal(new[] { 0, 1 }, data.Trees.Select(t => t.PrototypeIndex).ToArray());
    }

    [Fact]
    public void TreesFollowTheTerrainWhenItGrows()
    {
        using TerrainData data = Small();
        data.AddTree(new TreeInstance { Position = new Float2(0.5f, 0.5f) });
        data.Size = 64f;
        Assert.Equal(0.5f, Assert.Single(data.Trees).Position.X, 4);
    }

    [Fact]
    public void EverythingSurvivesSerialization()
    {
        using TerrainData data = RampTerrain();
        data.Edit(TerrainBrush.Paint, new Float2(16, 16), 4f, 1f, layer: 2);
        data.Edit(TerrainBrush.Hole, new Float2(8, 8), 2f, 1f);
        data.Edit(TerrainBrush.Detail, new Float2(24, 24), 4f, 1f);
        data.TreePrototypes.Add(new TreePrototype());
        data.AddTree(new TreeInstance { Position = new Float2(0.5f, 0.25f) });
        data.Layers[1].Tiling = 3f;
        data.DetailPrototypes[0].Density = 12f;

        using TerrainData clone = Serializer.Deserialize<TerrainData>(Serializer.Serialize(data))!;

        Assert.Equal(data.GetHeights(), clone.GetHeights());
        Assert.Equal(1f, clone.GetSplat(16, 16, 2), 2);
        Assert.True(clone.IsCellHole(7, 7));
        Assert.Equal(1f, clone.GetDetailDensity(0, 24, 24), 2);
        Assert.Equal(0.25f, Assert.Single(clone.Trees).Position.Y, 4);
        Assert.Equal(3f, clone.Layers[1].Tiling);
        Assert.Equal(12f, clone.DetailPrototypes[0].Density);
    }

    [Fact]
    public void MissingColorsFallBackToTheirDefaults()
    {
        using TerrainData data = Small();
        EchoObject echo = Serializer.Serialize(data);
        echo.Get("DetailPrototypes")!.List[0].Remove("HealthyColor");
        echo.Get("DetailPrototypes")!.List[0].Remove("DryColor");
        using TerrainData clone = Serializer.Deserialize<TerrainData>(echo)!;

        var defaults = new DetailPrototype();
        Assert.Equal(defaults.HealthyColor, clone.DetailPrototypes[0].HealthyColor);
        Assert.Equal(defaults.DryColor, clone.DetailPrototypes[0].DryColor);
    }

    // A terrain as it was saved before terrains were packages: 16 bit heights, float splats over a splat grid, holes
    // over the splat grid with 0 for a hole, detail bytes over a detail grid, and trees across the terrain
    private static EchoObject LegacyEcho()
    {
        const int res = 33, splat = 32, detail = 64;
        short[] heights = new short[res * res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                heights[z * res + x] = (short)(x / (float)(res - 1) * 32766);
        float[] splats = new float[splat * splat * 4];
        for (int i = 0; i < splat * splat; i++)
            splats[i * 4 + (i % splat < splat / 2 ? 0 : 1)] = 1f;
        byte[] holes = Enumerable.Repeat((byte)255, splat * splat).ToArray();
        holes[0] = 0;
        byte[] density = new byte[detail * detail];
        Array.Fill(density, (byte)255);

        static string Bytes<T>(T[] values) where T : struct => Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

        EchoObject echo = EchoObject.NewCompound();
        echo.Add("Name", new EchoObject("Old"));
        echo.Add("HeightmapResolution", new EchoObject(res));
        echo.Add("SplatmapResolution", new EchoObject(splat));
        echo.Add("Size", new EchoObject(32f));
        echo.Add("Height", new EchoObject(100f));
        EchoObject layers = EchoObject.NewList();
        for (int l = 0; l < 4; l++)
        {
            EchoObject layer = EchoObject.NewCompound();
            layer.Add("Tiling", new EchoObject(10f));
            layers.ListAdd(layer);
        }
        echo.Add("Layers", layers);
        echo.Add("Heights16", new EchoObject(Bytes(heights)));
        echo.Add("Splats", new EchoObject(Bytes(splats)));
        echo.Add("Holes", new EchoObject(Convert.ToBase64String(holes)));
        echo.Add("DetailResolution", new EchoObject(detail));
        EchoObject prototypes = EchoObject.NewList();
        EchoObject prototype = EchoObject.NewCompound();
        prototype.Add("RenderMode", new EchoObject(1));
        prototype.Add("BendFactor", new EchoObject(0.2f));
        prototypes.ListAdd(prototype);
        echo.Add("DetailPrototypes", prototypes);
        EchoObject densities = EchoObject.NewList();
        densities.ListAdd(new EchoObject(Convert.ToBase64String(density)));
        echo.Add("DetailLayers", densities);
        echo.Add("TreePrototypes", EchoObject.NewList());
        EchoObject trees = EchoObject.NewList();
        EchoObject tree = EchoObject.NewCompound();
        tree.Add("PosX", new EchoObject(0.5f));
        tree.Add("PosY", new EchoObject(0.25f));
        tree.Add("Proto", new EchoObject(0));
        tree.Add("Scale", new EchoObject(2f));
        tree.Add("Tint", Serializer.Serialize(default(Color)));
        trees.ListAdd(tree);
        echo.Add("Trees", trees);
        return echo;
    }

    [Fact]
    public void OldTerrainsBecomePackagesOnLoad()
    {
        using TerrainData data = Serializer.Deserialize<TerrainData>(LegacyEcho())!;

        Assert.Equal(33, data.HeightmapResolution);
        Assert.Equal(50f, data.GetInterpolatedHeight(0.5f, 0.5f), 1);
        Assert.Equal(1f, data.GetSplat(4, 16, 0), 2);
        Assert.Equal(1f, data.GetSplat(28, 16, 1), 2);
        Assert.True(data.IsCellHole(0, 0));
        Assert.False(data.IsCellHole(5, 5));
        Assert.Equal(1f, data.GetDetailDensity(0, 16, 16), 2);
        Assert.Equal(DetailRenderMode.Grass, data.DetailPrototypes[0].RenderMode);
        Assert.Equal(0.8f, data.DetailPrototypes[0].Stiffness, 4);

        TreeInstance tree = Assert.Single(data.Trees);
        Assert.Equal(0.25f, tree.Position.Y, 4);
        Assert.Equal(2f, tree.HeightScale);
        Assert.Equal(Color.White, tree.Tint);
    }

    [Fact]
    public void ThePackageIsBuiltOnceWhenManyThreadsAskAtOnce()
    {
        using TerrainData data = Small();
        Prowl.Graphite.Rendering.TerrainData[] seen = new Prowl.Graphite.Rendering.TerrainData[16];
        System.Threading.Tasks.Parallel.For(0, seen.Length, i => seen[i] = data.Terrain);
        Assert.All(seen, terrain => Assert.Same(seen[0], terrain));
    }

    [Fact]
    public void SavingAnUnchangedTerrainKeepsItsBytes_AndAnEditedOneWritesNewOnes()
    {
        using TerrainData data = Serializer.Deserialize<TerrainData>(Serializer.Serialize(RampTerrain()))!;
        byte[] loaded = Serializer.Serialize(data).Get("Package")!.ByteArrayValue;
        Assert.Same(loaded, Serializer.Serialize(data).Get("Package")!.ByteArrayValue);

        data.Edit(TerrainBrush.Raise, new Float2(16, 16), 4f, 5f);
        using TerrainData clone = Serializer.Deserialize<TerrainData>(Serializer.Serialize(data))!;
        Assert.Equal(data.GetHeight(16, 16), clone.GetHeight(16, 16));
    }

    [Fact]
    public void RestoringACaptureUndoesTheEditsSinceAndRedoesThem()
    {
        using TerrainData data = Small();
        TerrainSnapshot before = data.Capture();
        data.Edit(TerrainBrush.Raise, new Float2(16, 16), 4f, 5f);
        data.AddTree(new TreeInstance { Position = new Float2(0.5f, 0.5f) });
        TerrainSnapshot after = data.Capture();

        Assert.True(data.Restore(before));
        Assert.Equal(0f, data.GetHeight(16, 16));
        Assert.Empty(data.Trees);

        Assert.True(data.Restore(after));
        Assert.Equal(5f, data.GetHeight(16, 16), 2);
        Assert.Single(data.Trees);

        data.ResizeHeightmap(65);
        Assert.False(data.Restore(before));
    }

    #endregion
}

/// <summary>Terrain behaviour that needs a live scene: the collider.</summary>
public class TerrainSceneTests : RuntimeTestBase
{
    private TerrainComponent AddTerrain(Scene scene, TerrainData data, Float3 position)
    {
        GameObject go = CreateGameObject("Terrain");
        scene.Add(go);
        go.Transform.Position = position;
        TerrainComponent terrain = go.AddComponent<TerrainComponent>();
        terrain.Data = data;
        return terrain;
    }

    [Fact]
    public void OverlapQueries_ReportTerrain()
    {
        Scene scene = CreateScene(enable: true);
        var data = new TerrainData { Size = 64f, Height = 10f };
        data.ResizeHeightmap(33);
        TerrainComponent terrain = AddTerrain(scene, data, Float3.Zero);
        terrain.GameObject.AddComponent<TerrainCollider>();
        Update(scene);

        Assert.True(scene.Physics.CheckSphere(new Float3(32, 0, 32), 1f));
        Assert.False(scene.Physics.CheckSphere(new Float3(32, 5, 32), 1f));

        var hits = new List<ShapeCastHit>();
        Assert.Equal(1, scene.Physics.OverlapBox(new Float3(32, 0, 32), new Float3(2, 2, 2), Quaternion.Identity, hits));
        Assert.Same(terrain.GameObject.Transform, hits[0].Transform);

        Assert.Equal(1, scene.Physics.OverlapSphere(new Float3(31.3f, 0.3f, 32.6f), 0.5f, hits));
        Assert.True(hits[0].Normal.Y > 0.99f, $"normal {hits[0].Normal}");
        Assert.Equal(0.2, hits[0].Penetration, 2);
    }

    private sealed class TerrainContactRecorder : Component
    {
        public readonly List<Collision> Begins = [];
        public override void OnCollisionBegin(Collision collision) => Begins.Add(collision);
    }

    [Fact]
    public void Collision_AgainstTerrain_NamesTheTerrainOnBothSides()
    {
        Scene scene = CreateScene(enable: true);
        scene.Physics.UseMultithreading = false;
        var data = new TerrainData { Size = 64f, Height = 10f };
        data.ResizeHeightmap(33);
        TerrainComponent terrain = AddTerrain(scene, data, Float3.Zero);
        terrain.GameObject.AddComponent<TerrainCollider>();
        TerrainContactRecorder terrainRecorder = terrain.GameObject.AddComponent<TerrainContactRecorder>();
        Update(scene);

        GameObject box = CreateGameObject("Box");
        box.Transform.Position = new Float3(32, 1, 32);
        box.AddComponent<Rigidbody3D>();
        box.AddComponent<BoxCollider>();
        TerrainContactRecorder boxRecorder = box.AddComponent<TerrainContactRecorder>();
        scene.Add(box);

        Tick(scene, 120);

        Collision hit = Assert.Single(boxRecorder.Begins);
        Assert.Same(terrain.GameObject, hit.GameObject);
        Assert.True(hit.Normal.Y > 0.99f, $"normal {hit.Normal}");
        Assert.Same(box, Assert.Single(terrainRecorder.Begins).GameObject);
    }

    [Fact]
    public void ColliderFollowsTheDataWhenItChanges()
    {
        Scene scene = CreateScene(enable: true);
        var data = new TerrainData { Size = 64f, Height = 10f };
        data.ResizeHeightmap(33);
        TerrainComponent terrain = AddTerrain(scene, data, Float3.Zero);
        TerrainCollider collider = terrain.GameObject.AddComponent<TerrainCollider>();
        Update(scene);
        Assert.Equal(2f, collider.CellSize, 4);

        data.Size = 128f;
        Update(scene);
        Assert.Equal(4f, collider.CellSize, 4);
        Assert.True(collider.WorldBounds.Max.X >= 128f - 0.01f);

        data.Height = 50f;
        Update(scene);
        Assert.True(collider.WorldBounds.Max.Y >= 50f - 0.01f);

        data.ResizeHeightmap(65);
        Update(scene);
        Assert.Equal(2f, collider.CellSize, 4);
        Assert.Equal(65, collider.Width);
    }

    [Fact]
    public void ColliderFollowsASculptedBump()
    {
        Scene scene = CreateScene(enable: true);
        var data = new TerrainData { Size = 64f, Height = 10f };
        data.ResizeHeightmap(33);
        TerrainComponent terrain = AddTerrain(scene, data, new Float3(10, 2, 10));
        TerrainCollider collider = terrain.GameObject.AddComponent<TerrainCollider>();
        Update(scene);
        Assert.Equal(2f, collider.GetWorldHeight(42, 42), 3);

        data.Edit(TerrainBrush.Raise, new Float2(32, 32), 6f, 4f);
        Update(scene);
        Assert.Equal(6f, collider.GetWorldHeight(42, 42), 2);
        Assert.True(scene.Physics.CheckSphere(new Float3(42, 5.5f, 42), 1f));
    }
}
