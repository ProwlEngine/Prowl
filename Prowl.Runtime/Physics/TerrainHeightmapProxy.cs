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
    private readonly JBoundingBox _worldBoundingBox;
    private readonly JVector _terrainOrigin;
    private readonly float _cellSize;

    public int SetIndex { get; set; } = -1;
    public int NodePtr { get; set; }

    public JVector Velocity => JVector.Zero;
    public JBoundingBox WorldBoundingBox => _worldBoundingBox;

    /// <summary>
    /// Creates a new terrain heightmap proxy.
    /// </summary>
    /// <param name="heightProvider">Provider for heightmap data.</param>
    /// <param name="boundingBox">World-space bounding box of the terrain.</param>
    /// <param name="terrainOrigin">World-space origin (bottom-left corner) of the terrain.</param>
    /// <param name="cellSize">World-space size of each heightmap cell.</param>
    public TerrainHeightmapProxy(ITerrainHeightProvider heightProvider, JBoundingBox boundingBox, JVector terrainOrigin, float cellSize)
    {
        _heightProvider = heightProvider;
        _worldBoundingBox = boundingBox;
        _terrainOrigin = terrainOrigin;
        _cellSize = cellSize;
    }

    /// <summary>
    /// Performs a raycast against the terrain heightmap using grid traversal.
    /// Based on Jitter Physics 2 Demo 25 heightmap raycasting implementation.
    /// </summary>
    public bool RayCast(in JVector origin, in JVector direction, out JVector normal, out float lambda)
    {
        const float maxDistance = 10000.0f;

        // Transform ray origin from world space to grid space
        JVector gridOrigin;
        gridOrigin.X = (origin.X - _terrainOrigin.X) / _cellSize;
        gridOrigin.Y = (origin.Y - _terrainOrigin.Y) / _cellSize;
        gridOrigin.Z = (origin.Z - _terrainOrigin.Z) / _cellSize;

        // Direction doesn't need position offset, only scale
        JVector gridDirection;
        gridDirection.X = direction.X / _cellSize;
        gridDirection.Y = direction.Y / _cellSize;
        gridDirection.Z = direction.Z / _cellSize;

        // Only traverse on the XZ plane
        float dirX = gridDirection.X;
        float dirZ = gridDirection.Z;

        float len2 = dirX * dirX + dirZ * dirZ;

        // Ray is vertical or nearly vertical - check the cell directly below
        if (len2 < 1e-6f)
        {
            normal = JVector.Zero;
            lambda = 0.0f;
            return false;
        }

        float ilen = 1.0f / Maths.Sqrt(len2);

        dirX *= ilen;
        dirZ *= ilen;

        int x = (int)Maths.Floor(gridOrigin.X);
        int z = (int)Maths.Floor(gridOrigin.Z);

        int stepX = dirX > 0 ? 1 : -1;
        int stepZ = dirZ > 0 ? 1 : -1;

        float nextX = dirX > 0 ? (x + 1) - gridOrigin.X : gridOrigin.X - x;
        float nextZ = dirZ > 0 ? (z + 1) - gridOrigin.Z : gridOrigin.Z - z;

        float tMaxX = dirX != 0 ? nextX / Maths.Abs(dirX) : float.PositiveInfinity;
        float tMaxZ = dirZ != 0 ? nextZ / Maths.Abs(dirZ) : float.PositiveInfinity;

        float tDeltaX = gridDirection.X != 0 ? 1f / Maths.Abs(dirX) : float.PositiveInfinity;
        float tDeltaZ = gridDirection.Z != 0 ? 1f / Maths.Abs(dirZ) : float.PositiveInfinity;

        float t = 0f;

        while (t <= maxDistance)
        {
            // Check if we are out of bounds
            if (!_heightProvider.IsValidCell(x, z))
                goto continue_walk;

            // Check this quad
            if (_heightProvider.TryGetHeight(x + 0, z + 0, out float h00) &&
                _heightProvider.TryGetHeight(x + 1, z + 0, out float h10) &&
                _heightProvider.TryGetHeight(x + 1, z + 1, out float h11) &&
                _heightProvider.TryGetHeight(x + 0, z + 1, out float h01))
            {
                // Convert grid coordinates to world coordinates
                var a = new JVector((x + 0) * _cellSize + _terrainOrigin.X, h00, (z + 0) * _cellSize + _terrainOrigin.Z);
                var b = new JVector((x + 1) * _cellSize + _terrainOrigin.X, h10, (z + 0) * _cellSize + _terrainOrigin.Z);
                var c = new JVector((x + 1) * _cellSize + _terrainOrigin.X, h11, (z + 1) * _cellSize + _terrainOrigin.Z);
                var d = new JVector((x + 0) * _cellSize + _terrainOrigin.X, h01, (z + 1) * _cellSize + _terrainOrigin.Z);

                //  a ----- b
                //  | \     |
                //  |  \    |
                //  |   \   |
                //  |    \  |
                //  d ----- c

                JTriangle tri0 = new JTriangle(a, c, b);
                JTriangle tri1 = new JTriangle(a, d, c);

                tri0.RayIntersect(origin, direction, JTriangle.CullMode.BackFacing, out JVector normal0, out float lambda0Float);
                tri1.RayIntersect(origin, direction, JTriangle.CullMode.BackFacing, out JVector normal1, out float lambda1Float);

                float lambda0 = lambda0Float;
                float lambda1 = lambda1Float;

                if (lambda0 < float.MaxValue || lambda1 < float.MaxValue)
                {
                    if (lambda0 <= lambda1)
                    {
                        normal = normal0;
                        lambda = lambda0;
                    }
                    else
                    {
                        normal = normal1;
                        lambda = lambda1;
                    }

                    return true;
                }
            }

            continue_walk:

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
}

/// <summary>
/// Interface for providing height data for terrain collision.
/// </summary>
public interface ITerrainHeightProvider
{
    /// <summary>
    /// Gets the height at the specified integer grid coordinates.
    /// </summary>
    /// <param name="x">X grid coordinate.</param>
    /// <param name="z">Z grid coordinate.</param>
    /// <param name="height">The height value at this coordinate.</param>
    /// <returns>True if the coordinates are valid and height was retrieved.</returns>
    bool TryGetHeight(int x, int z, out float height);

    /// <summary>
    /// Checks if the specified cell coordinates are within valid bounds.
    /// </summary>
    /// <param name="x">X grid coordinate.</param>
    /// <param name="z">Z grid coordinate.</param>
    /// <returns>True if the cell is valid.</returns>
    bool IsValidCell(int x, int z);

    /// <summary>Check if a cell is a hole (should skip collision). Default false.</summary>
    bool IsCellHole(int x, int z) => false;

    /// <summary>
    /// Gets the width of the heightmap in grid cells.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height (depth) of the heightmap in grid cells.
    /// </summary>
    int Height { get; }
}
