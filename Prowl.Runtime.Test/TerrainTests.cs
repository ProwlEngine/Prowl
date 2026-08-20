// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Every terrain test lives here: physics raycasts, the coordinate conventions the maps share, and
// the cascade math behind procedural details.

using System.Linq;
using System.Text.RegularExpressions;

using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Every terrain test: the physics raycast, the coordinate conventions the height, splat and detail
/// maps share, and the cascade math behind procedural details.
/// </summary>
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

    // A ray that clips the terrain's bounds and leaves used to keep stepping to the distance limit,
    // thousands of cells past the edge of the grid.
    [Fact]
    public void RayLeavingTheGrid_TerminatesInsteadOfWalkingToTheDistanceLimit()
    {
        var proxy = new TerrainHeightmapProxy(new FlatTerrain(2.0f));

        // Above the terrain plane and heading away, so it never hits and exits the grid immediately.
        Assert.False(proxy.RayCast(new JVector(7.5f, 10.0f, 7.5f), Float3.Normalize(new Float3(1, 1, 1)) is var d
            ? new JVector(d.X, d.Y, d.Z) : default, out _, out _));
    }

    #endregion

    #region Coordinate conventions shared by the maps

    // Guards the coordinate conventions the terrain maps share. Heights are a vertex grid, splats and
    // details are cell grids, and every consumer (surface shader, grass placement, physics holes) has
    // to agree on where a given index lands in UV or the maps drift apart on screen.
    private const int kRes = 33;

    /// <summary>Terrain whose normalized height equals U everywhere, so height at UV is analytically known.</summary>
    private static TerrainData RampTerrain()
    {
        var data = new TerrainData { Size = 32f, Height = 100f };
        data.ResizeHeightmap(kRes);
        for (int z = 0; z < kRes; z++)
            for (int x = 0; x < kRes; x++)
                data.SetHeight(x, z, x / (float)(kRes - 1));
        return data;
    }

    [Theory]
    [InlineData(TerrainInterpolation.Bilinear)]
    [InlineData(TerrainInterpolation.Bicubic)]
    public void InterpolatedHeightFollowsTheVertexGrid(TerrainInterpolation mode)
    {
        using var data = RampTerrain();
        data.Interpolation = mode;

        // Both filters reproduce a linear ramp exactly, so any drift here is a convention shift.
        for (float u = 0.1f; u <= 0.9f; u += 0.05f)
            Assert.Equal(u * data.Height, data.GetInterpolatedHeight(u, 0.5f), 1);
    }

    [Fact]
    public void HeightSamplesLandOnTheirOwnUV()
    {
        using var data = RampTerrain();
        for (int x = 1; x < kRes - 1; x++)
        {
            Float2 uv = data.HeightmapToUV(x, 4);
            Assert.Equal(data.GetHeight(x, 4) * data.Height, data.GetInterpolatedHeight(uv.X, uv.Y), 1);
        }
    }

    [Fact]
    public void DetailCellsSitInsideTheSplatTexelTheyOverlap()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(data.SplatmapResolution * 2);

        // Two detail cells per splat texel, both must resolve to that same texel.
        for (int k = 0; k < data.SplatmapResolution; k += 37)
        {
            for (int sub = 0; sub < 2; sub++)
            {
                Float2 uv = data.DetailToUV(k * 2 + sub, 0);
                Assert.Equal(k, (int)(uv.X * data.SplatmapResolution));
            }
        }
    }

    [Fact]
    public void SplatTexelUVResolvesBackToItself()
    {
        using var data = new TerrainData();
        for (int x = 0; x < data.SplatmapResolution; x += 53)
        {
            Float2 uv = data.SplatmapToUV(x, x);
            Assert.Equal(x, (int)(uv.X * data.SplatmapResolution));
            Assert.Equal(x, (int)(uv.Y * data.SplatmapResolution));
        }
    }

    [Fact]
    public void HoleLookupHitsTheSplatTexelUnderTheHeightCell()
    {
        using var data = new TerrainData();
        int last = data.SplatmapResolution - 1;

        data.SetHole(0, 0, 0);
        data.SetHole(last, last, 0);

        Assert.True(data.IsCellHole(0, 0));
        Assert.True(data.IsCellHole(last, last));
        Assert.False(data.IsCellHole(1, 1));
        Assert.False(data.IsCellHole(last - 1, last - 1));
    }

    [Fact]
    public void DetailEditsBumpTheVersionRenderersWatch()
    {
        using var data = new TerrainData();
        int before = data.DetailsVersion;

        data.SetDetailDensity(0, 4, 4, 1f);

        Assert.NotEqual(before, data.DetailsVersion);
    }

    [Fact]
    public void HeightEditsBumpTheVersionRenderersWatch()
    {
        using var data = new TerrainData();
        int before = data.HeightsVersion;

        data.SetHeight(4, 4, 0.5f);

        Assert.NotEqual(before, data.HeightsVersion);
    }

    #endregion

    #region Procedural detail scatter

    // Covers the cascade math behind procedural details. Placement itself runs in the vertex shader,
    // but the guarantees that keep it stable are decided here: a grid that does not slide under the
    // camera, cascades that are strict subsets of each other, and bands that tile the draw distance.

    private const float kDensity = 2f;   // cells per metre, so half-metre cells
    private const float kDistance = 150f;
    private const int kCascades = 4;

    [Fact]
    public void OneCellPerMetreIsOneMetreCells()
    {
        // The density knob is cells per metre: 1 puts a blade in every square metre.
        Assert.Equal(1f, TerrainDetailRenderer.CascadeCellSize(1f, 0), 4);
        Assert.Equal(0.5f, TerrainDetailRenderer.CascadeCellSize(2f, 0), 4);
    }

    [Fact]
    public void EachCascadeDoublesItsCellSize()
    {
        float near = TerrainDetailRenderer.CascadeCellSize(kDensity, 0);

        Assert.Equal(near * 2f, TerrainDetailRenderer.CascadeCellSize(kDensity, 1), 4);
        Assert.Equal(near * 4f, TerrainDetailRenderer.CascadeCellSize(kDensity, 2), 4);
        Assert.Equal(near * 8f, TerrainDetailRenderer.CascadeCellSize(kDensity, 3), 4);
    }

    [Fact]
    public void BandsSplitTheDistanceEvenlyAndMeet()
    {
        float previousOuter = 0f;
        for (int cascade = 0; cascade < kCascades; cascade++)
        {
            TerrainDetailRenderer.CascadeBand(kDistance, kCascades, cascade, out float inner, out float outer);

            Assert.Equal(previousOuter, inner, 4);
            Assert.Equal(kDistance / kCascades, outer - inner, 4);
            previousOuter = outer;
        }

        Assert.Equal(kDistance, previousOuter, 4);
    }

    [Fact]
    public void OneCascadeCoversTheWholeDistance()
    {
        TerrainDetailRenderer.CascadeBand(kDistance, 1, 0, out float inner, out float outer);

        Assert.Equal(0f, inner);
        Assert.Equal(kDistance, outer, 4);
    }

    [Fact]
    public void CascadeGridDoesNotSlideWithTheCamera()
    {
        // Any camera inside one cell must resolve to the same origin, or the whole field crawls.
        int cells = TerrainDetailRenderer.CascadeCellsPerSide(kDensity, kDistance, kCascades, 0);
        Int2 a = TerrainDetailRenderer.CascadeOriginCell(10.0f, 10.0f, kDensity, 0, cells);
        Int2 b = TerrainDetailRenderer.CascadeOriginCell(10.4f, 10.4f, kDensity, 0, cells);

        Assert.Equal(a.X, b.X);
        Assert.Equal(a.Y, b.Y);
    }

    [Fact]
    public void CascadeOriginTracksTheCameraOneCellAtATime()
    {
        int cells = TerrainDetailRenderer.CascadeCellsPerSide(kDensity, kDistance, kCascades, 0);
        Int2 a = TerrainDetailRenderer.CascadeOriginCell(10.0f, 0f, kDensity, 0, cells);
        Int2 b = TerrainDetailRenderer.CascadeOriginCell(10.6f, 0f, kDensity, 0, cells);

        Assert.Equal(a.X + 1, b.X);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CoarseCascadesLandOnFineCells(int cascade)
    {
        // Cascade N samples every 2^N-th fine cell, so its origin has to be a multiple of that
        // stride for its blades to coincide with cascade 0's. Otherwise LOD would move blades
        // instead of thinning them.
        int stride = 1 << cascade;
        int cells = TerrainDetailRenderer.CascadeCellsPerSide(kDensity, kDistance, kCascades, cascade);

        for (float camera = -37.3f; camera < 37.3f; camera += 1.7f)
        {
            Int2 origin = TerrainDetailRenderer.CascadeOriginCell(camera, camera * 0.5f, kDensity, cascade, cells);

            Assert.Equal(0, origin.X % stride);
            Assert.Equal(0, origin.Y % stride);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CascadeSquareCoversItsBand(int cascade)
    {
        // The square of instances snaps to the cascade's grid while the band is a circle centred on
        // the camera, so the square has to hold the circle at the worst snapping offset, plus room
        // for blades that jitter in from just outside. Falling short leaves a sliver of missing
        // grass that slides around as the camera moves.
        TerrainDetailRenderer.CascadeBand(kDistance, kCascades, cascade, out _, out float outer);
        float cell = TerrainDetailRenderer.CascadeCellSize(kDensity, cascade);
        float maxWander = (1 << 3) * 0.5f * TerrainDetailRenderer.CascadeCellSize(kDensity, 0);

        int cells = TerrainDetailRenderer.CascadeCellsPerSide(kDensity, kDistance, kCascades, cascade);
        float squareHalfWidth = cells * 0.5f * cell;

        Assert.True(squareHalfWidth >= outer + cell + maxWander,
            $"cascade {cascade}: {squareHalfWidth} does not cover {outer + cell + maxWander}");
    }

    [Fact]
    public void CascadeCellCountIsEven()
    {
        // An odd square has no centre cell for the camera to sit on
        for (int cascade = 0; cascade < kCascades; cascade++)
            Assert.Equal(0, TerrainDetailRenderer.CascadeCellsPerSide(kDensity, kDistance, kCascades, cascade) % 2);
    }

    [Fact]
    public void AbsurdDensityStaysAffordable()
    {
        // Density and distance multiply out into instance counts, so the square is capped rather
        // than letting one slider allocate an unbounded draw.
        int cells = TerrainDetailRenderer.CascadeCellsPerSide(500f, 2000f, 1, 0);

        Assert.True(cells <= 512, $"{cells} cells per side is {cells * cells} instances");
    }

    [Fact]
    public void FartherCascadesDrawFewerBlades()
    {
        // Same painted density, four times the cell area, so a quarter of the blades per band
        float near = TerrainDetailRenderer.CascadeCellSize(kDensity, 0);
        float far = TerrainDetailRenderer.CascadeCellSize(kDensity, 1);

        Assert.Equal(4f, (far * far) / (near * near), 4);
    }

    [Fact]
    public void DetailDensitiesPackIntoRGBATextures()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(32);
        data.DetailPrototypes.Clear();
        for (int i = 0; i < 5; i++)
            data.AddDetailPrototype(new DetailPrototype());

        var textures = data.GetDetailTextures();

        Assert.Equal(2, textures.Count); // 4 prototypes per texture, so 5 needs two
        Assert.Equal(32u, textures[0].Width);
    }

    [Fact]
    public void DetailTexturesRebuildWhenDensitiesChange()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(32);
        var first = data.GetDetailTextures()[0];

        data.SetDetailDensity(0, 4, 4, 1f);
        var second = data.GetDetailTextures()[0];

        Assert.NotSame(first, second);
    }

    [Fact]
    public void ResizingDetailMapsKeepsWhatWasPainted()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(64);
        for (int z = 0; z < 64; z++)
            for (int x = 0; x < 32; x++)
                data.SetDetailDensity(0, x, z, 1f); // left half solid, right half empty

        data.ResizeDetailMaps(128);

        Assert.Equal(128, data.DetailResolution);
        Assert.Equal(1f, data.GetDetailDensity(0, 20, 64), 2);   // deep in the painted half
        Assert.Equal(0f, data.GetDetailDensity(0, 100, 64), 2);  // deep in the empty half
    }

    [Fact]
    public void ResizingDetailMapsDownKeepsWhatWasPainted()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(128);
        for (int z = 0; z < 128; z++)
            for (int x = 0; x < 64; x++)
                data.SetDetailDensity(0, x, z, 1f);

        data.ResizeDetailMaps(64);

        Assert.Equal(1f, data.GetDetailDensity(0, 10, 32), 2);
        Assert.Equal(0f, data.GetDetailDensity(0, 50, 32), 2);
    }

    [Fact]
    public void ResizingDetailMapsCanStartClean()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(64);
        data.SetDetailDensity(0, 10, 10, 1f);

        data.ResizeDetailMaps(64, resample: false);

        Assert.Equal(0f, data.GetDetailDensity(0, 10, 10));
    }

    [Fact]
    public void ResizingDetailMapsRebuildsTheTexture()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(64);
        var before = data.GetDetailTextures()[0];

        data.ResizeDetailMaps(128);
        var after = data.GetDetailTextures()[0];

        Assert.NotSame(before, after);
        Assert.Equal(128u, after.Width);
    }

    [Fact]
    public void DensityRoundTripsThroughByteStorage()
    {
        // Densities are stored as bytes, so a value survives to within half a step of 1/255
        using var data = new TerrainData();
        data.ResizeDetailMaps(32);

        foreach (float density in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            data.SetDetailDensity(0, 4, 4, density);
            Assert.Equal(density, data.GetDetailDensity(0, 4, 4), 2);
        }
    }

    [Fact]
    public void FullDensityIsExactlyOne()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(32);
        data.SetDetailDensity(0, 1, 1, 1f);

        Assert.Equal(1f, data.GetDetailDensity(0, 1, 1));
    }

    [Fact]
    public void DetailDensitySurvivesSerialization()
    {
        using var data = new TerrainData();
        data.ResizeDetailMaps(64);
        data.SetDetailDensity(0, 10, 20, 1f);
        data.SetDetailDensity(0, 11, 20, 0.5f);

        using var clone = Serializer.Deserialize<TerrainData>(Serializer.Serialize(data));

        Assert.Equal(64, clone.DetailResolution);
        Assert.Equal(1f, clone.GetDetailDensity(0, 10, 20), 2);
        Assert.Equal(0.5f, clone.GetDetailDensity(0, 11, 20), 2);
        Assert.Equal(0f, clone.GetDetailDensity(0, 12, 20));
    }

    [Fact]
    public void MismatchedLayerDataIsDiscarded()
    {
        // A layer whose length does not match the resolution is not this asset's data, and reading
        // it anyway would scatter grass from whatever the bytes happened to be.
        using var data = new TerrainData();
        data.ResizeDetailMaps(64);
        data.SetDetailDensity(0, 5, 5, 1f);

        var echo = Serializer.Serialize(data);
        echo.Get("DetailResolution")!.Value = 32;

        using var clone = Serializer.Deserialize<TerrainData>(echo);

        Assert.Equal(32, clone.DetailResolution);
        Assert.Equal(32 * 32, clone.DetailLayers[0].Length);
    }

    [Fact]
    public void UnpaintedPrototypesHaveNoBounds()
    {
        // A prototype nobody painted should cost nothing, and an empty rect is how the renderer
        // knows to skip it. Every prototype otherwise sweeps its full cascades regardless.
        using var data = new TerrainData();
        data.ResizeDetailMaps(64);

        Assert.False(data.TryGetDetailBounds(0, out _));
    }

    [Fact]
    public void PaintedBoundsFollowTerrainSize()
    {
        // Bounds are kept in UV, because Size can change without touching the detail version and
        // baked world bounds would then point at the wrong part of the terrain.
        using var data = new TerrainData { Size = 640f };
        data.ResizeDetailMaps(64);
        data.SetDetailDensity(0, 10, 20, 1f);
        data.TryGetDetailBounds(0, out Float4 before);

        data.Size = 1280f;
        data.TryGetDetailBounds(0, out Float4 after);

        Assert.Equal(before.X * 2f, after.X, 2);
        Assert.Equal(before.W * 2f, after.W, 2);
    }

    [Fact]
    public void EmptyLayersNeverReportAPatchAtTheOrigin()
    {
        // A layer the pack skips must read as unpainted. Left at its default it would be a
        // zero-area rect at the terrain origin, so grass would come and go with the camera.
        using var data = new TerrainData();
        data.ResizeDetailMaps(32);
        data.AddDetailPrototype(new DetailPrototype());
        data.DetailLayers[1] = [];  // wrong length for the resolution

        Assert.False(data.TryGetDetailBounds(0, out _));
        Assert.False(data.TryGetDetailBounds(1, out _));
    }

    [Fact]
    public void PaintedBoundsCoverThePaintedCells()
    {
        using var data = new TerrainData { Size = 640f };
        data.ResizeDetailMaps(64);        // 10 world units per cell
        data.SetDetailDensity(0, 10, 20, 1f);
        data.SetDetailDensity(0, 12, 24, 1f);

        Assert.True(data.TryGetDetailBounds(0, out Float4 rect));
        Assert.Equal(100f, rect.X, 2);    // cell 10 starts at 100
        Assert.Equal(200f, rect.Y, 2);    // cell 20 starts at 200
        Assert.Equal(130f, rect.Z, 2);    // cell 12 ends at 130
        Assert.Equal(250f, rect.W, 2);    // cell 24 ends at 250
    }

    [Fact]
    public void PaintedBoundsFollowFurtherPainting()
    {
        using var data = new TerrainData { Size = 640f };
        data.ResizeDetailMaps(64);
        data.SetDetailDensity(0, 10, 10, 1f);
        data.TryGetDetailBounds(0, out Float4 before);

        data.SetDetailDensity(0, 30, 30, 1f);
        data.TryGetDetailBounds(0, out Float4 after);

        Assert.True(after.Z > before.Z);
        Assert.True(after.W > before.W);
    }

    [Fact]
    public void CascadeSkipsBandsThatMissThePaintedArea()
    {
        var painted = new Float4(0f, 0f, 10f, 10f);

        // Camera on the patch: the near band has work, a far band has already passed it
        Assert.True(TerrainDetailRenderer.CascadeTouchesBounds(new Float2(5f, 5f), 0f, 20f, painted, 0f));
        Assert.False(TerrainDetailRenderer.CascadeTouchesBounds(new Float2(5f, 5f), 100f, 200f, painted, 0f));

        // Camera far away: every band is out of reach
        Assert.False(TerrainDetailRenderer.CascadeTouchesBounds(new Float2(500f, 500f), 0f, 20f, painted, 0f));

        // Camera far away but the band reaches back to the patch
        Assert.True(TerrainDetailRenderer.CascadeTouchesBounds(new Float2(500f, 5f), 400f, 600f, painted, 0f));
    }

    [Fact]
    public void CascadeBoundsTestAllowsForBladesThatWander()
    {
        var painted = new Float4(0f, 0f, 10f, 10f);

        // Just out of reach of the rect, but a blade that wandered out is still visible
        Assert.False(TerrainDetailRenderer.CascadeTouchesBounds(new Float2(16f, 5f), 0f, 5f, painted, 0f));
        Assert.True(TerrainDetailRenderer.CascadeTouchesBounds(new Float2(16f, 5f), 0f, 5f, painted, 2f));
    }

    [Fact]
    public void GrassShaderResolvesTheScatterInclude()
    {
        // A missing include is not a parse failure, it silently pastes nothing, so check that the
        // placement code actually made it into the vertex stage.
        string source = Shader.LoadDefault(DefaultShader.Grass).Passes.First().VertexSource;

        Assert.Contains("scatterResolve", source);
    }

    [Fact]
    public void EveryScatterUniformIsOneTheRendererWrites()
    {
        // Tripwire: a uniform added to TerrainScatter.glsl that TerrainDetailRenderer never sets
        // reads as zero and quietly breaks placement. Add it to the renderer, then to this list.
        string[] expected =
        [
            "_ScatterOriginCell", "_ScatterCellSize", "_ScatterCellsPerSide", "_ScatterLevel",
            "_ScatterMaxLevel", "_ScatterCentre", "_ScatterRadii",
        ];

        string source = Shader.LoadDefault(DefaultShader.Grass).Passes.First().VertexSource;
        var declared = Regex.Matches(source, @"uniform\s+\w+\s+(_Scatter\w+)\s*;")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(expected.OrderBy(n => n).ToArray(), declared);
    }

    #endregion

    #region Mesh detail scatter

    // Mesh prototypes render with user materials, so they cannot be placed in the shader. They get
    // one camera-relative instance buffer instead of one per patch, rebuilt only once the camera
    // has spent the slack the buffer was built with.

    [Fact]
    public void MeshBuildSurvivesSmallCameraMoves()
    {
        var origin = new Float2(100f, 100f);

        Assert.False(TerrainMeshDetailRenderer.BuildIsStale(origin, 150f, origin, 150f));
        Assert.False(TerrainMeshDetailRenderer.BuildIsStale(origin, 150f, new Float2(110f, 100f), 150f));
    }

    [Fact]
    public void MeshBuildGoesStaleOnceTheSlackIsSpent()
    {
        var origin = new Float2(100f, 100f);
        float slack = TerrainMeshDetailRenderer.BuildRadius(150f) - 150f;

        Assert.True(TerrainMeshDetailRenderer.BuildIsStale(origin, 150f, new Float2(100f + slack * 1.1f, 100f), 150f));
    }

    [Fact]
    public void MeshBuildGoesStaleWhenTheDistanceChanges()
    {
        var origin = new Float2(100f, 100f);

        Assert.True(TerrainMeshDetailRenderer.BuildIsStale(origin, 150f, origin, 300f));
    }

    [Fact]
    public void MeshBuildReachesPastTheDrawDistance()
    {
        // The slack is what stops instances popping in: anything inside the draw distance of the
        // new camera position was already inside the radius the previous build covered.
        float distance = 150f;
        float radius = TerrainMeshDetailRenderer.BuildRadius(distance);
        float slack = radius - distance;

        Assert.True(radius > distance);

        // Worst case move that still counts as fresh, from the old centre
        float worstMove = slack;
        Assert.True(radius - worstMove >= distance);
    }

    #endregion
}
