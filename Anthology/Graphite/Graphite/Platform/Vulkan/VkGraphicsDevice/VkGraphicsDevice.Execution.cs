using System;
using System.Collections.Generic;

using Silk.NET.Vulkan;

using VkFenceHandle = Silk.NET.Vulkan.Fence;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private struct SlotState
    {
        public VkFenceHandle Fence;
        public VkFence FenceWrapper;
        public VkUniformArena UniformArena;
        public List<VkCommandBuffer> RentedCommandBuffers;
        public ulong CurrentExecutionId;
    }

    private SlotState[] _slots;
    private readonly List<VkBuffer> _transientFreePool = [];
    private readonly object _transientFreePoolLock = new();

    private void InitializeSlots()
    {
        _slots = new SlotState[_maxExecutingTasks];
        for (uint i = 0; i < _maxExecutingTasks; i++)
        {
            VkFence slotWrapper = new(this, false);

            VkBuffer primary = new(this, new BufferDescription(_transientInitialSize,
                BufferUsage.Dynamic | BufferUsage.UniformBuffer) { TransientWrites = true });
            primary.Name = $"TransientPrimary[{i}]";

            _slots[i] = new SlotState
            {
                Fence = slotWrapper.DeviceFence,
                FenceWrapper = slotWrapper,
                UniformArena = new VkUniformArena(this, primary),
                RentedCommandBuffers = [],
                CurrentExecutionId = 0,
            };
        }
    }

    private protected override ExecutionTask BeginExecutionCore(ulong executionId, uint ringSlot)
    {
        ref SlotState slot = ref _slots[ringSlot];

        // The base class only hands out a slot whose previous execution has completed and been reclaimed,
        // so no fence wait is needed here; just recycle the slot's fence and transient memory.
        VkFenceHandle slotFence = slot.Fence;
        Vk.ResetFences(Device, 1, in slotFence).CheckResult();
        slot.FenceWrapper.Reset();

        // Return overflow buffers to the free pool and reset transient head
        List<VkBuffer> overflow = slot.UniformArena.OverflowBuffers;
        if (overflow.Count > 0)
        {
            lock (_transientFreePoolLock)
            {
                _transientFreePool.AddRange(overflow);
            }
            overflow.Clear();
        }
        slot.UniformArena.BeginExecution();

        // The slot's previous execution is complete, so its rented command buffers can be reclaimed.
        if (slot.RentedCommandBuffers.Count > 0)
        {
            foreach (VkCommandBuffer rented in slot.RentedCommandBuffers)
                _graphCommandBufferPool.Return(rented);
            slot.RentedCommandBuffers.Clear();
        }

        _descriptorSetCaches.SweepAll(executionId, _maxExecutingTasks);

        slot.CurrentExecutionId = executionId;

        return new VkExecutionTask(this, executionId, ringSlot, slot.FenceWrapper,
            slot.UniformArena, slot.RentedCommandBuffers);
    }

    private protected override void CompleteExecutionCore(ExecutionTask task)
    {
        uint ringSlot = task.RingSlot;
        VkFenceHandle slotFence = _slots[ringSlot].Fence;

        SubmitInfo si = new(sType: StructureType.SubmitInfo);
        si.CommandBufferCount = 0;

        lock (_graphicsQueueLock)
        {
            Vk.QueueSubmit(GraphicsQueue, 1, in si, slotFence).CheckResult();
            FlushValidationErrors();
        }
    }

    private protected override bool IsExecutionCompleteCore(ExecutionTask task)
    {
        ref SlotState slot = ref _slots[task.RingSlot];

        // The slot was reused by a newer execution, so this one has definitely finished.
        if (slot.CurrentExecutionId != task.Id)
            return true;

        Result status = Vk.GetFenceStatus(Device, slot.Fence);
        return status == Result.Success;
    }

    private protected override bool WaitForExecutionCore(ExecutionTask task, ulong nanosecondTimeout)
    {
        ref SlotState slot = ref _slots[task.RingSlot];

        if (slot.CurrentExecutionId != task.Id)
            return true;

        VkFenceHandle fence = slot.Fence;
        Result result = Vk.WaitForFences(Device, 1, in fence, true, nanosecondTimeout);
        return result == Result.Success;
    }

    internal VkBuffer CreateTransientBuffer(uint sizeInBytes)
    {
        lock (_transientFreePoolLock)
        {
            for (int i = 0; i < _transientFreePool.Count; i++)
            {
                if (_transientFreePool[i].SizeInBytes >= sizeInBytes)
                {
                    VkBuffer buf = _transientFreePool[i];
                    _transientFreePool.RemoveAt(i);
                    return buf;
                }
            }
        }

        VkBuffer overflow = new(this, new BufferDescription(sizeInBytes,
            BufferUsage.Dynamic | BufferUsage.UniformBuffer) { TransientWrites = true });
        overflow.Name = "TransientOverflow";
        return overflow;
    }

    private void DisposeSlots()
    {
        if (_slots != null)
        {
            foreach (ref SlotState slot in _slots.AsSpan())
            {
                slot.UniformArena?.PrimaryBuffer.Dispose();
                foreach (VkBuffer overflow in slot.UniformArena?.OverflowBuffers ?? [])
                    overflow.Dispose();
                slot.FenceWrapper?.Dispose();
            }
        }

        lock (_transientFreePoolLock)
        {
            foreach (VkBuffer buf in _transientFreePool)
                buf.Dispose();
            _transientFreePool.Clear();
        }
    }
}
