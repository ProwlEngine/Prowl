using System;
using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkDeviceMemoryManager
{
    private class ChunkAllocatorSet : IDisposable
    {
        private readonly Silk.NET.Vulkan.Vk _vk;
        private readonly Device _device;
        private readonly uint _memoryTypeIndex;
        private readonly bool _persistentMapped;
        private readonly List<ChunkAllocator> _allocators = [];

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

            ChunkAllocator newAllocator = new(_vk, _device, _memoryTypeIndex, _persistentMapped);
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
}
