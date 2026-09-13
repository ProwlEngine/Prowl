// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises the per-agent-type registry introduced on top of the single-mesh foundation: two
/// surfaces of different agent types in one scene bake and query independently of each other, and an
/// agent only ever resolves its own type's query, never another type's.
/// </summary>
public class NavMeshMultiSurfaceTests : RuntimeTestBase
{
    public NavMeshMultiSurfaceTests() => NavMeshAgentTypes.ResetDefault();

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

    private GameObject AddGround(Scene scene, string name, Float3 position, int layerIndex)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        go.LayerIndex = layerIndex;
        go.AddComponent<MeshRenderer>().Mesh = CreateQuad(10, 10);
        scene.Add(go);
        return go;
    }

    private static void BakeAndAssign(NavMeshSurface surface, LayerMask layerMask)
    {
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(surface.Scene!, layerMask, surface.AgentTypeId);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset; // implicit AssetRef<NavMesh> conversion from a raw instance
        surface.RebuildQuery();
    }

    [Fact]
    public void TwoSurfacesOfDifferentTypes_BakeAndQueryIndependently()
    {
        Scene scene = CreateScene(enable: true);
        int smallTypeId = NavMeshAgentTypes.Add("Small", 0.25f, 1f, 45f, 0.2f);

        AddGround(scene, "GroundA", new Float3(0, 0, 0), layerIndex: 0);
        AddGround(scene, "GroundB", new Float3(1000, 0, 0), layerIndex: 1);

        var surfaceA = CreateGameObject("SurfaceA").AddComponent<NavMeshSurface>();
        surfaceA.AgentTypeId = NavMeshAgentTypes.HumanoidId;
        surfaceA.LayerMask = LayerMask.FromMask(1u << 0);
        scene.Add(surfaceA.GameObject);

        var surfaceB = CreateGameObject("SurfaceB").AddComponent<NavMeshSurface>();
        surfaceB.AgentTypeId = smallTypeId;
        surfaceB.LayerMask = LayerMask.FromMask(1u << 1);
        scene.Add(surfaceB.GameObject);

        BakeAndAssign(surfaceA, surfaceA.LayerMask);
        BakeAndAssign(surfaceB, surfaceB.LayerMask);

        // Each surface's build only saw its own layer's ground - baking independently, not sharing
        // one combined mesh.
        Assert.Equal(NavMeshAgentTypes.HumanoidId, surfaceA.NavMeshAsset.Res!.AgentTypeId);
        Assert.Equal(smallTypeId, surfaceB.NavMeshAsset.Res!.AgentTypeId);

        var system = NavMeshSystem.GetOrCreate(scene);
        NavMeshQuery? queryHumanoid = system.GetQuery(NavMeshAgentTypes.HumanoidId);
        NavMeshQuery? querySmall = system.GetQuery(smallTypeId);

        Assert.NotNull(queryHumanoid);
        Assert.NotNull(querySmall);
        Assert.NotSame(queryHumanoid, querySmall);

        // The Humanoid query finds ground near the origin but not near GroundB's location -
        // GroundB was never part of its bake.
        NavMeshPath localPath = queryHumanoid!.FindPath(new Float3(-3, 0, 0), new Float3(3, 0, 0));
        Assert.True(localPath.Success);

        NavMeshPath farPath = queryHumanoid.FindPath(new Float3(997, 0, 0), new Float3(1003, 0, 0));
        Assert.False(farPath.Success);
    }

    [Fact]
    public void Agent_OnlyResolvesItsOwnAgentTypesSurface()
    {
        Scene scene = CreateScene(enable: true);
        int smallTypeId = NavMeshAgentTypes.Add("Small", 0.25f, 1f, 45f, 0.2f);

        AddGround(scene, "Ground", Float3.Zero, layerIndex: 0);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.AgentTypeId = NavMeshAgentTypes.HumanoidId; // baked for Humanoid only
        surface.LayerMask = LayerMask.Everything;
        scene.Add(surface.GameObject);
        BakeAndAssign(surface, surface.LayerMask);

        var humanoidAgent = CreateGameObject("HumanoidAgent").AddComponent<NavMeshAgent>();
        humanoidAgent.AgentTypeId = NavMeshAgentTypes.HumanoidId;
        scene.Add(humanoidAgent.GameObject);

        var smallAgent = CreateGameObject("SmallAgent").AddComponent<NavMeshAgent>();
        smallAgent.AgentTypeId = smallTypeId;
        scene.Add(smallAgent.GameObject);

        Assert.NotNull(humanoidAgent.Query);
        Assert.Null(smallAgent.Query); // no surface baked for the Small type in this scene
    }
}
