using System;
using System.Runtime.CompilerServices;


using Prowl.Graphite;


namespace Prowl.Graphite.Samples;


public sealed class Mesh : IDisposable, IVertexSource
{
    private GraphicsDevice _device;

    // Per-stream state
    private readonly (Array? Array, bool IsDirty)[] _streamArrays;
    private readonly Action<DeviceBuffer>[] _streamUploaders;
    private readonly int[] _streamElementSizes;
    private readonly DeviceBuffer?[] _vertexBuffers;

    // Index state
    private (Array? Array, bool IsDirty) _indexArray;
    private Action<DeviceBuffer>? _indexUploader;
    private DeviceBuffer? _indexBuffer;

    // Zero-filled placeholder bound for shader vertex inputs the mesh doesn't provide.
    // Sized lazily to fit the largest requesting layout.
    private DeviceBuffer? _zeroStream;
    private uint _zeroStreamCapacity;

    private bool _disposed;

    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }

    public bool Has32BitIndices { get; private set; }
    public bool IsReadable { get; set; }

    public VertexElementDescription[] InputLayout { get; private set; }

    public PrimitiveTopology Topology { get; set; }


    public Mesh(GraphicsDevice device, MeshCreateInfo? createInfo = null)
    {
        MeshCreateInfo info = createInfo ?? MeshCreateInfo.Default;

        VertexElementDescription[] layout = info.VertexLayout;

        InputLayout = info.VertexLayout;
        Topology = info.Topology;

        _device = device;
        _streamArrays = new (Array?, bool)[layout.Length];
        _streamUploaders = new Action<DeviceBuffer>[layout.Length];
        _streamElementSizes = new int[layout.Length];
        _vertexBuffers = new DeviceBuffer?[layout.Length];
    }


    public void SetVertexInput<T>(Span<T> buffer, int stream) where T : unmanaged
    {
        if (stream == 0)
            VertexCount = buffer.Length;
        else if (buffer.Length < VertexCount)
            throw new InvalidOperationException("Vertex stream buffer must be at least as large as the vertex count.");

        T[] copy = buffer.ToArray();

        _streamArrays[stream] = (copy, true);
        _streamElementSizes[stream] = Unsafe.SizeOf<T>();
        _streamUploaders[stream] = (buf) => _device.UpdateBuffer(buf, 0, copy);
    }


    public T[] GetVertexInput<T>(int stream) where T : unmanaged
    {
        if (!IsReadable)
            throw new InvalidOperationException("Cannot read vertex data from a write-only Mesh.");

        if (_streamArrays[stream].Array == null)
        {
            if (_vertexBuffers[stream] == null)
                return [];

            // GPU readback removed with the synchronous-blocking API. Only CPU-cached data
            // is currently returnable.
            throw new NotSupportedException("Vertex data GPU readback is currently unavailable.");
        }

        if (_streamArrays[stream].Array is T[] arr)
            return arr;

        Type? storedType = _streamArrays[stream].Array!.GetType().GetElementType();
        throw new InvalidCastException($"Stream {stream} stores {storedType} but {typeof(T)} was requested.");
    }


    public void SetIndexInput32(Span<uint> buffer)
    {
        Has32BitIndices = true;
        IndexCount = buffer.Length;

        uint[] copy = buffer.ToArray();
        _indexArray = (copy, true);
        _indexUploader = (buf) => _device.UpdateBuffer(buf, 0, copy);
    }


    public void SetIndexInput16(Span<ushort> buffer)
    {
        Has32BitIndices = false;
        IndexCount = buffer.Length;

        ushort[] copy = buffer.ToArray();
        _indexArray = (copy, true);
        _indexUploader = (buf) => _device.UpdateBuffer(buf, 0, copy);
    }


    public uint[] GetIndexInput32()
    {
        if (!IsReadable) throw new InvalidOperationException("Cannot read index data from a write-only Mesh.");
        if (!Has32BitIndices) throw new InvalidOperationException("Mesh stores 16-bit indices; cannot read as 32-bit.");

        if (_indexArray.Array == null)
        {
            if (_indexBuffer == null)
                return [];

            throw new NotSupportedException("Index data GPU readback is currently unavailable.");
        }

        return (uint[])_indexArray.Array;
    }


    public ushort[] GetIndexInput16()
    {
        if (!IsReadable) throw new InvalidOperationException("Cannot read index data from a write-only Mesh.");
        if (Has32BitIndices) throw new InvalidOperationException("Mesh stores 32-bit indices; cannot read as 16-bit.");

        if (_indexArray.Array == null)
        {
            if (_indexBuffer == null)
                return [];

            throw new NotSupportedException("Index data GPU readback is currently unavailable.");
        }

        return (ushort[])_indexArray.Array;
    }


    // Uploads all dirty CPU data to the GPU through the provided CommandBuffer.
    // Call this before submitting a draw call that uses this Mesh.
    public void Upload()
    {
        for (int i = 0; i < _streamArrays.Length; i++)
        {
            if (_streamArrays[i].Array == null || !_streamArrays[i].IsDirty)
                continue;

            EnsureVertexBuffer(i, _streamArrays[i].Array!.Length, _streamElementSizes[i]);
            _streamUploaders[i].Invoke(_vertexBuffers[i]!);
            _streamArrays[i] = (_streamArrays[i].Array, false);
        }

        if (_indexArray.Array != null && _indexArray.IsDirty)
        {
            int stride = Has32BitIndices ? sizeof(uint) : sizeof(ushort);
            EnsureIndexBuffer(_indexArray.Array.Length, stride);
            _indexUploader!.Invoke(_indexBuffer!);
            _indexArray = (_indexArray.Array, false);
        }

        if (!IsReadable)
        {
            _indexArray.Array = null;
            for (int i = 0; i < _streamArrays.Length; i++)
                _streamArrays[i].Array = null;
        }
    }


    private void EnsureBuffer(ref DeviceBuffer? buffer, int elementCount, int elementStride, BufferUsage usage)
    {
        uint requestedSize = (uint)(elementCount * elementStride);
        uint capacity = buffer?.SizeInBytes ?? 0;

        if (buffer != null && requestedSize <= capacity && requestedSize >= capacity * 0.33f)
            return;

        buffer?.Dispose();
        buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription
        {
            Usage = usage,
            SizeInBytes = (uint)(requestedSize * 1.5f),
        });
    }


    private void EnsureVertexBuffer(int stream, int elementCount, int elementStride)
    {
        DeviceBuffer? vertexBuffer = _vertexBuffers[stream];
        EnsureBuffer(ref vertexBuffer, elementCount, elementStride, BufferUsage.VertexBuffer);
        _vertexBuffers[stream] = vertexBuffer;
    }


    private void EnsureIndexBuffer(int elementCount, int elementStride)
    {
        EnsureBuffer(ref _indexBuffer, elementCount, elementStride, BufferUsage.IndexBuffer);
    }


    public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
    {
        Upload();

        VertexAttributeID lName = layout.Elements[0].Name;
        int index = Array.FindIndex(InputLayout, x => x.Name == lName);

        if (index > -1 && _vertexBuffers[index] != null)
            binding = new(_vertexBuffers[index]!);
        else
            binding = new(GetOrCreateZeroStream(layout.Stride), 0);
    }


    public bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
    {
        Upload();

        format = Has32BitIndices ? IndexFormat.UInt32 : IndexFormat.UInt16;
        indexCount = (uint)IndexCount;
        buffer = _indexBuffer!;

        return true;
    }


    private DeviceBuffer GetOrCreateZeroStream(uint stride)
    {
        // VertexCount may be 0 before any SetVertexInput; size for at least one vertex
        // so the buffer is non-empty.
        uint vertices = (uint)Math.Max(1, VertexCount);
        uint required = stride * vertices;

        if (_zeroStream != null && required <= _zeroStreamCapacity)
            return _zeroStream;

        _zeroStream?.Dispose();
        uint capacity = (uint)(required * 1.5f);
        _zeroStream = _device.ResourceFactory.CreateBuffer(new BufferDescription
        {
            Usage = BufferUsage.VertexBuffer,
            SizeInBytes = capacity,
        });
        _zeroStreamCapacity = capacity;

        byte[] zeros = new byte[capacity];

        _device.UpdateBuffer(_zeroStream, 0, zeros);

        return _zeroStream;
    }


    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (DeviceBuffer? buf in _vertexBuffers)
            buf?.Dispose();

        _zeroStream?.Dispose();
        _indexBuffer?.Dispose();
    }
}


public struct MeshCreateInfo
{
    public static MeshCreateInfo Default = new()
    {
        VertexLayout =
        [
            new VertexElementDescription("POSITION0", VertexElementFormat.Float3),
            new VertexElementDescription("NORMAL0",   VertexElementFormat.Float3),
            new VertexElementDescription("TANGENT0",  VertexElementFormat.Float3),
            new VertexElementDescription("UV0",       VertexElementFormat.Float4),
            new VertexElementDescription("UV1",      VertexElementFormat.Float4),
            new VertexElementDescription("UV2",      VertexElementFormat.Float4),
        ],

        Topology = PrimitiveTopology.TriangleStrip
    };

    public VertexElementDescription[] VertexLayout;
    public PrimitiveTopology Topology;
}
