using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

internal abstract class HistoryRings<TResource, TDesc>
    where TResource : GraphicsResource
    where TDesc : struct, IEquatable<TDesc>
{
    public const int RetentionExecutions = 120;

    private static readonly string s_expired = $"its FrameGraph freed it after {RetentionExecutions} executions without being created; create persistent resources again each frame they are used";
    private const string Resized = "its FrameGraph replaced it when its description changed";

    private sealed class Ring
    {
        public TResource[] Slots = null!;
        public TDesc Desc;
        public ulong AllocationExecutionId;
        public ulong LastRotationExecutionId;
        public long LastUsedExecution;
        public int CurrentIndex;
    }

    private readonly Dictionary<int, Ring> _rings = new();
    private readonly List<int> _expired = new();
    private readonly int _slots;
    private ulong _lastExecutionId;
    private long _executionCount;

    protected HistoryRings(int slots) => _slots = slots;

    protected abstract TResource Create(GraphicsDevice device, in TDesc desc, int scope, int index);

    public TResource Resolve(GraphicsDevice device, int scope, ulong executionId, int framesAgo, in TDesc desc)
    {
        Advance(executionId);

        if (!_rings.TryGetValue(scope, out Ring? ring) || !ring.Desc.Equals(desc))
        {
            Ring? old = ring;
            ring = new Ring
            {
                Slots = new TResource[_slots],
                Desc = desc,
                AllocationExecutionId = executionId,
                LastRotationExecutionId = executionId,
            };
            for (int i = 0; i < _slots; i++)
                ring.Slots[i] = Create(device, desc, scope, i);
            _rings[scope] = ring;
            if (old != null)
                Replaced(old.Slots, ring.Slots);
        }
        else if (ring.LastRotationExecutionId != executionId)
        {
            ring.CurrentIndex = (ring.CurrentIndex + 1) % _slots;
            ring.LastRotationExecutionId = executionId;
        }

        ring.LastUsedExecution = _executionCount;

        int index = ((ring.CurrentIndex - framesAgo) % _slots + _slots) % _slots;
        return ring.Slots[index];
    }

    public int Slots => _slots;

    public void Sweep(ulong executionId) => Advance(executionId);

    public int CurrentSlot(int scope) => _rings.TryGetValue(scope, out Ring? ring) ? ring.CurrentIndex : -1;

    public bool IsValid(int scope, in TDesc desc)
        => _rings.TryGetValue(scope, out Ring? ring)
            && ring.Desc.Equals(desc)
            && ring.AllocationExecutionId != _lastExecutionId;

    public void Release(int scope)
    {
        if (_rings.Remove(scope, out Ring? ring))
            DisposeRing(ring, "its FrameGraph released it");
    }

    public void DisposeAll()
    {
        foreach (Ring ring in _rings.Values)
            DisposeRing(ring, "its FrameGraph was disposed");
        _rings.Clear();
    }

    private void Advance(ulong executionId)
    {
        if (_executionCount != 0 && executionId == _lastExecutionId)
            return;

        _lastExecutionId = executionId;
        _executionCount++;

        foreach ((int scope, Ring ring) in _rings)
        {
            if (_executionCount - ring.LastUsedExecution > RetentionExecutions)
                _expired.Add(scope);
        }

        foreach (int scope in _expired)
        {
            DisposeRing(_rings[scope], s_expired);
            _rings.Remove(scope);
        }
        _expired.Clear();
    }

    protected virtual void Replaced(TResource[] old, TResource[] fresh)
    {
        foreach (TResource resource in old)
            resource.Dispose(Resized);
    }

    private static void DisposeRing(Ring ring, string reason)
    {
        foreach (TResource resource in ring.Slots)
            resource.Dispose(reason);
    }
}

internal sealed class TextureRings(RenderResourceID id, int slots) : HistoryRings<RenderTexture, RenderTextureDescription>(slots)
{
    protected override RenderTexture Create(GraphicsDevice device, in RenderTextureDescription desc, int scope, int index)
    {
        RenderTexture texture = device.ResourceFactory.CreateRenderTexture(desc);
        string? baseName = RenderResourceID.ToString(id);
        if (baseName != null)
            texture.Name = $"{baseName}[{scope}][{index}]";
        return texture;
    }
}

internal sealed class BufferRings(RenderResourceID id, int slots, List<(DeviceBuffer From, DeviceBuffer To)>? resizes) : HistoryRings<DeviceBuffer, BufferDescription>(slots)
{
    public bool KeepsContents => resizes != null;

    protected override DeviceBuffer Create(GraphicsDevice device, in BufferDescription desc, int scope, int index)
    {
        DeviceBuffer buffer = device.ResourceFactory.CreateBuffer(desc);
        string? baseName = RenderResourceID.ToString(id);
        if (baseName != null)
            buffer.Name = $"{baseName}[{scope}][{index}]";
        return buffer;
    }

    protected override void Replaced(DeviceBuffer[] old, DeviceBuffer[] fresh)
    {
        if (resizes == null)
        {
            base.Replaced(old, fresh);
            return;
        }

        for (int i = 0; i < old.Length; i++)
            resizes.Add((old[i], fresh[i]));
    }
}

internal sealed class ResourceStore : IDisposable
{
    private readonly Dictionary<RenderResourceID, TextureRings> _textures = new();
    private readonly Dictionary<RenderResourceID, BufferRings> _buffers = new();
    private readonly List<(DeviceBuffer From, DeviceBuffer To)> _resizes = new();
    private bool _disposed;

    public TextureRings TexturesFor(RenderResourceID id, int slots)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_textures.TryGetValue(id, out TextureRings? rings) || rings.Slots != slots)
        {
            rings?.DisposeAll();
            _textures[id] = rings = new TextureRings(id, slots);
        }
        return rings;
    }

    public BufferRings BuffersFor(RenderResourceID id, int slots)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool keepsContents = slots == 1;
        if (!_buffers.TryGetValue(id, out BufferRings? rings) || rings.Slots != slots || rings.KeepsContents != keepsContents)
        {
            rings?.DisposeAll();
            _buffers[id] = rings = new BufferRings(id, slots, keepsContents ? _resizes : null);
        }
        return rings;
    }

    public void Release(RenderResourceID id, int scope)
    {
        if (_textures.TryGetValue(id, out TextureRings? textures))
            textures.Release(scope);
        if (_buffers.TryGetValue(id, out BufferRings? buffers))
            buffers.Release(scope);
    }

    public void Sweep(ulong executionId)
    {
        foreach (TextureRings rings in _textures.Values)
            rings.Sweep(executionId);
        foreach (BufferRings rings in _buffers.Values)
            rings.Sweep(executionId);
    }

    public bool HasResizes => _resizes.Count > 0;

    public void RecordResizes(CommandBuffer cmd)
    {
        cmd.RecordFullBarrier();
        foreach ((DeviceBuffer from, DeviceBuffer to) in _resizes)
            cmd.CopyBuffer(from, 0, to, 0, Math.Min(from.SizeInBytes, to.SizeInBytes));
        cmd.RecordFullBarrier();
        foreach ((DeviceBuffer from, _) in _resizes)
            from.Dispose("its FrameGraph copied it into a larger buffer");
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
