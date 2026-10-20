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

    /// <summary>Disposes owned physical resources. No-op for imported resources.</summary>
    internal virtual void DisposeOwned() { }
}

internal abstract class HistoryRings<TResource, TDesc>
    where TResource : class, IDisposable
    where TDesc : struct, IEquatable<TDesc>
{
    public const int RetentionExecutions = 120;

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
            if (ring != null)
                DisposeRing(ring);

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

    public int CurrentSlot(GraphInstanceKey key) => _rings.TryGetValue(key, out Ring? ring) ? ring.CurrentIndex : -1;

    public bool IsValid(GraphInstanceKey key, ulong executionId, in TDesc desc)
    {
        Advance(executionId);
        return _rings.TryGetValue(key, out Ring? ring)
            && ring.Desc.Equals(desc)
            && ring.AllocationExecutionId != executionId;
    }

    public void DisposeAll()
    {
        foreach (Ring ring in _rings.Values)
            DisposeRing(ring);
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
            DisposeRing(_rings[key]);
            _rings.Remove(key);
        }
        _expired.Clear();
    }

    private static void DisposeRing(Ring ring)
    {
        foreach (TResource resource in ring.Slots)
            resource.Dispose();
    }
}

/// <summary>
/// Texture graph resource. History depth 0 = plain transient. Depth N = ring of N+1 copies per view rotated
/// per execution, so passes can read older frames' results (TAA, reprojection).
/// </summary>
public sealed class GraphTextureResource : GraphResource
{
    /// <summary>Size/format used on allocation.</summary>
    public GraphTextureDesc Description { get; }

    /// <summary>Prior executions readable by age. 0 = no history.</summary>
    public int HistoryDepth { get; }

    /// <summary>Load/store ops applied when bound as a raster target.</summary>
    public TargetLoadStoreOps Ops { get; }

    internal bool Storage { get; set; }

    private readonly TextureRings _rings;

    internal GraphTextureResource(RenderResourceID id, in GraphTextureDesc desc, int historyDepth = 0, TargetLoadStoreOps? ops = null) : base(id)
    {
        if (historyDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(historyDepth), "History depth cannot be negative.");
        Description = desc;
        HistoryDepth = historyDepth;
        Ops = ops ?? TargetLoadStoreOps.ForLifetime(persistent: historyDepth > 0);
        _rings = new TextureRings(id, historyDepth + 1);
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
            return new RenderTextureDescription((uint)width, (uint)height, colors, depthFormat, Description.SampleCount, Storage) { ArrayLayers = Description.ArrayLayers };

        return new RenderTextureDescription((uint)width, (uint)height, colors, Description.EnableDepth, Description.SampleCount, Storage) { ArrayLayers = Description.ArrayLayers };
    }

    internal RenderTexture ResolveHistory(GraphicsDevice device, GraphInstanceKey key, ulong executionId, int framesAgo, in RenderTextureDescription desc)
    {
        if (framesAgo < 0 || framesAgo > HistoryDepth)
            throw new ArgumentOutOfRangeException(nameof(framesAgo), $"framesAgo must be in [0, {HistoryDepth}] for resource '{RenderResourceID.ToString(Id)}'.");

        return _rings.Resolve(device, key, executionId, framesAgo, desc);
    }

    internal bool IsHistoryValid(GraphInstanceKey key, ulong executionId, in RenderTextureDescription desc)
        => HistoryDepth > 0 && _rings.IsValid(key, executionId, desc);

    internal int HistorySlot(GraphInstanceKey key) => HistoryDepth > 0 ? _rings.CurrentSlot(key) : -1;

    internal override void DisposeOwned() => _rings.DisposeAll();

    private sealed class TextureRings : HistoryRings<RenderTexture, RenderTextureDescription>
    {
        private readonly RenderResourceID _id;

        public TextureRings(RenderResourceID id, int slots) : base(slots) => _id = id;

        protected override RenderTexture Create(GraphicsDevice device, in RenderTextureDescription desc, GraphInstanceKey key, int index)
        {
            RenderTexture texture = device.ResourceFactory.CreateRenderTexture(desc);
            string? baseName = RenderResourceID.ToString(_id);
            if (baseName != null)
                texture.Name = $"{baseName}[{key}][{index}]";
            return texture;
        }
    }
}

/// <summary>
/// Buffer graph resource. History depth 0 = plain transient. Depth N = ring of N+1 copies per view rotated
/// per execution.
/// </summary>
public sealed class GraphBufferResource : GraphResource
{
    /// <summary>Size/usage used on allocation.</summary>
    public GraphBufferDesc Description { get; }

    /// <summary>Prior executions readable by age. 0 = no history.</summary>
    public int HistoryDepth { get; }

    private readonly BufferRings _rings;

    internal GraphBufferResource(RenderResourceID id, in GraphBufferDesc desc, int historyDepth = 0) : base(id)
    {
        if (historyDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(historyDepth), "History depth cannot be negative.");
        Description = desc;
        HistoryDepth = historyDepth;
        _rings = new BufferRings(id, historyDepth + 1);
    }

    internal DeviceBuffer ResolveHistory(GraphicsDevice device, GraphInstanceKey key, ulong executionId, int framesAgo, in BufferDescription desc)
    {
        if (framesAgo < 0 || framesAgo > HistoryDepth)
            throw new ArgumentOutOfRangeException(nameof(framesAgo), $"framesAgo must be in [0, {HistoryDepth}] for resource '{RenderResourceID.ToString(Id)}'.");

        return _rings.Resolve(device, key, executionId, framesAgo, desc);
    }

    internal bool IsHistoryValid(GraphInstanceKey key, ulong executionId, in BufferDescription desc)
        => HistoryDepth > 0 && _rings.IsValid(key, executionId, desc);

    internal int HistorySlot(GraphInstanceKey key) => HistoryDepth > 0 ? _rings.CurrentSlot(key) : -1;

    internal override void DisposeOwned() => _rings.DisposeAll();

    private sealed class BufferRings : HistoryRings<DeviceBuffer, BufferDescription>
    {
        private readonly RenderResourceID _id;

        public BufferRings(RenderResourceID id, int slots) : base(slots) => _id = id;

        protected override DeviceBuffer Create(GraphicsDevice device, in BufferDescription desc, GraphInstanceKey key, int index)
        {
            DeviceBuffer buffer = device.ResourceFactory.CreateBuffer(desc);
            string? baseName = RenderResourceID.ToString(_id);
            if (baseName != null)
                buffer.Name = $"{baseName}[{key}][{index}]";
            return buffer;
        }
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
