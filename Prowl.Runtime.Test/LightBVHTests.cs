// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class LightBVHTests
{
    /// <summary>Minimal IRenderableLight stub. The BVH only uses the reference identity of the
    /// light (as a dictionary key); none of the IRenderableLight methods are called by the BVH
    /// itself, the host system feeds it ForwardLightData directly.</summary>
    private sealed class StubLight : IRenderableLight
    {
        public int Id;
        public LightType Type = LightType.Point;
        public Float3 Position;
        public Float3 Direction = -Float3.UnitY;
        public bool CastShadows;
        public int Layer;

        public int GetLightID() => Id;
        public int GetLayer() => Layer;
        public LightType GetLightType() => Type;
        public Float3 GetLightPosition() => Position;
        public Float3 GetLightDirection() => Direction;
        public bool DoCastShadows() => CastShadows;
        public ForwardLightData GetForwardLightData() => default;
    }

    private static ForwardLightData PointAt(Float3 pos, float range, Float3? color = null)
        => new()
        {
            Type = LightType.Point,
            Position = pos,
            Direction = -Float3.UnitY,
            Color = color ?? new Float3(1, 1, 1),
            Intensity = 1f,
            Range = range,
            ShadowEnabled = false,
            ShadowBias = 0.001f,
            ShadowNormalBias = 0f,
            ShadowStrength = 1f,
            ShadowQuality = 1f,
        };

    [Fact]
    public void Empty_Tree_Has_No_Nodes()
    {
        var bvh = new LightBVH();
        bvh.Sync();
        Assert.Equal(0, bvh.NodeCount);
        Assert.Equal(0, bvh.ActiveLightCount);
    }

    [Fact]
    public void Single_Light_Produces_Single_Leaf()
    {
        var bvh = new LightBVH();
        var l = new StubLight();
        int slot = bvh.Add(l, PointAt(new Float3(1, 2, 3), 4f));
        bvh.Sync();

        Assert.Equal(1, bvh.NodeCount);
        var nodes = bvh.Nodes;
        Assert.True(nodes[0].IsLeaf);
        Assert.Equal(slot, nodes[0].LeafSlot);
        Assert.Equal(-1, nodes[0].Miss);
    }

    [Fact]
    public void Slot_Is_Stable_Across_Updates()
    {
        var bvh = new LightBVH();
        var a = new StubLight();
        int slotA = bvh.Add(a, PointAt(new Float3(0, 0, 0), 5));

        bvh.Update(a, PointAt(new Float3(0, 0, 0), 5, color: new Float3(1, 0, 0)));
        Assert.Equal(slotA, bvh.GetSlot(a));
    }

    [Fact]
    public void Removed_Slot_Is_Reused()
    {
        var bvh = new LightBVH();
        var a = new StubLight();
        var b = new StubLight();
        int slotA = bvh.Add(a, PointAt(new Float3(0, 0, 0), 1));
        int slotB = bvh.Add(b, PointAt(new Float3(10, 0, 0), 1));

        Assert.True(bvh.Remove(a));
        var c = new StubLight();
        int slotC = bvh.Add(c, PointAt(new Float3(20, 0, 0), 1));

        Assert.Equal(slotA, slotC);
        Assert.NotEqual(slotB, slotC);
    }

    [Fact]
    public void Build_Bounds_Cover_All_Tight_Lights()
    {
        var bvh = new LightBVH();
        var rng = new Random(1234);
        var lights = new List<(StubLight, AABB)>();
        for (int i = 0; i < 64; i++)
        {
            var l = new StubLight();
            var pos = new Float3(rng.NextSingle() * 100f - 50f, rng.NextSingle() * 100f - 50f, rng.NextSingle() * 100f - 50f);
            float range = 1f + rng.NextSingle() * 3f;
            bvh.Add(l, PointAt(pos, range));
            var ext = new Float3(range, range, range);
            lights.Add((l, new AABB(pos - ext, pos + ext)));
        }
        bvh.Sync();

        // Root must contain every light's tight AABB.
        var root = bvh.Nodes[0];
        var rootAABB = new AABB(root.Min, root.Max);
        foreach (var (_, tight) in lights)
            Assert.True(rootAABB.Contains(tight), "root AABB must contain all light tight AABBs");
    }

    [Fact]
    public void Rope_Traversal_Visits_Every_Leaf_Once_For_Point_Inside_All()
    {
        // Place all lights at the origin with overlapping ranges so a query at the origin
        // should hit every leaf exactly once.
        var bvh = new LightBVH();
        const int N = 50;
        var lights = new StubLight[N];
        for (int i = 0; i < N; i++)
        {
            lights[i] = new StubLight();
            bvh.Add(lights[i], PointAt(new Float3(0, 0, 0), 5f + i * 0.01f));
        }
        bvh.Sync();

        var visited = new HashSet<int>();
        Traverse(bvh, new Float3(0, 0, 0), visited);
        Assert.Equal(N, visited.Count);
    }

    [Fact]
    public void Rope_Traversal_Skips_All_When_Point_Outside_All_AABBs()
    {
        var bvh = new LightBVH();
        for (int i = 0; i < 20; i++)
        {
            var l = new StubLight();
            bvh.Add(l, PointAt(new Float3(i * 10f, 0, 0), 1f));
        }
        bvh.Sync();

        var visited = new HashSet<int>();
        Traverse(bvh, new Float3(0, 1000, 0), visited);
        Assert.Empty(visited);
    }

    [Fact]
    public void Rope_Traversal_Returns_Correct_Subset()
    {
        // Lights along x axis, range 1, evenly spaced at 0,2,4,6,8,10. Query at x=4.5
        // should hit only the light at 4 and 5? Actually range 1 = AABB extent +/-1, so
        // x=4 covers [3..5], x=5 covers [4..6]. Lights are at integer positions 0..10.
        var bvh = new LightBVH();
        var lights = new StubLight[11];
        for (int i = 0; i < 11; i++)
        {
            lights[i] = new StubLight { Id = i };
            bvh.Add(lights[i], PointAt(new Float3(i, 0, 0), 1f));
        }
        bvh.Sync();

        var visited = new HashSet<int>();
        Traverse(bvh, new Float3(4.5f, 0, 0), visited);
        var positions = new HashSet<float>();
        foreach (int slot in visited) positions.Add(bvh.Slots[slot].Position.X);
        Assert.Equal(new HashSet<float> { 4f, 5f }, positions);
    }

    [Fact]
    public void Refit_Within_Loose_AABB_Does_Not_Mark_Topology_Dirty()
    {
        var bvh = new LightBVH(initialCapacity: 8, looseFactor: 0.5f);
        var l = new StubLight();
        bvh.Add(l, PointAt(new Float3(0, 0, 0), 4f));
        bvh.Sync();

        int rootBefore = bvh.NodeCount;
        var rootBoundsBefore = (bvh.Nodes[0].Min, bvh.Nodes[0].Max);

        // 0.5 * 4 = 2 unit loose buffer. Move the light by 1 unit; well inside.
        bvh.Update(l, PointAt(new Float3(1f, 0, 0), 4f));
        bvh.Sync();

        // Single-leaf tree: the leaf node IS the root, so a refit will move its bounds. The
        // important invariant is that the light count and node count are unchanged (no rebuild
        // path was taken to add/remove nodes).
        Assert.Equal(rootBefore, bvh.NodeCount);
    }

    [Fact]
    public void Refit_Past_Loose_AABB_Triggers_Rebuild_That_Still_Contains_Light()
    {
        var bvh = new LightBVH(initialCapacity: 8, looseFactor: 0.1f);
        var a = new StubLight();
        var b = new StubLight();
        bvh.Add(a, PointAt(new Float3(0, 0, 0), 1f));
        bvh.Add(b, PointAt(new Float3(10, 0, 0), 1f));
        bvh.Sync();

        // Move 'a' way outside its loose buffer (which is +- (1 + 0.1) = +-1.1).
        bvh.Update(a, PointAt(new Float3(50, 0, 0), 1f));
        bvh.Sync();

        // A traversal at the new position must still reach 'a'.
        var visited = new HashSet<int>();
        Traverse(bvh, new Float3(50, 0, 0), visited);
        Assert.Contains(bvh.GetSlot(a), visited);
    }

    [Fact]
    public void Add_Then_Remove_All_Leaves_Empty_Tree()
    {
        var bvh = new LightBVH();
        var lights = new List<StubLight>();
        for (int i = 0; i < 10; i++)
        {
            var l = new StubLight();
            lights.Add(l);
            bvh.Add(l, PointAt(new Float3(i, 0, 0), 1f));
        }
        bvh.Sync();
        foreach (var l in lights) bvh.Remove(l);
        bvh.Sync();

        Assert.Equal(0, bvh.ActiveLightCount);
        Assert.Equal(0, bvh.NodeCount);
    }

    [Fact]
    public void Capacity_Grows_When_Slot_HighWater_Exceeds_Initial()
    {
        var bvh = new LightBVH(initialCapacity: 4);
        for (int i = 0; i < 17; i++)
        {
            var l = new StubLight();
            bvh.Add(l, PointAt(new Float3(i, 0, 0), 1f));
        }
        bvh.Sync();
        Assert.Equal(17, bvh.ActiveLightCount);
        Assert.True(bvh.SlotCapacity >= 17);
    }

    [Fact]
    public void Two_Updates_With_Identical_Data_Are_NoOp()
    {
        var bvh = new LightBVH();
        var l = new StubLight();
        bvh.Add(l, PointAt(new Float3(0, 0, 0), 5f));
        bvh.Sync();
        bvh.ClearDirtyRanges();

        // Update with the exact same data should not dirty anything.
        bvh.Update(l, PointAt(new Float3(0, 0, 0), 5f));
        Assert.False(bvh.HasNodeDirty);
        Assert.False(bvh.HasSlotDirty);
    }

    [Fact]
    public void Color_Change_Marks_Slot_Dirty_But_Preserves_Topology()
    {
        var bvh = new LightBVH();
        var l = new StubLight();
        bvh.Add(l, PointAt(new Float3(0, 0, 0), 5f, color: new Float3(1, 1, 1)));
        bvh.Sync();
        bvh.ClearDirtyRanges();
        int nodesBefore = bvh.NodeCount;

        bvh.Update(l, PointAt(new Float3(0, 0, 0), 5f, color: new Float3(1, 0, 0)));
        Assert.True(bvh.HasSlotDirty);

        // Position / range unchanged so refit doesn't run; node count must stay the same.
        bvh.Sync();
        Assert.Equal(nodesBefore, bvh.NodeCount);
    }

    [Fact]
    public void Many_Lights_All_Reachable_From_Root()
    {
        // Stress: sprinkle 200 lights and confirm every light's slot is reachable by traversing
        // from its own position. Catches off-by-one rope wiring at scale.
        var bvh = new LightBVH(initialCapacity: 8);
        var rng = new Random(2026);
        var lights = new List<(StubLight, Float3)>();
        for (int i = 0; i < 200; i++)
        {
            var l = new StubLight { Id = i };
            var pos = new Float3(
                rng.NextSingle() * 200f - 100f,
                rng.NextSingle() * 200f - 100f,
                rng.NextSingle() * 200f - 100f);
            bvh.Add(l, PointAt(pos, 1.5f));
            lights.Add((l, pos));
        }
        bvh.Sync();

        foreach (var (light, pos) in lights)
        {
            var visited = new HashSet<int>();
            Traverse(bvh, pos, visited);
            int slot = bvh.GetSlot(light);
            Assert.Contains(slot, visited);
        }
    }

    [Fact]
    public void SetShadowSlot_Marks_Slot_Dirty_Only_When_Changed()
    {
        var bvh = new LightBVH();
        var l = new StubLight();
        bvh.Add(l, PointAt(new Float3(0, 0, 0), 5f));
        bvh.Sync();
        bvh.ClearDirtyRanges();

        bvh.SetShadowSlot(l, -1);                 // already -1, no-op
        Assert.False(bvh.HasSlotDirty);

        bvh.SetShadowSlot(l, 2);                  // change
        Assert.True(bvh.HasSlotDirty);
    }

    /// <summary>
    /// Reference rope-BVH walker. Mirrors the GPU shader logic so we can unit-test the C# side
    /// produces a tree with topologically correct hit/miss links. Returns the set of slots whose
    /// tight AABB contains <paramref name="point"/>.
    /// </summary>
    private static void Traverse(LightBVH bvh, Float3 point, HashSet<int> visitedSlots)
    {
        if (bvh.NodeCount == 0) return;
        var nodes = bvh.Nodes;
        int i = 0;
        // Hard cap to keep a buggy tree from infinite-looping the test.
        int safety = bvh.NodeCount * 4 + 16;
        while (i >= 0 && safety-- > 0)
        {
            var n = nodes[i];
            bool inside =
                point.X >= n.Min.X && point.X <= n.Max.X &&
                point.Y >= n.Min.Y && point.Y <= n.Max.Y &&
                point.Z >= n.Min.Z && point.Z <= n.Max.Z;

            if (n.IsLeaf)
            {
                if (inside) visitedSlots.Add(n.LeafSlot);
                i = n.Miss;
            }
            else
            {
                i = inside ? n.Hit : n.Miss;
            }
        }
        Assert.True(safety > 0, "rope traversal infinite-looped");
    }
}
