// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

    /// <summary>
    /// Creates a new terrain collision filter.
    /// </summary>
    /// <param name="world">The Jitter2 physics world.</param>
    /// <param name="heightmapProxy">The heightmap proxy for raycasting.</param>
    /// <param name="heightProvider">The height data provider, also the source of the live grid placement.</param>
    public TerrainCollisionFilter(World world, TerrainHeightmapProxy heightmapProxy, ITerrainHeightProvider heightProvider)
    {
        _world = world;
        _heightmapProxy = heightmapProxy;
        _heightProvider = heightProvider;

        // Reserve unique IDs for all terrain triangles. Each grid cell has 2 triangles, and a terrain
        // whose data has not sized itself yet would ask for zero, which Jitter rejects outright.
        int totalTriangles = Maths.Max(1, _heightProvider.Width * _heightProvider.Height * 2);
        (_minTriangleIndex, _) = World.RequestId(totalTriangles);
    }

    /// <summary>
    /// Filters collision between two proxies.
    /// Returns false if this is a terrain collision (handled here), true otherwise (defers to other filters).
    /// </summary>
    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        // Identify our terrain by reference. NodePtr is a mutable index the tree reassigns, and a proxy
        // that has been removed carries a sentinel, so comparing it is fragile and disagreed with the
        // reference check on the next line.
        bool aIsTerrain = ReferenceEquals(proxyA, _heightmapProxy);
        bool bIsTerrain = ReferenceEquals(proxyB, _heightmapProxy);
        if (!aIsTerrain && !bIsTerrain)
            return true; // Not a terrain collision, let other filters handle it

        IDynamicTreeProxy collider = aIsTerrain ? proxyB : proxyA;

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
    /// Registers a contact against one terrain triangle. A degenerate cell (repeated corners, or a
    /// terrain whose data has not sized itself yet) has a zero-area cross product, and normalising that
    /// yields NaN rather than zero - feeding it to the solver poisons every body it touches.
    /// </summary>
    private void RegisterTriangleContact(RigidBodyShape rbs, ref RigidBodyData body, in CollisionTriangle triangle, ulong triangleIndex)
    {
        JVector normal = JVector.NormalizeSafe((triangle.B - triangle.A) % (triangle.C - triangle.A));
        if (normal.LengthSquared() <= 0.0f) return;

        if (NarrowPhase.MprEpa(triangle, rbs, body.Orientation, body.Position,
                out JVector pointA, out JVector pointB, out _, out _))
        {
            _world.RegisterContact(rbs.ShapeId, triangleIndex, _world.NullBody, rbs.RigidBody,
                pointA, pointB, normal);
        }
    }

    /// <summary>
    /// Processes collision between a rigidbody shape and the terrain.
    /// <para/>
    /// Runs on Jitter's broad-phase worker threads, and reads the heightmap and hole mask straight out
    /// of the TerrainData. Sculpting is main-thread and happens between steps, so the two never overlap;
    /// editing terrain from inside a <see cref="PhysicsWorld.PreStep"/> subscriber would be the one way
    /// to race this, and is not something to do. The grid placement is not read here at all: the
    /// TerrainCollider caches it on the main thread precisely so this path does not touch a Transform.
    /// </summary>
    private void ProcessTerrainCollision(RigidBodyShape rbs)
    {
        ref RigidBodyData body = ref rbs.RigidBody.Data;

        JVector terrainOrigin = _heightProvider.Origin;
        float cellSize = _heightProvider.CellSize;
        if (cellSize <= 0.0f) return;

        var min = rbs.WorldBoundingBox.Min;
        var max = rbs.WorldBoundingBox.Max;

        // Convert world space bounds to grid space
        int minX = Maths.Max(0, (int)Maths.Floor((min.X - terrainOrigin.X) / cellSize));
        int minZ = Maths.Max(0, (int)Maths.Floor((min.Z - terrainOrigin.Z) / cellSize));
        int maxX = Maths.Min(_heightProvider.Width - 1, (int)Maths.Ceiling((max.X - terrainOrigin.X) / cellSize));
        int maxZ = Maths.Min(_heightProvider.Height - 1, (int)Maths.Ceiling((max.Z - terrainOrigin.Z) / cellSize));

        // Test each potentially colliding grid cell
        for (int x = minX; x < maxX; x++)
        {
            for (int z = minZ; z < maxZ; z++)
            {
                // Skip invalid cells and holes
                if (!_heightProvider.IsValidCell(x, z))
                    continue;
                if (_heightProvider.IsCellHole(x, z))
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
                triangle.A = new JVector((x + 0) * cellSize + terrainOrigin.X, h00, (z + 0) * cellSize + terrainOrigin.Z);
                triangle.B = new JVector((x + 1) * cellSize + terrainOrigin.X, h11, (z + 1) * cellSize + terrainOrigin.Z);
                triangle.C = new JVector((x + 1) * cellSize + terrainOrigin.X, h10, (z + 0) * cellSize + terrainOrigin.Z);

                RegisterTriangleContact(rbs, ref body, triangle, triangleIndex);

                // Test second triangle of the quad (a-d-c)
                triangle.A = new JVector((x + 0) * cellSize + terrainOrigin.X, h00, (z + 0) * cellSize + terrainOrigin.Z);
                triangle.B = new JVector((x + 0) * cellSize + terrainOrigin.X, h01, (z + 1) * cellSize + terrainOrigin.Z);
                triangle.C = new JVector((x + 1) * cellSize + terrainOrigin.X, h11, (z + 1) * cellSize + terrainOrigin.Z);

                RegisterTriangleContact(rbs, ref body, triangle, triangleIndex + 1);
            }
        }
    }
}
