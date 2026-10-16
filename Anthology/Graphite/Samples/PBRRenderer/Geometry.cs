using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.Rendering;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class MeshBuilder
{
    private readonly List<Float3> _positions = new();
    private readonly List<Float3> _normals = new();
    private readonly List<Float2> _uvs = new();
    private readonly List<uint> _indices = new();

    public uint Vertex(Float3 position, Float3 normal, Float2 uv)
    {
        _positions.Add(position);
        _normals.Add(normal);
        _uvs.Add(uv);
        return (uint)_positions.Count - 1;
    }

    public void Triangle(uint a, uint b, uint c)
    {
        Float3 cross = Float3.Cross(_positions[(int)b] - _positions[(int)a], _positions[(int)c] - _positions[(int)a]);
        Float3 normal = _normals[(int)a] + _normals[(int)b] + _normals[(int)c];
        if (Float3.Dot(cross, normal) >= 0)
            _indices.AddRange([a, b, c]);
        else
            _indices.AddRange([a, c, b]);
    }

    public void Quad(uint a, uint b, uint c, uint d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    public MeshData Build() => new()
    {
        VertexCount = (uint)_positions.Count,
        Streams =
        [
            Stream(VertexSemantic.Position, VertexElementFormat.Float3, _positions),
            Stream(VertexSemantic.Normal, VertexElementFormat.Float3, _normals),
            Stream(VertexSemantic.UV0, VertexElementFormat.Float2, _uvs),
        ],
        Indices = _indices.ToArray(),
    };

    private static VertexStream Stream<T>(VertexSemantic semantic, VertexElementFormat format, List<T> data) where T : struct => new()
    {
        Semantic = semantic,
        Format = format,
        Data = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(data)).ToArray(),
    };
}


internal static class Geometry
{
    public static MeshData Cube()
    {
        MeshBuilder builder = new();
        Float3[] axes = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
        foreach (Float3 n in axes)
        {
            Float3 up = MathF.Abs(n.Y) > 0.5f ? new Float3(0, 0, 1) : new Float3(0, 1, 0);
            Float3 right = Float3.Cross(up, n);
            uint a = builder.Vertex((n - right - up) * 0.5f, n, new Float2(0, 0));
            uint b = builder.Vertex((n + right - up) * 0.5f, n, new Float2(1, 0));
            uint c = builder.Vertex((n + right + up) * 0.5f, n, new Float2(1, 1));
            uint d = builder.Vertex((n - right + up) * 0.5f, n, new Float2(0, 1));
            builder.Quad(a, b, c, d);
        }
        return builder.Build();
    }

    public static MeshData Sphere(int rings = 32, int segments = 64)
    {
        MeshBuilder builder = new();
        for (int y = 0; y <= rings; y++)
        {
            float v = y / (float)rings;
            float phi = v * MathF.PI;
            for (int x = 0; x <= segments; x++)
            {
                float u = x / (float)segments;
                float theta = u * MathF.PI * 2;
                Float3 n = new(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                builder.Vertex(n * 0.5f, n, new Float2(u, v));
            }
        }

        uint stride = (uint)segments + 1;
        for (int y = 0; y < rings; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                uint a = (uint)y * stride + (uint)x;
                builder.Quad(a, a + 1, a + stride + 1, a + stride);
            }
        }
        return builder.Build();
    }

    public static MeshData Cylinder(int segments = 32)
    {
        MeshBuilder builder = new();
        for (int x = 0; x <= segments; x++)
        {
            float u = x / (float)segments;
            float theta = u * MathF.PI * 2;
            Float3 n = new(MathF.Cos(theta), 0, MathF.Sin(theta));
            builder.Vertex(n * 0.5f + new Float3(0, -0.5f, 0), n, new Float2(u, 0));
            builder.Vertex(n * 0.5f + new Float3(0, 0.5f, 0), n, new Float2(u, 1));
        }
        for (int x = 0; x < segments; x++)
        {
            uint a = (uint)x * 2;
            builder.Quad(a, a + 2, a + 3, a + 1);
        }

        foreach (float side in new[] { 1f, -1f })
        {
            Float3 n = new(0, side, 0);
            uint center = builder.Vertex(new Float3(0, side * 0.5f, 0), n, new Float2(0.5f, 0.5f));
            for (int x = 0; x <= segments; x++)
            {
                float theta = x / (float)segments * MathF.PI * 2;
                uint rim = builder.Vertex(new Float3(MathF.Cos(theta) * 0.5f, side * 0.5f, MathF.Sin(theta) * 0.5f), n, new Float2(MathF.Cos(theta) * 0.5f + 0.5f, MathF.Sin(theta) * 0.5f + 0.5f));
                if (x > 0)
                    builder.Triangle(center, rim - 1, rim);
            }
        }
        return builder.Build();
    }

    public static MeshData FromModel(ModelAsset model)
    {
        uint[] indices = new uint[model.Indices.Length];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            indices[i] = model.Indices[i];
            indices[i + 1] = model.Indices[i + 1];
            indices[i + 2] = model.Indices[i + 2];
        }

        return new MeshData
        {
            VertexCount = (uint)model.Positions.Length,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes<Float3>(model.Positions).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes<Float3>(model.Normals).ToArray() },
                new VertexStream { Semantic = VertexSemantic.UV0, Format = VertexElementFormat.Float2, Data = MemoryMarshal.AsBytes<Float2>(model.UV0).ToArray() },
            ],
            Indices = indices,
        };
    }
}
