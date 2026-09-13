// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshAgent.UpcomingCorners"/>/<see cref="NavMeshAgent.NextCornerDistance"/>/
/// <see cref="NavMeshAgent.NextTurnAngle"/>/<see cref="NavMeshAgent.IsApproachingLink"/> - all read from
/// the crowd's own live corridor for an already-moving agent, not a fresh <see cref="NavMeshAgent.FindPath"/>
/// call, and checked against the same corridor <see cref="NavMeshQuery.FindPath"/> reports directly, on
/// both a flat plane and a tilted ramp.
/// </summary>
public class NavMeshPathLookaheadTests : RuntimeTestBase
{
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

    private GameObject AddQuad(Scene scene, string name, float width, float depth, Float3 position, float tiltDegrees = 0f)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        if (tiltDegrees != 0f)
            go.Transform.Rotation = Quaternion.FromEuler(new Float3(tiltDegrees, 0, 0));
        go.AddComponent<MeshRenderer>().Mesh = CreateQuad(width, depth);
        scene.Add(go);
        return go;
    }

    private static void Bake(NavMeshSurface surface, NavMeshBakeSettings? overrideSettings = null)
    {
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(surface.Scene!, surface.LayerMask);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, overrideSettings ?? surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, overrideSettings ?? surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset;
        surface.RebuildQuery();
    }

    private NavMeshAgent AddAgent(Scene scene, Float3 position)
    {
        var agent = CreateGameObject("Agent").AddComponent<NavMeshAgent>();
        agent.Transform.Position = position;
        scene.Add(agent.GameObject);
        return agent;
    }

    [Fact]
    public void UpcomingCorners_OnAFlatPlane_MatchesTheStraightDestination()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 30, 30, Float3.Zero);
        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        NavMeshAgent agent = AddAgent(scene, new Float3(-10, 0, 0));
        var destination = new Float3(10, 0, 0);
        Assert.True(agent.SetDestination(destination));
        Tick(scene, 5); // enough for the crowd to plan a corridor, not enough to have moved far

        ReadOnlySpan<NavMeshCorridorCorner> corners = agent.UpcomingCorners(4);
        Assert.True(corners.Length >= 1);

        NavMeshCorridorCorner last = corners[^1];
        Assert.True(Float3.Distance(last.Position, destination) < 1f, $"Expected the last known corner near the destination; was {last.Position}.");
        Assert.True(last.Distance > 0f);

        Assert.True(Math.Abs(agent.NextCornerDistance - corners[0].Distance) < 0.01f, "NextCornerDistance should match the first corridor corner's own distance.");
    }

    [Fact]
    public void UpcomingCorners_AroundABend_MatchesFindPathWithinATexel()
    {
        Scene scene = CreateScene(enable: true);
        // A "U" shaped floor: a bottom crossbar plus two side columns, empty in the middle - any path
        // between the columns' tops has to detour down through the crossbar and back up.
        AddQuad(scene, "Bottom", 10, 2, new Float3(0, 0, -4));
        AddQuad(scene, "LeftColumn", 2, 6, new Float3(-4, 0, 0));
        AddQuad(scene, "RightColumn", 2, 6, new Float3(4, 0, 0));
        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        var start = new Float3(-4, 0, 2.5f);
        var end = new Float3(4, 0, 2.5f);
        NavMeshAgent agent = AddAgent(scene, start);
        Assert.True(agent.SetDestination(end));
        Tick(scene, 5);

        ReadOnlySpan<NavMeshCorridorCorner> corners = agent.UpcomingCorners(8);
        Assert.True(corners.Length > 1, "a detour around the gap needs more than a straight one-corner path");

        NavMeshPath path = agent.FindPath(end);
        Assert.True(path.Success);

        // The crowd's own live corridor and a fresh FindPath call both resolve the same underlying
        // navmesh, so their corners should agree up to voxel-grid quantization (one cell's width).
        float texel = surface.EffectiveBakeSettings.CellSize;
        foreach (NavMeshCorridorCorner corner in corners)
        {
            float closest = float.MaxValue;
            foreach (Float3 pathCorner in path.Corners)
                closest = Math.Min(closest, Float3.Distance(corner.Position, pathCorner));
            Assert.True(closest < texel * 4f, $"Corridor corner {corner.Position} had no matching FindPath corner within a few texels (closest {closest}).");
        }
    }

    [Fact]
    public void NextCornerDistance_DecreasesAsTheAgentWalksTowardIt()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 30, 30, Float3.Zero);
        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        NavMeshAgent agent = AddAgent(scene, new Float3(-10, 0, 0));
        Assert.True(agent.SetDestination(new Float3(10, 0, 0)));
        Tick(scene, 5);

        float first = agent.NextCornerDistance;
        Tick(scene, 60);
        float later = agent.NextCornerDistance;

        Assert.True(later < first, $"Expected the distance to the next corner to shrink; was {first} then {later}.");
    }

    [Fact]
    public void NextTurnAngle_IsSubstantialApproachingABend_ButNotOnAStraightPlane()
    {
        Scene straightScene = CreateScene(enable: true);
        AddQuad(straightScene, "Ground", 30, 30, Float3.Zero);
        var straightSurface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        straightScene.Add(straightSurface.GameObject);
        Bake(straightSurface);

        NavMeshAgent straightAgent = AddAgent(straightScene, new Float3(-10, 0, 0));
        Assert.True(straightAgent.SetDestination(new Float3(10, 0, 0)));
        Tick(straightScene, 5);
        Assert.True(straightAgent.NextTurnAngle < 5f, $"A straight walk on open ground should have no meaningful turn; was {straightAgent.NextTurnAngle}.");

        Scene bendScene = CreateScene(enable: true);
        AddQuad(bendScene, "Bottom", 10, 2, new Float3(0, 0, -4));
        AddQuad(bendScene, "LeftColumn", 2, 6, new Float3(-4, 0, 0));
        AddQuad(bendScene, "RightColumn", 2, 6, new Float3(4, 0, 0));
        var bendSurface = CreateGameObject("BendSurface").AddComponent<NavMeshSurface>();
        bendScene.Add(bendSurface.GameObject);
        Bake(bendSurface);

        NavMeshAgent bendAgent = AddAgent(bendScene, new Float3(-4, 0, 2.5f));
        Assert.True(bendAgent.SetDestination(new Float3(4, 0, 2.5f)));
        Tick(bendScene, 5);

        Assert.True(bendAgent.NextTurnAngle > 20f, $"Expected a real bend heading down into the crossbar; was {bendAgent.NextTurnAngle}.");
    }

    [Fact]
    public void IsApproachingLink_TrueBeforeCrossing_FalseOnceCrossed()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Left", 10, 10, new Float3(0, 0, 0));
        AddQuad(scene, "Right", 10, 10, new Float3(20, 0, 0));

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        scene.Add(link.GameObject);

        NavMeshAgent agent = AddAgent(scene, new Float3(-3, 0, 0));
        Assert.True(agent.SetDestination(new Float3(23, 0, 0)));
        Tick(scene, 5);

        Assert.True(agent.IsApproachingLink(out NavMeshLink? approaching, out float distance));
        Assert.Same(link, approaching);
        Assert.True(distance > 0f);

        // Walk it all the way across the link and well past it.
        Tick(scene, 600);
        Assert.False(agent.IsApproachingLink(out _, out _), "Should no longer be approaching a link already crossed and left behind.");
    }

    [Fact]
    public void UpcomingCorners_OnARamp_StaysWithinTheRampsOwnHeightRange()
    {
        Scene scene = CreateScene(enable: true);
        float tilt = 20f;
        AddQuad(scene, "Ramp", 10, 10, Float3.Zero, tilt);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);

        NavMeshBakeSettings settings = surface.EffectiveBakeSettings;
        settings.CellSize = 0.1f;
        settings.CellHeight = 0.05f;
        Bake(surface, settings);

        float halfDepth = 5f;
        float maxHeight = halfDepth * MathF.Tan(tilt * MathF.PI / 180f);

        NavMeshAgent agent = AddAgent(scene, new Float3(0, 0, -4));
        Assert.True(agent.SetDestination(new Float3(0, 0, 4)));
        Tick(scene, 5);

        ReadOnlySpan<NavMeshCorridorCorner> corners = agent.UpcomingCorners(8);
        Assert.True(corners.Length >= 1);
        foreach (NavMeshCorridorCorner corner in corners)
            Assert.True(corner.Position.Y > -(maxHeight + 1f) && corner.Position.Y < maxHeight + 1f, $"Corner {corner.Position} fell outside the ramp's own height range (+/-{maxHeight}).");
    }
}
