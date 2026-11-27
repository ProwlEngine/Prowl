using System;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    protected override IntPtr MapCore(DeviceBuffer buffer)
    {
        VkMemoryBlock memoryBlock = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer).Memory;
        if (memoryBlock.DeviceMemory.Handle == 0)
        {
            return IntPtr.Zero;
        }

        return memoryBlock.IsPersistentMapped
            ? (IntPtr)memoryBlock.BlockMappedPointer
            : MemoryManager.Map(memoryBlock);
    }

    protected override void UnmapCore(DeviceBuffer buffer)
    {
        VkMemoryBlock memoryBlock = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer).Memory;
        if (memoryBlock.DeviceMemory.Handle != 0 && !memoryBlock.IsPersistentMapped)
        {
            Vk.UnmapMemory(Device, memoryBlock.DeviceMemory);
        }
    }
}
