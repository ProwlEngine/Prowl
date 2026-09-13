// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshAgent"/>'s crowd-based movement (<see cref="NavMeshCrowd"/>, wrapping
/// Prowl.Recast's <c>DtCrowd</c>): an agent actually walks toward a destination, retargeting an
/// already-joined agent never grows the crowd (the fix for the "Move() tears down and rebuilds the
/// crowd agent" bug PR #335's review flagged), agents of different types simulate in separate crowds,
/// and crossing a <see cref="NavMeshLink"/> fires its traversal event.
/// </summary>
public class NavMeshCrowdMovementTests : RuntimeTestBase
{
    public NavMeshCrowdMovementTests() => NavMeshAgentTypes.ResetDefault();

    public override void Dispose()
    {
        NavMeshAgentTypes.ResetDefault();
        base.Dispose();
    }

    private static Mesh CreateQuad(float width, float depth)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;
        return new Mesh
        {
            Vertices = [new(-hw, 0, -hd), new(hw, 0, -hd), new(hw, 0, hd), new(-hw, 0, hd)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    private GameObject AddGround(Scene scene, string name, Float3 position, int layerIndex = 0)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        go.LayerIndex = layerIndex;
        go.AddComponent<MeshRenderer>().Mesh = CreateQuad(10, 10);
        scene.Add(go);
        return go;
    }

    private static void Bake(NavMeshSurface surface, LayerMask? layerMask = null)
    {
        LayerMask mask = layerMask ?? surface.LayerMask;
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(surface.Scene!, mask, surface.AgentTypeId);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset;
        surface.RebuildQuery();
    }

    private NavMeshSurface CreateBakedFlatSurface(Scene scene)
    {
        AddGround(scene, "Ground", Float3.Zero);
        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);
        return surface;
    }

    private NavMeshAgent AddAgent(Scene scene, Float3 position, int agentTypeId = 0)
    {
        var agent = CreateGameObject("Agent").AddComponent<NavMeshAgent>();
        agent.AgentTypeId = agentTypeId == 0 ? NavMeshAgentTypes.HumanoidId : agentTypeId;
        agent.Transform.Position = position;
        scene.Add(agent.GameObject);
        return agent;
    }

    private static float HorizontalDistance(Float3 a, Float3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    public void SetDestination_WalksTheAgentTowardTheTarget()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));

        float startDistance = HorizontalDistance(agent.Position, new Float3(4, 0, 0));
        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));

        Tick(scene, 180); // 3 seconds at the default 60Hz fixed step

        float endDistance = HorizontalDistance(agent.Position, new Float3(4, 0, 0));
        Assert.True(endDistance < startDistance - 1f, $"Expected the agent to close in on its destination (start {startDistance}, end {endDistance}).");
    }

    [Fact]
    public void SetDestination_GivenEnoughTime_ReachesStoppingDistance()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-3, 0, 0));

        Assert.True(agent.SetDestination(new Float3(3, 0, 0)));
        Tick(scene, 300); // 5 seconds - comfortably enough at the default 3.5 units/s

        Assert.True(agent.HasArrived, $"Agent ended at {agent.Position}, still not within stopping distance.");
    }

    [Fact]
    public void SetDestination_CalledTwice_NeverGrowsTheCrowd()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));

        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        NavMeshCrowd? crowd = NavMeshSystem.GetOrCreate(scene).GetOrCreateCrowd(agent.AgentTypeId);
        Assert.NotNull(crowd);
        Assert.Equal(1, crowd!.ActiveAgentCount);

        Tick(scene, 10);
        Assert.True(agent.SetDestination(new Float3(-4, 0, 4))); // retarget mid-flight

        // Retargeting must reuse the same crowd slot, not tear it down and re-add it - the fix for the
        // exact bug PR #335's review flagged in its own Move().
        Assert.Equal(1, crowd.ActiveAgentCount);
        Assert.Same(crowd, NavMeshSystem.GetOrCreate(scene).GetOrCreateCrowd(agent.AgentTypeId));
    }

    [Fact]
    public void DisablingAnAgent_FreesItsCrowdSlot()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));

        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        NavMeshCrowd? crowd = NavMeshSystem.GetOrCreate(scene).GetOrCreateCrowd(agent.AgentTypeId);
        Assert.Equal(1, crowd!.ActiveAgentCount);

        agent.Enabled = false;

        Assert.Equal(0, crowd.ActiveAgentCount);
    }

    [Fact]
    public void AgentsOfDifferentTypes_SimulateInSeparateCrowds()
    {
        Scene scene = CreateScene(enable: true);
        int smallTypeId = NavMeshAgentTypes.Add("Small", 0.25f, 1f, 45f, 0.2f);

        AddGround(scene, "GroundA", new Float3(0, 0, 0), layerIndex: 0);
        AddGround(scene, "GroundB", new Float3(1000, 0, 0), layerIndex: 1);

        var surfaceA = CreateGameObject("SurfaceA").AddComponent<NavMeshSurface>();
        surfaceA.AgentTypeId = NavMeshAgentTypes.HumanoidId;
        surfaceA.LayerMask = LayerMask.FromMask(1u << 0);
        scene.Add(surfaceA.GameObject);
        Bake(surfaceA, surfaceA.LayerMask);

        var surfaceB = CreateGameObject("SurfaceB").AddComponent<NavMeshSurface>();
        surfaceB.AgentTypeId = smallTypeId;
        surfaceB.LayerMask = LayerMask.FromMask(1u << 1);
        scene.Add(surfaceB.GameObject);
        Bake(surfaceB, surfaceB.LayerMask);

        NavMeshAgent humanoidAgent = AddAgent(scene, new Float3(-4, 0, 0), NavMeshAgentTypes.HumanoidId);
        NavMeshAgent smallAgent = AddAgent(scene, new Float3(996, 0, 0), smallTypeId);

        Assert.True(humanoidAgent.SetDestination(new Float3(4, 0, 0)));
        Assert.True(smallAgent.SetDestination(new Float3(1004, 0, 0)));

        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);
        NavMeshCrowd? humanoidCrowd = system.GetOrCreateCrowd(NavMeshAgentTypes.HumanoidId);
        NavMeshCrowd? smallCrowd = system.GetOrCreateCrowd(smallTypeId);

        Assert.NotNull(humanoidCrowd);
        Assert.NotNull(smallCrowd);
        Assert.NotSame(humanoidCrowd, smallCrowd);
        Assert.Equal(1, humanoidCrowd!.ActiveAgentCount);
        Assert.Equal(1, smallCrowd!.ActiveAgentCount);
    }

    [Fact]
    public void CrossingALink_FiresItsTraversedEvent()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Left", new Float3(0, 0, 0));
        AddGround(scene, "Right", new Float3(20, 0, 0)); // 10-unit gap between edges

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);  // inside the Left slab, near its edge
        link.EndPoint = new Float3(16, 0, 0);   // inside the Right slab, near its edge
        scene.Add(link.GameObject); // registers and rebuilds the surface's query

        NavMeshAgent agent = AddAgent(scene, new Float3(-3, 0, 0));

        bool traversed = false;
        link.Traversed += crossingAgent => { if (crossingAgent == agent) traversed = true; };

        Assert.True(agent.SetDestination(new Float3(23, 0, 0)));
        Tick(scene, 600); // 10 seconds - well past the ~7s a 3.5 units/s agent needs for a 26-unit trip

        Assert.True(traversed, $"Agent ended at {agent.Position} without ever crossing the link.");
    }

    [Fact]
    public void CrossingALink_LiftsTheAgentAboveGroundHeight_ButSettlesBackDown()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Left", new Float3(0, 0, 0));
        AddGround(scene, "Right", new Float3(20, 0, 0));

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        scene.Add(link.GameObject);

        NavMeshAgent agent = AddAgent(scene, new Float3(-3, 0, 0));
        agent.JumpHeight = 1f;

        Assert.True(agent.SetDestination(new Float3(23, 0, 0)));

        // The jump arc is a visual added to Transform, not to Position (see that property's own doc
        // comment) - it's what a caller looking at rendered position, not simulated/pathing position,
        // would see.
        float highestY = float.MinValue;
        for (int i = 0; i < 600; i++)
        {
            Tick(scene, 1);
            if (agent.Transform.Position.Y > highestY) highestY = agent.Transform.Position.Y;
        }

        Assert.True(highestY > 0.3f, $"Expected a visible hop above ground height while crossing; highest Y observed was {highestY}.");
        Assert.True(agent.Transform.Position.Y < 0.3f, $"Expected the agent to settle back near ground height after crossing; ended at Y={agent.Transform.Position.Y}.");
    }

    [Fact]
    public void JumpHeightZero_DisablesTheHop()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Left", new Float3(0, 0, 0));
        AddGround(scene, "Right", new Float3(20, 0, 0));

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        scene.Add(link.GameObject);

        NavMeshAgent agent = AddAgent(scene, new Float3(-3, 0, 0));
        agent.JumpHeight = 0f;

        Assert.True(agent.SetDestination(new Float3(23, 0, 0)));

        float highestY = float.MinValue;
        for (int i = 0; i < 600; i++)
        {
            Tick(scene, 1);
            if (agent.Transform.Position.Y > highestY) highestY = agent.Transform.Position.Y;
        }

        Assert.True(highestY < 0.3f, $"Expected no hop with JumpHeight=0; highest Y observed was {highestY}.");
    }

    [Fact]
    public void RebuildingTheSurfaceQuery_LetsAnAlreadyJoinedAgentRejoinCleanly()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));

        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        Tick(scene, 10);

        // A link registering rebuilds this surface's query, replacing the DtNavMesh the agent's crowd
        // was built against - simulating the kind of scene edit that would otherwise leave a crowd
        // reference dangling.
        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        scene.Add(link.GameObject);
        surface.RebuildQuery();

        Assert.True(agent.SetDestination(new Float3(-4, 0, 0)));
        Tick(scene, 10); // must not throw

        NavMeshCrowd? crowd = NavMeshSystem.GetOrCreate(scene).GetOrCreateCrowd(agent.AgentTypeId);
        Assert.Equal(1, crowd!.ActiveAgentCount);
    }

    [Fact]
    public void GetDestination_NullBeforeARequest_SetAfterOne()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateBakedFlatSurface(scene);
        NavMeshCrowd crowd = NavMeshSystem.GetOrCreate(scene).GetOrCreateCrowd(surface.AgentTypeId)!;

        NavMeshCrowdHandle handle = crowd.AddAgent(new Float3(-4, 0, 0), 0.5f, 2f, 3.5f, 8f);
        Assert.Null(crowd.GetDestination(handle));

        Assert.True(crowd.SetDestination(handle, new Float3(4, 0, 0)));

        // GetDestination reports exactly what was requested, not a mesh-snapped approximation of it.
        Assert.Equal(new Float3(4, 0, 0), crowd.GetDestination(handle));

        crowd.RemoveAgent(handle);
        Assert.Null(crowd.GetDestination(handle)); // freeing the slot drops the tracked destination too
    }

    [Fact]
    public void UpdateRotation_TurnsTheAgentToFaceItsSteeringDirection()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));
        agent.AngularSpeed = 100000f; // effectively instant, so this test isn't timing-sensitive

        Assert.True(agent.SetDestination(new Float3(4, 0, 0))); // due +X
        Tick(scene, 30);

        Float3 forward = Quaternion.Forward(agent.Transform.Rotation);
        Assert.True(Float3.Dot(Float3.Normalize(new Float3(forward.X, 0, forward.Z)), Float3.UnitX) > 0.9f,
            $"Expected the agent to face roughly +X; forward was {forward}.");
    }

    [Fact]
    public void UpdateRotationOff_LeavesRotationUntouched()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));
        agent.UpdateRotation = false;
        Quaternion original = agent.Transform.Rotation;

        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        Tick(scene, 60);

        Assert.Equal(original, agent.Transform.Rotation);
    }

    [Fact]
    public void UpdatePositionOff_StillSteersButNeverWritesTransform()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));
        agent.UpdatePosition = false;
        Float3 transformStart = agent.Transform.Position;

        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        Tick(scene, 60);

        Assert.Equal(transformStart, agent.Transform.Position); // never written
        Assert.True(HorizontalDistance(agent.Position, transformStart) > 0.5f, "The crowd simulation itself must still have moved.");
    }

    [Fact]
    public void BaseOffset_LiftsWhatIsWrittenToTransformWithoutAffectingPosition()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));
        agent.BaseOffset = 1.5f;
        Tick(scene, 1); // Update() needs at least one tick to have joined+written anything

        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        Tick(scene, 1);

        Assert.Equal(agent.Position.Y + 1.5f, agent.Transform.Position.Y, 3);
    }

    [Fact]
    public void Warp_TeleportsImmediatelyAndCanStillBeGivenANewDestinationAfterward()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene); // a 10x10 quad - big enough to hold both the warp target and the new destination
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));
        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        Tick(scene, 30);

        Assert.True(agent.Warp(new Float3(2, 0, 2)));
        Assert.Equal(new Float3(2, 0, 2), agent.Transform.Position);

        Assert.True(agent.SetDestination(new Float3(-2, 0, -2)));
        Tick(scene, 200);
        Assert.True(agent.HasArrived, $"Agent ended at {agent.Position} after a warp + fresh destination.");
    }

    [Fact]
    public void ResetPath_StopsTheAgentInPlace()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));

        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        Tick(scene, 30);

        agent.ResetPath();
        Tick(scene, 30); // let any residual velocity from the moment of the reset bleed off
        Float3 stoppedNear = agent.Position;
        Tick(scene, 60);

        Assert.True(HorizontalDistance(agent.Position, stoppedNear) < 0.2f, $"Expected the agent to stay put after ResetPath; moved to {agent.Position}.");
        Assert.False(agent.HasArrived); // it never actually reached (4,0,0)
    }

    [Fact]
    public void FindClosestEdge_FindsTheGroundsBoundary()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene); // a 10x10 quad, so the boundary is ~5 units from center
        NavMeshAgent agent = AddAgent(scene, Float3.Zero);
        Tick(scene, 1);

        Assert.True(agent.FindClosestEdge(10f, out Float3 hitPosition, out Float3 hitNormal, out float distance));
        Assert.True(distance is > 3f and < 5f, $"Expected the ground's edge a few units out; distance was {distance}.");
        Assert.True(Float3.Length(hitNormal) > 0.9f, "Expected a normalized hit normal.");
        _ = hitPosition;
    }

    [Fact]
    public void Raycast_ClearAheadOnTheSameSlab_ReturnsFalse()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-2, 0, 0));
        Tick(scene, 1);

        Assert.False(agent.Raycast(new Float3(2, 0, 0), out Float3 hitPoint));
        Assert.Equal(new Float3(2, 0, 0), hitPoint);
    }

    [Fact]
    public void IsOnOffMeshLink_TrueWhileCrossingAndExposesTheLink()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Left", new Float3(0, 0, 0));
        AddGround(scene, "Right", new Float3(20, 0, 0));

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        scene.Add(link.GameObject);

        NavMeshAgent agent = AddAgent(scene, new Float3(-3, 0, 0));
        Assert.True(agent.SetDestination(new Float3(23, 0, 0)));

        bool caughtOnLink = false;
        for (int i = 0; i < 600 && !caughtOnLink; i++)
        {
            Tick(scene, 1);
            if (!agent.IsOnOffMeshLink) continue;
            caughtOnLink = true;
            Assert.Same(link, agent.CurrentOffMeshLinkData);
        }

        Assert.True(caughtOnLink, $"Agent ended at {agent.Position} without ever reporting IsOnOffMeshLink.");
    }

    [Fact]
    public void DrawGizmos_NeverThrows_WithOrWithoutAnActiveDestination()
    {
        Scene scene = CreateScene(enable: true);
        CreateBakedFlatSurface(scene);
        NavMeshAgent agent = AddAgent(scene, new Float3(-4, 0, 0));

        agent.DrawGizmos();
        agent.DrawGizmosSelected(); // no crowd joined yet - must be a no-op, not a throw

        Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        agent.DrawGizmos();
        agent.DrawGizmosSelected(); // has a destination now - exercises the path-drawing branch
    }
}
