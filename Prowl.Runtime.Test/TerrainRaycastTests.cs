// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.LinearMath;

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests <see cref="TerrainHeightmapProxy"/>'s grid traversal directly, against a stub height provider,
/// so the heightmap raycast can be exercised without a TerrainData asset or a scene.
/// </summary>
public class TerrainRaycastTests
{
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
}
