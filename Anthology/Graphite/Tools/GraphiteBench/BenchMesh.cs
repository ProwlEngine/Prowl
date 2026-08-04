using System;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;


internal sealed class BenchMesh : IVertexSource, IDisposable
{
    private readonly VertexAttributeID[] _names;
    private readonly DeviceBuffer[] _buffers;
    private readonly DeviceBuffer _indexBuffer;

    public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;
    public uint IndexCount { get; }

    private BenchMesh(VertexAttributeID[] names, DeviceBuffer[] buffers, DeviceBuffer indexBuffer, uint indexCount)
    {
        _names = names;
        _buffers = buffers;
        _indexBuffer = indexBuffer;
        IndexCount = indexCount;
    }

    public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
    {
        VertexAttributeID wanted = layout.Elements[0].Name;
        for (int i = 0; i < _names.Length; i++)
        {
            if (_names[i] == wanted)
            {
                binding = new VertexBinding(_buffers[i]);
                return;
            }
        }

        throw new InvalidOperationException($"Bench mesh has no stream for vertex attribute '{VertexAttributeID.ToString(wanted)}'.");
    }

    public bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
    {
        buffer = _indexBuffer;
        format = IndexFormat.UInt16;
        indexCount = IndexCount;
        return true;
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

        DeviceBuffer positionBuffer = Upload(gd, positions, BufferUsage.VertexBuffer);
        DeviceBuffer uvBuffer = Upload(gd, uvs, BufferUsage.VertexBuffer);
        DeviceBuffer indexBuffer = Upload(gd, indices, BufferUsage.IndexBuffer);

        return new BenchMesh(
            [VertexAttributeID.Intern("POSITION0"), VertexAttributeID.Intern("UV0")],
            [positionBuffer, uvBuffer],
            indexBuffer,
            (uint)indices.Length);
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

        _indexBuffer.Dispose();
    }
}
