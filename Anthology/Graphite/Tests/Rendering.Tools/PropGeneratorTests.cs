using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tools.Tests;

public sealed class PropGeneratorTests
{
    private static Float3[] Positions(MeshData mesh) =>
        MemoryMarshal.Cast<byte, Float3>(mesh.Streams.First(s => s.Semantic == VertexSemantic.Position).Data.Span).ToArray();

    private static int Triangles(MeshData mesh, int sub) => (int)mesh.SubMeshes[sub].IndexCount / 3;

    [Fact]
    public void Rock_IsTheSameEveryTimeAndRestsOnTheGround()
    {
        RockDesc desc = new() { Seed = 12, Size = 2f };
        MeshData a = RockGenerator.Build(desc), b = RockGenerator.Build(desc);
        Assert.Equal(a.Indices.ToArray(), b.Indices.ToArray());
        Assert.Equal(Positions(a), Positions(b));
        Float3[] p = Positions(a);
        Assert.Equal(0f, p.Min(v => v.Y), 4);
        Assert.InRange(p.Max(v => v.X) - p.Min(v => v.X), 1.2f, 3f);
    }

    // Closed and wound counter clockwise from outside: every edge is met once each way, and the faces point away from the middle
    [Fact]
    public void Rock_IsClosedAndFacesOut()
    {
        MeshData mesh = RockGenerator.Build(new RockDesc { Seed = 3 }, detail: 2);
        Float3[] p = Positions(mesh);
        uint[] index = mesh.Indices.ToArray();
        Float3 middle = p.Aggregate(Float3.Zero, (s, v) => s + v) / p.Length;
        Dictionary<(Float3, Float3), int> edges = new();
        float outward = 0;
        for (int t = 0; t < index.Length; t += 3)
        {
            Float3 a = p[index[t]], b = p[index[t + 1]], c = p[index[t + 2]];
            outward += Float3.Dot(Float3.Cross(b - a, c - a), (a + b + c) / 3f - middle);
            foreach ((Float3 from, Float3 to) in ((Float3, Float3)[])[(a, b), (b, c), (c, a)])
                edges[(from, to)] = edges.GetValueOrDefault((from, to)) + 1;
        }
        Assert.True(outward > 0);
        Assert.All(edges, e => Assert.Equal(1, edges.GetValueOrDefault((e.Key.Item2, e.Key.Item1))));
    }

    [Fact]
    public void Rock_LevelsGetCoarser()
    {
        RockDesc desc = new() { Seed = 4 };
        int[] counts = [.. Enumerable.Range(0, 3).Select(d => Triangles(RockGenerator.Build(desc, d), 0))];
        Assert.True(counts[0] > counts[1] && counts[1] > counts[2], string.Join(" ", counts));
    }

    [Theory]
    [InlineData(TreeShape.Broadleaf)]
    [InlineData(TreeShape.Conifer)]
    [InlineData(TreeShape.Bush)]
    public void Tree_GrowsBarkAndLeavesToItsHeight(TreeShape shape)
    {
        TreeDesc desc = new() { Seed = 5, Shape = shape, Height = shape == TreeShape.Bush ? 1.5f : 10f, LeafSize = shape == TreeShape.Bush ? 0.45f : 1.1f, Levels = shape == TreeShape.Conifer ? 2 : 3 };
        MeshData a = TreeGenerator.Build(desc), b = TreeGenerator.Build(desc);
        Assert.Equal(Positions(a), Positions(b));
        Assert.Equal(2, a.SubMeshes.Length);
        Assert.True(Triangles(a, 0) > 100 && Triangles(a, 1) > 50, $"{Triangles(a, 0)} {Triangles(a, 1)}");
        float top = Positions(a).Max(v => v.Y);
        Assert.InRange(top, desc.Height * 0.7f, desc.Height * 1.4f);

        int[] counts = [.. Enumerable.Range(0, 3).Select(d => { MeshData m = TreeGenerator.Build(desc, d); return Triangles(m, 0) + Triangles(m, 1); })];
        Assert.True(counts[0] > counts[1] && counts[1] > counts[2], string.Join(" ", counts));
    }

    [Fact]
    public void LeafCards_AreCutOut()
    {
        foreach (PropImage image in (PropImage[])[PropTextures.Leaves(128), PropTextures.Needles(128)])
        {
            int covered = 0;
            for (int i = 3; i < image.Pixels.Length; i += 4)
                covered += image.Pixels[i] > 127 ? 1 : 0;
            int total = image.Pixels.Length / 4;
            Assert.InRange(covered, total / 20, total * 3 / 4);
        }
    }
}
