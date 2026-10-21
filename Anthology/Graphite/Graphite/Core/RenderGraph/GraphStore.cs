using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Persistent and history resources of graphs, kept across rebuilds so contents survive a graph built again with the same ids.
/// </summary>
public sealed class GraphStore : IDisposable
{
    private readonly Dictionary<RenderResourceID, TextureRings> _textures = new();
    private readonly Dictionary<RenderResourceID, BufferRings> _buffers = new();
    private readonly List<(DeviceBuffer From, DeviceBuffer To)> _resizes = new();
    private bool _disposed;

    internal void Bind(GraphResource resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (resource)
        {
            case GraphTextureResource { Retained: true } texture:
                texture.Rings = TexturesFor(texture.Id, texture.HistoryDepth + 1);
                break;

            case GraphBufferResource { Retained: true } buffer:
                buffer.Rings = BuffersFor(buffer.Id, buffer.HistoryDepth + 1);
                break;
        }
    }

    /// <summary>Resolves a persistent texture by id before any graph declares it. A later graph that declares the same id, size and storage use binds to the same texture.</summary>
    public RenderTexture ResolvePersistent(GraphicsDevice device, RenderResourceID id, in GraphTextureDesc description, GraphInstanceKey key, GraphExtent extent = default, bool storage = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (description.SizeMode == TextureSizeMode.InstanceRelative && !extent.IsValid)
            throw new ArgumentException($"Resource '{RenderResourceID.ToString(id)}' is sized relative to its instance, so it needs an extent.", nameof(extent));

        GraphTextureResource texture = new(id, description, persistent: true) { Storage = storage, Rings = TexturesFor(id, 1) };
        return texture.ResolveHistory(device, key, device.LastBegunExecutionId + 1, 0, texture.Allocation(extent));
    }

    /// <summary>Resolves a persistent buffer by id before any graph declares it. A later graph that declares the same id binds to the same buffer.</summary>
    public DeviceBuffer ResolvePersistent(GraphicsDevice device, RenderResourceID id, in GraphBufferDesc description, GraphInstanceKey key)
    {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(_disposed, this);
        GraphBufferResource buffer = new(id, description, persistent: true) { Rings = BuffersFor(id, 1) };
        return buffer.ResolveHistory(device, key, device.LastBegunExecutionId + 1, 0, description.ToBufferDescription());
    }

    /// <summary>Frees a persistent texture now instead of waiting for it to expire. A later resolve makes it again.</summary>
    public void ReleasePersistent(RenderResourceID id, GraphInstanceKey key)
    {
        if (_textures.TryGetValue(id, out TextureRings? rings))
            rings.Release(key);
    }

    /// <summary>Frees a persistent buffer now instead of waiting for it to expire. A later resolve makes it again.</summary>
    public void ReleasePersistentBuffer(RenderResourceID id, GraphInstanceKey key)
    {
        if (_buffers.TryGetValue(id, out BufferRings? rings))
            rings.Release(key);
    }

    private TextureRings TexturesFor(RenderResourceID id, int slots)
    {
        if (!_textures.TryGetValue(id, out TextureRings? rings) || rings.Slots != slots)
        {
            rings?.DisposeAll();
            _textures[id] = rings = new TextureRings(id, slots);
        }
        return rings;
    }

    private BufferRings BuffersFor(RenderResourceID id, int slots)
    {
        bool keepsContents = slots == 1;
        if (!_buffers.TryGetValue(id, out BufferRings? rings) || rings.Slots != slots || rings.KeepsContents != keepsContents)
        {
            rings?.DisposeAll();
            _buffers[id] = rings = new BufferRings(id, slots, keepsContents ? _resizes : null);
        }
        return rings;
    }

    internal void Sweep(ulong executionId)
    {
        foreach (TextureRings rings in _textures.Values)
            rings.Sweep(executionId);
        foreach (BufferRings rings in _buffers.Values)
            rings.Sweep(executionId);
    }

    internal bool HasResizes => _resizes.Count > 0;

    internal void RecordResizes(CommandBuffer cmd)
    {
        cmd.RecordFullBarrier();
        foreach ((DeviceBuffer from, DeviceBuffer to) in _resizes)
            cmd.CopyBuffer(from, 0, to, 0, Math.Min(from.SizeInBytes, to.SizeInBytes));
        cmd.RecordFullBarrier();
        foreach ((DeviceBuffer from, _) in _resizes)
            from.Dispose();
        _resizes.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (TextureRings rings in _textures.Values)
            rings.DisposeAll();
        foreach (BufferRings rings in _buffers.Values)
            rings.DisposeAll();
        foreach ((DeviceBuffer from, DeviceBuffer to) in _resizes)
            from.Dispose();
        _textures.Clear();
        _buffers.Clear();
        _resizes.Clear();
    }
}
