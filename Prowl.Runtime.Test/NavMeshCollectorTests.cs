// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
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
}
