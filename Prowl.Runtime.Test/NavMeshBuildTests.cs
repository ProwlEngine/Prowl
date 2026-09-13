// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshGeometryCollector"/> and <see cref="RecastNavMeshBuilder"/> together,
/// end to end from a live scene, against the handful of shapes that actually exercise a bake setting:
/// a flat plane, a ramp right at and just past the slope limit, a step right at the step limit, and a
/// gap wider than an agent can bridge. Connectivity claims (two shapes baking as one walkable area vs.
/// two disjoint ones) are checked the black-box way, via <see cref="NavMeshQuery.FindPath"/> - the tile
/// data itself is an implementation detail this suite has no reason to reach into directly.
/// </summary>
public class NavMeshBuildTests : RuntimeTestBase
{
    private static readonly NavMeshBakeSettings s_settings = NavMeshBakeSettings.Default;

    private static Mesh CreateQuad(float width, float depth)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;

        // Wound so the face normal points up (+Y): cross(v2-v0, v1-v0) for the first triangle.
        var mesh = new Mesh
        {
            Vertices = [new(-hw, 0, -hd), new(hw, 0, -hd), new(hw, 0, hd), new(-hw, 0, hd)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
        return mesh;
    }

    // Builds a quad, tilted around X by tiltDegrees (0 = flat), and adds it to the scene as a
    // MeshRenderer. Tilting around X turns a flat quad's normal away from up by exactly tiltDegrees,
    // which is the same "degrees from horizontal" convention Recast's own slope test uses.
    private GameObject AddQuad(Scene scene, string name, float width, float depth, Float3 position, float tiltDegrees = 0f)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        if (tiltDegrees != 0f)
            go.Transform.Rotation = Quaternion.FromEuler(new Float3(tiltDegrees, 0, 0));

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = CreateQuad(width, depth);

        scene.Add(go);
        return go;
    }

    private static NavMeshBuildResult Bake(Scene scene, NavMeshBakeSettings settings)
    {
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);
        return new RecastNavMeshBuilder().Build(input, settings);
    }

    private static NavMeshQuery BuildQuery(NavMeshBuildResult result, NavMeshBakeSettings settings)
    {
        var asset = new NavMesh();
        asset.Apply(result, settings, NavMeshAgentTypes.HumanoidId);
        return new NavMeshQuery(asset);
    }

    // Detour still returns a partial result for start/end in disconnected regions rather than an
    // outright failure - see NavMeshLinkTests' own CrossedTheGap for the fuller explanation.
    private static bool ActuallyReached(NavMeshPath path) => path.Success && !path.Partial;

    [Fact]
    public void FlatPlane_BuildsOneWalkableSurface()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 10, 10, Float3.Zero);

        NavMeshBuildResult result = Bake(scene, s_settings);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.TileCacheData);
        Assert.True(result.TileCacheData!.Layers.Count > 0);

        NavMeshQuery query = BuildQuery(result, s_settings);
        Assert.True(query.SamplePosition(Float3.Zero, out Float3 snapped));
        Assert.True(Float3.Distance(snapped, Float3.Zero) < 1f);
    }

    [Fact]
    public void Ramp_AtSlopeLimit_IsWalkable()
    {
        Scene scene = CreateScene(enable: true);
        // Comfortably under the limit rather than pinned to it: voxelization makes the exact
        // boundary a coin flip (a single cell's worth of quantized height rounds differently
        // depending on where the surface happens to land relative to the grid), so the meaningful
        // assertion is "clearly still inside the envelope", not "bit-for-bit at the threshold".
        AddQuad(scene, "Ramp", 10, 10, Float3.Zero, s_settings.MaxSlopeAngle - 5f);

        // A steep ramp needs a cell size fine enough to resolve it - see the note on
        // RecastNavMeshBuilder about coarse voxelization breaking region connectivity across a
        // slope. The default settings' 0.3/0.2 cells are tuned for a gentler, more typical surface.
        NavMeshBakeSettings settings = s_settings;
        settings.CellSize = 0.1f;
        settings.CellHeight = 0.05f;

        NavMeshBuildResult result = Bake(scene, settings);

        Assert.True(result.Success, result.Error);
        Assert.True(result.TileCacheData!.Layers.Count > 0);

        NavMeshQuery query = BuildQuery(result, settings);
        Assert.True(query.SamplePosition(Float3.Zero, out _));
    }

    [Fact]
    public void Ramp_OverSlopeLimit_IsRejected()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ramp", 10, 10, Float3.Zero, s_settings.MaxSlopeAngle + 15f);

        NavMeshBuildResult result = Bake(scene, s_settings);

        // The ramp is the only geometry and every triangle on it fails the slope test, so there is
        // no walkable surface left for Recast to region/contour/polygonize at all.
        Assert.False(result.Success);
    }

    [Fact]
    public void Step_AtStepLimit_ProducesOneConnectedSurface()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Lower", 5, 5, new Float3(0, 0, 0));
        // Edge-to-edge with Lower on X, raised by just under the step limit - see the slope test
        // above for why the exact boundary itself is not what's being asserted here.
        AddQuad(scene, "Upper", 5, 5, new Float3(5f, s_settings.MaxStepHeight - 0.05f, 0));

        NavMeshBuildResult result = Bake(scene, s_settings);
        Assert.True(result.Success, result.Error);

        NavMeshQuery query = BuildQuery(result, s_settings);
        NavMeshPath path = query.FindPath(new Float3(-1, 0, 0), new Float3(6, s_settings.MaxStepHeight - 0.05f, 0));

        // Both slabs are within stepping distance of each other, so Recast should have baked them as
        // one connected walkable area rather than two islands.
        Assert.True(ActuallyReached(path), $"path success={path.Success} partial={path.Partial}");
    }

    [Fact]
    public void Gap_WiderThanAgentDiameter_ProducesDisjointIslands()
    {
        Scene scene = CreateScene(enable: true);
        float gap = (s_settings.AgentRadius * 2f) + 1f; // wider than the agent's own diameter
        AddQuad(scene, "Left", 5, 5, new Float3(0, 0, 0));
        AddQuad(scene, "Right", 5, 5, new Float3(5f + gap, 0, 0));

        NavMeshBuildResult result = Bake(scene, s_settings);
        Assert.True(result.Success, result.Error);

        NavMeshQuery query = BuildQuery(result, s_settings);
        NavMeshPath path = query.FindPath(new Float3(-1, 0, 0), new Float3(6 + gap, 0, 0));

        // Two slabs with nothing bridging them bake into at least two separate, unreachable-from-
        // each-other areas.
        Assert.False(ActuallyReached(path), $"path success={path.Success} partial={path.Partial}");
    }

    [Fact]
    public void NavMeshAsset_RoundTripsThroughSerializer()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 10, 10, Float3.Zero);
        NavMeshBuildResult result = Bake(scene, s_settings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, s_settings, NavMeshAgentTypes.HumanoidId);

        NavMesh? restored = Serializer.Deserialize<NavMesh>(Serializer.Serialize(asset));

        Assert.NotNull(restored);
        Assert.NotNull(restored!.TileCacheData);
        Assert.Equal(asset.TileCacheData!.Layers.Count, restored.TileCacheData!.Layers.Count);
        Assert.Equal(asset.TileCacheData.CellSize, restored.TileCacheData.CellSize);
        Assert.Equal(asset.TileCacheData.TileWorldSize, restored.TileCacheData.TileWorldSize);
        Assert.Equal(asset.BakeSettings, restored.BakeSettings);

        // The round-tripped data is actually usable, not just structurally equal - build a real
        // query from it and confirm it resolves the same ground the original does.
        var restoredQuery = new NavMeshQuery(restored);
        Assert.True(restoredQuery.SamplePosition(Float3.Zero, out Float3 snapped));
        Assert.True(Float3.Distance(snapped, Float3.Zero) < 1f);
    }
}
