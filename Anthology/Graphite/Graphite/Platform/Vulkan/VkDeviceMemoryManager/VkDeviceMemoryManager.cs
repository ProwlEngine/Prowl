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
    private ulong _totalAllocatedBytes;
    private readonly Dictionary<uint, ChunkAllocatorSet> _allocatorsByMemoryTypeUnmapped = [];
    private readonly Dictionary<uint, ChunkAllocatorSet> _allocatorsByMemoryType = [];

    private readonly vkGetBufferMemoryRequirements2_t? _getBufferMemoryRequirements2;
    private readonly vkGetImageMemoryRequirements2_t? _getImageMemoryRequirements2;

    public VkDeviceMemoryManager(
        Silk.NET.Vulkan.Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        ulong bufferImageGranularity,
        vkGetBufferMemoryRequirements2_t? getBufferMemoryRequirements2,
        vkGetImageMemoryRequirements2_t? getImageMemoryRequirements2)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _bufferImageGranularity = bufferImageGranularity;
        _getBufferMemoryRequirements2 = getBufferMemoryRequirements2;
        _getImageMemoryRequirements2 = getImageMemoryRequirements2;
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
        if (dedicated)
        {
            if (dedicatedImage.Handle != 0 && _getImageMemoryRequirements2 != null)
            {
                ImageMemoryRequirementsInfo2KHR requirementsInfo = new()
                {
                    SType = StructureType.ImageMemoryRequirementsInfo2
                };
                requirementsInfo.Image = dedicatedImage;
                MemoryRequirements2KHR requirements = new()
                {
                    SType = StructureType.MemoryRequirements2
                };
                _getImageMemoryRequirements2(_device, &requirementsInfo, &requirements);
                size = requirements.MemoryRequirements.Size;
            }
            else if (dedicatedBuffer.Handle != 0 && _getBufferMemoryRequirements2 != null)
            {
                BufferMemoryRequirementsInfo2KHR requirementsInfo = new()
                {
                    SType = StructureType.BufferMemoryRequirementsInfo2
                };
                requirementsInfo.Buffer = dedicatedBuffer;
                MemoryRequirements2KHR requirements = new()
                {
                    SType = StructureType.MemoryRequirements2
                };
                _getBufferMemoryRequirements2(_device, &requirementsInfo, &requirements);
                size = requirements.MemoryRequirements.Size;
            }
        }
        else
        {
            // Round up to the nearest multiple of bufferImageGranularity.
            size = ((size / _bufferImageGranularity) + 1) * _bufferImageGranularity;
        }
        _totalAllocatedBytes += size;

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

                MemoryDedicatedAllocateInfoKHR dedicatedAI;
                if (dedicated)
                {
                    dedicatedAI = new MemoryDedicatedAllocateInfoKHR
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
        _totalAllocatedBytes -= block.Size;
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
