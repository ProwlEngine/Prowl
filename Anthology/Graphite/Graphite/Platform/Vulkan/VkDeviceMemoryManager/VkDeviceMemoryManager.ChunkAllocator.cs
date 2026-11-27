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
            for (int i = 0; i < _freeBlocks.Count; i++)
            {
                VkMemoryBlock freeBlock = _freeBlocks[i];
                ulong alignedOffset = (freeBlock.Offset + alignment - 1) / alignment * alignment;
                ulong padding = alignedOffset - freeBlock.Offset;
                if (freeBlock.Size < padding + size)
                {
                    continue;
                }

                ulong tail = freeBlock.Size - padding - size;
                block = new VkMemoryBlock(
                    freeBlock.DeviceMemory,
                    alignedOffset,
                    size,
                    _memoryTypeIndex,
                    freeBlock.BaseMappedPointer,
                    false);

                if (padding > 0)
                {
                    freeBlock.Size = padding;
                    _freeBlocks[i] = freeBlock;
                    if (tail > 0)
                    {
                        _freeBlocks.Insert(i + 1, new VkMemoryBlock(
                            freeBlock.DeviceMemory,
                            alignedOffset + size,
                            tail,
                            _memoryTypeIndex,
                            freeBlock.BaseMappedPointer,
                            false));
                    }
                }
                else if (tail > 0)
                {
                    freeBlock.Offset += size;
                    freeBlock.Size = tail;
                    _freeBlocks[i] = freeBlock;
                }
                else
                {
                    _freeBlocks.RemoveAt(i);
                }

#if DEBUG
                CheckAllocatedBlock(block);
#endif
                return true;
            }

            block = default;
            return false;
        }

        public void Free(VkMemoryBlock block)
        {
#if DEBUG
            RemoveAllocatedBlock(block);
#endif
            int lo = 0;
            int hi = _freeBlocks.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_freeBlocks[mid].Offset < block.Offset)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            bool mergePrev = lo > 0 && _freeBlocks[lo - 1].End == block.Offset;
            bool mergeNext = lo < _freeBlocks.Count && block.End == _freeBlocks[lo].Offset;

            if (mergePrev)
            {
                VkMemoryBlock prev = _freeBlocks[lo - 1];
                prev.Size += block.Size;
                if (mergeNext)
                {
                    prev.Size += _freeBlocks[lo].Size;
                    _freeBlocks.RemoveAt(lo);
                }
                _freeBlocks[lo - 1] = prev;
            }
            else if (mergeNext)
            {
                VkMemoryBlock next = _freeBlocks[lo];
                next.Offset = block.Offset;
                next.Size += block.Size;
                _freeBlocks[lo] = next;
            }
            else
            {
                _freeBlocks.Insert(lo, block);
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
