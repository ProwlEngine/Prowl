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

    public override void ResetFence(Fence fence)
    {
        VkFenceHandle vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
        Vk.ResetFences(Device, 1, &vkFence);
    }

    public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
    {
        VkFenceHandle vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
        Result result = Vk.WaitForFences(Device, 1, &vkFence, true, nanosecondTimeout);
        return result == Result.Success;
    }

    internal void SubmitCommandBufferInternal(CommandBuffer cl)
    {
        SubmitCommandBuffer(cl, 0, null, 0, null, null);
    }

    private protected override void SubmitTransferCore(TransferCommandBuffer commandBuffer)
    {
        VkTransferCommandBuffer vkCb = Util.AssertSubtype<TransferCommandBuffer, VkTransferCommandBuffer>(commandBuffer);
        SubmitCommandBuffer(
            null, vkCb.CommandBuffer, 0, null, 0, null, null,
            timingPool: vkCb.TakePendingTimingPool(), statsPool: null, bufferName: vkCb.Name, isTransfer: true, pass: null,
            transferId: vkCb.Id);
    }

    /// <summary>
    /// Submits a one-shot command buffer and blocks until GPU finishes. Doesn't touch frame ring state, safe anytime.
    /// </summary>
    internal void SubmitAndWaitTransfer(Silk.NET.Vulkan.CommandBuffer cb, QueryPool? timingPool, string bufferName, ulong bufferId)
    {
        VkFenceHandle fence = GetFreeSubmissionFence();

        SubmitInfo si = new(sType: StructureType.SubmitInfo)
        {
            CommandBufferCount = 1,
            PCommandBuffers = &cb
        };

        lock (_graphicsQueueLock)
        {
            Vk.QueueSubmit(GraphicsQueue, 1, &si, fence).CheckResult();
            FlushValidationErrors();
        }

        Vk.WaitForFences(Device, 1, &fence, true, ulong.MaxValue).CheckResult();
        Vk.ResetFences(Device, 1, &fence).CheckResult();
        ReturnSubmissionFence(fence);

        if (timingPool is { } pool)
        {
            double milliseconds = ResolveTiming(pool);
            Profiler?.RecordExecutionTime(new CommandBufferInfo(bufferId, bufferName, null), isTransfer: true, milliseconds);
        }
    }

    private protected override void SubmitAndWaitCore(TransferCommandBuffer commandBuffer)
    {
        VkTransferCommandBuffer vkCb = Util.AssertSubtype<TransferCommandBuffer, VkTransferCommandBuffer>(commandBuffer);
        vkCb.SubmitAndWait();
    }

    private void SubmitCommandBuffer(
        CommandBuffer cl,
        uint waitSemaphoreCount,
        VkSemaphore* waitSemaphoresPtr,
        uint signalSemaphoreCount,
        VkSemaphore* signalSemaphoresPtr,
        Fence? fence)
    {
        VkCommandBuffer vkCL = Util.AssertSubtype<CommandBuffer, VkCommandBuffer>(cl);
        Silk.NET.Vulkan.CommandBuffer vkCB = vkCL.CommandBuffer;

        vkCL.CommandBufferSubmitted(vkCB);
        SubmitCommandBuffer(
            vkCL, vkCB, waitSemaphoreCount, waitSemaphoresPtr, signalSemaphoreCount, signalSemaphoresPtr, fence,
            vkCL.TakePendingTimingPool(), vkCL.TakePendingStatsPool(), vkCL.Name, isTransfer: false, pass: vkCL.Pass);
    }

    internal void SubmitCommandBuffer(
        VkCommandBuffer? vkCL,
        Silk.NET.Vulkan.CommandBuffer vkCB,
        uint waitSemaphoreCount,
        VkSemaphore* waitSemaphoresPtr,
        uint signalSemaphoreCount,
        VkSemaphore* signalSemaphoresPtr,
        Fence? fence,
        QueryPool? timingPool = null,
        QueryPool? statsPool = null,
        string bufferName = "",
        bool isTransfer = false,
        PassInfo? pass = null,
        ulong transferId = 0)
    {
        CheckSubmittedFences();

        bool useExtraFence = fence != null;

        SubmitInfo si = new(sType: StructureType.SubmitInfo)
        {
            CommandBufferCount = 1,
            PCommandBuffers = &vkCB
        };

        PipelineStageFlags waitDstStageMask = PipelineStageFlags.ColorAttachmentOutputBit;
        si.PWaitDstStageMask = &waitDstStageMask;

        si.PWaitSemaphores = waitSemaphoresPtr;
        si.WaitSemaphoreCount = waitSemaphoreCount;
        si.PSignalSemaphores = signalSemaphoresPtr;
        si.SignalSemaphoreCount = signalSemaphoreCount;

        VkFenceHandle vkFence;
        VkFenceHandle submissionFence;
        if (useExtraFence)
        {
            vkFence = Util.AssertSubtype<Fence, VkFence>(fence!).DeviceFence;
            submissionFence = GetFreeSubmissionFence();
        }
        else
        {
            vkFence = GetFreeSubmissionFence();
            submissionFence = vkFence;
        }

        lock (_graphicsQueueLock)
        {
            Vk.QueueSubmit(GraphicsQueue, 1, &si, vkFence).CheckResult();
            FlushValidationErrors();

            if (useExtraFence)
            {
                Vk.QueueSubmit(GraphicsQueue, 0, (SubmitInfo*)null, submissionFence).CheckResult();
            }
        }

        lock (_submittedFencesLock)
        {
            _submittedFences.Add(new FenceSubmissionInfo(submissionFence, vkCL, vkCB, timingPool, statsPool, bufferName, isTransfer, pass, transferId));
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

        if (fsi.OwnsFence)
        {
            Vk.ResetFences(Device, 1, &fence).CheckResult();
            ReturnSubmissionFence(fence);
        }

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
            bool ownsFence = true)
        {
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
