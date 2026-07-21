using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Pool of device buffers keyed by description, reclaimed when the owning execution's fence signals.
/// <para>
/// Buffers survive across executions. Once the GPU finishes, the buffer goes back to the
/// free-list instead of being destroyed, so a later rent reuses it. Reclaim is lazy, on next rent,
/// no dedicated thread.
/// </para>
/// <para>One lock guards the free-list, so concurrent rents never collide.</para>
/// </summary>
internal sealed class TransientBufferPool : System.IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly object _lock = new();
    private readonly Dictionary<BufferDescription, List<Pooled>> _free = [];
    private readonly List<Pooled> _rented = [];
    private bool _disposed;

    public TransientBufferPool(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>Rents a buffer matching desc. Goes back to free-list once executionId completes.</summary>
    public DeviceBuffer Rent(in BufferDescription desc, ulong executionId)
    {
        if (desc.SizeInBytes == 0)
            throw new RenderException("Cannot rent a transient buffer with a zero size.");

        lock (_lock)
        {
            if (_disposed)
                throw new RenderException("Cannot rent from a disposed transient buffer pool.");

            ReclaimCompleted();

            if (_free.TryGetValue(desc, out List<Pooled>? list) && list.Count > 0)
            {
                Pooled recycled = list[^1];
                list.RemoveAt(list.Count - 1);
                recycled.RentedExecutionId = executionId;
                _rented.Add(recycled);
                return recycled.Buffer;
            }

            Pooled created = new(_device.ResourceFactory.CreateBuffer(desc), desc);
            created.Buffer.SetTransientWrites(true);
            created.RentedExecutionId = executionId;
            _rented.Add(created);
            return created.Buffer;
        }
    }

    private void ReclaimCompleted()
    {
        for (int i = _rented.Count - 1; i >= 0; i--)
        {
            Pooled pooled = _rented[i];
            if (!_device.IsExecutionIdComplete(pooled.RentedExecutionId))
                continue;

            _rented.RemoveAt(i);
            pooled.RentedExecutionId = 0;

            if (!_free.TryGetValue(pooled.Desc, out List<Pooled>? list))
            {
                list = [];
                _free[pooled.Desc] = list;
            }
            list.Add(pooled);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (Pooled pooled in _rented)
                pooled.Buffer.Dispose();
            _rented.Clear();

            foreach (List<Pooled> list in _free.Values)
                foreach (Pooled pooled in list)
                    pooled.Buffer.Dispose();
            _free.Clear();
        }
    }

    private sealed class Pooled
    {
        public DeviceBuffer Buffer { get; }
        public BufferDescription Desc { get; }
        public ulong RentedExecutionId;

        public Pooled(DeviceBuffer buffer, in BufferDescription desc)
        {
            Buffer = buffer;
            Desc = desc;
        }
    }
}
