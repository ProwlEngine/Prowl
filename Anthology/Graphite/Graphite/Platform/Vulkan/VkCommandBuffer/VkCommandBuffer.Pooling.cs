using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private readonly object _commandBufferListLock = new();
    private readonly Queue<Silk.NET.Vulkan.CommandBuffer> _availableCommandBuffers = new();
    private readonly List<Silk.NET.Vulkan.CommandBuffer> _submittedCommandBuffers = [];

    private StagingResourceInfo _currentStagingInfo;
    private readonly object _stagingLock = new();
    private readonly Dictionary<Silk.NET.Vulkan.CommandBuffer, StagingResourceInfo> _submittedStagingInfos = [];
    private readonly List<StagingResourceInfo> _availableStagingInfos = [];
    private readonly List<VkBuffer> _availableStagingBuffers = [];

    private Silk.NET.Vulkan.CommandBuffer GetNextCommandBuffer()
    {
        lock (_commandBufferListLock)
        {
            if (_availableCommandBuffers.Count > 0)
            {
                Silk.NET.Vulkan.CommandBuffer cachedCB = _availableCommandBuffers.Dequeue();
                _gd.Vk.ResetCommandBuffer(cachedCB, 0).CheckResult();
                return cachedCB;
            }
        }

        CommandBufferAllocateInfo cbAI = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            CommandBufferCount = 1,
            Level = CommandBufferLevel.Primary
        };
        _gd.Vk.AllocateCommandBuffers(_gd.Device, in cbAI, out Silk.NET.Vulkan.CommandBuffer cb).CheckResult();
        return cb;
    }

    public void CommandBufferSubmitted(Silk.NET.Vulkan.CommandBuffer cb)
    {
        lock (_stagingLock)
        {
            _submittedStagingInfos.Add(cb, _currentStagingInfo);
        }
        _currentStagingInfo = null;
    }

    public void CommandBufferCompleted(Silk.NET.Vulkan.CommandBuffer completedCB)
    {

        lock (_commandBufferListLock)
        {
            for (int i = 0; i < _submittedCommandBuffers.Count; i++)
            {
                Silk.NET.Vulkan.CommandBuffer submittedCB = _submittedCommandBuffers[i];
                if (submittedCB.Handle == completedCB.Handle)
                {
                    _availableCommandBuffers.Enqueue(completedCB);
                    _submittedCommandBuffers.RemoveAt(i);
                    i -= 1;
                }
            }
        }

        lock (_stagingLock)
        {
            if (_submittedStagingInfos.Remove(completedCB, out StagingResourceInfo? info))
            {
                RecycleStagingInfo(info);
            }
        }
    }

    private VkBuffer GetStagingBuffer(uint size)
    {
        lock (_stagingLock)
        {
            VkBuffer? staging = null;

            foreach (VkBuffer buffer in _availableStagingBuffers)
            {
                if (buffer.SizeInBytes >= size)
                {
                    staging = buffer;
                    _availableStagingBuffers.Remove(buffer);
                    break;
                }
            }

            if (staging == null)
            {
                staging = (VkBuffer)_gd.ResourceFactory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
                staging.Name = $"Staging Buffer (CommandBuffer {Name})";
            }

            _currentStagingInfo.BuffersUsed.Add(staging);
            return staging;
        }
    }

    private VkBuffer GetFilledStagingBuffer(IntPtr source, uint sizeInBytes)
    {
        VkBuffer staging = GetStagingBuffer(sizeInBytes);
        Unsafe.CopyBlock((byte*)staging.Memory.BlockMappedPointer, source.ToPointer(), sizeInBytes);
        return staging;
    }

    private class StagingResourceInfo
    {
        public List<VkBuffer> BuffersUsed { get; } = [];

        public void Clear()
        {
            BuffersUsed.Clear();
        }
    }

    private StagingResourceInfo GetStagingResourceInfo()
    {
        lock (_stagingLock)
        {
            StagingResourceInfo ret;
            int availableCount = _availableStagingInfos.Count;
            if (availableCount > 0)
            {
                ret = _availableStagingInfos[availableCount - 1];
                _availableStagingInfos.RemoveAt(availableCount - 1);
            }
            else
            {
                ret = new StagingResourceInfo();
            }

            return ret;
        }
    }

    private void RecycleStagingInfo(StagingResourceInfo info)
    {
        lock (_stagingLock)
        {
            foreach (VkBuffer buffer in info.BuffersUsed)
            {
                _availableStagingBuffers.Add(buffer);
            }

            info.Clear();

            _availableStagingInfos.Add(info);
        }
    }
}
