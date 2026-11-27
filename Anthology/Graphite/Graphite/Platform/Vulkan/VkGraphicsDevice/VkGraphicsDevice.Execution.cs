using System;
using System.Collections.Generic;
using System.Threading;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private struct SlotState
    {
        public ulong FinalSerial;
        public VkUniformArena UniformArena;
        public CommandPool Pool;
        public List<VkCommandBuffer> Wrappers;
        public int WrappersInUse;
        public List<VkCommandBuffer> QueuedCommandBuffers;
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
            VkBuffer primary = new(this, new BufferDescription(_transientInitialSize,
                BufferUsage.Dynamic | BufferUsage.UniformBuffer));
            primary.Name = $"TransientPrimary[{i}]";

            _slots[i] = new SlotState
            {
                UniformArena = new VkUniformArena(this, primary),
                Pool = CreateCommandPool(),
                Wrappers = [],
                QueuedCommandBuffers = [],
                CurrentExecutionId = 0,
            };
        }
    }

    private protected override ExecutionTask BeginExecutionCore(ulong executionId, uint ringSlot)
    {
        ref SlotState slot = ref _slots[ringSlot];

        PollSubmissions();
        Volatile.Write(ref slot.FinalSerial, 0);

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

        ResetCommandPool(slot.Pool);
        lock (slot.Wrappers)
        {
            for (int i = 0; i < slot.WrappersInUse; i++)
                slot.Wrappers[i].ResetForReuse();
            slot.WrappersInUse = 0;
        }

        _descriptorSetCaches.SweepAll(executionId, _maxExecutingTasks);

        slot.CurrentExecutionId = executionId;

        return new VkExecutionTask(this, executionId, ringSlot,
            slot.UniformArena, slot.QueuedCommandBuffers);
    }

    private protected override void CompleteExecutionCore(ExecutionTask task)
    {
        VkExecutionTask vkTask = Util.AssertSubtype<ExecutionTask, VkExecutionTask>(task);
        ulong serial = vkTask.FinalSubmit();
        Volatile.Write(ref _slots[task.RingSlot].FinalSerial, serial);
    }

    private protected override bool IsExecutionCompleteCore(ExecutionTask task)
    {
        ref SlotState slot = ref _slots[task.RingSlot];

        if (slot.CurrentExecutionId != task.Id)
            return true;

        ulong serial = Volatile.Read(ref slot.FinalSerial);
        return serial != 0 && GetCompletedSerial() >= serial;
    }

    private protected override void PollSubmissionsCore() => PollSubmissions();

    private protected override bool WaitForExecutionCore(ExecutionTask task, ulong nanosecondTimeout)
    {
        ref SlotState slot = ref _slots[task.RingSlot];

        if (slot.CurrentExecutionId != task.Id)
            return true;

        ulong serial = Volatile.Read(ref slot.FinalSerial);
        return serial != 0 && WaitForSerial(serial, nanosecondTimeout);
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
            BufferUsage.Dynamic | BufferUsage.UniformBuffer));
        overflow.Name = "TransientOverflow";
        return overflow;
    }

    private void DisposeSlots()
    {
        if (_slots != null)
        {
            foreach (ref SlotState slot in _slots.AsSpan())
            {
                foreach (VkCommandBuffer wrapper in slot.Wrappers)
                    wrapper.Dispose();
                Vk.DestroyCommandPool(Device, slot.Pool, null);
                slot.UniformArena?.PrimaryBuffer.Dispose();
                foreach (VkBuffer overflow in slot.UniformArena?.OverflowBuffers ?? [])
                    overflow.Dispose();
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
