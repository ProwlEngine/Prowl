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
                int textureSlots = texture.HistoryDepth + 1;
                if (!_textures.TryGetValue(texture.Id, out TextureRings? textures) || textures.Slots != textureSlots)
                {
                    textures?.DisposeAll();
                    _textures[texture.Id] = textures = new TextureRings(texture.Id, textureSlots);
                }
                texture.Rings = textures;
                break;

            case GraphBufferResource { Retained: true } buffer:
                int bufferSlots = buffer.HistoryDepth + 1;
                bool keepsContents = buffer.HistoryDepth == 0;
                if (!_buffers.TryGetValue(buffer.Id, out BufferRings? buffers) || buffers.Slots != bufferSlots || buffers.KeepsContents != keepsContents)
                {
                    buffers?.DisposeAll();
                    _buffers[buffer.Id] = buffers = new BufferRings(buffer.Id, bufferSlots, keepsContents ? _resizes : null);
                }
                buffer.Rings = buffers;
                break;
        }
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
