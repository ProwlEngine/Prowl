using System;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;


internal sealed class BenchMesh : IDisposable
{
    private readonly DeviceBuffer[] _buffers;

    public VertexSource Source { get; }
    public uint IndexCount { get; }

    private BenchMesh(VertexSource source, DeviceBuffer[] buffers, uint indexCount)
    {
        Source = source;
        _buffers = buffers;
        IndexCount = indexCount;
    }

    public static BenchMesh CreateCube(GraphicsDevice gd)
    {
        Float3[] positions = new Float3[24];
        Float2[] uvs = new Float2[24];
        ushort[] indices = new ushort[36];

        Float3[] normals =
        [
            new(0, 0, -1), new(0, 0, 1), new(-1, 0, 0),
            new(1, 0, 0), new(0, -1, 0), new(0, 1, 0)
        ];

        for (int face = 0; face < 6; face++)
        {
            Float3 n = normals[face];
            Float3 up = MathF.Abs(n.Y) > 0.5f ? new Float3(0, 0, 1) : new Float3(0, 1, 0);
            Float3 right = Float3.Cross(up, n);

            int v = face * 4;
            positions[v + 0] = (n - right - up) * 0.5f;
            positions[v + 1] = (n + right - up) * 0.5f;
            positions[v + 2] = (n + right + up) * 0.5f;
            positions[v + 3] = (n - right + up) * 0.5f;

            uvs[v + 0] = new Float2(0, 0);
            uvs[v + 1] = new Float2(1, 0);
            uvs[v + 2] = new Float2(1, 1);
            uvs[v + 3] = new Float2(0, 1);

            int t = face * 6;
            indices[t + 0] = (ushort)(v + 0);
            indices[t + 1] = (ushort)(v + 1);
            indices[t + 2] = (ushort)(v + 2);
            indices[t + 3] = (ushort)(v + 0);
            indices[t + 4] = (ushort)(v + 2);
            indices[t + 5] = (ushort)(v + 3);
        }

        return Create(gd, positions, uvs, indices, IndexFormat.UInt16);
    }

    public static BenchMesh CreateQuad(GraphicsDevice gd)
    {
        Float3[] positions = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)];
        Float2[] uvs = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        ushort[] indices = [0, 1, 2, 0, 2, 3];
        return Create(gd, positions, uvs, indices, IndexFormat.UInt16);
    }

    public static BenchMesh CreateGrid(GraphicsDevice gd, int cells)
    {
        int side = cells + 1;
        Float3[] positions = new Float3[side * side];
        Float2[] uvs = new Float2[side * side];
        uint[] indices = new uint[cells * cells * 6];

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                Float2 uv = new(x / (float)cells, y / (float)cells);
                positions[y * side + x] = new Float3(uv.X * 2 - 1, uv.Y * 2 - 1, 0);
                uvs[y * side + x] = uv;
            }
        }

        int t = 0;
        for (int y = 0; y < cells; y++)
        {
            for (int x = 0; x < cells; x++)
            {
                uint v = (uint)(y * side + x);
                indices[t++] = v;
                indices[t++] = v + 1;
                indices[t++] = v + (uint)side + 1;
                indices[t++] = v;
                indices[t++] = v + (uint)side + 1;
                indices[t++] = v + (uint)side;
            }
        }

        return Create(gd, positions, uvs, indices, IndexFormat.UInt32);
    }

    private static BenchMesh Create<TIndex>(GraphicsDevice gd, Float3[] positions, Float2[] uvs, TIndex[] indices, IndexFormat format) where TIndex : unmanaged
    {
        DeviceBuffer positionBuffer = Upload(gd, positions, BufferUsage.VertexBuffer);
        DeviceBuffer uvBuffer = Upload(gd, uvs, BufferUsage.VertexBuffer);
        DeviceBuffer indexBuffer = Upload(gd, indices, BufferUsage.IndexBuffer);

        VertexSource source = new VertexSource()
            .SetBuffer("POSITION0", positionBuffer)
            .SetBuffer("UV0", uvBuffer)
            .SetIndexBuffer(indexBuffer, format, (uint)indices.Length);

        return new BenchMesh(source, [positionBuffer, uvBuffer, indexBuffer], (uint)indices.Length);
    }

    private static DeviceBuffer Upload<T>(GraphicsDevice gd, T[] data, BufferUsage usage) where T : unmanaged
    {
        uint stride = (uint)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        DeviceBuffer buffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(stride * (uint)data.Length, usage));
        gd.UpdateBuffer(buffer, 0, data);
        return buffer;
    }

    public void Dispose()
    {
        foreach (DeviceBuffer buffer in _buffers)
            buffer.Dispose();
    }
}
