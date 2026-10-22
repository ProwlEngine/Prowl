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
/// A persistent graph buffer the CPU fills every frame through the frame's scatter, like instance lists and indirect
/// commands. It grows by doubling. The store frees it when unused, so owners refresh it each frame they use it.
/// </summary>
internal sealed class GpuArray
{
    private readonly GraphicsDevice _device;
    private readonly GraphStore _store;
    private readonly PersistentBuffer _persistent;

    public GpuArray(GraphicsDevice device, GraphStore store, RenderResourceID id, BufferUsage usage, GraphInstanceKey key)
    {
        _device = device;
        _store = store;
        _persistent = new PersistentBuffer(id, usage | BufferUsage.StructuredBufferReadWrite, key);
        Buffer = _persistent.Resolve(device, store, 256);
    }

    public DeviceBuffer Buffer { get; private set; }

    public PersistentBuffer Persistent => _persistent;

    /// <summary>Resolves the buffer again, so one the store freed while unused is replaced before it is bound.</summary>
    public void Refresh() => Buffer = _persistent.Resolve(_device, _store, Buffer.SizeInBytes);

    public void Write<T>(Uploader uploader, ReadOnlySpan<T> data) where T : unmanaged
    {
        uint bytes = (uint)(data.Length * Unsafe.SizeOf<T>());
        uint size = Buffer.SizeInBytes;
        while (size < bytes)
            size *= 2;
        Buffer = _persistent.Resolve(_device, _store, size);
        if (bytes > 0)
            uploader.Write(Buffer, 0, MemoryMarshal.Cast<T, uint>(data));
    }
}
