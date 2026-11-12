using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tools.Tests;

public sealed class LodBuilderTests
{
    // A unit sphere with normals and UVs, its seam column doubled, split into a top and a bottom submesh
    private static MeshData Sphere(int stacks = 24, int slices = 32, bool split = false)
    {
        List<Float3> positions = new(), normals = new();
        List<Float2> uvs = new();
        for (int i = 0; i <= stacks; i++)
        {
            float v = (float)i / stacks, phi = v * MathF.PI;
            for (int j = 0; j <= slices; j++)
            {
                float u = (float)j / slices, theta = u * MathF.PI * 2;
                Float3 p = new(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                positions.Add(p);
                normals.Add(p);
                uvs.Add(new Float2(u, v));
            }
        }
        List<uint> top = new(), bottom = new();
        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint a = (uint)(i * (slices + 1) + j), b = a + 1, c = a + (uint)slices + 1, d = c + 1;
                List<uint> into = split && i >= stacks / 2 ? bottom : top;
                if (i > 0)
                    into.AddRange([a, c, b]);
                if (i < stacks - 1)
                    into.AddRange([b, c, d]);
            }
        }
        uint[] indices = [.. top, .. bottom];
        return new MeshData
        {
            VertexCount = (uint)positions.Count,
            Streams =
            [
                Stream(VertexSemantic.Position, VertexElementFormat.Float3, positions.ToArray()),
                Stream(VertexSemantic.Normal, VertexElementFormat.Float3, normals.ToArray()),
                Stream(VertexSemantic.UV0, VertexElementFormat.Float2, uvs.ToArray()),
            ],
            Indices = indices,
            SubMeshes = split
                ? [new SubMesh { IndexStart = 0, IndexCount = (uint)top.Count }, new SubMesh { IndexStart = (uint)top.Count, IndexCount = (uint)bottom.Count }]
                : [],
        };
    }

    private static VertexStream Stream<T>(VertexSemantic semantic, VertexElementFormat format, T[] data) where T : unmanaged
        => new() { Semantic = semantic, Format = format, Data = MemoryMarshal.AsBytes(data.AsSpan()).ToArray() };

    private static T[] Read<T>(MeshData mesh, VertexSemantic semantic, VertexElementFormat format) where T : unmanaged
    {
        VertexStream stream = mesh.Streams.Single(s => s.Semantic == semantic);
        Assert.Equal(format, stream.Format);
        T[] values = MemoryMarshal.Cast<byte, T>(stream.Data.Span).ToArray();
        Assert.Equal((int)mesh.VertexCount, values.Length);
        return values;
    }

    private static int Triangles(MeshData mesh) => mesh.Indices.Length / 3;

    [Fact]
    public void EachLevel_KeepsAboutItsShareOfTheTriangles()
    {
        MeshData source = Sphere();
        LodChain chain = LodBuilder.Build(source, new LodBuildOptions { Ratios = [0.5f, 0.25f, 0.125f] });

        Assert.Equal(3, chain.Levels.Count);
        int previous = Triangles(source);
        foreach ((MeshData level, float ratio) in chain.Levels.Zip([0.5f, 0.25f, 0.125f]))
        {
            Assert.InRange(Triangles(level), 1, (int)(Triangles(source) * ratio) + 2);
            Assert.True(Triangles(level) < previous);
            Assert.All(level.Indices.ToArray(), index => Assert.True(index < level.VertexCount));
            previous = Triangles(level);
        }
    }

    [Fact]
    public void EveryCorner_IsOneTheSourceHad()
    {
        MeshData source = Sphere();
        HashSet<(Float3, Float3, Float2)> corners = Corners(source);
        foreach (MeshData level in LodBuilder.Build(source).Levels)
            Assert.Subset(corners, Corners(level));
    }

    private static HashSet<(Float3, Float3, Float2)> Corners(MeshData mesh)
    {
        Float3[] positions = Read<Float3>(mesh, VertexSemantic.Position, VertexElementFormat.Float3);
        Float3[] normals = Read<Float3>(mesh, VertexSemantic.Normal, VertexElementFormat.Float3);
        Float2[] uvs = Read<Float2>(mesh, VertexSemantic.UV0, VertexElementFormat.Float2);
        return mesh.Indices.ToArray().Select(i => (positions[i], normals[i], uvs[i])).ToHashSet();
    }

    [Fact]
    public void SubMeshes_KeepTheirOwnTriangles()
    {
        MeshData source = Sphere(split: true);
        foreach (MeshData level in LodBuilder.Build(source).Levels)
        {
            Assert.Equal(2, level.SubMeshes.Length);
            Float3[] positions = Read<Float3>(level, VertexSemantic.Position, VertexElementFormat.Float3);
            uint[] indices = level.Indices.ToArray();
            for (int s = 0; s < 2; s++)
            {
                SubMesh sub = level.SubMeshes[s];
                Assert.True(sub.IndexCount > 0);
                for (uint i = sub.IndexStart; i < sub.IndexStart + sub.IndexCount; i++)
                    Assert.True(s == 0 ? positions[indices[i]].Y >= -1e-5f : positions[indices[i]].Y <= 1e-5f);
            }
        }
    }

    [Fact]
    public void ScreenSizes_FallAsTheErrorsRise()
    {
        LodChain chain = LodBuilder.Build(Sphere());
        Assert.Equal(chain.Levels.Count, chain.Errors.Count);
        Assert.Equal(chain.Levels.Count, chain.ScreenSizes.Count);
        float size = float.PositiveInfinity, error = 0;
        for (int i = 0; i < chain.Levels.Count; i++)
        {
            Assert.True(chain.ScreenSizes[i] > 0 && chain.ScreenSizes[i] < size);
            Assert.True(chain.Errors[i] >= error);
            (size, error) = (chain.ScreenSizes[i], chain.Errors[i]);
        }
    }

    [Fact]
    public void AFlatMesh_SimplifiesWithoutError_ItsSizesStillFalling()
    {
        Float3[] positions = Enumerable.Range(0, 17 * 17).Select(i => new Float3(i % 17, 0, i / 17)).ToArray();
        List<uint> indices = new();
        for (uint z = 0; z < 16; z++)
        {
            for (uint x = 0; x < 16; x++)
            {
                uint a = z * 17 + x;
                indices.AddRange([a, a + 17, a + 1, a + 1, a + 17, a + 18]);
            }
        }
        MeshData plane = new() { VertexCount = (uint)positions.Length, Streams = [Stream(VertexSemantic.Position, VertexElementFormat.Float3, positions)], Indices = indices.ToArray() };

        LodChain chain = LodBuilder.Build(plane);
        Assert.All(chain.Errors, e => Assert.True(e < 1e-4f));
        Assert.True(chain.ScreenSizes[0] <= 1);
        for (int i = 1; i < chain.ScreenSizes.Count; i++)
            Assert.True(chain.ScreenSizes[i] > 0 && chain.ScreenSizes[i] < chain.ScreenSizes[i - 1]);
    }

    [Fact]
    public void AMeshNotOfTriangleLists_IsRejected()
    {
        MeshData lines = Sphere();
        lines.SubMeshes = [new SubMesh { IndexStart = 0, IndexCount = (uint)lines.Indices.Length, Topology = PrimitiveTopology.LineList }];
        Assert.Throws<ArgumentException>(() => LodBuilder.Build(lines));
    }
}
