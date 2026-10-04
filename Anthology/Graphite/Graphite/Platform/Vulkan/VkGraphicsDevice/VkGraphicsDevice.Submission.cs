using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Silk.NET.Vulkan;

using VkFenceHandle = Silk.NET.Vulkan.Fence;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private readonly object _submittedFencesLock = new();
    private readonly ConcurrentQueue<VkFenceHandle> _availableSubmissionFences = new();
    private readonly List<FenceSubmissionInfo> _submittedFences = [];
    private int _graphicsQueueSubmitCount;

    /// <summary>Test hook: total vkQueueSubmit calls made against the graphics queue.</summary>
    internal int GraphicsQueueSubmitCount => System.Threading.Volatile.Read(ref _graphicsQueueSubmitCount);

    private const PipelineStageFlags AcquireWaitStages = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.TransferBit;

    private readonly List<VkSwapchain> _swapchains = [];
    private VkSemaphore[] _acquireWaitSemaphores = new VkSemaphore[1];
    private PipelineStageFlags[] _acquireWaitStages = new PipelineStageFlags[1];

    internal void RegisterSwapchain(VkSwapchain swapchain)
    {
        lock (_graphicsQueueLock)
            _swapchains.Add(swapchain);
    }

    internal void UnregisterSwapchain(VkSwapchain swapchain)
    {
        lock (_graphicsQueueLock)
            _swapchains.Remove(swapchain);
    }

    private int GatherAcquireWaits_NoLock()
    {
        int count = 0;
        foreach (VkSwapchain swapchain in _swapchains)
        {
            VkSemaphore pending = swapchain.TakePendingAcquire();
            if (pending.Handle == 0)
                continue;

            if (count == _acquireWaitSemaphores.Length)
            {
                System.Array.Resize(ref _acquireWaitSemaphores, count * 2);
                System.Array.Resize(ref _acquireWaitStages, count * 2);
            }

            _acquireWaitSemaphores[count] = pending;
            _acquireWaitStages[count] = AcquireWaitStages;
            count++;
        }
        return count;
    }

    private void SubmitSemaphoresOnly_NoLock(VkSemaphore* signal)
    {
        int waitCount = GatherAcquireWaits_NoLock();
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

    internal void ConsumePendingAcquires()
    {
        lock (_graphicsQueueLock)
            SubmitSemaphoresOnly_NoLock(null);
    }

    internal void SignalPresentSemaphore(VkSemaphore semaphore)
    {
        lock (_graphicsQueueLock)
            SubmitSemaphoresOnly_NoLock(&semaphore);
    }

    internal void WaitForGraphicsQueueIdle()
    {
        lock (_graphicsQueueLock)
            Vk.QueueWaitIdle(GraphicsQueue);
    }

    /// <summary>
    /// Submits an execution's queued command buffers as one vkQueueSubmit. A null slot fence takes a
    /// pooled one instead, for a mid-execution flush.
    /// </summary>
    internal void SubmitExecutionBatch(List<VkCommandBuffer> commandBuffers, VkFenceHandle? slotFence)
    {
        FlushPendingInitCommands();
        int count = commandBuffers.Count;
        if (count == 0 && slotFence == null)
            return;

        CheckSubmittedFences();

        bool poolFence = slotFence == null;
        VkFenceHandle fence = slotFence ?? GetFreeSubmissionFence();

        Silk.NET.Vulkan.CommandBuffer[] handles = ArrayPool<Silk.NET.Vulkan.CommandBuffer>.Shared.Rent(count + 1);
        try
        {
            for (int i = 0; i < count; i++)
            {
                VkCommandBuffer cb = commandBuffers[i];
                Silk.NET.Vulkan.CommandBuffer handle = cb.CommandBuffer;
                cb.CommandBufferSubmitted(handle);
                handles[i] = handle;
            }

            fixed (Silk.NET.Vulkan.CommandBuffer* pHandles = handles)
            {
                SubmitInfo si = new(sType: StructureType.SubmitInfo)
                {
                    CommandBufferCount = (uint)count,
                    PCommandBuffers = count > 0 ? pHandles : null,
                };

                lock (_graphicsQueueLock)
                {
                    int waitCount = GatherAcquireWaits_NoLock();
                    fixed (VkSemaphore* waits = _acquireWaitSemaphores)
                    fixed (PipelineStageFlags* stages = _acquireWaitStages)
                    {
                        si.WaitSemaphoreCount = (uint)waitCount;
                        si.PWaitSemaphores = waits;
                        si.PWaitDstStageMask = stages;
                        _graphicsQueueSubmitCount++;
                        Vk.QueueSubmit(GraphicsQueue, 1, &si, fence).CheckResult();
                    }
                    FlushValidationErrors();
                }
            }

            lock (_submittedFencesLock)
            {
                for (int i = 0; i < count; i++)
                {
                    VkCommandBuffer cb = commandBuffers[i];
                    _submittedFences.Add(new FenceSubmissionInfo(
                        fence, cb, handles[i], cb.TakePendingTimingPool(), cb.TakePendingStatsPool(),
                        cb.Name, isTransfer: false, cb.Pass, transferId: 0,
                        ownsFence: poolFence && i == count - 1));
                }
            }
        }
        finally
        {
            ArrayPool<Silk.NET.Vulkan.CommandBuffer>.Shared.Return(handles);
        }
    }

    private protected override GpuSubmission RecordCore(System.Action<CommandBuffer> record, string name)
    {
        VkCommandBuffer cb = _recordCommandBufferPool.Rent();
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
            _recordCommandBufferPool.Return(cb);
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
        CheckSubmittedFences();

        VkFenceHandle fence = GetFreeSubmissionFence();
        submission.Fence = fence;

        Silk.NET.Vulkan.CommandBuffer handle = cb.CommandBuffer;
        cb.CommandBufferSubmitted(handle);

        SubmitInfo si = new(sType: StructureType.SubmitInfo)
        {
            CommandBufferCount = 1,
            PCommandBuffers = &handle
        };

        lock (_graphicsQueueLock)
        {
            int waitCount = GatherAcquireWaits_NoLock();
            fixed (VkSemaphore* waits = _acquireWaitSemaphores)
            fixed (PipelineStageFlags* stages = _acquireWaitStages)
            {
                si.WaitSemaphoreCount = (uint)waitCount;
                si.PWaitSemaphores = waits;
                si.PWaitDstStageMask = stages;
                _graphicsQueueSubmitCount++;
                Vk.QueueSubmit(GraphicsQueue, 1, &si, fence).CheckResult();
            }
            FlushValidationErrors();
        }

        lock (_submittedFencesLock)
        {
            _submittedFences.Add(new FenceSubmissionInfo(
                fence, cb, handle, cb.TakePendingTimingPool(), cb.TakePendingStatsPool(),
                cb.Name, isTransfer: true, pass: null, transferId: 0, submission: submission));
        }
    }

    internal void PollSubmissions() => CheckSubmittedFences();

    internal void SubmitCommandBuffer(
        VkCommandBuffer? vkCL,
        Silk.NET.Vulkan.CommandBuffer vkCB,
        QueryPool? timingPool = null,
        QueryPool? statsPool = null,
        string bufferName = "",
        bool isTransfer = false,
        PassInfo? pass = null,
        ulong transferId = 0,
        bool waitAcquire = true)
    {
        FlushPendingInitCommands();
        CheckSubmittedFences();

        SubmitInfo si = new(sType: StructureType.SubmitInfo)
        {
            CommandBufferCount = 1,
            PCommandBuffers = &vkCB
        };

        VkFenceHandle vkFence = GetFreeSubmissionFence();

        lock (_graphicsQueueLock)
        {
            int waitCount = waitAcquire ? GatherAcquireWaits_NoLock() : 0;
            fixed (VkSemaphore* waits = _acquireWaitSemaphores)
            fixed (PipelineStageFlags* stages = _acquireWaitStages)
            {
                si.WaitSemaphoreCount = (uint)waitCount;
                si.PWaitSemaphores = waits;
                si.PWaitDstStageMask = stages;
                _graphicsQueueSubmitCount++;
                Vk.QueueSubmit(GraphicsQueue, 1, &si, vkFence).CheckResult();
            }
            FlushValidationErrors();
        }

        lock (_submittedFencesLock)
        {
            _submittedFences.Add(new FenceSubmissionInfo(vkFence, vkCL, vkCB, timingPool, statsPool, bufferName, isTransfer, pass, transferId));
        }
    }

    private void CheckSubmittedFences()
    {
        lock (_submittedFencesLock)
        {
            for (int i = 0; i < _submittedFences.Count; i++)
            {
                FenceSubmissionInfo fsi = _submittedFences[i];
                if (Vk.GetFenceStatus(Device, fsi.Fence) == Result.Success)
                {
                    CompleteFenceSubmission(fsi);
                    _submittedFences.RemoveAt(i);
                    i -= 1;
                }
                else
                {
                    break; // Submissions are in order; later submissions cannot complete if this one hasn't.
                }
            }
        }
    }

    private void CompleteFenceSubmission(FenceSubmissionInfo fsi)
    {
        VkFenceHandle fence = fsi.Fence;
        Silk.NET.Vulkan.CommandBuffer completedCB = fsi.VulkanCommandBuffer;

        fsi.CommandBuffer?.CommandBufferCompleted(completedCB);

        CommandBufferInfo profilerInfo = fsi.CommandBuffer?.ProfilerInfo ?? new(fsi.TransferId, fsi.BufferName, null);

        if (fsi.TimingPool is { } pool)
        {
            double milliseconds = ResolveTiming(pool);
            Profiler?.RecordExecutionTime(profilerInfo, fsi.IsTransfer, milliseconds);
        }

        if (fsi.StatsPool is { } statsPool)
        {
            GpuVertexStats stats = ResolvePipelineStats(statsPool);
            Profiler?.RecordGpuVertexStats(profilerInfo, in stats);
        }

        if (fsi.Submission is { } submission)
        {
            lock (submission.SyncRoot)
            {
                submission.MarkComplete();
                Vk.ResetFences(Device, 1, &fence).CheckResult();
                ReturnSubmissionFence(fence);
            }
            if (fsi.CommandBuffer != null)
                _recordCommandBufferPool.Return(fsi.CommandBuffer);
        }
        else if (fsi.OwnsFence)
        {
            Vk.ResetFences(Device, 1, &fence).CheckResult();
            ReturnSubmissionFence(fence);
        }

        List<ResourceRefCount>? retained = null;
        lock (_stagingResourcesLock)
        {
            if (_submittedStagingTextures.TryGetValue(completedCB, out VkTexture? stagingTex))
            {
                _submittedStagingTextures.Remove(completedCB);
                _availableStagingTextures.Add(stagingTex);
            }
            if (_submittedStagingBuffers.TryGetValue(completedCB, out VkBuffer? stagingBuffer))
            {
                _submittedStagingBuffers.Remove(completedCB);
                if (stagingBuffer.SizeInBytes <= MaxStagingBufferSize)
                {
                    _availableStagingBuffers.Add(stagingBuffer);
                }
                else
                {
                    stagingBuffer.Dispose();
                }
            }
            if (_submittedSharedCommandPools.TryGetValue(completedCB, out SharedCommandPool? sharedPool))
            {
                _submittedSharedCommandPools.Remove(completedCB);
                retained = sharedPool.Retained.Count > 0 ? [.. sharedPool.Retained] : null;
                sharedPool.Retained.Clear();
                lock (_graphicsCommandPoolLock)
                {
                    if (sharedPool.IsCached)
                    {
                        _sharedGraphicsCommandPools.Push(sharedPool);
                    }
                    else
                    {
                        sharedPool.Destroy();
                    }
                }
            }
        }
        if (retained != null)
        {
            foreach (ResourceRefCount refCount in retained)
                refCount.Decrement();
        }
    }

    private void ReturnSubmissionFence(VkFenceHandle fence)
    {
        _availableSubmissionFences.Enqueue(fence);
    }

    private VkFenceHandle GetFreeSubmissionFence()
    {
        if (_availableSubmissionFences.TryDequeue(out VkFenceHandle availableFence))
        {
            return availableFence;
        }
        else
        {
            FenceCreateInfo fenceCI = new(sType: StructureType.FenceCreateInfo);
            VkFenceHandle newFence;
            Vk.CreateFence(Device, &fenceCI, null, &newFence).CheckResult();
            return newFence;
        }
    }

    private struct FenceSubmissionInfo
    {
        public VkFenceHandle Fence;
        public VkCommandBuffer? CommandBuffer;
        public Silk.NET.Vulkan.CommandBuffer VulkanCommandBuffer;
        public QueryPool? TimingPool;
        public QueryPool? StatsPool;
        public string BufferName;
        public bool IsTransfer;
        public PassInfo? Pass;
        public ulong TransferId;
        public VkGpuSubmission? Submission;

        /// <summary>False when the fence is a slot fence, or is shared with a later entry.</summary>
        public bool OwnsFence;

        public FenceSubmissionInfo(
            VkFenceHandle fence,
            VkCommandBuffer? commandBuffer,
            Silk.NET.Vulkan.CommandBuffer vulkanCommandBuffer,
            QueryPool? timingPool,
            QueryPool? statsPool,
            string bufferName,
            bool isTransfer,
            PassInfo? pass,
            ulong transferId,
            bool ownsFence = true,
            VkGpuSubmission? submission = null)
        {
            Submission = submission;
            OwnsFence = ownsFence;
            Fence = fence;
            CommandBuffer = commandBuffer;
            VulkanCommandBuffer = vulkanCommandBuffer;
            TimingPool = timingPool;
            StatsPool = statsPool;
            BufferName = bufferName;
            IsTransfer = isTransfer;
            Pass = pass;
            TransferId = transferId;
        }
    }
}
