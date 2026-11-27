using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private readonly object _commandPoolLock = new();
    private readonly Stack<(CommandPool Pool, Silk.NET.Vulkan.CommandBuffer CommandBuffer)> _freeImmediatePools = new();
    private readonly Queue<(CommandPool Pool, Silk.NET.Vulkan.CommandBuffer CommandBuffer, ulong Serial)> _inFlightImmediatePools = new();
    private int _allocatedCommandBufferCount;
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
            _allocatedCommandBufferCount++;
            return cb;
        }
    }

    /// <summary>Test hook: total native command buffers ever allocated.</summary>
    internal int AllocatedCommandBufferCount
    {
        get
        {
            lock (_commandPoolLock)
                return _allocatedCommandBufferCount;
        }
    }

    private void ResetCommandPool(CommandPool pool)
    {
        lock (_commandPoolLock)
            Vk.ResetCommandPool(Device, pool, 0).CheckResult();
    }

    private CommandPool AcquireImmediatePool(out Silk.NET.Vulkan.CommandBuffer cb)
    {
        ulong completed = GetCompletedSerial();
        lock (_commandPoolLock)
        {
            while (_inFlightImmediatePools.Count > 0 && _inFlightImmediatePools.Peek().Serial <= completed)
            {
                (CommandPool done, Silk.NET.Vulkan.CommandBuffer doneCb, _) = _inFlightImmediatePools.Dequeue();
                _freeImmediatePools.Push((done, doneCb));
            }

            if (_freeImmediatePools.TryPop(out (CommandPool Pool, Silk.NET.Vulkan.CommandBuffer CommandBuffer) free))
            {
                Vk.ResetCommandPool(Device, free.Pool, 0).CheckResult();
                cb = free.CommandBuffer;
                return free.Pool;
            }
        }

        CommandPool created = CreateCommandPool();
        cb = AllocatePrimaryCommandBuffer(created);
        return created;
    }

    private void TagImmediatePool(CommandPool pool, Silk.NET.Vulkan.CommandBuffer cb, ulong serial)
    {
        lock (_commandPoolLock)
        {
            if (serial == 0)
                _freeImmediatePools.Push((pool, cb));
            else
                _inFlightImmediatePools.Enqueue((pool, cb, serial));
        }
    }

    private Silk.NET.Vulkan.CommandBuffer BeginImmediateCommandBuffer(out CommandPool pool)
    {
        pool = AcquireImmediatePool(out Silk.NET.Vulkan.CommandBuffer cb);
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
        SubmitImmediate(cb, pool);
    }

    private VkCommandBuffer RentRecordCommandBuffer()
    {
        CommandPool pool = AcquireImmediatePool(out Silk.NET.Vulkan.CommandBuffer handle);
        VkCommandBuffer cb;
        lock (_commandPoolLock)
        {
            if (_freeRecordCommandBuffers.Count > 0)
            {
                cb = _freeRecordCommandBuffers.Pop();
                cb.SetPool(pool, handle);
                return cb;
            }
        }

        cb = new VkCommandBuffer(this, pool);
        cb.SetPool(pool, handle);
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
            foreach ((CommandPool pool, _) in _freeImmediatePools)
                Vk.DestroyCommandPool(Device, pool, null);
            foreach ((CommandPool pool, _, _) in _inFlightImmediatePools)
                Vk.DestroyCommandPool(Device, pool, null);
            _freeImmediatePools.Clear();
            _inFlightImmediatePools.Clear();

            foreach (VkCommandBuffer cb in _allRecordCommandBuffers)
                cb.Dispose();
            _allRecordCommandBuffers.Clear();
            _freeRecordCommandBuffers.Clear();
        }
    }
}
