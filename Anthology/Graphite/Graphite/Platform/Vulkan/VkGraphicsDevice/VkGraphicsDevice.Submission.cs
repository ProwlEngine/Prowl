using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Silk.NET.Vulkan;

using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private readonly object _pendingLock = new();
    private readonly Dictionary<ulong, ExecutionQueryState> _executionQueries = new();
    private readonly Queue<PendingSubmission> _pending = new();
    private VkSemaphore _timelineSemaphore;
    private int _graphicsQueueSubmitCount;

    /// <summary>Test hook: total vkQueueSubmit calls made against the graphics queue.</summary>
    internal int GraphicsQueueSubmitCount => System.Threading.Volatile.Read(ref _graphicsQueueSubmitCount);

    private const PipelineStageFlags AcquireWaitStages = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.TransferBit;

    private VkSemaphore[] _acquireWaitSemaphores = new VkSemaphore[1];
    private PipelineStageFlags[] _acquireWaitStages = new PipelineStageFlags[1];
    private ulong[] _acquireWaitValues = new ulong[1];

    private int AddAcquireWait_NoLock(int count, VkSwapchain swapchain)
    {
        VkSemaphore pending = swapchain.TakePendingAcquire();
        if (pending.Handle == 0)
            return count;

        if (count == _acquireWaitSemaphores.Length)
        {
            System.Array.Resize(ref _acquireWaitSemaphores, count * 2);
            System.Array.Resize(ref _acquireWaitStages, count * 2);
            System.Array.Resize(ref _acquireWaitValues, count * 2);
        }

        _acquireWaitSemaphores[count] = pending;
        _acquireWaitStages[count] = AcquireWaitStages;
        return count + 1;
    }

    private int GatherAcquireWaits_NoLock(System.ReadOnlySpan<VkCommandBuffer> commandBuffers)
    {
        int count = 0;
        foreach (VkCommandBuffer cb in commandBuffers)
        {
            foreach (VkSwapchain swapchain in cb.UsedSwapchains)
                count = AddAcquireWait_NoLock(count, swapchain);
        }
        return count;
    }

    private void SubmitSemaphoresOnly_NoLock(VkSwapchain swapchain, VkSemaphore* signal)
    {
        int waitCount = AddAcquireWait_NoLock(0, swapchain);
        if (waitCount == 0 && signal == null)
            return;

        fixed (VkSemaphore* waits = _acquireWaitSemaphores)
        fixed (PipelineStageFlags* stages = _acquireWaitStages)
        {
            SubmitInfo si = new(sType: StructureType.SubmitInfo)
            {
                WaitSemaphoreCount = (uint)waitCount,
                PWaitSemaphores = waits,
                PWaitDstStageMask = stages,
                SignalSemaphoreCount = signal != null ? 1u : 0u,
                PSignalSemaphores = signal,
            };

            _graphicsQueueSubmitCount++;
            Vk.QueueSubmit(GraphicsQueue, 1, &si, default).CheckResult();
            FlushValidationErrors();
        }
    }

    internal void ConsumePendingAcquires(VkSwapchain swapchain)
    {
        lock (_graphicsQueueLock)
            SubmitSemaphoresOnly_NoLock(swapchain, null);
    }

    internal void SignalPresentSemaphore(VkSwapchain swapchain, VkSemaphore semaphore)
    {
        lock (_graphicsQueueLock)
            SubmitSemaphoresOnly_NoLock(swapchain, &semaphore);
    }

    internal void WaitForGraphicsQueueIdle()
    {
        lock (_graphicsQueueLock)
            Vk.QueueWaitIdle(GraphicsQueue);
    }

    private void CreateTimelineSemaphore()
    {
        SemaphoreTypeCreateInfo typeCI = new(sType: StructureType.SemaphoreTypeCreateInfo)
        {
            SemaphoreType = SemaphoreType.Timeline,
            InitialValue = 0,
        };
        SemaphoreCreateInfo semaphoreCI = new(sType: StructureType.SemaphoreCreateInfo, pNext: &typeCI);
        Vk.CreateSemaphore(Device, in semaphoreCI, null, out _timelineSemaphore).CheckResult();
    }

    internal ulong GetCompletedSerial()
    {
        Vk.GetSemaphoreCounterValue(Device, _timelineSemaphore, out ulong value).CheckResult();
        return value;
    }

    internal bool WaitForSerial(ulong serial, ulong nanosecondTimeout)
    {
        VkSemaphore timeline = _timelineSemaphore;
        SemaphoreWaitInfo waitInfo = new(sType: StructureType.SemaphoreWaitInfo)
        {
            SemaphoreCount = 1,
            PSemaphores = &timeline,
            PValues = &serial,
        };
        return Vk.WaitSemaphores(Device, in waitInfo, nanosecondTimeout) == Result.Success;
    }

    private ulong Submit(System.ReadOnlySpan<Silk.NET.Vulkan.CommandBuffer> handles, System.ReadOnlySpan<VkCommandBuffer> tracked, VkGpuSubmission? submission = null)
    {
        FlushPendingInitCommands();
        PollSubmissions();

        lock (_graphicsQueueLock)
        {
            int waitCount = GatherAcquireWaits_NoLock(tracked);
            ulong serial = NextSubmitSerial();
            VkSemaphore timeline = _timelineSemaphore;

            fixed (Silk.NET.Vulkan.CommandBuffer* pHandles = handles)
            fixed (VkSemaphore* waits = _acquireWaitSemaphores)
            fixed (PipelineStageFlags* stages = _acquireWaitStages)
            fixed (ulong* waitValues = _acquireWaitValues)
            {
                TimelineSemaphoreSubmitInfo timelineInfo = new(sType: StructureType.TimelineSemaphoreSubmitInfo)
                {
                    WaitSemaphoreValueCount = (uint)waitCount,
                    PWaitSemaphoreValues = waitValues,
                    SignalSemaphoreValueCount = 1,
                    PSignalSemaphoreValues = &serial,
                };
                SubmitInfo si = new(sType: StructureType.SubmitInfo)
                {
                    PNext = &timelineInfo,
                    WaitSemaphoreCount = (uint)waitCount,
                    PWaitSemaphores = waits,
                    PWaitDstStageMask = stages,
                    CommandBufferCount = (uint)handles.Length,
                    PCommandBuffers = pHandles,
                    SignalSemaphoreCount = 1,
                    PSignalSemaphores = &timeline,
                };

                _graphicsQueueSubmitCount++;
                Vk.QueueSubmit(GraphicsQueue, 1, &si, default).CheckResult();
            }

            FlushValidationErrors();

            if (submission != null)
                submission.Serial = serial;

            lock (_pendingLock)
            {
                foreach (VkCommandBuffer cb in tracked)
                {
                    CommandBufferInfo info = cb.ProfilerInfo;
                    IGpuStatsProfiler? gpuStats = cb.Profilers.GpuStats;
                    if (info.ExecutionId != 0 && gpuStats != null)
                    {
                        ExecutionQueryState queryState = QueryStateFor_NoLock(info.ExecutionId);
                        queryState.Stats = gpuStats;
                        queryState.Outstanding++;
                    }

                    _pending.Enqueue(new PendingSubmission
                    {
                        Serial = serial,
                        CommandBuffer = cb,
                        Info = info,
                        GpuStats = gpuStats,
                        Queries = cb.TakePendingQueries(),
                        IsTransfer = submission != null,
                        Submission = submission,
                    });
                }
            }

            return serial;
        }
    }

    internal ulong SubmitImmediate(Silk.NET.Vulkan.CommandBuffer cb, CommandPool pool)
    {
        ulong serial = Submit(new System.ReadOnlySpan<Silk.NET.Vulkan.CommandBuffer>(in cb), []);
        TagImmediatePool(pool, serial);
        return serial;
    }

    /// <summary>
    /// Submits an execution's queued command buffers as one vkQueueSubmit and returns its serial. An empty
    /// batch is only submitted when it is the execution's final one, so the execution still gets a serial.
    /// </summary>
    internal ulong SubmitExecutionBatch(List<VkCommandBuffer> commandBuffers, ulong executionId, bool isFinal, IGpuStatsProfiler? gpuStats)
    {
        FlushPendingInitCommands();
        int count = commandBuffers.Count;
        if (count == 0 && !isFinal)
            return 0;

        Silk.NET.Vulkan.CommandBuffer[] handles = ArrayPool<Silk.NET.Vulkan.CommandBuffer>.Shared.Rent(count + 1);
        try
        {
            for (int i = 0; i < count; i++)
                handles[i] = commandBuffers[i].CommandBuffer;

            ulong serial = Submit(new System.ReadOnlySpan<Silk.NET.Vulkan.CommandBuffer>(handles, 0, count), CollectionsMarshal.AsSpan(commandBuffers));
            if (isFinal)
                CloseExecutionQueries(executionId, gpuStats);
            return serial;
        }
        finally
        {
            ArrayPool<Silk.NET.Vulkan.CommandBuffer>.Shared.Return(handles);
        }
    }

    private protected override GpuSubmission RecordCore(System.Action<CommandBuffer> record, string name)
    {
        VkCommandBuffer cb = RentRecordCommandBuffer();
        try
        {
            cb.Name = name;
            cb.Begin();
            cb.RecordFullBarrier();
            record(cb);
            cb.RecordFullBarrier();
            cb.End();
        }
        catch
        {
            if (cb.IsRecording)
                cb.End();
            TagImmediatePool(cb.CommandPool, 0);
            ReturnRecordCommandBuffer(cb);
            throw;
        }

        VkGpuSubmission submission = new(this);
        Silk.NET.Vulkan.CommandBuffer handle = cb.CommandBuffer;
        ulong serial = Submit(new System.ReadOnlySpan<Silk.NET.Vulkan.CommandBuffer>(in handle), new System.ReadOnlySpan<VkCommandBuffer>(in cb), submission);
        TagImmediatePool(cb.CommandPool, serial);
        return submission;
    }

    internal void PollSubmissions()
    {
        ulong completed = GetCompletedSerial();

        List<PendingSubmission>? done = null;
        lock (_pendingLock)
        {
            while (_pending.Count > 0 && _pending.Peek().Serial <= completed)
                (done ??= []).Add(_pending.Dequeue());
        }

        if (done != null)
        {
            foreach (PendingSubmission submission in done)
                CompleteSubmission(in submission);
        }

        if (completed != 0)
            RetireThrough(completed);
    }

    private void CompleteSubmission(in PendingSubmission pending)
    {
        if (pending.CommandBuffer is { } cb)
        {
            ResolveQueries(in pending.Queries, pending.Info, pending.IsTransfer, pending.GpuStats);

            if (pending.Submission != null)
                ReturnRecordCommandBuffer(cb);
        }

        if (pending.Info.ExecutionId != 0)
            ReleaseExecutionQuery(pending.Info.ExecutionId);
    }

    private ExecutionQueryState QueryStateFor_NoLock(ulong executionId)
    {
        if (!_executionQueries.TryGetValue(executionId, out ExecutionQueryState? state))
            _executionQueries[executionId] = state = new ExecutionQueryState();
        return state;
    }

    private void CloseExecutionQueries(ulong executionId, IGpuStatsProfiler? gpuStats)
    {
        if (gpuStats == null)
            return;

        bool resolved;
        lock (_pendingLock)
        {
            ExecutionQueryState state = QueryStateFor_NoLock(executionId);
            state.Stats = gpuStats;
            state.Closed = true;
            resolved = state.Outstanding == 0;
            if (resolved)
                _executionQueries.Remove(executionId);
        }

        if (resolved)
            gpuStats.RecordExecutionResolved(executionId);
    }

    private void ReleaseExecutionQuery(ulong executionId)
    {
        bool resolved = false;
        IGpuStatsProfiler? gpuStats = null;
        lock (_pendingLock)
        {
            if (_executionQueries.TryGetValue(executionId, out ExecutionQueryState? state))
            {
                gpuStats = state.Stats;
                state.Outstanding--;
                resolved = state.Closed && state.Outstanding == 0;
                if (resolved)
                    _executionQueries.Remove(executionId);
            }
        }

        if (resolved)
            gpuStats?.RecordExecutionResolved(executionId);
    }

    private sealed class ExecutionQueryState
    {
        public int Outstanding;
        public bool Closed;
        public IGpuStatsProfiler? Stats;
    }

    private struct PendingSubmission
    {
        public ulong Serial;
        public VkCommandBuffer? CommandBuffer;
        public CommandBufferInfo Info;
        public GpuQueries Queries;
        public IGpuStatsProfiler? GpuStats;
        public bool IsTransfer;
        public VkGpuSubmission? Submission;
    }
}
