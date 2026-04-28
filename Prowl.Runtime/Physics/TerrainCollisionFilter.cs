// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Broad phase filter for terrain collision detection.
/// Detects collisions between dynamic objects and terrain heightmaps.
/// Based on Jitter Physics 2 Demo 25 heightmap collision implementation.
/// </summary>
public class TerrainCollisionFilter : IBroadPhaseFilter
{
    private readonly World _world;
    private readonly TerrainHeightmapProxy _heightmapProxy;
    private readonly ITerrainHeightProvider _heightProvider;
    private readonly ulong _minTriangleIndex;
    private readonly JVector _terrainOrigin;
    private readonly float _cellSize;

    /// <summary>
    /// Creates a new terrain collision filter.
    /// </summary>
    /// <param name="world">The Jitter2 physics world.</param>
    /// <param name="heightmapProxy">The heightmap proxy for raycasting.</param>
    /// <param name="heightProvider">The height data provider.</param>
    /// <param name="terrainOrigin">World-space origin of the terrain.</param>
    /// <param name="cellSize">World-space size of each heightmap cell.</param>
    public TerrainCollisionFilter(World world, TerrainHeightmapProxy heightmapProxy, ITerrainHeightProvider heightProvider, JVector terrainOrigin, float cellSize)
    {
        _world = world;
        _heightmapProxy = heightmapProxy;
        _heightProvider = heightProvider;
        _terrainOrigin = terrainOrigin;
        _cellSize = cellSize;

        // Reserve unique IDs for all terrain triangles
        // Each grid cell has 2 triangles
        int totalTriangles = _heightProvider.Width * _heightProvider.Height * 2;
        (_minTriangleIndex, _) = World.RequestId(totalTriangles);
    }

    /// <summary>
    /// Filters collision between two proxies.
    /// Returns false if this is a terrain collision (handled here), true otherwise (defers to other filters).
    /// </summary>
    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        // Check if either proxy is our terrain
        if (proxyA.NodePtr != _heightmapProxy.NodePtr && proxyB.NodePtr != _heightmapProxy.NodePtr)
            return true; // Not a terrain collision, let other filters handle it

        // Determine which proxy is the terrain and which is the collider
        var collider = proxyA == _heightmapProxy ? proxyB : proxyA;

        // Only process collisions with RigidBodyShapes
        if (collider is not RigidBodyShape rbs)
            return false;

        // Don't collide with static or inactive bodies
        var bodyData = rbs.RigidBody.Data;
        if (bodyData.MotionType != MotionType.Dynamic || !bodyData.IsActive)
            return false;

        // Process the terrain collision
        ProcessTerrainCollision(rbs);

        // Return false to indicate we've handled this collision
        return false;
    }

    /// <summary>
    /// Processes collision between a rigidbody shape and the terrain.
    /// </summary>
    private void ProcessTerrainCollision(RigidBodyShape rbs)
    {
        ref RigidBodyData body = ref rbs.RigidBody.Data;

        var min = rbs.WorldBoundingBox.Min;
        var max = rbs.WorldBoundingBox.Max;

        // Convert world space bounds to grid space
        int minX = Maths.Max(0, (int)Maths.Floor((min.X - _terrainOrigin.X) / _cellSize));
        int minZ = Maths.Max(0, (int)Maths.Floor((min.Z - _terrainOrigin.Z) / _cellSize));
        int maxX = Maths.Min(_heightProvider.Width - 1, (int)Maths.Ceiling((max.X - _terrainOrigin.X) / _cellSize));
        int maxZ = Maths.Min(_heightProvider.Height - 1, (int)Maths.Ceiling((max.Z - _terrainOrigin.Z) / _cellSize));

        // Test each potentially colliding grid cell
        for (int x = minX; x < maxX; x++)
        {
            for (int z = minZ; z < maxZ; z++)
            {
                // Skip invalid cells
                if (!_heightProvider.IsValidCell(x, z))
                    continue;

                // Get heights for this quad
                if (!_heightProvider.TryGetHeight(x + 0, z + 0, out float h00) ||
                    !_heightProvider.TryGetHeight(x + 1, z + 0, out float h10) ||
                    !_heightProvider.TryGetHeight(x + 1, z + 1, out float h11) ||
                    !_heightProvider.TryGetHeight(x + 0, z + 1, out float h01))
                {
                    continue;
                }

                // Test first triangle of the quad (a-c-b)
                ulong triangleIndex = _minTriangleIndex + (ulong)(2 * (x * _heightProvider.Height + z));

                CollisionTriangle triangle;
                // Convert grid coordinates to world coordinates
                triangle.A = new JVector((x + 0) * _cellSize + _terrainOrigin.X, h00, (z + 0) * _cellSize + _terrainOrigin.Z);
                triangle.B = new JVector((x + 1) * _cellSize + _terrainOrigin.X, h11, (z + 1) * _cellSize + _terrainOrigin.Z);
                triangle.C = new JVector((x + 1) * _cellSize + _terrainOrigin.X, h10, (z + 0) * _cellSize + _terrainOrigin.Z);

                JVector normal = JVector.Normalize((triangle.B - triangle.A) % (triangle.C - triangle.A));

                bool hit = NarrowPhase.MprEpa(triangle, rbs, body.Orientation, body.Position,
                    out JVector pointA, out JVector pointB, out _, out float penetration);

                if (hit)
                {
                    _world.RegisterContact(rbs.ShapeId, triangleIndex, _world.NullBody, rbs.RigidBody,
                        pointA, pointB, normal);
                }

                // Test second triangle of the quad (a-d-c)
                triangleIndex += 1;
                triangle.A = new JVector((x + 0) * _cellSize + _terrainOrigin.X, h00, (z + 0) * _cellSize + _terrainOrigin.Z);
                triangle.B = new JVector((x + 0) * _cellSize + _terrainOrigin.X, h01, (z + 1) * _cellSize + _terrainOrigin.Z);
                triangle.C = new JVector((x + 1) * _cellSize + _terrainOrigin.X, h11, (z + 1) * _cellSize + _terrainOrigin.Z);

                normal = JVector.Normalize((triangle.B - triangle.A) % (triangle.C - triangle.A));

                hit = NarrowPhase.MprEpa(triangle, rbs, body.Orientation, body.Position,
                    out pointA, out pointB, out _, out penetration);

                if (hit)
                {
                    _world.RegisterContact(rbs.ShapeId, triangleIndex, _world.NullBody, rbs.RigidBody,
                        pointA, pointB, normal);
                }
            }
        }
    }
}
