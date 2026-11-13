using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering.Tools;

// Collects vertices with every stream the prop generators write and triangles per submesh
internal sealed class PropMesh(int subMeshes)
{
    public readonly List<Float3> Positions = new(), Normals = new();
    public readonly List<Float4> Tangents = new(), Colors = new();
    public readonly List<Float2> Uvs = new();
    private readonly List<uint>[] _indices = CreateLists(subMeshes);

    public int Count => Positions.Count;

    public uint Vertex(Float3 position, Float3 normal, Float4 tangent, Float2 uv, Float4 color)
    {
        Positions.Add(position);
        Normals.Add(normal);
        Tangents.Add(tangent);
        Uvs.Add(uv);
        Colors.Add(color);
        return (uint)(Positions.Count - 1);
    }

    public void Triangle(int subMesh, uint a, uint b, uint c) => _indices[subMesh].AddRange([a, b, c]);

    public void Quad(int subMesh, uint a, uint b, uint c, uint d)
    {
        Triangle(subMesh, a, b, c);
        Triangle(subMesh, a, c, d);
    }

    public int Triangles(int subMesh) => _indices[subMesh].Count / 3;

    public MeshData ToMesh()
    {
        List<uint> indices = new();
        SubMesh[] subs = new SubMesh[_indices.Length];
        for (int i = 0; i < _indices.Length; i++)
        {
            subs[i] = new SubMesh { IndexStart = (uint)indices.Count, IndexCount = (uint)_indices[i].Count };
            indices.AddRange(_indices[i]);
        }
        return new MeshData
        {
            VertexCount = (uint)Positions.Count,
            Streams =
            [
                Stream(VertexSemantic.Position, VertexElementFormat.Float3, Positions),
                Stream(VertexSemantic.Normal, VertexElementFormat.Float3, Normals),
                Stream(VertexSemantic.Tangent, VertexElementFormat.Float4, Tangents),
                Stream(VertexSemantic.UV0, VertexElementFormat.Float2, Uvs),
                Stream(VertexSemantic.Color, VertexElementFormat.Float4, Colors),
            ],
            Indices = indices.ToArray(),
            SubMeshes = subs,
        };
    }

    private static List<uint>[] CreateLists(int count)
    {
        List<uint>[] lists = new List<uint>[count];
        for (int i = 0; i < count; i++)
            lists[i] = new List<uint>();
        return lists;
    }

    private static VertexStream Stream<T>(VertexSemantic semantic, VertexElementFormat format, List<T> data) where T : struct
        => new() { Semantic = semantic, Format = format, Data = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(data)).ToArray() };
}
