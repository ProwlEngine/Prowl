using System.Collections.Generic;

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
        RefCount.Increment();
        foreach (ResourceRefCount rrc in _currentStagingInfo.Resources)
        {
            rrc.Increment();
        }

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

        RefCount.Decrement();
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
                staging.Name = $"Staging Buffer (CommandBuffer {_name})";
            }

            _currentStagingInfo.BuffersUsed.Add(staging);
            return staging;
        }
    }

    private class StagingResourceInfo
    {
        public List<VkBuffer> BuffersUsed { get; } = [];
        public HashSet<ResourceRefCount> Resources { get; } = [];

        /// <summary>Unique id of the recording currently using this info; stamps retained resources.</summary>
        public ulong RecordingId;

        public void Clear()
        {
            BuffersUsed.Clear();
            Resources.Clear();
        }
    }

    private static ulong s_nextRecordingId = 1;

    // Retains a resource for the current recording, but only once: a resource already stamped with this
    // recording's id is known to be in the staging set, so the (hashed) set insertion is skipped.
    private void AddStagingResource(ResourceRefCount rc)
    {
        ulong id = _currentStagingInfo.RecordingId;
        if (rc.StagingMark == id)
            return;
        rc.StagingMark = id;
        _currentStagingInfo.Resources.Add(rc);
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

            ret.RecordingId = System.Threading.Interlocked.Increment(ref s_nextRecordingId);
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

            foreach (ResourceRefCount rrc in info.Resources)
            {
                rrc.Decrement();
            }

            info.Clear();

            _availableStagingInfos.Add(info);
        }
    }
}
