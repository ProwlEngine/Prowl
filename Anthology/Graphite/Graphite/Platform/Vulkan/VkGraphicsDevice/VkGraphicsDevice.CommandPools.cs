using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private readonly object _commandPoolLock = new();
    private readonly List<(CommandPool Pool, ulong Serial)> _immediatePools = [];
    private readonly Stack<VkCommandBuffer> _freeRecordCommandBuffers = new();
    private readonly List<VkCommandBuffer> _allRecordCommandBuffers = [];

    private CommandPool CreateCommandPool()
    {
        CommandPoolCreateInfo poolCI = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = GraphicsQueueIndex
        };
        Vk.CreateCommandPool(Device, in poolCI, null, out CommandPool pool).CheckResult();
        return pool;
    }

    internal Silk.NET.Vulkan.CommandBuffer AllocatePrimaryCommandBuffer(CommandPool pool)
    {
        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = pool,
            CommandBufferCount = 1,
            Level = CommandBufferLevel.Primary
        };
        lock (_commandPoolLock)
        {
            Vk.AllocateCommandBuffers(Device, in allocateInfo, out Silk.NET.Vulkan.CommandBuffer cb).CheckResult();
            return cb;
        }
    }

    private void ResetCommandPool(CommandPool pool)
    {
        lock (_commandPoolLock)
            Vk.ResetCommandPool(Device, pool, 0).CheckResult();
    }

    private CommandPool AcquireImmediatePool()
    {
        ulong completed = GetCompletedSerial();
        lock (_commandPoolLock)
        {
            for (int i = 0; i < _immediatePools.Count; i++)
            {
                if (_immediatePools[i].Serial > completed)
                    continue;

                CommandPool pool = _immediatePools[i].Pool;
                _immediatePools.RemoveAt(i);
                Vk.ResetCommandPool(Device, pool, 0).CheckResult();
                return pool;
            }
        }

        return CreateCommandPool();
    }

    private void TagImmediatePool(CommandPool pool, ulong serial)
    {
        lock (_commandPoolLock)
            _immediatePools.Add((pool, serial));
    }

    private Silk.NET.Vulkan.CommandBuffer BeginImmediateCommandBuffer(CommandPool pool)
    {
        Silk.NET.Vulkan.CommandBuffer cb = AllocatePrimaryCommandBuffer(pool);
        CommandBufferBeginInfo beginInfo = new(sType: StructureType.CommandBufferBeginInfo)
        {
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        Vk.BeginCommandBuffer(cb, in beginInfo).CheckResult();
        return cb;
    }

    private void EndAndSubmitImmediate(CommandPool pool, Silk.NET.Vulkan.CommandBuffer cb)
    {
        Vk.EndCommandBuffer(cb).CheckResult();

        FlushPendingInitCommands();
        PollSubmissions();

        lock (_graphicsQueueLock)
        {
            ulong serial = SubmitSignalingTimeline_NoLock(&cb, 1, 0);
            TagImmediatePool(pool, serial);
        }
    }

    private VkCommandBuffer RentRecordCommandBuffer()
    {
        CommandPool pool = AcquireImmediatePool();
        VkCommandBuffer cb;
        lock (_commandPoolLock)
        {
            if (_freeRecordCommandBuffers.Count > 0)
            {
                cb = _freeRecordCommandBuffers.Pop();
                cb.SetPool(pool);
                return cb;
            }
        }

        cb = new VkCommandBuffer(this, pool);
        lock (_commandPoolLock)
            _allRecordCommandBuffers.Add(cb);
        return cb;
    }

    private void ReturnRecordCommandBuffer(VkCommandBuffer cb)
    {
        cb.ResetForReuse();
        lock (_commandPoolLock)
            _freeRecordCommandBuffers.Push(cb);
    }

    private void DisposeCommandPools()
    {
        lock (_commandPoolLock)
        {
            foreach ((CommandPool pool, _) in _immediatePools)
                Vk.DestroyCommandPool(Device, pool, null);
            _immediatePools.Clear();

            foreach (VkCommandBuffer cb in _allRecordCommandBuffers)
                cb.Dispose();
            _allRecordCommandBuffers.Clear();
            _freeRecordCommandBuffers.Clear();
        }
    }
}
