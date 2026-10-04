using System;

using Silk.NET.Vulkan;


using VkBufferHandle = Silk.NET.Vulkan.Buffer;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkBuffer : DeviceBuffer
{
    private readonly VkGraphicsDevice _gd;
    private VkBufferHandle _deviceBuffer;
    private VkMemoryBlock _memory;

    public VkBufferHandle DeviceBuffer => _deviceBuffer;
    public VkMemoryBlock Memory => _memory;

    public VkBuffer(VkGraphicsDevice gd, in BufferDescription description)
        : base(description)
    {
        _gd = gd;

        CreateNativeBuffer();

    }

    private void CreateNativeBuffer()
    {
        BufferUsageFlags vkUsage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit;
        if ((Usage & BufferUsage.VertexBuffer) == BufferUsage.VertexBuffer)
        {
            vkUsage |= BufferUsageFlags.VertexBufferBit;
        }
        if ((Usage & BufferUsage.IndexBuffer) == BufferUsage.IndexBuffer)
        {
            vkUsage |= BufferUsageFlags.IndexBufferBit;
        }
        if ((Usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer)
        {
            vkUsage |= BufferUsageFlags.UniformBufferBit;
        }
        if ((Usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite
            || (Usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly)
        {
            vkUsage |= BufferUsageFlags.StorageBufferBit;
        }
        if ((Usage & BufferUsage.IndirectBuffer) == BufferUsage.IndirectBuffer)
        {
            vkUsage |= BufferUsageFlags.IndirectBufferBit;
        }

        BufferCreateInfo bufferCI = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = SizeInBytes,
            Usage = vkUsage
        };
        _gd.Vk.CreateBuffer(_gd.Device, in bufferCI, null, out _deviceBuffer).CheckResult();

        BufferMemoryRequirementsInfo2 memReqInfo2 = new()
        {
            SType = StructureType.BufferMemoryRequirementsInfo2,
            Buffer = _deviceBuffer
        };
        MemoryDedicatedRequirements dedicatedReqs = new() { SType = StructureType.MemoryDedicatedRequirements };
        MemoryRequirements2 memReqs2 = new() { SType = StructureType.MemoryRequirements2, PNext = &dedicatedReqs };
        _gd.Vk.GetBufferMemoryRequirements2(_gd.Device, &memReqInfo2, &memReqs2);
        MemoryRequirements bufferMemReqs = memReqs2.MemoryRequirements;
        bool prefersDedicatedAllocation = dedicatedReqs.PrefersDedicatedAllocation || dedicatedReqs.RequiresDedicatedAllocation;

        bool isStaging = (Usage & BufferUsage.Staging) == BufferUsage.Staging;
        bool hostVisible = isStaging || (Usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;

        MemoryPropertyFlags memoryPropertyFlags =
            hostVisible
            ? MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            : MemoryPropertyFlags.DeviceLocalBit;
        if (isStaging)
        {
            // Use "host cached" memory for staging when available, for better performance of GPU -> CPU transfers
            bool hostCachedAvailable = _gd.Vk.TryFindMemoryType(
                _gd.PhysicalDeviceMemProperties,
                bufferMemReqs.MemoryTypeBits,
                memoryPropertyFlags | MemoryPropertyFlags.HostCachedBit,
                out _);
            if (hostCachedAvailable)
            {
                memoryPropertyFlags |= MemoryPropertyFlags.HostCachedBit;
            }
        }

        _memory = _gd.MemoryManager.Allocate(
            _gd.PhysicalDeviceMemProperties,
            bufferMemReqs.MemoryTypeBits,
            memoryPropertyFlags,
            hostVisible,
            bufferMemReqs.Size,
            bufferMemReqs.Alignment,
            prefersDedicatedAllocation,
            default,
            _deviceBuffer);
        _gd.Vk.BindBufferMemory(_gd.Device, _deviceBuffer, _memory.DeviceMemory, _memory.Offset).CheckResult();

        _gd.RecordBufferAllocation(Usage, SizeInBytes);
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore()
    {
        _gd.DisposeWhenRetired(DestroyNative);
    }

    private void DestroyNative()
    {
        _gd.Vk.DestroyBuffer(_gd.Device, _deviceBuffer, null);
        _gd.MemoryManager.Free(Memory);
        _gd.RecordBufferFree(Usage, SizeInBytes);
    }
}
