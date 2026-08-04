using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    private readonly object _deferredDisposalLock = new();
    private readonly List<IDisposable> _disposables = [];
    private readonly List<(ulong ExecutionId, IDisposable Disposable)> _executionRetiredDisposables = [];

    /// <summary>
    /// Queues object for disposal once the device goes idle. Use for stuff that might still be in use now.
    /// </summary>
    /// <param name="disposable">Object to dispose once idle.</param>
    public void DisposeWhenIdle(IDisposable disposable)
    {
        lock (_deferredDisposalLock)
        {
            _disposables.Add(disposable);
        }
    }

    /// <summary>
    /// Disposes the object once the execution finishes on the GPU. Freed on next reclaim (BeginExecution or WaitForIdle).
    /// </summary>
    /// <param name="executionId">Execution that gates the disposal.</param>
    /// <param name="disposable">Object to dispose once done.</param>
    internal void DisposeWhenFrameComplete(ulong executionId, IDisposable disposable)
    {
        if (executionId == 0)
        {
            disposable.Dispose();
            return;
        }

        lock (_deferredDisposalLock)
        {
            _executionRetiredDisposables.Add((executionId, disposable));
        }
    }

    private void FlushDeferredDisposals()
    {
        lock (_deferredDisposalLock)
        {
            foreach (IDisposable disposable in _disposables)
            {
                disposable.Dispose();
            }
            _disposables.Clear();
        }
    }

    private void FlushExecutionRetiredDisposables()
    {
        lock (_deferredDisposalLock)
        {
            for (int i = _executionRetiredDisposables.Count - 1; i >= 0; i--)
            {
                (ulong executionId, IDisposable disposable) = _executionRetiredDisposables[i];
                if (!IsExecutionIdCompleteFromReclaim(executionId))
                    continue;

                _executionRetiredDisposables.RemoveAt(i);
                disposable.Dispose();
            }
        }
    }

    // Reclaim-time completeness check that does not take _executionLock (the caller already holds it):
    // an id no longer among the active tasks has been reclaimed and is therefore complete.
    private bool IsExecutionIdCompleteFromReclaim(ulong executionId)
    {
        foreach (ExecutionTask task in _activeTasks)
        {
            if (task.Id == executionId)
                return false;
        }
        return true;
    }
}
