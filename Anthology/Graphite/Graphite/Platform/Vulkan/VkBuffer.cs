using System;

using Silk.NET.Vulkan;


using VkBufferHandle = Silk.NET.Vulkan.Buffer;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkBuffer : DeviceBuffer
{
    private readonly VkGraphicsDevice _gd;
    private VkBufferHandle _deviceBuffer;
    private VkMemoryBlock _memory;
    private MemoryRequirements _bufferMemoryRequirements;
    public ResourceRefCount RefCount { get; }
    private bool _destroyed;
    private string _name;
    public override bool IsDisposed => _destroyed;

    public override uint SizeInBytes { get; }
    public override BufferUsage Usage { get; }

    public VkBufferHandle DeviceBuffer => _deviceBuffer;
    public VkMemoryBlock Memory => _memory;

    public MemoryRequirements BufferMemoryRequirements => _bufferMemoryRequirements;

    public VkBuffer(VkGraphicsDevice gd, uint sizeInBytes, BufferUsage usage, string? callerMember = null)
    {
        _gd = gd;
        SizeInBytes = sizeInBytes;
        Usage = usage;

        CreateNativeBuffer();

        RefCount = new ResourceRefCount(DisposeCore);

        _gd.RecordBufferAllocation(Usage, SizeInBytes);
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

        bool prefersDedicatedAllocation;
        if (_gd.GetBufferMemoryRequirements2 != null)
        {
            BufferMemoryRequirementsInfo2KHR memReqInfo2 = new()
            {
                SType = StructureType.BufferMemoryRequirementsInfo2Khr,
                Buffer = _deviceBuffer
            };
            MemoryRequirements2KHR memReqs2 = new()
            {
                SType = StructureType.MemoryRequirements2Khr
            };
            MemoryDedicatedRequirementsKHR dedicatedReqs = new()
            {
                SType = StructureType.MemoryDedicatedRequirementsKhr
            };
            memReqs2.PNext = &dedicatedReqs;
            _gd.GetBufferMemoryRequirements2(_gd.Device, &memReqInfo2, &memReqs2);
            _bufferMemoryRequirements = memReqs2.MemoryRequirements;
            prefersDedicatedAllocation = dedicatedReqs.PrefersDedicatedAllocation || dedicatedReqs.RequiresDedicatedAllocation;
        }
        else
        {
            _gd.Vk.GetBufferMemoryRequirements(_gd.Device, _deviceBuffer, out _bufferMemoryRequirements);
            prefersDedicatedAllocation = false;
        }

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
                _bufferMemoryRequirements.MemoryTypeBits,
                memoryPropertyFlags | MemoryPropertyFlags.HostCachedBit,
                out _);
            if (hostCachedAvailable)
            {
                memoryPropertyFlags |= MemoryPropertyFlags.HostCachedBit;
            }
        }

        VkMemoryBlock memoryToken = _gd.MemoryManager.Allocate(
            _gd.PhysicalDeviceMemProperties,
            _bufferMemoryRequirements.MemoryTypeBits,
            memoryPropertyFlags,
            hostVisible,
            _bufferMemoryRequirements.Size,
            _bufferMemoryRequirements.Alignment,
            prefersDedicatedAllocation,
            default,
            _deviceBuffer);
        _memory = memoryToken;
        _gd.Vk.BindBufferMemory(_gd.Device, _deviceBuffer, _memory.DeviceMemory, _memory.Offset).CheckResult();
    }

    protected internal override void OrphanCore(GraphicsDevice device, ulong inFlightFrameId)
    {
        VkBufferHandle retiredBuffer = _deviceBuffer;
        VkMemoryBlock retiredMemory = _memory;

        CreateNativeBuffer();

        device.DisposeWhenFrameComplete(inFlightFrameId, new RetiredNativeBuffer(_gd, retiredBuffer, retiredMemory));
    }

    private sealed class RetiredNativeBuffer : IDisposable
    {
        private readonly VkGraphicsDevice _gd;
        private readonly VkBufferHandle _buffer;
        private readonly VkMemoryBlock _memory;

        public RetiredNativeBuffer(VkGraphicsDevice gd, VkBufferHandle buffer, VkMemoryBlock memory)
        {
            _gd = gd;
            _buffer = buffer;
            _memory = memory;
        }

        public void Dispose()
        {
            _gd.Vk.DestroyBuffer(_gd.Device, _buffer, null);
            _gd.MemoryManager.Free(_memory);
        }
    }

    public override string Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetResourceName(this, value);
        }
    }

    public override void Dispose()
    {
        RefCount.Decrement();
    }

    private void DisposeCore()
    {
        if (!_destroyed)
        {
            _destroyed = true;
            _gd.Vk.DestroyBuffer(_gd.Device, _deviceBuffer, null);
            _gd.MemoryManager.Free(Memory);
            _gd.RecordBufferFree(Usage, SizeInBytes);
        }
    }
}
