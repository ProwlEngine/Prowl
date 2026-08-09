// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision;
using Jitter2.LinearMath;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Proxy for terrain heightmap that supports raycasting against the terrain.
/// Implements IDynamicTreeProxy and IRayCastable for Jitter2 physics integration.
/// </summary>
public class TerrainHeightmapProxy : IDynamicTreeProxy, IRayCastable
{
    private readonly ITerrainHeightProvider _heightProvider;

    public int SetIndex { get; set; } = -1;
    public int NodePtr { get; set; }

    public JVector Velocity => JVector.Zero;

    /// <summary>
    /// Read live from the provider, since the terrain can be moved or scaled after registration.
    /// The dynamic tree caches this, so <see cref="PhysicsWorld.RefreshTerrain"/> must be called when
    /// it changes.
    /// </summary>
    public JBoundingBox WorldBoundingBox => _heightProvider.WorldBounds;

    /// <summary>
    /// Creates a new terrain heightmap proxy.
    /// </summary>
    /// <param name="heightProvider">Provider for heightmap data and grid placement.</param>
    public TerrainHeightmapProxy(ITerrainHeightProvider heightProvider)
    {
        _heightProvider = heightProvider;
    }

    /// <summary>
    /// Performs a raycast against the terrain heightmap using grid traversal.
    /// Based on Jitter Physics 2 Demo 25 heightmap raycasting implementation.
    /// </summary>
    public bool RayCast(in JVector origin, in JVector direction, out JVector normal, out float lambda)
    {
        normal = JVector.Zero;
        lambda = 0.0f;

        float cellSize = _heightProvider.CellSize;
        if (cellSize <= 0.0f) return false;

        // Into terrain-local space, where the grid is axis-aligned whatever the terrain's rotation.
        // The direction is transformed as a direction and deliberately NOT renormalized: leaving its
        // length alone is what keeps lambda the same parameter in both spaces, so the hit distance the
        // caller gets back needs no correction even when the terrain is scaled.
        Float4x4 worldToLocal = _heightProvider.WorldToLocal;
        Float3 localOrigin = Float4x4.TransformPoint(new Float3(origin.X, origin.Y, origin.Z), worldToLocal);
        Float3 localDir = Float4x4.TransformNormal(new Float3(direction.X, direction.Y, direction.Z), worldToLocal);

        Float3 gridOrigin = localOrigin / cellSize;

        float dirX = localDir.X;
        float dirZ = localDir.Z;
        float len2 = dirX * dirX + dirZ * dirZ;

        int x = (int)Maths.Floor(gridOrigin.X);
        int z = (int)Maths.Floor(gridOrigin.Z);

        // A ray straight down the grid's own up axis never crosses a cell boundary, so there is nothing
        // to walk: test the single cell it is above. This is the most common terrain query there is -
        // ground height under a point, a grounding check, a suspension ray - and it used to miss outright.
        if (len2 < 1e-12f)
            return RayCastCell(x, z, cellSize, localOrigin, localDir, out normal, out lambda);

        float ilen = 1.0f / Maths.Sqrt(len2);
        float stepDirX = dirX * ilen;
        float stepDirZ = dirZ * ilen;

        int stepX = stepDirX > 0 ? 1 : -1;
        int stepZ = stepDirZ > 0 ? 1 : -1;

        float nextX = stepDirX > 0 ? (x + 1) - gridOrigin.X : gridOrigin.X - x;
        float nextZ = stepDirZ > 0 ? (z + 1) - gridOrigin.Z : gridOrigin.Z - z;

        float tMaxX = stepDirX != 0 ? nextX / Maths.Abs(stepDirX) : float.PositiveInfinity;
        float tMaxZ = stepDirZ != 0 ? nextZ / Maths.Abs(stepDirZ) : float.PositiveInfinity;

        float tDeltaX = stepDirX != 0 ? 1f / Maths.Abs(stepDirX) : float.PositiveInfinity;
        float tDeltaZ = stepDirZ != 0 ? 1f / Maths.Abs(stepDirZ) : float.PositiveInfinity;

        // Bounded by the grid, not just by distance: a ray clipping the terrain's bounds can leave the
        // grid after a cell or two, and without this it would keep stepping to the distance limit.
        int width = _heightProvider.Width;
        int height = _heightProvider.Height;

        // In grid cells, since that is what t counts.
        const float maxCells = 10000.0f;
        float t = 0f;

        while (t <= maxCells)
        {
            if (HasLeftGrid(x, z, stepX, stepZ, width, height)) break;

            if (RayCastCell(x, z, cellSize, localOrigin, localDir, out normal, out lambda))
                return true;

            if (tMaxX < tMaxZ)
            {
                x += stepX;
                t = tMaxX;
                tMaxX += tDeltaX;
            }
            else
            {
                z += stepZ;
                t = tMaxZ;
                tMaxZ += tDeltaZ;
            }
        }

        normal = JVector.Zero;
        lambda = 0.0f;
        return false;
    }

    /// <summary>
    /// Whether the walk has left the grid for good: outside its bounds and stepping further away. A ray
    /// that starts outside and is heading in still has cells ahead of it, so only the receding case ends
    /// the walk.
    /// </summary>
    private static bool HasLeftGrid(int x, int z, int stepX, int stepZ, int width, int height)
    {
        if (x < 0 && stepX <= 0) return true;
        if (z < 0 && stepZ <= 0) return true;
        if (x >= width - 1 && stepX >= 0) return true;
        if (z >= height - 1 && stepZ >= 0) return true;

        return false;
    }

    /// <summary>
    /// Intersects a terrain-local ray with one cell's two triangles, nearest hit winning. The normal
    /// comes back in world space, since that is what a caller wants; lambda is already shared between
    /// the two spaces.
    /// </summary>
    private bool RayCastCell(int x, int z, float cellSize, in Float3 localOrigin, in Float3 localDir,
        out JVector normal, out float lambda)
    {
        normal = JVector.Zero;
        lambda = 0.0f;

        if (!_heightProvider.IsValidCell(x, z) || _heightProvider.IsCellHole(x, z)) return false;

        if (!_heightProvider.TryGetHeight(x + 0, z + 0, out float h00) ||
            !_heightProvider.TryGetHeight(x + 1, z + 0, out float h10) ||
            !_heightProvider.TryGetHeight(x + 1, z + 1, out float h11) ||
            !_heightProvider.TryGetHeight(x + 0, z + 1, out float h01))
            return false;

        var a = new JVector((x + 0) * cellSize, h00, (z + 0) * cellSize);
        var b = new JVector((x + 1) * cellSize, h10, (z + 0) * cellSize);
        var c = new JVector((x + 1) * cellSize, h11, (z + 1) * cellSize);
        var d = new JVector((x + 0) * cellSize, h01, (z + 1) * cellSize);

        //  a ----- b
        //  | \     |
        //  |  \    |
        //  |   \   |
        //  |    \  |
        //  d ----- c

        var rayOrigin = new JVector(localOrigin.X, localOrigin.Y, localOrigin.Z);
        var rayDir = new JVector(localDir.X, localDir.Y, localDir.Z);

        new JTriangle(a, c, b).RayIntersect(rayOrigin, rayDir, JTriangle.CullMode.BackFacing, out JVector normal0, out float lambda0);
        new JTriangle(a, d, c).RayIntersect(rayOrigin, rayDir, JTriangle.CullMode.BackFacing, out JVector normal1, out float lambda1);

        if (lambda0 >= float.MaxValue && lambda1 >= float.MaxValue) return false;

        bool first = lambda0 <= lambda1;
        JVector localNormal = first ? normal0 : normal1;
        lambda = first ? lambda0 : lambda1;

        // A normal is a direction, and under a scaled transform it needs the inverse transpose. The
        // terrain matrix has no shear, so transforming by the inverse's transpose is just this.
        Float3 worldNormal = Float4x4.TransformNormal(
            new Float3(localNormal.X, localNormal.Y, localNormal.Z),
            Float4x4.Transpose(_heightProvider.WorldToLocal));

        worldNormal = Float3.Normalize(worldNormal);
        normal = new JVector(worldNormal.X, worldNormal.Y, worldNormal.Z);
        return true;
    }
}

/// <summary>
/// Supplies the height grid and where it sits in the world.
/// <para/>
/// The grid itself is defined in terrain-local space: cell (x,z) sits at <c>(x, height, z) * CellSize</c>
/// with no rotation, scale or offset baked in. <see cref="LocalToWorld"/> carries all of that, which is
/// what lets terrain be rotated arbitrarily - queries are brought into local space, walked on the
/// axis-aligned grid there, and their results taken back out.
/// </summary>
public interface ITerrainHeightProvider
{
    /// <summary>
    /// Height at integer grid coordinates, in terrain-local units. Scale and world offset are not
    /// applied here; they come from <see cref="LocalToWorld"/>.
    /// </summary>
    /// <returns>True if the coordinates are valid and height was retrieved.</returns>
    bool TryGetHeight(int x, int z, out float height);

    /// <summary>Whether a cell exists. Cells are indexed 0..Width-2 and 0..Height-2, one per quad.</summary>
    bool IsValidCell(int x, int z);

    /// <summary>Check if a cell is a hole (should skip collision). Default false.</summary>
    bool IsCellHole(int x, int z) => false;

    /// <summary>Number of height samples along X.</summary>
    int Width { get; }

    /// <summary>Number of height samples along Z.</summary>
    int Height { get; }

    /// <summary>Spacing between height samples, in terrain-local units.</summary>
    float CellSize { get; }

    /// <summary>
    /// Terrain-local to world, carrying position, rotation and scale. Read per query, so the terrain can
    /// be moved, turned or scaled after registration; cached on the main thread by the implementation,
    /// because the broad phase samples it from worker threads.
    /// </summary>
    Float4x4 LocalToWorld { get; }

    /// <summary>The inverse of <see cref="LocalToWorld"/>, for bringing world queries into the grid.</summary>
    Float4x4 WorldToLocal { get; }

    /// <summary>World-space bounds enclosing the terrain at its full height range, rotation included.</summary>
    JBoundingBox WorldBounds { get; }

    /// <summary>
    /// The range of cells a world-space bounding box could touch, clamped to the grid. Upper bounds are
    /// exclusive, so callers iterate <c>x &lt; maxX</c>.
    /// <para/>
    /// The box is brought into local space corner by corner and re-bounded there, because a rotated
    /// terrain turns an axis-aligned world box into an oriented one. Re-bounding is conservative, which
    /// is what a broad-phase range has to be.
    /// </summary>
    /// <returns>False when the box cannot touch the grid at all.</returns>
    bool TryGetCellRange(in JBoundingBox worldBounds, out int minX, out int minZ, out int maxX, out int maxZ)
    {
        minX = minZ = maxX = maxZ = 0;

        float cellSize = CellSize;
        if (cellSize <= 0.0f) return false;

        Float4x4 worldToLocal = WorldToLocal;
        Float3 min = new(float.MaxValue), max = new(float.MinValue);

        for (int i = 0; i < 8; i++)
        {
            var corner = new Float3(
                (i & 1) == 0 ? worldBounds.Min.X : worldBounds.Max.X,
                (i & 2) == 0 ? worldBounds.Min.Y : worldBounds.Max.Y,
                (i & 4) == 0 ? worldBounds.Min.Z : worldBounds.Max.Z);

            Float3 local = Float4x4.TransformPoint(corner, worldToLocal);
            min = Maths.Min(min, local);
            max = Maths.Max(max, local);
        }

        minX = Maths.Max(0, (int)Maths.Floor(min.X / cellSize));
        minZ = Maths.Max(0, (int)Maths.Floor(min.Z / cellSize));
        maxX = Maths.Min(Width - 1, (int)Maths.Ceiling(max.X / cellSize));
        maxZ = Maths.Min(Height - 1, (int)Maths.Ceiling(max.Z / cellSize));

        return minX < maxX && minZ < maxZ;
    }

    /// <summary>
    /// The four world-space corners of one cell, laid out as
    /// <c>a</c>=(x,z), <c>b</c>=(x+1,z), <c>c</c>=(x+1,z+1), <c>d</c>=(x,z+1).
    /// </summary>
    /// <returns>False when any corner has no height, so the cell should be skipped.</returns>
    bool TryGetWorldCorners(int x, int z, out JVector a, out JVector b, out JVector c, out JVector d)
    {
        a = b = c = d = JVector.Zero;

        if (!TryGetHeight(x + 0, z + 0, out float h00) ||
            !TryGetHeight(x + 1, z + 0, out float h10) ||
            !TryGetHeight(x + 1, z + 1, out float h11) ||
            !TryGetHeight(x + 0, z + 1, out float h01))
            return false;

        float cellSize = CellSize;
        Float4x4 localToWorld = LocalToWorld;

        a = ToWorld(new Float3((x + 0) * cellSize, h00, (z + 0) * cellSize), localToWorld);
        b = ToWorld(new Float3((x + 1) * cellSize, h10, (z + 0) * cellSize), localToWorld);
        c = ToWorld(new Float3((x + 1) * cellSize, h11, (z + 1) * cellSize), localToWorld);
        d = ToWorld(new Float3((x + 0) * cellSize, h01, (z + 1) * cellSize), localToWorld);
        return true;
    }

    private static JVector ToWorld(Float3 local, in Float4x4 localToWorld)
    {
        Float3 world = Float4x4.TransformPoint(local, localToWorld);
        return new JVector(world.X, world.Y, world.Z);
    }
}
