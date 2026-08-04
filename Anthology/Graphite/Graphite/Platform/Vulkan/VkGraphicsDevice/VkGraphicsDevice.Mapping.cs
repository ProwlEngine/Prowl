using System;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    protected override MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource)
    {
        VkMemoryBlock memoryBlock = default;
        IntPtr mappedPtr = IntPtr.Zero;
        uint sizeInBytes;
        uint offset = 0;
        uint rowPitch = 0;
        uint depthPitch = 0;
        if (resource is VkBuffer buffer)
        {
            memoryBlock = buffer.Memory;
            sizeInBytes = buffer.SizeInBytes;
        }
        else
        {
            VkTexture texture = Util.AssertSubtype<MappableResource, VkTexture>(resource);
            SubresourceLayout layout = texture.GetSubresourceLayout(subresource);
            memoryBlock = texture.Memory;
            sizeInBytes = (uint)layout.Size;
            offset = (uint)layout.Offset;
            rowPitch = (uint)layout.RowPitch;
            depthPitch = (uint)layout.DepthPitch;
        }

        if (memoryBlock.DeviceMemory.Handle != 0)
        {
            if (memoryBlock.IsPersistentMapped)
            {
                mappedPtr = (IntPtr)memoryBlock.BlockMappedPointer;
            }
            else
            {
                mappedPtr = MemoryManager.Map(memoryBlock);
            }
        }

        byte* dataPtr = (byte*)mappedPtr.ToPointer() + offset;
        return new MappedResource(
            resource,
            mode,
            (IntPtr)dataPtr,
            sizeInBytes,
            subresource,
            rowPitch,
            depthPitch);
    }

    protected override void UnmapCore(MappableResource resource, uint subresource)
    {
        VkMemoryBlock memoryBlock = default;
        if (resource is VkBuffer buffer)
        {
            memoryBlock = buffer.Memory;
        }
        else
        {
            VkTexture tex = Util.AssertSubtype<MappableResource, VkTexture>(resource);
            memoryBlock = tex.Memory;
        }

        if (memoryBlock.DeviceMemory.Handle != 0 && !memoryBlock.IsPersistentMapped)
        {
            Vk.UnmapMemory(Device, memoryBlock.DeviceMemory);
        }
    }
}
