using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A buffer the CPU fills every frame through the frame's scatter, like instance lists and indirect commands. It grows
/// by doubling.
/// </summary>
internal sealed class GpuArray
{
    private readonly GraphicsDevice _device;
    private readonly RenderStore _store;
    private readonly PersistentBuffer _persistent;

    public GpuArray(GraphicsDevice device, RenderStore store, RenderResourceID id, BufferUsage usage)
    {
        _device = device;
        _store = store;
        _persistent = new PersistentBuffer(id, usage | BufferUsage.StructuredBufferReadWrite);
        Buffer = _persistent.Resolve(device, store, 256);
    }

    public DeviceBuffer Buffer { get; private set; }

    public PersistentBuffer Persistent => _persistent;

    public void Release() => _persistent.Release(_store);

    /// <summary>Writes the data at the start, the buffer holding at least reserveBytes for what the GPU writes after it.</summary>
    public void Write<T>(Uploader uploader, ReadOnlySpan<T> data, uint reserveBytes = 0) where T : unmanaged
    {
        uint bytes = (uint)(data.Length * Unsafe.SizeOf<T>());
        Reserve(Math.Max(bytes, reserveBytes));
        if (bytes > 0)
            uploader.Write(Buffer, 0, MemoryMarshal.Cast<T, uint>(data));
    }

    /// <summary>Makes the buffer hold at least the bytes, for what the GPU alone writes.</summary>
    public void Reserve(uint bytes)
    {
        uint size = Buffer.SizeInBytes;
        while (size < bytes)
            size *= 2;
        Buffer = _persistent.Resolve(_device, _store, size);
    }
}
