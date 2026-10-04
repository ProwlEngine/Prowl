using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Pool of render-texture bundles, keyed by desc, reclaimed when the owning execution's fence signals.
/// <para>
/// Textures survive across executions. Finished bundles go back to the free-list instead of being
/// destroyed. Reclaim is lazy, on next rent, no dedicated thread.
/// </para>
/// <para>
/// One lock guards the free-list. Concurrent rents never get the same bundle.
/// </para>
/// </summary>
internal sealed class TransientTexturePool(GraphicsDevice device) : IDisposable
{
    private readonly GraphicsDevice _device = device;
    private readonly object _lock = new();
    private readonly Dictionary<RenderTextureDescription, List<PooledBundle>> _free = [];
    private readonly List<PooledBundle> _rented = [];
    private readonly List<RenderTextureDescription> _emptyKeys = [];
    private ulong _lastEvictionExecutionId;
    private bool _disposed;

    internal const ulong RetentionExecutions = 120;

    /// <summary>
    /// Rents a bundle matching desc. Goes back to the free-list once executionId completes.
    /// </summary>
    public PooledBundle Rent(in RenderTextureDescription desc, ulong executionId)
    {
        if (desc.Width == 0 || desc.Height == 0)
            throw new RenderException("Cannot rent a transient texture with a zero width or height.");
        if (desc.ColorFormats.Length == 0 && !desc.Depth)
            throw new RenderException("Cannot rent a transient texture bundle with no color attachments and no depth attachment.");

        lock (_lock)
        {
            if (_disposed)
                throw new RenderException("Cannot rent from a disposed transient texture pool.");

            ReclaimCompleted();
            EvictUnused(executionId);

            if (_free.TryGetValue(desc, out List<PooledBundle>? list) && list.Count > 0)
            {
                PooledBundle recycled = list[^1];
                list.RemoveAt(list.Count - 1);
                recycled.RentedExecutionId = executionId;
                recycled.LastRentedExecutionId = executionId;
                _rented.Add(recycled);
                return recycled;
            }

            PooledBundle created = Create(desc);
            created.RentedExecutionId = executionId;
            created.LastRentedExecutionId = executionId;
            _rented.Add(created);
            return created;
        }
    }

    private void ReclaimCompleted()
    {
        for (int i = _rented.Count - 1; i >= 0; i--)
        {
            PooledBundle bundle = _rented[i];
            if (!_device.IsExecutionIdComplete(bundle.RentedExecutionId))
                continue;

            _rented.RemoveAt(i);
            bundle.RentedExecutionId = 0;

            if (!_free.TryGetValue(bundle.Desc, out List<PooledBundle>? list))
            {
                list = [];
                _free[bundle.Desc] = list;
            }
            list.Add(bundle);
        }
    }

    private PooledBundle Create(in RenderTextureDescription desc)
        => new(_device.ResourceFactory.CreateRenderTexture(desc));

    private void EvictUnused(ulong executionId)
    {
        if (executionId == _lastEvictionExecutionId)
            return;

        _lastEvictionExecutionId = executionId;
        foreach (KeyValuePair<RenderTextureDescription, List<PooledBundle>> pair in _free)
        {
            List<PooledBundle> list = pair.Value;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                PooledBundle entry = list[i];
                if (executionId - entry.LastRentedExecutionId <= RetentionExecutions)
                    continue;

                list.RemoveAt(i);
                entry.DisposeResources();
            }

            if (list.Count == 0)
                _emptyKeys.Add(pair.Key);
        }

        foreach (RenderTextureDescription key in _emptyKeys)
            _free.Remove(key);
        _emptyKeys.Clear();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (PooledBundle bundle in _rented)
                bundle.DisposeResources();
            _rented.Clear();

            foreach (List<PooledBundle> list in _free.Values)
                foreach (PooledBundle bundle in list)
                    bundle.DisposeResources();
            _free.Clear();
        }
    }

    /// <summary>
    /// A pooled RenderTexture and the execution that owns it.
    /// </summary>
    internal sealed class PooledBundle(RenderTexture texture)
    {
        public RenderTexture Texture { get; } = texture;
        public RenderTextureDescription Desc => Texture.Desc;

        /// <summary>Owning execution. 0 means free.</summary>
        public ulong RentedExecutionId;
        public ulong LastRentedExecutionId;

        public void DisposeResources() => Texture.Dispose();
    }
}
