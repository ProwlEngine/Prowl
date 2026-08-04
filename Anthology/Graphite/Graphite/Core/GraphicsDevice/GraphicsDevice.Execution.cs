using System;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>Max in-flight executions.</summary>
    protected internal uint _maxExecutingTasks;

    /// <summary>Start size of each slot's transient bump-allocator buffer, bytes.</summary>
    protected internal uint _transientInitialSize;

    /// <summary>Soft cap on per-execution transient usage, bytes.</summary>
    protected internal uint _transientSoftCapBytes;

    /// <summary>Hard cap on per-execution transient usage, bytes.</summary>
    protected internal uint _transientHardCapBytes;

    /// <summary>True once the soft cap warning has fired.</summary>
    protected internal bool _transientSoftCapWarned;

    /// <summary>Ever-up execution counter. 0 = nothing started yet.</summary>
    protected ulong _executionIdCounter;

    /// <summary>Last known-done execution id. Updated lazily.</summary>
    protected ulong _lastCompletedExecutionId;

    private readonly object _executionLock = new();
    private readonly List<ExecutionTask> _activeTasks = [];
    private Queue<uint> _freeSlots;

    /// <summary>
    /// Latest GPU-completed execution id. Advances on reclaim. 0 = nothing done yet.
    /// </summary>
    public ulong LastCompletedExecutionId => Volatile.Read(ref _lastCompletedExecutionId);

    /// <summary>
    /// Max in-flight executions. Past this, BeginExecution blocks till the oldest finishes.
    /// </summary>
    public uint MaxExecutingTasks => _maxExecutingTasks;

    /// <summary>
    /// In-flight execution count. Reclaims finished ones along the way.
    /// </summary>
    public uint ExecutingTasks
    {
        get
        {
            lock (_executionLock)
            {
                ReclaimCompletedExecutions_NoLock();
                return (uint)_activeTasks.Count;
            }
        }
    }

    /// <summary>
    /// Snapshot of in-flight executions, oldest first.
    /// </summary>
    public IReadOnlyList<ExecutionTask> ActiveExecutions
    {
        get
        {
            lock (_executionLock)
            {
                ReclaimCompletedExecutions_NoLock();
                return _activeTasks.ToArray();
            }
        }
    }

    /// <summary>
    /// Starts a new execution, grabs a free ring slot, blocks on the oldest if all slots are busy.
    /// <para>
    /// Replaces the old BeginFrame/EndFrame pair. No "current" execution - the graph builds on this directly.
    /// </para>
    /// </summary>
    /// <returns>New execution handle.</returns>
    public ExecutionTask BeginExecution()
    {
        lock (_executionLock)
        {
            ReclaimCompletedExecutions_NoLock();

            if (_freeSlots.Count == 0)
            {
                ExecutionTask oldest = _activeTasks[0];
                WaitForExecutionCore(oldest, ulong.MaxValue);
                ReclaimCompletedExecutions_NoLock();
            }

            uint ringSlot = _freeSlots.Dequeue();
            ulong id = ++_executionIdCounter;

            ExecutionTask task = BeginExecutionCore(id, ringSlot);
            _activeTasks.Add(task);
            return task;
        }
    }

    /// <summary>
    /// Marks execution done; fence signals when GPU work finishes. Non-blocking, stays in flight till the slot's reclaimed. Replaces old EndFrame.
    /// </summary>
    /// <param name="task">Execution to complete, from BeginExecution.</param>
    /// <exception cref="ArgumentNullException">Thrown if task is null.</exception>
    public void CompleteExecution(ExecutionTask task)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(CompleteExecution));
        CompleteExecutionCore(task);
    }

    /// <summary>
    /// Whether the execution finished on the GPU. Bumps LastCompletedExecutionId as a side effect.
    /// </summary>
    /// <param name="task">Execution to check.</param>
    /// <returns>True if complete, false if still in flight.</returns>
    public bool IsExecutionComplete(ExecutionTask task)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(IsExecutionComplete));
        bool complete = IsExecutionCompleteCore(task);
        if (complete)
            AdvanceLastCompletedExecutionId(task.Id);
        return complete;
    }

    /// <summary>
    /// Whether the execution id has finished. Used by the transient texture pool for reclaim checks. A never-started id counts as not complete.
    /// </summary>
    internal bool IsExecutionIdComplete(ulong executionId)
    {
        if (executionId == 0)
            return true;

        lock (_executionLock)
        {
            foreach (ExecutionTask task in _activeTasks)
            {
                if (task.Id == executionId)
                    return IsExecutionCompleteCore(task);
            }

            // Not in flight: either it started and was already reclaimed (complete), or it was never started.
            return executionId <= _executionIdCounter;
        }
    }

    /// <summary>
    /// Blocks until the execution finishes on the GPU, or until timeout.
    /// </summary>
    /// <param name="task">Execution to wait for.</param>
    /// <param name="nanosecondTimeout">Max wait in ns. ulong.MaxValue = no timeout.</param>
    /// <returns>True if it finished before timeout, false otherwise.</returns>
    public bool WaitForExecution(ExecutionTask task, ulong nanosecondTimeout = ulong.MaxValue)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(WaitForExecution));
        bool completed = WaitForExecutionCore(task, nanosecondTimeout);
        if (completed)
            AdvanceLastCompletedExecutionId(task.Id);
        return completed;
    }

    /// <summary>
    /// Blocks till all submitted work and in-flight executions are done. Reclaims every slot.
    /// </summary>
    public void WaitForIdle()
    {
        WaitForIdleCore();
        lock (_executionLock)
        {
            Volatile.Write(ref _lastCompletedExecutionId, _executionIdCounter);
            _activeTasks.Clear();
            _freeSlots.Clear();
            for (uint i = 0; i < _maxExecutingTasks; i++)
                _freeSlots.Enqueue(i);
        }
        FlushExecutionRetiredDisposables();
        FlushDeferredDisposals();
    }

    /// <summary>
    /// Submits a recorded transfer command buffer now and blocks till the GPU finishes it. Not tied to the execution ring or fences at all. For one-off transfer work like readback or streaming uploads.
    /// </summary>
    /// <param name="commandBuffer">Recorded transfer command buffer, already Ended.</param>
    public void SubmitAndWait(TransferCommandBuffer commandBuffer)
    {
        SubmitAndWait_CheckEnded(commandBuffer);
        SubmitAndWaitCore(commandBuffer);

        Profiler?.RecordSubmit(commandBuffer.ProfilerInfo, isTransfer: true);
    }

    /// <summary>
    /// Submits a recorded transfer command buffer without blocking the calling thread. Not tied to the execution ring or fences.
    /// </summary>
    /// <param name="commandBuffer">Recorded transfer command buffer, already Ended.</param>
    internal void SubmitTransfer(TransferCommandBuffer commandBuffer)
    {
        SubmitAndWait_CheckEnded(commandBuffer);
        SubmitTransferCore(commandBuffer);

        Profiler?.RecordSubmit(commandBuffer.ProfilerInfo, isTransfer: true);
    }

    /// <summary>
    /// Sets up execution/transient options. Call before PostDeviceCreated in each backend constructor.
    /// </summary>
    /// <param name="options">Options to read from.</param>
    protected void InitializeFrameOptions(GraphicsDeviceOptions options)
    {
        _maxExecutingTasks = options.MaxFramesInFlight == 0 ? 3 : options.MaxFramesInFlight;
        _freeSlots = new Queue<uint>((int)_maxExecutingTasks);
        for (uint i = 0; i < _maxExecutingTasks; i++)
            _freeSlots.Enqueue(i);
        _transientInitialSize = options.TransientBufferInitialSize == 0 ? 4 * 1024 * 1024 : options.TransientBufferInitialSize;
        _transientSoftCapBytes = options.TransientBufferSoftCapBytes == 0 ? 64 * 1024 * 1024 : options.TransientBufferSoftCapBytes;
        _transientHardCapBytes = options.TransientBufferHardCapBytes == 0 ? 256 * 1024 * 1024 : options.TransientBufferHardCapBytes;

        if (_transientSoftCapBytes < _transientInitialSize)
            _transientSoftCapBytes = _transientInitialSize;
        if (_transientHardCapBytes < _transientSoftCapBytes)
            _transientHardCapBytes = _transientSoftCapBytes;

        InitializeFrameOptions_SetValidationEnabled(options);
        InitializeFrameOptions_InitializeProfiling(options);
    }

    private void AdvanceLastCompletedExecutionId(ulong executionId)
        => Volatile.Write(ref _lastCompletedExecutionId, Math.Max(Volatile.Read(ref _lastCompletedExecutionId), executionId));

    private void ReclaimCompletedExecutions_NoLock()
    {
        for (int i = _activeTasks.Count - 1; i >= 0; i--)
        {
            ExecutionTask task = _activeTasks[i];
            if (!IsExecutionCompleteCore(task))
                continue;

            _activeTasks.RemoveAt(i);
            _freeSlots.Enqueue(task.RingSlot);
            AdvanceLastCompletedExecutionId(task.Id);
        }

        FlushExecutionRetiredDisposables();
    }

    private protected abstract ExecutionTask BeginExecutionCore(ulong executionId, uint ringSlot);
    private protected abstract void CompleteExecutionCore(ExecutionTask task);
    private protected abstract bool IsExecutionCompleteCore(ExecutionTask task);
    private protected abstract bool WaitForExecutionCore(ExecutionTask task, ulong nanosecondTimeout);
    private protected abstract void WaitForIdleCore();
    private protected abstract void SubmitAndWaitCore(TransferCommandBuffer commandBuffer);
    private protected abstract void SubmitTransferCore(TransferCommandBuffer commandBuffer);
}
