// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class NavMeshQueryTests
{
    private static NavMeshBuildSettings TestSettings() => new()
    {
        OverrideVoxelSize = true,
        VoxelSize = 0.25f,
        OverrideTileSize = true,
        TileSize = 64,
    };

    private static NavMeshGeometrySource Quad(float sizeX, float sizeZ, Float3 offset)
    {
        Float3[] verts =
        [
            new(0, 0, 0),
            new(0, 0, sizeZ),
            new(sizeX, 0, sizeZ),
            new(sizeX, 0, 0),
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];
        return new NavMeshGeometrySource(verts, indices, Float4x4.CreateTranslation(offset));
    }

    /// <summary>A 20x20 floor whose middle is blocked by a wall spanning z=0..16 at x≈10,
    /// leaving a 4-unit passage at the far +Z side. Paths from left to right must detour
    /// through the passage.</summary>
    private static NavMeshWorld BuildUShapeWorld()
    {
        // Wall as a tall box: two vertical faces won't rasterize as walkable, and the floor
        // is interrupted because the wall volume overwrites walkable spans below its top.
        Float3[] wallVerts =
        [
            // A solid box from (9.5,0,0) to (10.5,3,16)
            new(9.5f, 0, 0), new(9.5f, 0, 16), new(10.5f, 0, 16), new(10.5f, 0, 0),      // bottom
            new(9.5f, 3, 0), new(9.5f, 3, 16), new(10.5f, 3, 16), new(10.5f, 3, 0),      // top
        ];
        int[] wallIndices =
        [
            4, 5, 6, 4, 6, 7,      // top face (up-facing, but too high to connect: walkable island)
            0, 6, 5, 0, 7, 6,      // sides via bottom ring (windings vary; solidity is what matters)
            0, 5, 1, 0, 4, 5,
            1, 6, 2, 1, 5, 6,
            2, 7, 3, 2, 6, 7,
            3, 4, 0, 3, 7, 4,
        ];

        var world = new NavMeshWorld();
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(),
        [
            Quad(20, 20, Float3.Zero),
            new NavMeshGeometrySource(wallVerts, wallIndices, Float4x4.Identity),
        ]);
        Assert.NotNull(data);
        Assert.NotNull(world.AddNavMeshData(data!));
        return world;
    }

    [Fact]
    public void CalculatePath_AroundWall_IsCompleteAndDetours()
    {
        NavMeshWorld world = BuildUShapeWorld();
        var path = new NavMeshPath();

        // Straight across the wall at z=8: must detour via the z>16 passage.
        bool found = world.CalculatePath(new Float3(5, 0, 8), new Float3(15, 0, 8), NavMesh.AllAreas, path);

        Assert.True(found);
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
        Assert.True(path.CornerCount >= 3, $"A detour needs intermediate corners, got {path.CornerCount}.");

        // The detour must pass beyond the wall's far end (z > 16 - some slack for corner cutting).
        double maxZ = 0;
        foreach (Float3 corner in path.Corners)
            maxZ = System.Math.Max(maxZ, corner.Z);
        Assert.True(maxZ > 14.0, $"Path should route around the wall end (max corner z was {maxZ:0.0}).");

        // Path length must be well above the straight-line distance of 10.
        double length = 0;
        Float3[] corners = path.Corners;
        for (int i = 1; i < corners.Length; i++)
            length += Float3.Distance(corners[i - 1], corners[i]);
        Assert.True(length > 14.0, $"Detour should be much longer than the 10-unit straight line, got {length:0.0}.");
    }

    [Fact]
    public void CalculatePath_ToDisconnectedIsland_IsPartial()
    {
        var world = new NavMeshWorld();
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(),
        [
            Quad(10, 10, Float3.Zero),
            Quad(10, 10, new Float3(30, 0, 0)),  // 20-unit gap: unreachable
        ]);
        Assert.NotNull(data);
        world.AddNavMeshData(data!);

        var path = new NavMeshPath();
        bool found = world.CalculatePath(new Float3(5, 0, 5), new Float3(35, 0, 5), NavMesh.AllAreas, path);

        Assert.True(found, "A partial path to the closest reachable point should still be returned.");
        Assert.Equal(NavMeshPathStatus.PathPartial, path.Status);

        // The partial path must end on the first island, not teleport across the gap.
        Float3 last = path.Corners[path.CornerCount - 1];
        Assert.True(last.X <= 10.5f, $"Partial path leaked off its island (end x = {last.X:0.0}).");
    }

    [Fact]
    public void SamplePosition_SnapsToFloor_AndRespectsMaxDistance()
    {
        var world = new NavMeshWorld();
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [Quad(10, 10, Float3.Zero)]);
        world.AddNavMeshData(data!);

        // 1.5 units above the floor: inside a 2-unit radius, outside a 0.5-unit radius.
        Assert.True(world.SamplePosition(new Float3(5, 1.5f, 5), out NavMeshHit hit, 2f, NavMesh.AllAreas));
        Assert.True(hit.Hit);
        Assert.True(System.Math.Abs(hit.Position.Y) < 0.3f, $"Sample should land on the floor, got y={hit.Position.Y:0.00}.");
        Assert.True(System.Math.Abs(hit.Position.X - 5) < 0.3f);

        Assert.False(world.SamplePosition(new Float3(5, 1.5f, 5), out _, 0.5f, NavMesh.AllAreas));
    }

    [Fact]
    public void Raycast_AcrossFloor_ClearAndBlocked()
    {
        var world = new NavMeshWorld();
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [Quad(10, 10, Float3.Zero)]);
        world.AddNavMeshData(data!);

        // Within the floor: unobstructed.
        Assert.False(world.Raycast(new Float3(2, 0, 5), new Float3(8, 0, 5), out NavMeshHit clear, NavMesh.AllAreas));
        Assert.False(clear.Hit);

        // Off the edge: blocked at the border.
        Assert.True(world.Raycast(new Float3(5, 0, 5), new Float3(25, 0, 5), out NavMeshHit blocked, NavMesh.AllAreas));
        Assert.True(blocked.Hit);
        Assert.True(blocked.Position.X < 10.5f, $"Blocked ray should stop at the mesh border, got x={blocked.Position.X:0.0}.");
    }

    [Fact]
    public void FindClosestEdge_ReturnsBorder()
    {
        var world = new NavMeshWorld();
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [Quad(10, 10, Float3.Zero)]);
        world.AddNavMeshData(data!);

        Assert.True(world.FindClosestEdge(new Float3(5, 0, 5), out NavMeshHit hit, NavMesh.AllAreas));
        Assert.True(hit.Hit);
        // From the center of a 10x10 eroded floor, the nearest border is a few units away.
        Assert.InRange(hit.Distance, 1f, 6f);
    }

    [Fact]
    public void AreaMask_ExcludingArea_BlocksPath()
    {
        var world = new NavMeshWorld();
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [Quad(10, 10, Float3.Zero)], defaultArea: 3);
        world.AddNavMeshData(data!);

        var path = new NavMeshPath();
        // Mask that excludes area 3: nothing is traversable.
        int maskWithout3 = ~(1 << 3);
        Assert.False(world.CalculatePath(new Float3(2, 0, 2), new Float3(8, 0, 8), maskWithout3, path));
        Assert.Equal(NavMeshPathStatus.PathInvalid, path.Status);

        // Including it works.
        Assert.True(world.CalculatePath(new Float3(2, 0, 2), new Float3(8, 0, 8), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    [Fact]
    public void Queries_WithNoNavMesh_ReturnFalse()
    {
        var world = new NavMeshWorld();
        var path = new NavMeshPath();

        Assert.False(world.CalculatePath(Float3.Zero, new Float3(1, 0, 1), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathInvalid, path.Status);
        Assert.False(world.SamplePosition(Float3.Zero, out _, 1f, NavMesh.AllAreas));
        Assert.False(world.Raycast(Float3.Zero, new Float3(1, 0, 1), out _, NavMesh.AllAreas));
        Assert.False(world.TryRentQuery(out _));
    }

    [Fact]
    public void ParallelQueries_AreSafe()
    {
        NavMeshWorld world = BuildUShapeWorld();

        System.Threading.Tasks.Parallel.For(0, 64, i =>
        {
            var path = new NavMeshPath();
            var from = new Float3(2 + (i % 7), 0, 2 + (i % 11));
            var to = new Float3(18 - (i % 5), 0, 3 + (i % 13));
            bool found = world.CalculatePath(from, to, NavMesh.AllAreas, path);
            Assert.True(found, $"Query {i} from {from} to {to} failed.");
            Assert.True(world.SamplePosition(from, out _, 2f, NavMesh.AllAreas));
        });
    }

    [Fact]
    public void MutateTileCache_DrainsPoolAndKeepsWorking()
    {
        var world = new NavMeshWorld();
        NavMeshData? data = NavMeshBuilder.Build(TestSettings(), [Quad(10, 10, Float3.Zero)]);
        NavMeshInstance? instance = world.AddNavMeshData(data!);
        Assert.NotNull(instance);

        var path = new NavMeshPath();
        Assert.True(world.CalculatePath(new Float3(2, 0, 2), new Float3(8, 0, 8), NavMesh.AllAreas, path));

        bool mutated = false;
        world.MutateTileCache(instance!, _ => mutated = true);
        Assert.True(mutated);

        // Queries still work after the pool was invalidated.
        Assert.True(world.CalculatePath(new Float3(2, 0, 2), new Float3(8, 0, 8), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }
}
