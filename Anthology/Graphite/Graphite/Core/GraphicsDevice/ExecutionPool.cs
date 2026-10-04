using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

internal sealed class ExecutionPool<TDesc, TResource>(
    GraphicsDevice device,
    Func<TDesc, TResource> create,
    Action<TResource> dispose,
    string name) : IDisposable
    where TDesc : notnull
    where TResource : class
{
    internal const ulong RetentionExecutions = 120;

    private readonly GraphicsDevice _device = device;
    private readonly Func<TDesc, TResource> _create = create;
    private readonly Action<TResource> _dispose = dispose;
    private readonly string _name = name;
    private readonly object _lock = new();
    private readonly Dictionary<TDesc, List<Entry>> _free = [];
    private readonly List<Entry> _rented = [];
    private readonly List<TDesc> _emptyKeys = [];
    private ulong _lastEvictionExecutionId;
    private bool _disposed;

    public TResource Rent(in TDesc desc, ulong executionId)
    {
        lock (_lock)
        {
            if (_disposed)
                throw new RenderException($"Cannot rent from a disposed {_name} pool.");

            ReclaimCompleted();
            EvictUnused(executionId);

            Entry entry;
            if (_free.TryGetValue(desc, out List<Entry>? list) && list.Count > 0)
            {
                entry = list[^1];
                list.RemoveAt(list.Count - 1);
            }
            else
            {
                entry = new Entry(_create(desc), desc);
            }

            entry.RentedExecutionId = executionId;
            entry.LastRentedExecutionId = executionId;
            _rented.Add(entry);
            return entry.Resource;
        }
    }

    private void ReclaimCompleted()
    {
        for (int i = _rented.Count - 1; i >= 0; i--)
        {
            Entry entry = _rented[i];
            if (!_device.IsExecutionIdComplete(entry.RentedExecutionId))
                continue;

            _rented.RemoveAt(i);
            entry.RentedExecutionId = 0;

            if (!_free.TryGetValue(entry.Desc, out List<Entry>? list))
            {
                list = [];
                _free[entry.Desc] = list;
            }
            list.Add(entry);
        }
    }

    private void EvictUnused(ulong executionId)
    {
        if (executionId == _lastEvictionExecutionId)
            return;

        _lastEvictionExecutionId = executionId;
        foreach (KeyValuePair<TDesc, List<Entry>> pair in _free)
        {
            List<Entry> list = pair.Value;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Entry entry = list[i];
                if (executionId - entry.LastRentedExecutionId <= RetentionExecutions)
                    continue;

                list.RemoveAt(i);
                _dispose(entry.Resource);
            }

            if (list.Count == 0)
                _emptyKeys.Add(pair.Key);
        }

        foreach (TDesc key in _emptyKeys)
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

            foreach (Entry entry in _rented)
                _dispose(entry.Resource);
            _rented.Clear();

            foreach (List<Entry> list in _free.Values)
                foreach (Entry entry in list)
                    _dispose(entry.Resource);
            _free.Clear();
        }
    }

    private sealed class Entry(TResource resource, TDesc desc)
    {
        public TResource Resource { get; } = resource;
        public TDesc Desc { get; } = desc;
        public ulong RentedExecutionId;
        public ulong LastRentedExecutionId;
    }
}
