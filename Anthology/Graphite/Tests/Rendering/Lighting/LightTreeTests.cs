using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Prowl.Graphite.Rendering.Tests;

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
        Vector4[] lights = [.. Enumerable.Repeat(new Vector4(1, 2, 3, 4), 500)];
        foreach (bool morton in new[] { false, true })
        {
            var tree = new LightTreeBuilder();
            tree.Build(lights, 8, morton);
            Assert.Equal(500, Query(tree, lights, new Vector3(1, 2, 3)).Count);
        }
    }
}
