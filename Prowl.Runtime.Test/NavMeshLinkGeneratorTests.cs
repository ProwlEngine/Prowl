// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshLinkGenerator"/> - jump and drop links produced automatically at bake time
/// from an agent type's own <see cref="NavMeshBakeSettings.JumpDistance"/>/<see cref="NavMeshBakeSettings.DropHeight"/>,
/// against a fixture with three separate platforms: one level and close enough to jump to (bidirectional),
/// one lower and close enough to drop to (one-directional), and one close enough in principle but with a
/// real wall standing in the gap (no link at all).
/// </summary>
public class NavMeshLinkGeneratorTests : RuntimeTestBase
{
    private static Mesh CreateHorizontalQuad(float width, float depth)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;
        return new Mesh
        {
            Vertices = [new(-hw, 0, -hd), new(hw, 0, -hd), new(hw, 0, hd), new(-hw, 0, hd)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    private GameObject AddPlatform(Scene scene, string name, Float3 position, float size = 6f)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        go.AddComponent<MeshRenderer>().Mesh = CreateHorizontalQuad(size, size);
        scene.Add(go);
        return go;
    }

    // A tall, thin vertical wall (a horizontal quad tipped up on edge) spanning the given width at a
    // fixed Z - too steep to ever become walkable itself, but present in the raw collected triangle
    // soup NavMeshLinkGenerator's clearance check scans.
    private GameObject AddWall(Scene scene, string name, float centerX, float z, float width, float height)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = new Float3(centerX, height * 0.5f, z);
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(90, 0, 0));
        go.AddComponent<MeshRenderer>().Mesh = CreateHorizontalQuad(width, height);
        scene.Add(go);
        return go;
    }

    private NavMeshBuildResult Bake(Scene scene, NavMeshBakeSettings settings)
    {
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);
        return new RecastNavMeshBuilder().Build(input, settings);
    }

    [Fact]
    public void Generate_ProducesABidirectionalJumpAndAOneWayDrop_ButNoLinkAcrossAWall()
    {
        Scene scene = CreateScene(enable: true);

        // Platform A: the hub every other platform is tested against. Every gap below is nominally 0.6
        // units, deliberately narrow - contour simplification erodes a baked boundary further inward
        // than agent radius alone would, so a generous JumpDistance/DropHeight margin (well beyond the
        // nominal gap) is what actually makes this fixture robust rather than pinned to an exact figure.
        AddPlatform(scene, "A", new Float3(0, 0, 0));             // x:-3..3, z:-3..3, y=0

        // Platform B: level with A, a gap along +X - should produce a bidirectional jump link.
        AddPlatform(scene, "B", new Float3(6.6f, 0, 0));          // x:3.6..9.6, z:-3..3, y=0

        // Platform C: 2 units lower than A, a gap along -Z - should produce a one-way drop link from A
        // down to C (not the other way - C is too low to jump back up from within level tolerance).
        AddPlatform(scene, "C", new Float3(0, -2, -6.6f));        // x:-3..3, z:-9.6..-3.6, y=-2

        // Platform D: level with A, a same-sized gap along +Z - but a real wall fills it, so no link
        // should be generated here at all despite being otherwise identical to the A-B case.
        AddPlatform(scene, "D", new Float3(0, 0, 6.6f));          // x:-3..3, z:3.6..9.6, y=0
        AddWall(scene, "Wall", centerX: 0, z: 3.3f, width: 8f, height: 3f);

        NavMeshBakeSettings settings = NavMeshBakeSettings.Default;
        settings.JumpDistance = 6f;
        settings.DropHeight = 6f;

        NavMeshBuildResult result = Bake(scene, settings);
        Assert.True(result.Success, result.Error);

        var generated = result.TileCacheData!.GeneratedLinks;
        Assert.NotEmpty(generated);

        // Membership in a platform's own footprint (generous relative to its nominal half-extent of 3,
        // since contour simplification erodes a baked boundary further inward than agent radius alone
        // would - a fixed edge coordinate isn't a reliable thing to pin a test to, but "somewhere over
        // this platform" is) rather than pinning a link to one specific pair of edges: a generous
        // JumpDistance/DropHeight (needed for robustness against that same erosion) legitimately finds
        // more than one valid crossing between two platforms with multiple boundary edges each, and any
        // of them satisfies what this test actually cares about.
        static bool Over(Float3 p, Float3 center) => Maths.Abs(p.X - center.X) <= 3f && Maths.Abs(p.Z - center.Z) <= 3f;

        var a = new Float3(0, 0, 0);
        var b = new Float3(6.6f, 0, 0);
        var c = new Float3(0, -2, -6.6f);
        var d = new Float3(0, 0, 6.6f);

        string Describe() => string.Join(" | ", generated.Select(l => $"({l.Start}->{l.End}, bidir={l.Bidirectional})"));

        // A <-> B: a bidirectional jump link somewhere between the two platforms.
        Assert.True(
            generated.Any(l => l.Bidirectional && ((Over(l.Start, a) && Over(l.End, b)) || (Over(l.Start, b) && Over(l.End, a)))),
            $"Expected a bidirectional jump link between A and B. Generated: {Describe()}");

        // A -> C: a one-directional drop link from the higher platform down to the lower one.
        Assert.True(
            generated.Any(l => !l.Bidirectional && l.Start.Y > l.End.Y
                && ((Over(l.Start, a) && Over(l.End, c)) || (Over(l.Start, c) && Over(l.End, a)))),
            $"Expected a one-directional drop link from A down to C. Generated: {Describe()}");

        // A <-> D: nothing at all, since the wall blocks the only straight line between them.
        Assert.DoesNotContain(generated, l => (Over(l.Start, a) && Over(l.End, d)) || (Over(l.Start, d) && Over(l.End, a)));
    }

    [Fact]
    public void Generate_ReturnsEmpty_WhenNeitherJumpDistanceNorDropHeightIsSet()
    {
        Scene scene = CreateScene(enable: true);
        AddPlatform(scene, "A", new Float3(0, 0, 0));
        AddPlatform(scene, "B", new Float3(8, 0, 0));

        NavMeshBuildResult result = Bake(scene, NavMeshBakeSettings.Default); // JumpDistance/DropHeight both default to 0
        Assert.True(result.Success, result.Error);

        Assert.Empty(result.TileCacheData!.GeneratedLinks);
    }

    [Fact]
    public void GeneratedLinks_AreWovenIntoTheQuery_AndBridgeTheGap()
    {
        Scene scene = CreateScene(enable: true);
        AddPlatform(scene, "A", new Float3(0, 0, 0));
        AddPlatform(scene, "B", new Float3(6.6f, 0, 0));

        NavMeshBakeSettings settings = NavMeshBakeSettings.Default;
        settings.JumpDistance = 6f;

        NavMeshBuildResult result = Bake(scene, settings);
        Assert.True(result.Success, result.Error);
        Assert.NotEmpty(result.TileCacheData!.GeneratedLinks);

        var asset = new NavMesh();
        asset.Apply(result, settings, NavMeshAgentTypes.HumanoidId);
        var query = new NavMeshQuery(asset);

        NavMeshPath path = query.FindPath(new Float3(-2, 0, 0), new Float3(8, 0, 0));
        Assert.True(path.Success);
        Assert.False(path.Partial, "The generated jump link should bridge the gap with a complete, non-partial path.");
        Assert.Contains(true, path.CornerIsOffMeshLink);
    }
}
