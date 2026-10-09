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

    /// <summary>Fixed cap on per-execution transient usage, bytes. Over this throws.</summary>
    protected internal const uint TransientHardCapBytes = 256 * 1024 * 1024;

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
    /// Starts a new execution, grabs a free ring slot, blocks on the oldest if all slots are busy.
    /// <para>
    /// Replaces the old BeginFrame/EndFrame pair. No "current" execution - the graph builds on this directly.
    /// </para>
    /// </summary>
    /// <param name="profilers">Profilers for this execution only. Empty creates one from each <see cref="GlobalProfilers"/> factory.</param>
    /// <returns>New execution handle.</returns>
    public ExecutionTask BeginExecution(params IProfiler[] profilers)
    {
        ValidationHelpers.RequireNotNull(this, profilers, nameof(profilers), nameof(BeginExecution));
        ProfilerSet set = ResolveProfilers(profilers);
        ExecutionTask task = BeginExecutionSlot(set);
        try
        {
            set.BeginExecution(task.Id);
        }
        catch
        {
            task.Profilers = ProfilerSet.Empty;
            CompleteExecution(task);
            throw;
        }

        return task;
    }

    private ExecutionTask BeginExecutionSlot(ProfilerSet profilers)
    {
        lock (_executionLock)
        {
            ReclaimCompletedExecutions_NoLock();

            while (_freeSlots.Count == 0)
            {
                ExecutionTask? oldest = _activeTasks.Find(t => t.IsCompleted);
                if (oldest == null)
                {
                    Monitor.Wait(_executionLock);
                    ReclaimCompletedExecutions_NoLock();
                    continue;
                }

                WaitForExecutionCore(oldest, ulong.MaxValue);
                ReclaimCompletedExecutions_NoLock();
            }

            uint ringSlot = _freeSlots.Dequeue();
            ulong id = ++_executionIdCounter;

            ExecutionTask task = BeginExecutionCore(id, ringSlot);
            task.Profilers = profilers;
            _activeTasks.Add(task);
            return task;
        }
    }

    /// <summary>
    /// Marks execution done; it completes when its GPU work finishes. Non-blocking, stays in flight till the slot's reclaimed. Replaces old EndFrame.
    /// </summary>
    /// <param name="task">Execution to complete, from BeginExecution.</param>
    /// <exception cref="ArgumentNullException">Thrown if task is null.</exception>
    public void CompleteExecution(ExecutionTask task)
    {
        ValidationHelpers.RequireNotNull(this, task, nameof(task), nameof(CompleteExecution));
        CompleteExecutionCore(task);
        lock (_executionLock)
        {
            task.MarkCompleted();
            Monitor.PulseAll(_executionLock);
        }
    }

    /// <summary>
    /// Whether the execution finished on the GPU. Non-blocking. Also polls submissions, so profiler results of finished executions are delivered from here.
    /// </summary>
    /// <param name="task">Execution to check.</param>
    /// <returns>True if complete, false if still in flight.</returns>
    public bool IsExecutionComplete(ExecutionTask task)
    {
        ValidationHelpers.RequireNotNull(this, task, nameof(task), nameof(IsExecutionComplete));
        PollSubmissionsCore();
        return IsExecutionIdComplete(task.Id);
    }

    internal bool IsExecutionIdComplete(ulong executionId)
    {
        if (executionId <= LastCompletedExecutionId)
            return true;

        lock (_executionLock)
        {
            ReclaimCompletedExecutions_NoLock();
        }
        return executionId <= LastCompletedExecutionId;
    }

    /// <summary>
    /// Whether the execution with this id finished on the GPU. Non-blocking, and polls submissions like the task overload.
    /// </summary>
    /// <param name="executionId">Id of an execution started on this device.</param>
    /// <returns>True if complete, false if still in flight or not yet completed by its dispatcher.</returns>
    public bool IsExecutionComplete(ulong executionId)
    {
        ValidateExecutionId(executionId, nameof(IsExecutionComplete));
        PollSubmissionsCore();
        return IsExecutionIdComplete(executionId);
    }

    /// <summary>
    /// Blocks until the execution with this id finishes on the GPU, or until timeout. Returns false at once if its dispatcher has not completed it yet.
    /// </summary>
    /// <param name="executionId">Id of an execution started on this device.</param>
    /// <param name="nanosecondTimeout">Max wait in ns. ulong.MaxValue = no timeout.</param>
    /// <returns>True if it finished before timeout, false otherwise.</returns>
    public bool WaitForExecution(ulong executionId, ulong nanosecondTimeout = ulong.MaxValue)
    {
        ValidateExecutionId(executionId, nameof(WaitForExecution));
        ExecutionTask? task;
        lock (_executionLock)
        {
            task = _activeTasks.Find(t => t.Id == executionId);
        }

        if (task != null)
            return WaitForExecution(task, nanosecondTimeout);

        PollSubmissionsCore();
        return IsExecutionIdComplete(executionId);
    }

    private void ValidateExecutionId(ulong executionId, string operation)
    {
        if (executionId == 0 || executionId > Volatile.Read(ref _executionIdCounter))
            throw new ArgumentOutOfRangeException(nameof(executionId), $"{operation}: no execution with id {executionId} was started on this device.");
    }

    /// <summary>
    /// Blocks until the execution finishes on the GPU, or until timeout.
    /// </summary>
    /// <param name="task">Execution to wait for.</param>
    /// <param name="nanosecondTimeout">Max wait in ns. ulong.MaxValue = no timeout.</param>
    /// <returns>True if it finished before timeout, false otherwise.</returns>
    public bool WaitForExecution(ExecutionTask task, ulong nanosecondTimeout = ulong.MaxValue)
    {
        ValidationHelpers.RequireNotNull(this, task, nameof(task), nameof(WaitForExecution));
        bool completed = WaitForExecutionCore(task, nanosecondTimeout);
        if (completed)
        {
            PollSubmissionsCore();
            lock (_executionLock)
            {
                ReclaimCompletedExecutions_NoLock();
            }
        }
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
        FlushAllRetired();
    }

    /// <summary>
    /// Records transfer work into a pooled command buffer and submits it now, outside any graph. Only update, copy and mipmap commands are allowed. Wait on the result or drop it for fire-and-forget uploads.
    /// </summary>
    /// <param name="record">Records the work. Runs synchronously on the calling thread.</param>
    /// <param name="name">Optional debug name.</param>
    /// <returns>Handle that completes when the GPU is done.</returns>
    public GpuSubmission Record(System.Action<CommandBuffer> record, string name = "")
    {
        ValidationHelpers.RequireNotNull(this, record, nameof(record), nameof(Record));
        return RecordCore(record, name);
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

        InitializeFrameOptions_SetValidationEnabled(options);
    }

    private void ReclaimCompletedExecutions_NoLock()
    {
        for (int i = _activeTasks.Count - 1; i >= 0; i--)
        {
            ExecutionTask task = _activeTasks[i];
            if (!IsExecutionCompleteCore(task))
                continue;

            _activeTasks.RemoveAt(i);
            _freeSlots.Enqueue(task.RingSlot);
        }

        ulong completed = _activeTasks.Count == 0 ? _executionIdCounter : _activeTasks[0].Id - 1;
        if (completed > _lastCompletedExecutionId)
            Volatile.Write(ref _lastCompletedExecutionId, completed);

        FlushRetired(everything: false);
    }

    private protected abstract ExecutionTask BeginExecutionCore(ulong executionId, uint ringSlot);
    private protected abstract void CompleteExecutionCore(ExecutionTask task);
    private protected abstract bool IsExecutionCompleteCore(ExecutionTask task);
    private protected abstract void PollSubmissionsCore();

    private protected abstract bool WaitForExecutionCore(ExecutionTask task, ulong nanosecondTimeout);
    private protected abstract void WaitForIdleCore();
    private protected abstract GpuSubmission RecordCore(System.Action<CommandBuffer> record, string name);
}
