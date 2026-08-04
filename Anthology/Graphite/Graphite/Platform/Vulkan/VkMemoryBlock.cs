using System;
using System.Diagnostics;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

[DebuggerDisplay("[Mem:{DeviceMemory.Handle}] Off:{Offset}, Size:{Size} End:{Offset+Size}")]
internal unsafe struct VkMemoryBlock : IEquatable<VkMemoryBlock>
{
    public readonly uint MemoryTypeIndex;
    public readonly DeviceMemory DeviceMemory;
    public readonly void* BaseMappedPointer;
    public readonly bool DedicatedAllocation;

    public ulong Offset;
    public ulong Size;

    public readonly void* BlockMappedPointer => ((byte*)BaseMappedPointer) + Offset;
    public readonly bool IsPersistentMapped => BaseMappedPointer != null;
    public readonly ulong End => Offset + Size;

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

    public readonly bool Equals(VkMemoryBlock other)
    {
        return DeviceMemory.Handle.Equals(other.DeviceMemory.Handle)
            && Offset.Equals(other.Offset)
            && Size.Equals(other.Size);
    }
}
