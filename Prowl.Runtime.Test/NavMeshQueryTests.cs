// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshQuery"/> against the same kind of simple baked fixtures
/// <see cref="NavMeshBuildTests"/> uses: a straight path on a flat plane, a path that has to detour
/// around a gap, a start point that is off the mesh but within snapping range, and a destination with
/// nothing baked anywhere near it.
/// </summary>
public class NavMeshQueryTests : RuntimeTestBase
{
    private static readonly NavMeshBakeSettings s_settings = NavMeshBakeSettings.Default;

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

    private GameObject AddQuad(Scene scene, string name, float width, float depth, Float3 position)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = CreateQuad(width, depth);
        scene.Add(go);
        return go;
    }

    private static NavMeshQuery BakeQuery(Scene scene, NavMeshBakeSettings settings)
    {
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, settings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, settings, NavMeshAgentTypes.HumanoidId);
        return new NavMeshQuery(asset);
    }

    [Fact]
    public void FindPath_OnFlatPlane_ReturnsStraightPath()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 20, 20, Float3.Zero);
        NavMeshQuery query = BakeQuery(scene, s_settings);

        var start = new Float3(-8, 0, 0);
        var end = new Float3(8, 0, 0);
        NavMeshPath path = query.FindPath(start, end);

        Assert.True(path.Success);
        Assert.False(path.Partial);
        Assert.True(path.Corners.Length >= 2);
        AssertNear(start, path.Corners[0], 1.0f);
        AssertNear(end, path.Corners[^1], 1.0f);
        Assert.NotEmpty(path.CorridorPolygons);
    }

    [Fact]
    public void FindPath_AroundAGap_DetoursThroughTheOpening()
    {
        Scene scene = CreateScene(enable: true);
        // A "U" shaped floor: a bottom crossbar plus two side columns, with the middle-top area left
        // empty. A straight line from one column's top to the other's crosses that empty gap, so any
        // path between them has to detour down through the crossbar and back up the other column.
        AddQuad(scene, "Bottom", 10, 2, new Float3(0, 0, -4));
        AddQuad(scene, "LeftColumn", 2, 6, new Float3(-4, 0, 0));
        AddQuad(scene, "RightColumn", 2, 6, new Float3(4, 0, 0));
        NavMeshQuery query = BakeQuery(scene, s_settings);

        var start = new Float3(-4, 0, 2.5f);
        var end = new Float3(4, 0, 2.5f);
        NavMeshPath path = query.FindPath(start, end);

        Assert.True(path.Success);
        Assert.True(path.Corners.Length > 2, "a detour around the gap needs more than a straight two-point path");

        // Every corner should stay near one of the three slabs (i.e. actually on baked ground), and at
        // least one corner should dip down near the crossbar rather than cutting straight across the
        // empty middle at z close to the start/end height.
        bool wentThroughCrossbar = false;
        foreach (Float3 c in path.Corners)
            if (c.Z < -2f) wentThroughCrossbar = true;
        Assert.True(wentThroughCrossbar, "path should route down through the bottom crossbar, not cut through the gap");
    }

    [Fact]
    public void FindPath_StartAboveTheMesh_SnapsDownToIt()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 20, 20, Float3.Zero);
        NavMeshQuery query = BakeQuery(scene, s_settings);

        // Well within the search extents (agent height + margin) but not actually on the mesh.
        var start = new Float3(-8, 0.5f, 0);
        var end = new Float3(8, 0, 0);
        NavMeshPath path = query.FindPath(start, end);

        Assert.True(path.Success);
        // The snapped start corner should have settled onto the ground plane, not stayed at y=0.5.
        // Tolerance is a cell height, not zero: the nearest-poly height estimate is voxel-quantized.
        Assert.True(Maths.Abs(path.Corners[0].Y) <= s_settings.CellHeight + 0.05f);
    }

    [Fact]
    public void FindPath_UnreachableEnd_ReturnsNone()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 10, 10, Float3.Zero);
        NavMeshQuery query = BakeQuery(scene, s_settings);

        var start = new Float3(0, 0, 0);
        var end = new Float3(500, 0, 500); // nothing baked anywhere near here
        NavMeshPath path = query.FindPath(start, end);

        Assert.False(path.Success);
        Assert.Empty(path.Corners);
    }

    [Fact]
    public void Raycast_AcrossOpenGround_ReachesTheTarget()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 20, 20, Float3.Zero);
        NavMeshQuery query = BakeQuery(scene, s_settings);

        bool hit = query.Raycast(new Float3(-8, 0, 0), new Float3(8, 0, 0), out Float3 hitPoint);

        Assert.False(hit); // open ground the whole way: no wall to hit before the target
        AssertNear(new Float3(8, 0, 0), hitPoint, 0.01f);
    }

    [Fact]
    public void SamplePosition_OnGround_ReturnsGroundHeight()
    {
        Scene scene = CreateScene(enable: true);
        AddQuad(scene, "Ground", 10, 10, Float3.Zero);
        NavMeshQuery query = BakeQuery(scene, s_settings);

        bool found = query.SamplePosition(new Float3(1, 2, 1), out Float3 result);

        Assert.True(found);
        // Tolerance is a cell height, not zero: the nearest-poly height estimate is voxel-quantized.
        Assert.True(Maths.Abs(result.Y) <= s_settings.CellHeight + 0.05f);
    }

    private static void AssertNear(Float3 expected, Float3 actual, float tolerance)
    {
        Assert.True(Float3.Distance(expected, actual) <= tolerance,
            $"expected {expected} to be within {tolerance} of {actual}, distance was {Float3.Distance(expected, actual)}");
    }
}
