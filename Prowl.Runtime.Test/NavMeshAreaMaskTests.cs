// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshAgent.AreaMask"/> actually restricting which areas a path (and a
/// crowd-moving agent) can cross - both at the <see cref="NavMeshQuery.FindPath"/> level and at the
/// <see cref="NavMeshCrowd"/> level, since those are two independently-implemented filters (Detour's
/// per-query filter vs. its per-agent-type crowd filter slots) that both need to honor the mask.
/// </summary>
public class NavMeshAreaMaskTests : RuntimeTestBase
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

    private GameObject AddGround(Scene scene, string name, Float3 position)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        go.AddComponent<MeshRenderer>().Mesh = CreateQuad(10, 10);
        scene.Add(go);
        return go;
    }

    private static void Bake(NavMeshSurface surface)
    {
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(surface.Scene!, surface.LayerMask);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset;
        surface.RebuildQuery();
    }

    // Detour still returns a result for a start/end in disconnected regions, flagged partial - see
    // NavMeshLinkTests' own CrossedTheGap for the full explanation.
    private static bool CrossedTheGap(NavMeshPath path) => path.Success && !path.Partial;

    [Fact]
    public void FindPath_ExcludingTheLinksArea_CannotCrossTheGap()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Left", new Float3(0, 0, 0));
        AddGround(scene, "Right", new Float3(20, 0, 0)); // 10-unit gap between edges

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        link.AreaIndex = NavMeshAreas.Jump;
        scene.Add(link.GameObject);

        NavMeshQuery query = surface.Query!;
        Float3 start = new Float3(-3, 0, 0);
        Float3 end = new Float3(23, 0, 0);

        uint everyAreaExceptJump = uint.MaxValue & ~(1u << NavMeshAreas.Jump);

        Assert.True(CrossedTheGap(query.FindPath(start, end, uint.MaxValue)));
        Assert.False(CrossedTheGap(query.FindPath(start, end, everyAreaExceptJump)));
    }

    [Fact]
    public void FindPath_ExcludingAModifierVolumesArea_CannotCrossGroundTaggedWithIt()
    {
        const int hazardArea = 7;

        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Ground", Float3.Zero);

        // Covers the whole ground, comfortably larger than it: a flat 10x10 quad bakes down to just two
        // triangles, and NavMeshGeometryCollector.ApplyModifierVolumes re-marks a triangle by its
        // centroid, not by partial overlap - a volume has to actually contain both triangles' centroids
        // (each a third of the way in from a corner, not at the quad's own center) to reliably re-mark
        // either of them at all.
        var hazardGo = CreateGameObject("Hazard");
        var hazard = hazardGo.AddComponent<NavMeshModifierVolume>();
        hazard.Size = new Float3(12f, 4f, 12f);
        hazard.Area = hazardArea;
        scene.Add(hazardGo);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        NavMeshQuery query = surface.Query!;
        Float3 start = new Float3(-3, 0, 0);
        Float3 end = new Float3(3, 0, 0);

        uint everyAreaExceptHazard = uint.MaxValue & ~(1u << hazardArea);

        Assert.True(CrossedTheGap(query.FindPath(start, end, uint.MaxValue)));
        Assert.False(CrossedTheGap(query.FindPath(start, end, everyAreaExceptHazard)));
    }

    [Fact]
    public void SetDestination_ExcludingTheLinksArea_TheAgentNeverCrossesTheGap()
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
        link.AreaIndex = NavMeshAreas.Jump;
        scene.Add(link.GameObject);

        var agent = CreateGameObject("Agent").AddComponent<NavMeshAgent>();
        agent.Transform.Position = new Float3(-3, 0, 0);
        agent.AreaMask = uint.MaxValue & ~(1u << NavMeshAreas.Jump);
        scene.Add(agent.GameObject);

        Assert.True(agent.SetDestination(new Float3(23, 0, 0)));
        Tick(scene, 600); // 10 seconds - comfortably enough time to have crossed, if it could

        // Never made it past the gap: still on (or very near) the Left slab, nowhere close to the target.
        Assert.True(agent.Position.X < 6f, $"Agent ended at {agent.Position}, but should never have been able to cross the gap.");
    }

    [Fact]
    public void SetDestination_WithTheLinksAreaIncluded_TheAgentCrossesNormally()
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
        link.AreaIndex = NavMeshAreas.Jump;
        scene.Add(link.GameObject);

        var agent = CreateGameObject("Agent").AddComponent<NavMeshAgent>();
        agent.Transform.Position = new Float3(-3, 0, 0);
        // Default AreaMask (every area) explicitly - the control case for the exclusion test above.
        scene.Add(agent.GameObject);

        Assert.True(agent.SetDestination(new Float3(23, 0, 0)));
        Tick(scene, 600);

        Assert.True(agent.Position.X > 15f, $"Agent ended at {agent.Position}, expected it to have crossed to the far slab.");
    }

    [Fact]
    public void MoreThanSixteenDistinctAreaMasks_FallsBackSafelyInsteadOfThrowing()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Ground", Float3.Zero);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);

        // Detour's crowd only has 16 filter slots; 20 agents each with a distinct AreaMask forces the
        // 17th-and-beyond to fall back rather than crash or corrupt an earlier agent's slot.
        for (int i = 0; i < 20; i++)
        {
            var agent = CreateGameObject($"Agent {i}").AddComponent<NavMeshAgent>();
            agent.Transform.Position = new Float3(-4 + i * 0.3f, 0, 0);
            agent.AreaMask = (uint)(1 << (i % 30)) | 1u; // bit 0 (Walkable) always included, so it can still path
            scene.Add(agent.GameObject);

            Assert.True(agent.SetDestination(new Float3(4, 0, 0)));
        }

        Tick(scene, 60); // must not throw
    }
}
