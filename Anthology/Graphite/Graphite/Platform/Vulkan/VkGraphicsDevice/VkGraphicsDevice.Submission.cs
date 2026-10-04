using System.Buffers;
using System.Collections.Generic;

using Silk.NET.Vulkan;

using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private readonly object _pendingLock = new();
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

    private int GatherAcquireWaits_NoLock(VkCommandBuffer? cb)
    {
        int count = 0;
        if (cb == null)
            return count;

        foreach (VkSwapchain swapchain in cb.UsedSwapchains)
            count = AddAcquireWait_NoLock(count, swapchain);
        return count;
    }

    private int GatherAcquireWaits_NoLock(List<VkCommandBuffer> commandBuffers)
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

    private ulong SubmitSignalingTimeline_NoLock(Silk.NET.Vulkan.CommandBuffer* commandBuffers, uint commandBufferCount, int waitCount)
    {
        ulong serial = NextSubmitSerial();
        VkSemaphore timeline = _timelineSemaphore;

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
                CommandBufferCount = commandBufferCount,
                PCommandBuffers = commandBuffers,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &timeline,
            };

            _graphicsQueueSubmitCount++;
            Vk.QueueSubmit(GraphicsQueue, 1, &si, default).CheckResult();
        }

        FlushValidationErrors();
        return serial;
    }

    /// <summary>
    /// Submits an execution's queued command buffers as one vkQueueSubmit and returns its serial. An empty
    /// batch is only submitted when it is the execution's final one, so the execution still gets a serial.
    /// </summary>
    internal ulong SubmitExecutionBatch(List<VkCommandBuffer> commandBuffers, bool isFinal)
    {
        FlushPendingInitCommands();
        int count = commandBuffers.Count;
        if (count == 0 && !isFinal)
            return 0;

        PollSubmissions();

        Silk.NET.Vulkan.CommandBuffer[] handles = ArrayPool<Silk.NET.Vulkan.CommandBuffer>.Shared.Rent(count + 1);
        try
        {
            for (int i = 0; i < count; i++)
            {
                VkCommandBuffer cb = commandBuffers[i];
                Silk.NET.Vulkan.CommandBuffer handle = cb.CommandBuffer;
                handles[i] = handle;
            }

            lock (_graphicsQueueLock)
            {
                int waitCount = GatherAcquireWaits_NoLock(commandBuffers);
                ulong serial;
                fixed (Silk.NET.Vulkan.CommandBuffer* pHandles = handles)
                    serial = SubmitSignalingTimeline_NoLock(pHandles, (uint)count, waitCount);

                lock (_pendingLock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        VkCommandBuffer cb = commandBuffers[i];
                        _pending.Enqueue(new PendingSubmission
                        {
                            Serial = serial,
                            CommandBuffer = cb,
                            TimingPool = cb.TakePendingTimingPool(),
                            StatsPool = cb.TakePendingStatsPool(),
                        });
                    }
                }

                return serial;
            }
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
        SubmitRecorded(cb, submission);
        Profiler?.RecordSubmit(cb.ProfilerInfo, isTransfer: true);
        return submission;
    }

    private void SubmitRecorded(VkCommandBuffer cb, VkGpuSubmission submission)
    {
        FlushPendingInitCommands();
        PollSubmissions();

        Silk.NET.Vulkan.CommandBuffer handle = cb.CommandBuffer;

        lock (_graphicsQueueLock)
        {
            int waitCount = GatherAcquireWaits_NoLock(cb);
            ulong serial = SubmitSignalingTimeline_NoLock(&handle, 1, waitCount);
            submission.Serial = serial;
            TagImmediatePool(cb.CommandPool, serial);

            lock (_pendingLock)
            {
                _pending.Enqueue(new PendingSubmission
                {
                    Serial = serial,
                    CommandBuffer = cb,
                    TimingPool = cb.TakePendingTimingPool(),
                    StatsPool = cb.TakePendingStatsPool(),
                    IsTransfer = true,
                    Submission = submission,
                });
            }
        }
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
            if (pending.TimingPool is { } timingPool)
            {
                double milliseconds = ResolveTiming(timingPool);
                Profiler?.RecordExecutionTime(cb.ProfilerInfo, pending.IsTransfer, milliseconds);
            }

            if (pending.StatsPool is { } statsPool)
            {
                GpuVertexStats stats = ResolvePipelineStats(statsPool);
                Profiler?.RecordGpuVertexStats(cb.ProfilerInfo, in stats);
            }

            if (pending.Submission != null)
                ReturnRecordCommandBuffer(cb);
        }
    }

    private struct PendingSubmission
    {
        public ulong Serial;
        public VkCommandBuffer? CommandBuffer;
        public QueryPool? TimingPool;
        public QueryPool? StatsPool;
        public bool IsTransfer;
        public VkGpuSubmission? Submission;
    }
}
