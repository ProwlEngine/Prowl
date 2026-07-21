using System;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Base tracker for a graph resource. Holds the stable interned identity passes reference by ID so the
/// graph can order producers before consumers. Concrete resources (texture, buffer, imported) derive from
/// this and carry their own strict description type; shared identity and ordering machinery live here.
/// </summary>
public abstract class GraphResource
{
    /// <summary>Stable interned identity used to reference this resource across passes.</summary>
    public RenderResourceID Id { get; }

    private protected GraphResource(RenderResourceID id) => Id = id;

    /// <summary>Disposes any physical resources this graph resource owns. Imported resources own nothing.</summary>
    internal virtual void DisposeOwned() { }
}

/// <summary>
/// A texture graph resource. With a history depth of zero it is a plain per-execution transient; with a
/// history depth of N it owns a persistent ring of N+1 physical copies rotated one step per execution, so a
/// pass can read older executions' results (temporal reprojection, TAA history).
/// </summary>
public sealed class GraphTextureResource : GraphResource
{
    /// <summary>How this texture is sized and formatted when allocated.</summary>
    public GraphTextureDesc Description { get; }

    /// <summary>Number of prior executions readable by age. Zero means a plain transient with no history.</summary>
    public int HistoryDepth { get; }

    /// <summary>Load/store operations a raster pass applies when it binds this as its target.</summary>
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
/// A buffer graph resource. With a history depth of zero it is a plain per-execution transient; with a
/// history depth of N it owns a persistent ring of N+1 physical copies rotated one step per execution.
/// </summary>
public sealed class GraphBufferResource : GraphResource
{
    /// <summary>Size and usage of the buffer when allocated.</summary>
    public GraphBufferDesc Description { get; }

    /// <summary>Number of prior executions readable by age. Zero means a plain transient with no history.</summary>
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
/// An externally-owned texture imported into the graph so it participates in ordering and resolution. The
/// caller keeps ownership; the graph never disposes it. The backend transitions it implicitly on first use,
/// so its incoming layout is respected.
/// </summary>
public sealed class GraphImportedTextureResource : GraphResource
{
    /// <summary>The external render target this resource resolves to.</summary>
    public RenderTexture Texture { get; }

    /// <summary>Load/store operations a raster pass applies when it binds this as its target. Loads by default.</summary>
    public TargetLoadStoreOps Ops { get; }

    internal GraphImportedTextureResource(RenderResourceID id, RenderTexture texture, TargetLoadStoreOps? ops = null) : base(id)
    {
        Texture = texture;
        Ops = ops ?? TargetLoadStoreOps.ForLifetime(persistent: true);
    }
}
