using Silk.NET.Vulkan;
using static Veldrid.Vk.VulkanUtil;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using VkBufferHandle = Silk.NET.Vulkan.Buffer;

namespace Veldrid.Vk
{
    internal unsafe class VkDeviceMemoryManager : IDisposable
    {
        private const ulong MinDedicatedAllocationSizeDynamic = 1024 * 1024 * 64;
        private const ulong MinDedicatedAllocationSizeNonDynamic = 1024 * 1024 * 256;
        private readonly Device _device;
        private readonly PhysicalDevice _physicalDevice;
        private readonly ulong _bufferImageGranularity;
        private readonly Silk.NET.Vulkan.Vk _vk;
        private readonly object _lock = new object();
        private ulong _totalAllocatedBytes;
        private readonly Dictionary<uint, ChunkAllocatorSet> _allocatorsByMemoryTypeUnmapped = new Dictionary<uint, ChunkAllocatorSet>();
        private readonly Dictionary<uint, ChunkAllocatorSet> _allocatorsByMemoryType = new Dictionary<uint, ChunkAllocatorSet>();

        private readonly vkGetBufferMemoryRequirements2_t _getBufferMemoryRequirements2;
        private readonly vkGetImageMemoryRequirements2_t _getImageMemoryRequirements2;

        public VkDeviceMemoryManager(
            Silk.NET.Vulkan.Vk vk,
            Device device,
            PhysicalDevice physicalDevice,
            ulong bufferImageGranularity,
            vkGetBufferMemoryRequirements2_t getBufferMemoryRequirements2,
            vkGetImageMemoryRequirements2_t getImageMemoryRequirements2)
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
            ulong alignment)
        {
            return Allocate(
                memProperties,
                memoryTypeBits,
                flags,
                persistentMapped,
                size,
                alignment,
                false,
                default,
                default);
        }

        public VkMemoryBlock Allocate(
            PhysicalDeviceMemoryProperties memProperties,
            uint memoryTypeBits,
            MemoryPropertyFlags flags,
            bool persistentMapped,
            ulong size,
            ulong alignment,
            bool dedicated,
            Image dedicatedImage,
            VkBufferHandle dedicatedBuffer)
        {
            if (dedicated)
            {
                if (dedicatedImage.Handle != 0 && _getImageMemoryRequirements2 != null)
                {
                    ImageMemoryRequirementsInfo2KHR requirementsInfo = new ImageMemoryRequirementsInfo2KHR
                    {
                        SType = StructureType.ImageMemoryRequirementsInfo2
                    };
                    requirementsInfo.Image = dedicatedImage;
                    MemoryRequirements2KHR requirements = new MemoryRequirements2KHR
                    {
                        SType = StructureType.MemoryRequirements2
                    };
                    _getImageMemoryRequirements2(_device, &requirementsInfo, &requirements);
                    size = requirements.MemoryRequirements.Size;
                }
                else if (dedicatedBuffer.Handle != 0 && _getBufferMemoryRequirements2 != null)
                {
                    BufferMemoryRequirementsInfo2KHR requirementsInfo = new BufferMemoryRequirementsInfo2KHR
                    {
                        SType = StructureType.BufferMemoryRequirementsInfo2
                    };
                    requirementsInfo.Buffer = dedicatedBuffer;
                    MemoryRequirements2KHR requirements = new MemoryRequirements2KHR
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
                if (!TryFindMemoryType(memProperties, memoryTypeBits, flags, out var memoryTypeIndex))
                {
                    throw new VeldridException("No suitable memory type.");
                }

                ulong minDedicatedAllocationSize = persistentMapped
                    ? MinDedicatedAllocationSizeDynamic
                    : MinDedicatedAllocationSizeNonDynamic;

                if (dedicated || size >= minDedicatedAllocationSize)
                {
                    MemoryAllocateInfo allocateInfo = new MemoryAllocateInfo
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

                    DeviceMemory memory;
                    Result allocationResult = _vk.AllocateMemory(_device, in allocateInfo, null, out memory);
                    if (allocationResult != Result.Success)
                    {
                        throw new VeldridException("Unable to allocate sufficient Vulkan memory.");
                    }

                    void* mappedPtr = null;
                    if (persistentMapped)
                    {
                        Result mapResult = _vk.MapMemory(_device, memory, 0, size, 0, &mappedPtr);
                        if (mapResult != Result.Success)
                        {
                            throw new VeldridException("Unable to map newly-allocated Vulkan memory.");
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
                        throw new VeldridException("Unable to allocate sufficient Vulkan memory.");
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
            ChunkAllocatorSet ret = null;
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

        private class ChunkAllocatorSet : IDisposable
        {
            private readonly Silk.NET.Vulkan.Vk _vk;
            private readonly Device _device;
            private readonly uint _memoryTypeIndex;
            private readonly bool _persistentMapped;
            private readonly List<ChunkAllocator> _allocators = new List<ChunkAllocator>();

            public ChunkAllocatorSet(Silk.NET.Vulkan.Vk vk, Device device, uint memoryTypeIndex, bool persistentMapped)
            {
                _vk = vk;
                _device = device;
                _memoryTypeIndex = memoryTypeIndex;
                _persistentMapped = persistentMapped;
            }

            public bool Allocate(ulong size, ulong alignment, out VkMemoryBlock block)
            {
                foreach (ChunkAllocator allocator in _allocators)
                {
                    if (allocator.Allocate(size, alignment, out block))
                    {
                        return true;
                    }
                }

                ChunkAllocator newAllocator = new ChunkAllocator(_vk, _device, _memoryTypeIndex, _persistentMapped);
                _allocators.Add(newAllocator);
                return newAllocator.Allocate(size, alignment, out block);
            }

            public void Free(VkMemoryBlock block)
            {
                foreach (ChunkAllocator chunk in _allocators)
                {
                    if (chunk.Memory.Handle == block.DeviceMemory.Handle)
                    {
                        chunk.Free(block);
                    }
                }
            }

            public void Dispose()
            {
                foreach (ChunkAllocator allocator in _allocators)
                {
                    allocator.Dispose();
                }
            }
        }

        private class ChunkAllocator : IDisposable
        {
            private const ulong PersistentMappedChunkSize = 1024 * 1024 * 64;
            private const ulong UnmappedChunkSize = 1024 * 1024 * 256;
            private readonly Silk.NET.Vulkan.Vk _vk;
            private readonly Device _device;
            private readonly uint _memoryTypeIndex;
            private readonly bool _persistentMapped;
            private readonly List<VkMemoryBlock> _freeBlocks = new List<VkMemoryBlock>();
            private readonly DeviceMemory _memory;
            private readonly void* _mappedPtr;

            private ulong _totalMemorySize;
            private ulong _totalAllocatedBytes = 0;

            public DeviceMemory Memory => _memory;

            public ChunkAllocator(Silk.NET.Vulkan.Vk vk, Device device, uint memoryTypeIndex, bool persistentMapped)
            {
                _vk = vk;
                _device = device;
                _memoryTypeIndex = memoryTypeIndex;
                _persistentMapped = persistentMapped;
                _totalMemorySize = persistentMapped ? PersistentMappedChunkSize : UnmappedChunkSize;

                MemoryAllocateInfo memoryAI = new MemoryAllocateInfo
                {
                    SType = StructureType.MemoryAllocateInfo
                };
                memoryAI.AllocationSize = _totalMemorySize;
                memoryAI.MemoryTypeIndex = _memoryTypeIndex;
                Result result = _vk.AllocateMemory(_device, in memoryAI, null, out _memory);
                CheckResult(result);

                void* mappedPtr = null;
                if (persistentMapped)
                {
                    result = _vk.MapMemory(_device, _memory, 0, _totalMemorySize, 0, &mappedPtr);
                    CheckResult(result);
                }
                _mappedPtr = mappedPtr;

                VkMemoryBlock initialBlock = new VkMemoryBlock(
                    _memory,
                    0,
                    _totalMemorySize,
                    _memoryTypeIndex,
                    _mappedPtr,
                    false);
                _freeBlocks.Add(initialBlock);
            }

            public bool Allocate(ulong size, ulong alignment, out VkMemoryBlock block)
            {
                checked
                {
                    for (int i = 0; i < _freeBlocks.Count; i++)
                    {
                        VkMemoryBlock freeBlock = _freeBlocks[i];
                        ulong alignedBlockSize = freeBlock.Size;
                        if (freeBlock.Offset % alignment != 0)
                        {
                            ulong alignmentCorrection = (alignment - freeBlock.Offset % alignment);
                            if (alignedBlockSize <= alignmentCorrection)
                            {
                                continue;
                            }
                            alignedBlockSize -= alignmentCorrection;
                        }

                        if (alignedBlockSize >= size) // Valid match -- split it and return.
                        {
                            _freeBlocks.RemoveAt(i);

                            freeBlock.Size = alignedBlockSize;
                            if ((freeBlock.Offset % alignment) != 0)
                            {
                                freeBlock.Offset += alignment - (freeBlock.Offset % alignment);
                            }

                            block = freeBlock;

                            if (alignedBlockSize != size)
                            {
                                VkMemoryBlock splitBlock = new VkMemoryBlock(
                                    freeBlock.DeviceMemory,
                                    freeBlock.Offset + size,
                                    freeBlock.Size - size,
                                    _memoryTypeIndex,
                                    freeBlock.BaseMappedPointer,
                                    false);
                                _freeBlocks.Insert(i, splitBlock);
                                block = freeBlock;
                                block.Size = size;
                            }

#if DEBUG
                            CheckAllocatedBlock(block);
#endif
                            _totalAllocatedBytes += alignedBlockSize;
                            return true;
                        }
                    }

                    block = default(VkMemoryBlock);
                    return false;
                }
            }

            public void Free(VkMemoryBlock block)
            {
                for (int i = 0; i < _freeBlocks.Count; i++)
                {
                    if (_freeBlocks[i].Offset > block.Offset)
                    {
                        _freeBlocks.Insert(i, block);
                        MergeContiguousBlocks();
#if DEBUG
                        RemoveAllocatedBlock(block);
#endif
                        return;
                    }
                }

                _freeBlocks.Add(block);
#if DEBUG
                RemoveAllocatedBlock(block);
#endif
                _totalAllocatedBytes -= block.Size;
            }

            private void MergeContiguousBlocks()
            {
                int contiguousLength = 1;
                for (int i = 0; i < _freeBlocks.Count - 1; i++)
                {
                    ulong blockStart = _freeBlocks[i].Offset;
                    while (i + contiguousLength < _freeBlocks.Count
                        && _freeBlocks[i + contiguousLength - 1].End == _freeBlocks[i + contiguousLength].Offset)
                    {
                        contiguousLength += 1;
                    }

                    if (contiguousLength > 1)
                    {
                        ulong blockEnd = _freeBlocks[i + contiguousLength - 1].End;
                        _freeBlocks.RemoveRange(i, contiguousLength);
                        VkMemoryBlock mergedBlock = new VkMemoryBlock(
                            Memory,
                            blockStart,
                            blockEnd - blockStart,
                            _memoryTypeIndex,
                            _mappedPtr,
                            false);
                        _freeBlocks.Insert(i, mergedBlock);
                        contiguousLength = 0;
                    }
                }
            }

#if DEBUG
            private List<VkMemoryBlock> _allocatedBlocks = new List<VkMemoryBlock>();

            private void CheckAllocatedBlock(VkMemoryBlock block)
            {
                foreach (VkMemoryBlock oldBlock in _allocatedBlocks)
                {
                    Debug.Assert(!BlocksOverlap(block, oldBlock), "Allocated blocks have overlapped.");
                }

                _allocatedBlocks.Add(block);
            }

            private bool BlocksOverlap(VkMemoryBlock first, VkMemoryBlock second)
            {
                ulong firstStart = first.Offset;
                ulong firstEnd = first.Offset + first.Size;
                ulong secondStart = second.Offset;
                ulong secondEnd = second.Offset + second.Size;

                return (firstStart <= secondStart && firstEnd > secondStart
                    || firstStart >= secondStart && firstEnd <= secondEnd
                    || firstStart < secondEnd && firstEnd >= secondEnd
                    || firstStart <= secondStart && firstEnd >= secondEnd);
            }

            private void RemoveAllocatedBlock(VkMemoryBlock block)
            {
                Debug.Assert(_allocatedBlocks.Remove(block), "Unable to remove a supposedly allocated block.");
            }
#endif

            public void Dispose()
            {
                _vk.FreeMemory(_device, _memory, null);
            }
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
            Result result = _vk.MapMemory(_device, memoryBlock.DeviceMemory, memoryBlock.Offset, memoryBlock.Size, 0, &ret);
            CheckResult(result);
            return (IntPtr)ret;
        }
    }

    [DebuggerDisplay("[Mem:{DeviceMemory.Handle}] Off:{Offset}, Size:{Size} End:{Offset+Size}")]
    internal unsafe struct VkMemoryBlock : IEquatable<VkMemoryBlock>
    {
        public readonly uint MemoryTypeIndex;
        public readonly DeviceMemory DeviceMemory;
        public readonly void* BaseMappedPointer;
        public readonly bool DedicatedAllocation;

        public ulong Offset;
        public ulong Size;

        public void* BlockMappedPointer => ((byte*)BaseMappedPointer) + Offset;
        public bool IsPersistentMapped => BaseMappedPointer != null;
        public ulong End => Offset + Size;

        public VkMemoryBlock(
            DeviceMemory memory,
            ulong offset,
            ulong size,
            uint memoryTypeIndex,
            void* mappedPtr,
            bool dedicatedAllocation)
        {
            DeviceMemory = memory;
            Offset = offset;
            Size = size;
            MemoryTypeIndex = memoryTypeIndex;
            BaseMappedPointer = mappedPtr;
            DedicatedAllocation = dedicatedAllocation;
        }

        public bool Equals(VkMemoryBlock other)
        {
            return DeviceMemory.Handle.Equals(other.DeviceMemory.Handle)
                && Offset.Equals(other.Offset)
                && Size.Equals(other.Size);
        }
    }
}
