// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Rendering;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Prowl.Runtime.Test;

public class LightTreeTests
{
    private static Vector4[] RandomLights(int count, int seed, float extent, float flatness = 1f)
    {
        var random = new Random(seed);
        var lights = new Vector4[count];
        for (int i = 0; i < count; i++)
            lights[i] = new Vector4(
                (random.NextSingle() * 2f - 1f) * extent,
                (random.NextSingle() * 2f - 1f) * extent * flatness,
                (random.NextSingle() * 2f - 1f) * extent,
                0.5f + random.NextSingle() * 6f);
        return lights;
    }

    // The same walk the shader does: every node whose box holds the point, then every light whose sphere does
    internal static HashSet<int> Query(LightTreeBuilder tree, Vector4[] lights, Vector3 p)
    {
        var found = new HashSet<int>();
        if (tree.NodeCount == 0) return found;
        var stack = new Stack<uint>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            LightTreeNode node = tree.Nodes[(int)stack.Pop()];
            for (int i = 0; i < 4; i++)
            {
                uint child = node.Child(i);
                if (child == LightTreeBuilder.Invalid || !node.Box(i).Contains(p)) continue;
                if ((child & LightTreeBuilder.LeafBit) == 0)
                {
                    stack.Push(child);
                    continue;
                }
                uint first = child & ~LightTreeBuilder.LeafBit;
                for (uint k = first; k < first + node.Count(i); k++)
                {
                    int light = tree.Order[(int)k];
                    Vector4 s = lights[light];
                    if (Vector3.DistanceSquared(p, new Vector3(s.X, s.Y, s.Z)) <= s.W * s.W)
                        found.Add(light);
                }
            }
        }
        return found;
    }

    private static HashSet<int> BruteForce(Vector4[] lights, Vector3 p)
    {
        var found = new HashSet<int>();
        for (int i = 0; i < lights.Length; i++)
            if (Vector3.DistanceSquared(p, new Vector3(lights[i].X, lights[i].Y, lights[i].Z)) <= lights[i].W * lights[i].W)
                found.Add(i);
        return found;
    }

    [Theory]
    [InlineData(false, 1, 8)]
    [InlineData(false, 7, 8)]
    [InlineData(false, 3000, 8)]
    [InlineData(true, 1, 4)]
    [InlineData(true, 9, 4)]
    [InlineData(true, 3000, 4)]
    [InlineData(true, 3000, 64)]
    public void EveryPointFindsExactlyTheLightsReachingIt(bool morton, int count, int leaf)
    {
        Vector4[] lights = RandomLights(count, count * 31 + leaf, 60f);
        var tree = new LightTreeBuilder();
        tree.Build(lights, leaf, morton);

        var random = new Random(5);
        for (int q = 0; q < 400; q++)
        {
            var p = new Vector3((random.NextSingle() * 2f - 1f) * 66f, (random.NextSingle() * 2f - 1f) * 66f, (random.NextSingle() * 2f - 1f) * 66f);
            Assert.Equal(BruteForce(lights, p).OrderBy(i => i), Query(tree, lights, p).OrderBy(i => i));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryLightSitsInExactlyOneLeaf_AndTheShaderStackIsEnough(bool morton)
    {
        Vector4[] lights = RandomLights(20000, 11, 400f, flatness: 0.02f);
        var tree = new LightTreeBuilder();
        tree.Build(lights, morton ? 16 : 8, morton);

        Assert.Equal(Enumerable.Range(0, lights.Length), tree.Order.ToArray().OrderBy(i => i));

        int inLeaves = 0;
        foreach (LightTreeNode node in tree.Nodes)
            for (int i = 0; i < 4; i++)
                if (node.Child(i) != LightTreeBuilder.Invalid && (node.Child(i) & LightTreeBuilder.LeafBit) != 0)
                    inLeaves += (int)node.Count(i);
        Assert.Equal(lights.Length, inLeaves);
        Assert.True(3 * tree.Depth <= LightTreeBuilder.ShaderStack, $"depth {tree.Depth} needs more than {LightTreeBuilder.ShaderStack} stack entries");
    }

    [Fact]
    public void CoincidentLights_StillBuild()
    {
        var lights = Enumerable.Repeat(new Vector4(1, 2, 3, 4), 500).ToArray();
        foreach (bool morton in new[] { false, true })
        {
            var tree = new LightTreeBuilder();
            tree.Build(lights, 8, morton);
            Assert.Equal(500, Query(tree, lights, new Vector3(1, 2, 3)).Count);
        }
    }
}

public class ForwardLightTreesTests
{
    private sealed class StubLight : IRenderableLight
    {
        public int GetLightID() => 0;
        public int GetLayer() => 0;
        public LightType GetLightType() => LightType.Point;
        public Float3 GetLightPosition() => Float3.Zero;
        public Float3 GetLightDirection() => -Float3.UnitY;
        public bool DoCastShadows() => false;
        public ForwardLightData GetForwardLightData() => default;
    }

    private static ForwardLightData PointAt(Float3 position, float range, float intensity = 1f) => new()
    {
        Type = LightType.Point,
        Position = position,
        Direction = -Float3.UnitY,
        Color = new Float3(1, 1, 1),
        Intensity = intensity,
        Range = range,
    };

    // The walk LightTree.glsl does, over the tables as uploaded: both roots, four boxes a node, a sphere test a light.
    // Returns the record index of every light reaching the point.
    private static List<int> Walk(ForwardLightTrees trees, Float3 p)
    {
        var found = new List<int>();
        var stack = new Stack<uint>();
        if (trees.DynamicRoot >= 0) stack.Push((uint)trees.DynamicRoot);
        if (trees.StaticRoot >= 0) stack.Push((uint)trees.StaticRoot);
        while (stack.Count > 0)
        {
            int t = (int)stack.Pop() * LightTreeNode.Vec4Count;
            Float4 minX = trees.NodeTable[t], minY = trees.NodeTable[t + 1], minZ = trees.NodeTable[t + 2];
            Float4 maxX = trees.NodeTable[t + 3], maxY = trees.NodeTable[t + 4], maxZ = trees.NodeTable[t + 5];
            Float4 child = trees.NodeTable[t + 6], count = trees.NodeTable[t + 7];
            for (int i = 0; i < 4; i++)
            {
                bool inside = minX[i] <= p.X && p.X <= maxX[i] && minY[i] <= p.Y && p.Y <= maxY[i] && minZ[i] <= p.Z && p.Z <= maxZ[i];
                if (!inside) continue;
                uint slot = BitConverter.SingleToUInt32Bits(child[i]);
                if ((slot & LightTreeBuilder.LeafBit) == 0)
                {
                    stack.Push(slot);
                    continue;
                }
                int first = (int)(slot & ~LightTreeBuilder.LeafBit);
                int last = first + (int)BitConverter.SingleToUInt32Bits(count[i]);
                for (int k = first; k < last; k++)
                {
                    Float4 sphere = trees.LightTable[k * ForwardLightTrees.TexelsPerLight];
                    if (LengthSquared(p - new Float3(sphere.X, sphere.Y, sphere.Z)) <= sphere.W * sphere.W)
                        found.Add(k);
                }
            }
        }
        return found;
    }

    private static float LengthSquared(Float3 v) => Float3.Dot(v, v);

    // Bits, since an empty child's lanes are NaN
    private static int[] NodeBits(ForwardLightTrees trees) => Enumerable.Range(0, 64)
        .SelectMany(i => new[] { trees.NodeTable[i].X, trees.NodeTable[i].Y, trees.NodeTable[i].Z, trees.NodeTable[i].W })
        .Select(BitConverter.SingleToInt32Bits).ToArray();

    private static Float3 PositionOf(ForwardLightTrees trees, int record)
    {
        Float4 t = trees.LightTable[record * ForwardLightTrees.TexelsPerLight];
        return new Float3(t.X, t.Y, t.Z);
    }

    private static (StubLight Light, ForwardLightData Data)[] Scatter(ForwardLightTrees trees, int count, int seed, Func<int, bool> isStatic)
    {
        var random = new Random(seed);
        var lights = new (StubLight, ForwardLightData)[count];
        for (int i = 0; i < count; i++)
        {
            var position = new Float3((random.NextSingle() * 2f - 1f) * 50f, (random.NextSingle() * 2f - 1f) * 5f, (random.NextSingle() * 2f - 1f) * 50f);
            lights[i] = (new StubLight(), PointAt(position, 1f + random.NextSingle() * 6f));
            trees.Track(lights[i].Item1, lights[i].Item2, isStatic(i));
        }
        return lights;
    }

    private static void AssertEveryPointFindsItsLights((StubLight Light, ForwardLightData Data)[] lights, ForwardLightTrees trees, int seed)
    {
        var random = new Random(seed);
        for (int q = 0; q < 300; q++)
        {
            var p = new Float3((random.NextSingle() * 2f - 1f) * 55f, (random.NextSingle() * 2f - 1f) * 8f, (random.NextSingle() * 2f - 1f) * 55f);
            var expected = lights.Where(l => LengthSquared(p - l.Data.Position) <= l.Data.Range * l.Data.Range)
                                 .Select(l => l.Data.Position).OrderBy(v => v.X).ThenBy(v => v.Z).ToList();
            var found = Walk(trees, p).Select(r => PositionOf(trees, r)).OrderBy(v => v.X).ThenBy(v => v.Z).ToList();
            Assert.Equal(expected, found);
        }
    }

    [Fact]
    public void BothTrees_TogetherFindExactlyTheLightsReachingEachPoint()
    {
        var trees = new ForwardLightTrees();
        var lights = Scatter(trees, 3000, 1, i => i % 3 != 0);
        trees.BeginFrame(null);
        trees.Prepare();

        Assert.Equal(2000, trees.StaticCount);
        Assert.Equal(1000, trees.DynamicVisible);
        AssertEveryPointFindsItsLights(lights, trees, 2);
    }

    [Fact]
    public void DynamicTree_StaysCorrectAcrossTheRing_AsLightsMove()
    {
        var trees = new ForwardLightTrees();
        var lights = Scatter(trees, 600, 3, i => i < 200);
        for (int frame = 0; frame < 5; frame++)
        {
            for (int i = 200; i < lights.Length; i++)
            {
                lights[i].Data.Position += new Float3(1.5f, 0, -0.5f);
                trees.Track(lights[i].Light, lights[i].Data, false);
            }
            trees.BeginFrame(null);
            trees.Prepare();
            AssertEveryPointFindsItsLights(lights, trees, 10 + frame);
        }
    }

    [Fact]
    public void DynamicLights_OutsideTheView_AreCulled()
    {
        var trees = new ForwardLightTrees();
        var ahead = new StubLight();
        var behind = new StubLight();
        trees.Track(ahead, PointAt(new Float3(0, 0, 20), 2f), false);
        trees.Track(behind, PointAt(new Float3(0, 0, -20), 2f), false);

        var view = Float4x4.CreateLookTo(Float3.Zero, Float3.UnitZ, Float3.UnitY);
        var projection = Float4x4.CreatePerspectiveFov(1f, 1f, 0.1f, 100f);
        trees.BeginFrame(Frustum.FromMatrix(projection * view));
        trees.Prepare();

        Assert.Equal(1, trees.DynamicVisible);
        Assert.Single(Walk(trees, new Float3(0, 0, 20)));
        Assert.Empty(Walk(trees, new Float3(0, 0, -20)));
    }

    [Fact]
    public void StaticDataChange_PatchesTheRecord_WithoutRebuilding()
    {
        var trees = new ForwardLightTrees();
        var lights = Scatter(trees, 100, 4, _ => true);
        trees.BeginFrame(null);
        trees.Prepare();
        int root = trees.StaticRoot;
        int[] nodes = NodeBits(trees);

        var changed = lights[37];
        changed.Data.Intensity = 9f;
        trees.Track(changed.Light, changed.Data, true);
        trees.SetShadowSlot(changed.Light, 5);
        trees.BeginFrame(null);
        trees.Prepare();

        Assert.Equal(root, trees.StaticRoot);
        Assert.Equal(nodes, NodeBits(trees));
        int record = Walk(trees, changed.Data.Position).Single(r => PositionOf(trees, r) == changed.Data.Position);
        Assert.Equal(9f, trees.LightTable[record * ForwardLightTrees.TexelsPerLight + 1].W);
        Assert.Equal(5, BitConverter.SingleToInt32Bits(trees.LightTable[record * ForwardLightTrees.TexelsPerLight + 4].Z));
    }

    [Fact]
    public void MovingAStaticLight_RebuildsSoItIsFoundWhereItWent()
    {
        var trees = new ForwardLightTrees();
        var lights = Scatter(trees, 200, 5, _ => true);
        trees.BeginFrame(null);
        trees.Prepare();

        lights[0].Data.Position = new Float3(500, 0, 500);
        trees.Track(lights[0].Light, lights[0].Data, true);
        trees.BeginFrame(null);
        trees.Prepare();

        Assert.Single(Walk(trees, new Float3(500, 0, 500)));
        AssertEveryPointFindsItsLights(lights, trees, 6);
    }

    [Fact]
    public void LightsSwitchingTrees_OrDisappearing_AreFoundOnlyWhereTheyAre()
    {
        var trees = new ForwardLightTrees();
        var lights = Scatter(trees, 300, 7, i => i % 2 == 0);
        trees.BeginFrame(null);
        trees.Prepare();

        for (int i = 0; i < 50; i++)
            trees.Track(lights[i].Light, lights[i].Data, i % 2 != 0);
        var kept = lights.Skip(25).ToArray();
        trees.RemoveUnseen(new HashSet<IRenderableLight>(kept.Select(l => (IRenderableLight)l.Light), ReferenceEqualityComparer.Instance));
        trees.BeginFrame(null);
        trees.Prepare();

        Assert.Equal(275, trees.StaticCount + trees.DynamicCount);
        AssertEveryPointFindsItsLights(kept, trees, 8);
    }
}

public class SceneLightSystemTests : RuntimeTestBase
{
    private sealed class ControlledLight : Light
    {
        public ForwardLightData Data;

        public override LightType GetLightType() => Data.Type;
        public override Float3 GetLightPosition() => Data.Position;
        public override ForwardLightData GetForwardLightData() => Data;
    }

    private ControlledLight CreateLight(bool isStatic)
    {
        var go = CreateGameObject("Light");
        go.IsStatic = isStatic;
        var light = go.AddComponent<ControlledLight>();
        light.Data = new ForwardLightData { Type = LightType.Point, Range = 5f, Color = new Float3(1, 1, 1), Intensity = 1f };
        return light;
    }

    [Fact]
    public void StaticLight_PicksUpShadowAndIntensityChanges()
    {
        var system = new SceneLightSystem();
        var light = CreateLight(isStatic: true);

        system.Reconcile([light], Float3.Zero, LayerMask.Everything);

        light.Data.ShadowEnabled = true;
        light.Data.Intensity = 3f;
        system.Reconcile([light], Float3.Zero, LayerMask.Everything);

        ForwardLightData data = system.Trees.DataOf(light);
        Assert.True(system.Trees.IsStatic(light));
        Assert.True(data.ShadowEnabled);
        Assert.Equal(3f, data.Intensity);
    }

    [Fact]
    public void LightThatStopsCasting_LosesItsShadowSlot()
    {
        var system = new SceneLightSystem();
        var go = CreateGameObject("Lamp");
        go.IsStatic = true;
        var light = go.AddComponent<PointLight>();
        light.Range = 5f;
        go.Transform.Position = new Float3(0, 0, 10);

        var view = Float4x4.CreateLookTo(Float3.Zero, Float3.UnitZ, Float3.UnitY);
        var projection = Float4x4.CreatePerspectiveFov(1f, 1f, 0.1f, 100f);
        var camera = new ShadowCamera(this, Float3.Zero, Frustum.FromMatrix(projection * view), projection, 720,
            ShadowFitView.FromProjection(Float3.Zero, Quaternion.Identity, projection, 0.1f));
        var pipeline = new DefaultRenderPipeline();
        using var shadows = new ShadowRenderer();

        system.Reconcile([light], Float3.Zero, LayerMask.Everything);
        system.RenderShadows(pipeline, shadows, camera, []);
        Assert.True(system.Trees.ShadowSlotOf(light) >= 0);

        light.CastShadows = false;
        system.Reconcile([light], Float3.Zero, LayerMask.Everything);
        system.RenderShadows(pipeline, shadows, camera, []);
        Assert.Equal(-1, system.Trees.ShadowSlotOf(light));
    }

    [Fact]
    public void TwoCameras_EachShadowTheLightInTheirOwnAtlas_UnderOneSharedId()
    {
        var system = new SceneLightSystem();
        var go = CreateGameObject("Lamp");
        var light = go.AddComponent<PointLight>();
        light.Range = 5f;
        go.Transform.Position = new Float3(0, 0, 10);

        var projection = Float4x4.CreatePerspectiveFov(1f, 1f, 0.1f, 100f);
        ShadowCamera CameraAt(object key, Float3 position)
        {
            var view = Float4x4.CreateLookTo(position, Float3.UnitZ, Float3.UnitY);
            return new ShadowCamera(key, position, Frustum.FromMatrix(projection * view), projection, 720,
                ShadowFitView.FromProjection(position, Quaternion.Identity, projection, 0.1f));
        }
        var pipeline = new DefaultRenderPipeline();
        using var near = new ShadowRenderer();
        using var far = new ShadowRenderer();

        system.Reconcile([light], Float3.Zero, LayerMask.Everything);
        int id = system.Trees.ShadowSlotOf(light);
        system.RenderShadows(pipeline, near, CameraAt(near, new Float3(0, 0, 4)), []);
        system.RenderShadows(pipeline, far, CameraAt(far, new Float3(0, 0, -60)), []);

        Assert.True(id >= 0);
        Assert.Equal(id, system.Trees.ShadowSlotOf(light));
        Assert.True(near.GetTileSize(light) > far.GetTileSize(light));
        Assert.Equal(1, near.LightsShadowed);
        Assert.Equal(1, far.LightsShadowed);
    }

    private ControlledLight CreateDirectional(float intensity)
    {
        var light = CreateLight(isStatic: false);
        light.Data.Type = LightType.Directional;
        light.Data.Intensity = intensity;
        return light;
    }

    [Fact]
    public void BrightestDirectional_IsMain_OthersAreExtras()
    {
        var system = new SceneLightSystem();
        var dim = CreateDirectional(1f);
        var bright = CreateDirectional(3f);
        var mid = CreateDirectional(2f);

        system.Reconcile([dim, bright, mid], Float3.Zero, LayerMask.Everything);

        Assert.Same(bright, system.Directional);
        Assert.Equal(2, system.ExtraDirectionals.Count);
        Assert.Contains(dim, system.ExtraDirectionals);
        Assert.Contains(mid, system.ExtraDirectionals);
    }

    [Fact]
    public void ExtraDirectionals_AreCappedKeepingTheBrightest()
    {
        var system = new SceneLightSystem();
        var lights = new List<IRenderableLight>();
        for (int i = 0; i < SceneLightSystem.MaxExtraDirectionalLights + 3; i++)
            lights.Add(CreateDirectional(i + 1));

        system.Reconcile(lights, Float3.Zero, LayerMask.Everything);

        Assert.Equal(SceneLightSystem.MaxExtraDirectionalLights, system.ExtraDirectionals.Count);
        Assert.DoesNotContain(lights[0], system.ExtraDirectionals);
        Assert.DoesNotContain(lights[1], system.ExtraDirectionals);
    }
}

public class ShaderDataTableTests
{
    [Fact]
    public void Capacity_StartsAtWhatWasAskedInWholeRows_ThenAtLeastDoubles_KeepingWhatWasWritten()
    {
        var table = new ShaderDataTable("Block", "_Tex", GraphicsFeature.StorageBuffers);
        table.EnsureCapacity(3000);
        Assert.Equal(3072, table.Capacity);

        table.Write(2999, [new Float4(1, 2, 3, 4)]);
        table.EnsureCapacity(3100);
        Assert.Equal(6144, table.Capacity);
        Assert.Equal(new Float4(1, 2, 3, 4), table[2999]);

        table.EnsureCapacity(100);
        Assert.Equal(6144, table.Capacity);
    }
}
