using System;
using System.Collections.Generic;
using System.Diagnostics;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkDeviceMemoryManager
{
    private class ChunkAllocator : IDisposable
    {
        private const ulong PersistentMappedChunkSize = 1024 * 1024 * 64;
        private const ulong UnmappedChunkSize = 1024 * 1024 * 256;
        private readonly Silk.NET.Vulkan.Vk _vk;
        private readonly Device _device;
        private readonly uint _memoryTypeIndex;
        private readonly bool _persistentMapped;
        private readonly List<VkMemoryBlock> _freeBlocks = [];
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

            MemoryAllocateInfo memoryAI = new()
            {
                SType = StructureType.MemoryAllocateInfo
            };
            memoryAI.AllocationSize = _totalMemorySize;
            memoryAI.MemoryTypeIndex = _memoryTypeIndex;
            _vk.AllocateMemory(_device, in memoryAI, null, out _memory).CheckResult();

            void* mappedPtr = null;
            if (persistentMapped)
            {
                _vk.MapMemory(_device, _memory, 0, _totalMemorySize, 0, &mappedPtr).CheckResult();
            }
            _mappedPtr = mappedPtr;

            VkMemoryBlock initialBlock = new(
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
                            VkMemoryBlock splitBlock = new(
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

                block = default;
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
                    VkMemoryBlock mergedBlock = new(
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
        private List<VkMemoryBlock> _allocatedBlocks = [];

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
}
