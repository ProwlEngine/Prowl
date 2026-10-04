using System;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    private readonly object _retireLock = new();
    private readonly List<(ulong Serial, ulong ExecutionId, Action Destroy)> _retired = [];
    private ulong _submitSerial;
    private ulong _completedSerial;

    /// <summary>
    /// Hands out the next queue submission serial. Call once per queue submit, in submit order.
    /// </summary>
    internal ulong NextSubmitSerial() => Interlocked.Increment(ref _submitSerial);

    /// <summary>
    /// Queues native destruction for after the next submission and the latest open execution have finished on the GPU.
    /// </summary>
    /// <param name="destroy">Frees the native object.</param>
    internal void DisposeWhenRetired(Action destroy)
    {
        ulong serial = Volatile.Read(ref _submitSerial) + 1;
        ulong executionId = Volatile.Read(ref _executionIdCounter);
        if (executionId <= Volatile.Read(ref _lastCompletedExecutionId))
            executionId = 0;

        lock (_retireLock)
        {
            _retired.Add((serial, executionId, destroy));
        }
    }

    /// <summary>
    /// Records that every submission up to the serial finished, then frees whatever that unblocks.
    /// </summary>
    /// <param name="serial">Highest finished submission serial.</param>
    internal void RetireThrough(ulong serial)
    {
        lock (_retireLock)
        {
            if (serial > _completedSerial)
                _completedSerial = serial;
        }
        FlushRetired(everything: false);
    }

    private void FlushRetired(bool everything)
    {
        List<Action>? ready = null;
        while (true)
        {
            lock (_retireLock)
            {
                ulong completedExecution = Volatile.Read(ref _lastCompletedExecutionId);
                for (int i = 0; i < _retired.Count; i++)
                {
                    (ulong serial, ulong executionId, Action destroy) = _retired[i];
                    if (!everything && (serial > _completedSerial || executionId > completedExecution))
                        continue;

                    (ready ??= []).Add(destroy);
                    _retired.RemoveAt(i);
                    i--;
                }
            }

            if (ready == null || ready.Count == 0)
                return;

            foreach (Action destroy in ready)
                destroy();
            ready.Clear();
        }
    }

    /// <summary>
    /// Frees everything queued. Only valid when the GPU is idle.
    /// </summary>
    internal void FlushAllRetired()
    {
        lock (_retireLock)
        {
            _completedSerial = Volatile.Read(ref _submitSerial);
        }
        FlushRetired(everything: true);
    }
}
