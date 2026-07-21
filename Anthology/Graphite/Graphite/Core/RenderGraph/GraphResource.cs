using System;

namespace Prowl.Graphite.RenderGraph;

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

/// <summary>
/// Texture graph resource. History depth 0 = plain transient. Depth N = ring of N+1 copies rotated per
/// execution, so passes can read older frames' results (TAA, reprojection).
/// </summary>
public sealed class GraphTextureResource : GraphResource
{
    /// <summary>Size/format used on allocation.</summary>
    public GraphTextureDesc Description { get; }

    /// <summary>Prior executions readable by age. 0 = no history.</summary>
    public int HistoryDepth { get; }

    /// <summary>Load/store ops applied when bound as a raster target.</summary>
    public TargetLoadStoreOps Ops { get; }

    private RenderTexture[]? _ring;
    private RenderTextureDescription _ringDesc;
    private ulong _lastRotationExecutionId;
    private int _currentIndex;

    internal GraphTextureResource(RenderResourceID id, in GraphTextureDesc desc, int historyDepth = 0, TargetLoadStoreOps? ops = null) : base(id)
    {
        if (historyDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(historyDepth), "History depth cannot be negative.");
        Description = desc;
        HistoryDepth = historyDepth;
        Ops = ops ?? TargetLoadStoreOps.ForLifetime(persistent: historyDepth > 0);
    }

    internal RenderTexture ResolveHistory(GraphicsDevice device, ulong executionId, int framesAgo, in RenderTextureDescription desc)
    {
        if (framesAgo < 0 || framesAgo > HistoryDepth)
            throw new ArgumentOutOfRangeException(nameof(framesAgo), $"framesAgo must be in [0, {HistoryDepth}] for resource '{RenderResourceID.ToString(Id)}'.");

        int slots = HistoryDepth + 1;

        if (_ring == null || !_ringDesc.Equals(desc))
        {
            DisposeOwned();
            _ring = new RenderTexture[slots];
            for (int i = 0; i < slots; i++)
                _ring[i] = device.ResourceFactory.CreateRenderTexture(desc);
            _ringDesc = desc;
            _currentIndex = 0;
            _lastRotationExecutionId = executionId;
        }
        else if (executionId != _lastRotationExecutionId)
        {
            _currentIndex = (_currentIndex + 1) % slots;
            _lastRotationExecutionId = executionId;
        }

        int index = ((_currentIndex - framesAgo) % slots + slots) % slots;
        return _ring[index];
    }

    internal override void DisposeOwned()
    {
        if (_ring == null)
            return;

        foreach (RenderTexture texture in _ring)
            texture.Dispose();
        _ring = null;
    }
}

/// <summary>
/// Buffer graph resource. History depth 0 = plain transient. Depth N = ring of N+1 copies rotated per
/// execution.
/// </summary>
public sealed class GraphBufferResource : GraphResource
{
    /// <summary>Size/usage used on allocation.</summary>
    public GraphBufferDesc Description { get; }

    /// <summary>Prior executions readable by age. 0 = no history.</summary>
    public int HistoryDepth { get; }

    private DeviceBuffer[]? _ring;
    private BufferDescription _ringDesc;
    private ulong _lastRotationExecutionId;
    private int _currentIndex;

    internal GraphBufferResource(RenderResourceID id, in GraphBufferDesc desc, int historyDepth = 0) : base(id)
    {
        if (historyDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(historyDepth), "History depth cannot be negative.");
        Description = desc;
        HistoryDepth = historyDepth;
    }

    internal DeviceBuffer ResolveHistory(GraphicsDevice device, ulong executionId, int framesAgo, in BufferDescription desc)
    {
        if (framesAgo < 0 || framesAgo > HistoryDepth)
            throw new ArgumentOutOfRangeException(nameof(framesAgo), $"framesAgo must be in [0, {HistoryDepth}] for resource '{RenderResourceID.ToString(Id)}'.");

        int slots = HistoryDepth + 1;

        if (_ring == null || !_ringDesc.Equals(desc))
        {
            DisposeOwned();
            _ring = new DeviceBuffer[slots];
            for (int i = 0; i < slots; i++)
            {
                DeviceBuffer buffer = device.ResourceFactory.CreateBuffer(desc);
                buffer.SetTransientWrites(true);
                _ring[i] = buffer;
            }
            _ringDesc = desc;
            _currentIndex = 0;
            _lastRotationExecutionId = executionId;
        }
        else if (executionId != _lastRotationExecutionId)
        {
            _currentIndex = (_currentIndex + 1) % slots;
            _lastRotationExecutionId = executionId;
        }

        int index = ((_currentIndex - framesAgo) % slots + slots) % slots;
        return _ring[index];
    }

    internal override void DisposeOwned()
    {
        if (_ring == null)
            return;

        foreach (DeviceBuffer buffer in _ring)
            buffer.Dispose();
        _ring = null;
    }
}

/// <summary>
/// Externally-owned texture imported into the graph. Caller keeps ownership, graph never disposes it.
/// Backend transitions it implicitly on first use, respecting incoming layout.
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
