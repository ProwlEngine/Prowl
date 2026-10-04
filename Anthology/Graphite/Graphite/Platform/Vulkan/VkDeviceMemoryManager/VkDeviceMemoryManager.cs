using System;
using System.Collections.Generic;

using Silk.NET.Vulkan;

using VkBufferHandle = Silk.NET.Vulkan.Buffer;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkDeviceMemoryManager : IDisposable
{
    private const ulong MinDedicatedAllocationSizeDynamic = 1024 * 1024 * 64;
    private const ulong MinDedicatedAllocationSizeNonDynamic = 1024 * 1024 * 256;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly ulong _bufferImageGranularity;
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly object _lock = new();
    private readonly Dictionary<uint, ChunkAllocatorSet> _allocatorsByMemoryTypeUnmapped = [];
    private readonly Dictionary<uint, ChunkAllocatorSet> _allocatorsByMemoryType = [];

    public VkDeviceMemoryManager(
        Silk.NET.Vulkan.Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        ulong bufferImageGranularity)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _bufferImageGranularity = bufferImageGranularity;
    }

    public VkMemoryBlock Allocate(
        PhysicalDeviceMemoryProperties memProperties,
        uint memoryTypeBits,
        MemoryPropertyFlags flags,
        bool persistentMapped,
        ulong size,
        ulong alignment,
        bool dedicated = false,
        Image dedicatedImage = default,
        VkBufferHandle dedicatedBuffer = default)
    {
        if (!dedicated)
        {
            size = (size + _bufferImageGranularity - 1) / _bufferImageGranularity * _bufferImageGranularity;
        }

        lock (_lock)
        {
            if (!_vk.TryFindMemoryType(memProperties, memoryTypeBits, flags, out uint memoryTypeIndex))
            {
                throw new RenderException("No suitable memory type.");
            }

            ulong minDedicatedAllocationSize = persistentMapped
                ? MinDedicatedAllocationSizeDynamic
                : MinDedicatedAllocationSizeNonDynamic;

            if (dedicated || size >= minDedicatedAllocationSize)
            {
                MemoryAllocateInfo allocateInfo = new()
                {
                    SType = StructureType.MemoryAllocateInfo
                };
                allocateInfo.AllocationSize = size;
                allocateInfo.MemoryTypeIndex = memoryTypeIndex;

                MemoryDedicatedAllocateInfo dedicatedAI;
                if (dedicated)
                {
                    dedicatedAI = new MemoryDedicatedAllocateInfo
                    {
                        SType = StructureType.MemoryDedicatedAllocateInfo
                    };
                    dedicatedAI.Buffer = dedicatedBuffer;
                    dedicatedAI.Image = dedicatedImage;
                    allocateInfo.PNext = &dedicatedAI;
                }

                Result allocationResult = _vk.AllocateMemory(_device, in allocateInfo, null, out DeviceMemory memory);
                if (allocationResult != Result.Success)
                {
                    throw new RenderException("Unable to allocate sufficient Vulkan memory.");
                }

                void* mappedPtr = null;
                if (persistentMapped)
                {
                    Result mapResult = _vk.MapMemory(_device, memory, 0, size, 0, &mappedPtr);
                    if (mapResult != Result.Success)
                    {
                        throw new RenderException("Unable to map newly-allocated Vulkan memory.");
                    }
                }

                return new VkMemoryBlock(memory, 0, size, memoryTypeBits, mappedPtr, true);
            }
            else
            {
                ChunkAllocatorSet allocator = GetAllocator(memoryTypeIndex, persistentMapped);
                bool result = allocator.Allocate(size, alignment, out VkMemoryBlock ret);
                if (!result)
                {
                    throw new RenderException("Unable to allocate sufficient Vulkan memory.");
                }

                return ret;
            }
        }
    }

    public void Free(VkMemoryBlock block)
    {
        lock (_lock)
        {
            if (block.DedicatedAllocation)
            {
                _vk.FreeMemory(_device, block.DeviceMemory, null);
            }
            else
            {
                GetAllocator(block.MemoryTypeIndex, block.IsPersistentMapped).Free(block);
            }
        }
    }

    private ChunkAllocatorSet GetAllocator(uint memoryTypeIndex, bool persistentMapped)
    {
        ChunkAllocatorSet? ret = null;
        if (persistentMapped)
        {
            if (!_allocatorsByMemoryType.TryGetValue(memoryTypeIndex, out ret))
            {
                ret = new ChunkAllocatorSet(_vk, _device, memoryTypeIndex, true);
                _allocatorsByMemoryType.Add(memoryTypeIndex, ret);
            }
        }
        else
        {
            if (!_allocatorsByMemoryTypeUnmapped.TryGetValue(memoryTypeIndex, out ret))
            {
                ret = new ChunkAllocatorSet(_vk, _device, memoryTypeIndex, false);
                _allocatorsByMemoryTypeUnmapped.Add(memoryTypeIndex, ret);
            }
        }

        return ret;
    }

    public void Dispose()
    {
        foreach (KeyValuePair<uint, ChunkAllocatorSet> kvp in _allocatorsByMemoryType)
        {
            kvp.Value.Dispose();
        }

        foreach (KeyValuePair<uint, ChunkAllocatorSet> kvp in _allocatorsByMemoryTypeUnmapped)
        {
            kvp.Value.Dispose();
        }
    }

    internal IntPtr Map(VkMemoryBlock memoryBlock)
    {
        void* ret;
        _vk.MapMemory(_device, memoryBlock.DeviceMemory, memoryBlock.Offset, memoryBlock.Size, 0, &ret).CheckResult();
        return (IntPtr)ret;
    }
}
