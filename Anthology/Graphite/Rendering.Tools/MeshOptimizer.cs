using System;

using Prowl.Vector.Geometry;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>
/// Reorders a mesh for the GPU without changing what it draws, with <see cref="VertexCacheOptimizer"/>: each triangle
/// list's triangles for the post transform cache, then the vertices in the order the triangles first use them, every
/// stream and blend shape frame with them. For tools and import, not for frames.
/// </summary>
public static class MeshOptimizer
{
    public static MeshData Optimize(MeshData mesh)
    {
        uint[] indices = mesh.Indices.ToArray();
        if (mesh.SubMeshes.Length == 0)
            VertexCacheOptimizer.Optimize(indices.AsSpan(0, indices.Length / 3 * 3), mesh.VertexCount);
        foreach (SubMesh sub in mesh.SubMeshes)
        {
            if (sub.Topology == PrimitiveTopology.TriangleList)
                VertexCacheOptimizer.Optimize(indices.AsSpan((int)sub.IndexStart, (int)(sub.IndexCount / 3 * 3)), mesh.VertexCount);
        }
        return Reindex(mesh, indices);
    }

    // Vertices renumbered in the order the indices first use them, unused ones after
    private static MeshData Reindex(MeshData mesh, uint[] indices)
    {
        uint count = mesh.VertexCount;
        uint[] remap = VertexCacheOptimizer.FirstUseOrder(indices, count);
        for (int i = 0; i < indices.Length; i++)
            indices[i] = remap[indices[i]];

        VertexStream[] streams = new VertexStream[mesh.Streams.Length];
        for (int s = 0; s < streams.Length; s++)
        {
            VertexStream stream = mesh.Streams[s];
            ReadOnlySpan<byte> data = stream.Data.Span;
            int size = count == 0 ? 0 : data.Length / (int)count;
            byte[] moved = new byte[data.Length];
            for (int v = 0; v < count; v++)
                data.Slice(v * size, size).CopyTo(moved.AsSpan((int)remap[v] * size, size));
            streams[s] = new VertexStream { Semantic = stream.Semantic, Format = stream.Format, Data = moved };
        }

        return new MeshData
        {
            VertexCount = count,
            Streams = streams,
            Indices = indices,
            SubMeshes = mesh.SubMeshes,
            BlendShapes = Array.ConvertAll(mesh.BlendShapes, shape => new BlendShapeData { Name = shape.Name, Frames = Array.ConvertAll(shape.Frames, frame => Remap(frame, remap)) }),
            GpuStreams = mesh.GpuStreams,
            Bounds = mesh.Bounds,
            Displacement = mesh.Displacement,
        };
    }

    // A frame's deltas on the renumbered vertices, still in vertex order
    private static BlendShapeFrame Remap(BlendShapeFrame frame, uint[] remap)
    {
        int[] order = new int[frame.Vertices.Length];
        uint[] vertices = new uint[order.Length];
        for (int i = 0; i < order.Length; i++)
            (order[i], vertices[i]) = (i, remap[frame.Vertices[i]]);
        Array.Sort(vertices, order);
        return new BlendShapeFrame
        {
            Weight = frame.Weight,
            Vertices = vertices,
            Positions = Array.ConvertAll(order, i => frame.Positions[i]),
            Normals = frame.Normals == null ? null : Array.ConvertAll(order, i => frame.Normals[i]),
            Tangents = frame.Tangents == null ? null : Array.ConvertAll(order, i => frame.Tangents[i]),
        };
    }
}
