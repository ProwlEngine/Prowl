using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Base graph resource tracker. Holds the interned ID passes use to order producers before consumers.
/// </summary>
public abstract class GraphResource
{
    /// <summary>ID for this resource across passes.</summary>
    public RenderResourceID Id { get; }

    private protected GraphResource(RenderResourceID id) => Id = id;

}

internal abstract class HistoryRings<TResource, TDesc>
    where TResource : GraphicsResource
    where TDesc : struct, IEquatable<TDesc>
{
    public const int RetentionExecutions = 120;

    private static readonly string s_expired = $"its GraphStore freed it after {RetentionExecutions} executions without a resolve; resolve persistent resources again each frame they are used";

    private sealed class Ring
    {
        public TResource[] Slots = null!;
        public TDesc Desc;
        public ulong AllocationExecutionId;
        public ulong LastRotationExecutionId;
        public long LastUsedExecution;
        public int CurrentIndex;
    }

    private readonly Dictionary<GraphInstanceKey, Ring> _rings = new();
    private readonly List<GraphInstanceKey> _expired = new();
    private readonly int _slots;
    private ulong _lastExecutionId;
    private long _executionCount;

    protected HistoryRings(int slots) => _slots = slots;

    protected abstract TResource Create(GraphicsDevice device, in TDesc desc, GraphInstanceKey key, int index);

    public TResource Resolve(GraphicsDevice device, GraphInstanceKey key, ulong executionId, int framesAgo, in TDesc desc)
    {
        Advance(executionId);

        if (!_rings.TryGetValue(key, out Ring? ring) || !ring.Desc.Equals(desc))
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
                ring.Slots[i] = Create(device, desc, key, i);
            _rings[key] = ring;
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

    public int CurrentSlot(GraphInstanceKey key) => _rings.TryGetValue(key, out Ring? ring) ? ring.CurrentIndex : -1;

    public bool IsValid(GraphInstanceKey key, ulong executionId, in TDesc desc)
    {
        Advance(executionId);
        return _rings.TryGetValue(key, out Ring? ring)
            && ring.Desc.Equals(desc)
            && ring.AllocationExecutionId != executionId;
    }

    public void Release(GraphInstanceKey key)
    {
        if (_rings.Remove(key, out Ring? ring))
            DisposeRing(ring, "its GraphStore released it");
    }

    public void DisposeAll()
    {
        foreach (Ring ring in _rings.Values)
            DisposeRing(ring, "its GraphStore was disposed");
        _rings.Clear();
    }

    private void Advance(ulong executionId)
    {
        if (_executionCount != 0 && executionId == _lastExecutionId)
            return;

        _lastExecutionId = executionId;
        _executionCount++;

        foreach ((GraphInstanceKey key, Ring ring) in _rings)
        {
            if (_executionCount - ring.LastUsedExecution > RetentionExecutions)
                _expired.Add(key);
        }

        foreach (GraphInstanceKey key in _expired)
        {
            DisposeRing(_rings[key], s_expired);
            _rings.Remove(key);
        }
        _expired.Clear();
    }

    protected virtual void Replaced(TResource[] old, TResource[] fresh)
    {
        foreach (TResource resource in old)
            resource.Dispose();
    }

    private static void DisposeRing(Ring ring, string reason)
    {
        foreach (TResource resource in ring.Slots)
            resource.Dispose(reason);
    }
}

/// <summary>
/// Texture graph resource: transient by default, kept across executions when persistent, and a ring of HistoryDepth+1
/// copies per instance key when it has history, rotated per execution so passes can read older frames' results.
/// </summary>
public sealed class GraphTextureResource : GraphResource
{
    /// <summary>Size/format used on allocation.</summary>
    public GraphTextureDesc Description { get; }

    /// <summary>Prior executions readable by age. 0 = no history.</summary>
    public int HistoryDepth { get; }

    /// <summary>Keeps its contents across executions without history.</summary>
    public bool Persistent { get; }

    /// <summary>True when the graph keeps it across executions, through history or persistence.</summary>
    public bool Retained => Persistent || HistoryDepth > 0;

    /// <summary>Load/store ops applied when bound as a raster target.</summary>
    public TargetLoadStoreOps Ops { get; }

    internal bool Storage { get; set; }

    internal TextureRings? Rings { get; set; }

    internal GraphTextureResource(RenderResourceID id, in GraphTextureDesc desc, int historyDepth = 0, TargetLoadStoreOps? ops = null, bool persistent = false) : base(id)
    {
        if (historyDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(historyDepth), "History depth cannot be negative.");
        Description = desc;
        HistoryDepth = historyDepth;
        Persistent = persistent;
        Ops = ops ?? TargetLoadStoreOps.ForLifetime(persistent: Retained);
    }

    internal RenderTextureDescription Allocation(GraphInstance instance)
    {
        if (Description.SizeMode == TextureSizeMode.InstanceRelative && !instance.Extent.IsValid)
            throw new InvalidOperationException(
                $"Resource '{RenderResourceID.ToString(Id)}' is sized relative to its instance, but instance '{instance.Name}' has no extent.");

        return Allocation(instance.Extent);
    }

    internal RenderTextureDescription Allocation(GraphExtent extent)
    {
        (int width, int height) = Description.Resolve(extent.Width, extent.Height);
        PixelFormat[] colors = Description.ColorFormats ?? Array.Empty<PixelFormat>();
        if (Description.EnableDepth && Description.DepthFormat is PixelFormat depthFormat)
            return new RenderTextureDescription((uint)width, (uint)height, colors, depthFormat, Description.SampleCount, Storage) { ArrayLayers = Description.ArrayLayers, MipLevels = Description.MipLevels, Cube = Description.Cube };

        return new RenderTextureDescription((uint)width, (uint)height, colors, Description.EnableDepth, Description.SampleCount, Storage) { ArrayLayers = Description.ArrayLayers, MipLevels = Description.MipLevels, Cube = Description.Cube };
    }

    internal RenderTexture ResolveHistory(GraphicsDevice device, GraphInstanceKey key, ulong executionId, int framesAgo, in RenderTextureDescription desc)
    {
        if (framesAgo < 0 || framesAgo > HistoryDepth)
            throw new ArgumentOutOfRangeException(nameof(framesAgo), $"framesAgo must be in [0, {HistoryDepth}] for resource '{RenderResourceID.ToString(Id)}'.");

        return Rings!.Resolve(device, key, executionId, framesAgo, desc);
    }

    internal bool IsHistoryValid(GraphInstanceKey key, ulong executionId, in RenderTextureDescription desc)
        => Retained && Rings!.IsValid(key, executionId, desc);

    internal int HistorySlot(GraphInstanceKey key) => HistoryDepth > 0 ? Rings!.CurrentSlot(key) : -1;
}

internal sealed class TextureRings(RenderResourceID id, int slots) : HistoryRings<RenderTexture, RenderTextureDescription>(slots)
{
    protected override RenderTexture Create(GraphicsDevice device, in RenderTextureDescription desc, GraphInstanceKey key, int index)
    {
        RenderTexture texture = device.ResourceFactory.CreateRenderTexture(desc);
        string? baseName = RenderResourceID.ToString(id);
        if (baseName != null)
            texture.Name = $"{baseName}[{key}][{index}]";
        return texture;
    }
}

/// <summary>
/// Buffer graph resource: transient by default, kept across executions when persistent, and a ring of HistoryDepth+1
/// copies per instance key when it has history. A persistent buffer that changes size keeps its contents.
/// </summary>
public sealed class GraphBufferResource : GraphResource
{
    /// <summary>Size/usage used on allocation.</summary>
    public GraphBufferDesc Description { get; }

    /// <summary>Prior executions readable by age. 0 = no history.</summary>
    public int HistoryDepth { get; }

    /// <summary>Keeps its contents across executions without history.</summary>
    public bool Persistent { get; }

    /// <summary>True when the graph keeps it across executions, through history or persistence.</summary>
    public bool Retained => Persistent || HistoryDepth > 0;

    internal BufferRings? Rings { get; set; }

    internal GraphBufferResource(RenderResourceID id, in GraphBufferDesc desc, int historyDepth = 0, bool persistent = false) : base(id)
    {
        if (historyDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(historyDepth), "History depth cannot be negative.");
        Description = desc;
        HistoryDepth = historyDepth;
        Persistent = persistent;
    }

    internal DeviceBuffer ResolveHistory(GraphicsDevice device, GraphInstanceKey key, ulong executionId, int framesAgo, in BufferDescription desc)
    {
        if (framesAgo < 0 || framesAgo > HistoryDepth)
            throw new ArgumentOutOfRangeException(nameof(framesAgo), $"framesAgo must be in [0, {HistoryDepth}] for resource '{RenderResourceID.ToString(Id)}'.");

        return Rings!.Resolve(device, key, executionId, framesAgo, desc);
    }

    internal bool IsHistoryValid(GraphInstanceKey key, ulong executionId, in BufferDescription desc)
        => Retained && Rings!.IsValid(key, executionId, desc);

    internal int HistorySlot(GraphInstanceKey key) => HistoryDepth > 0 ? Rings!.CurrentSlot(key) : -1;
}

internal sealed class BufferRings(RenderResourceID id, int slots, List<(DeviceBuffer From, DeviceBuffer To)>? resizes) : HistoryRings<DeviceBuffer, BufferDescription>(slots)
{
    public bool KeepsContents => resizes != null;

    protected override DeviceBuffer Create(GraphicsDevice device, in BufferDescription desc, GraphInstanceKey key, int index)
    {
        DeviceBuffer buffer = device.ResourceFactory.CreateBuffer(desc);
        string? baseName = RenderResourceID.ToString(id);
        if (baseName != null)
            buffer.Name = $"{baseName}[{key}][{index}]";
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

/// <summary>
/// Externally owned buffer imported into the graph. Caller keeps ownership, graph never disposes it. Its first
/// access in an execution gets a barrier against every earlier write, so a buffer another execution wrote is
/// safe to read once declared.
/// </summary>
public sealed class GraphImportedBufferResource : GraphResource
{
    /// <summary>External buffer this resolves to.</summary>
    public DeviceBuffer Buffer { get; }

    internal GraphImportedBufferResource(RenderResourceID id, DeviceBuffer buffer) : base(id)
    {
        Buffer = buffer;
    }
}

/// <summary>
/// Externally-owned texture imported into the graph. Caller keeps ownership, graph never disposes it.
/// Must be in its resting layout when the execution starts; the graph returns it there when the execution ends.
/// </summary>
public sealed class GraphImportedTextureResource : GraphResource
{
    /// <summary>External render target this resolves to.</summary>
    public RenderTexture Texture { get; }

    /// <summary>Load/store ops applied when bound as a raster target. Loads by default.</summary>
    public TargetLoadStoreOps Ops { get; }

    internal GraphImportedTextureResource(RenderResourceID id, RenderTexture texture, TargetLoadStoreOps? ops = null) : base(id)
    {
        Texture = texture;
        Ops = ops ?? TargetLoadStoreOps.ForLifetime(persistent: true);
    }
}

/// <summary>A texture the instance binds at dispatch under the resource's id. Caller keeps ownership, graph never disposes it.</summary>
public sealed class GraphTextureSlotResource : GraphResource
{
    /// <summary>Load/store ops applied when bound as a raster target. Loads by default.</summary>
    public TargetLoadStoreOps Ops { get; }

    internal GraphTextureSlotResource(RenderResourceID id, TargetLoadStoreOps? ops = null) : base(id)
    {
        Ops = ops ?? TargetLoadStoreOps.ForLifetime(persistent: true);
    }
}

/// <summary>A buffer the instance binds at dispatch under the resource's id. Caller keeps ownership, graph never disposes it.</summary>
public sealed class GraphBufferSlotResource : GraphResource
{
    internal GraphBufferSlotResource(RenderResourceID id) : base(id) { }
}

/// <summary>
/// The instance's target for this execution: its <see cref="GraphInstance.Target"/>. Write-only.
/// Passes that write it are skipped for instances with no target.
/// </summary>
public sealed class GraphInstanceTargetResource : GraphResource
{
    /// <summary>Reserved ID every instance target declaration uses.</summary>
    public static readonly RenderResourceID InstanceTargetId = RenderResourceID.Intern("Graphite.InstanceTarget");

    /// <summary>Load/store ops applied when bound as a raster target. Clears by default.</summary>
    public TargetLoadStoreOps Ops { get; }

    /// <summary>Depth format the target needs, null for color only.</summary>
    public PixelFormat? DepthFormat { get; }

    internal GraphInstanceTargetResource(TargetLoadStoreOps? ops = null, PixelFormat? depthFormat = null) : base(InstanceTargetId)
    {
        Ops = ops ?? TargetLoadStoreOps.ForLifetime(persistent: false);
        DepthFormat = depthFormat;
    }
}
