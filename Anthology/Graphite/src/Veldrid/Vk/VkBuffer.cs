using System;
using Silk.NET.Vulkan;
using static Veldrid.Vk.VulkanUtil;
using VkBufferHandle = Silk.NET.Vulkan.Buffer;

namespace Veldrid.Vk
{
    internal unsafe class VkBuffer : DeviceBuffer
    {
        private readonly VkGraphicsDevice _gd;
        private readonly VkBufferHandle _deviceBuffer;
        private readonly VkMemoryBlock _memory;
        private readonly MemoryRequirements _bufferMemoryRequirements;
        public ResourceRefCount RefCount { get; }
        private bool _destroyed;
        private string _name;
        public override bool IsDisposed => _destroyed;

        public override uint SizeInBytes { get; }
        public override BufferUsage Usage { get; }

        public VkBufferHandle DeviceBuffer => _deviceBuffer;
        public VkMemoryBlock Memory => _memory;

        public MemoryRequirements BufferMemoryRequirements => _bufferMemoryRequirements;

        public VkBuffer(VkGraphicsDevice gd, uint sizeInBytes, BufferUsage usage, string callerMember = null)
        {
            _gd = gd;
            SizeInBytes = sizeInBytes;
            Usage = usage;

            BufferUsageFlags vkUsage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit;
            if ((usage & BufferUsage.VertexBuffer) == BufferUsage.VertexBuffer)
            {
                vkUsage |= BufferUsageFlags.VertexBufferBit;
            }
            if ((usage & BufferUsage.IndexBuffer) == BufferUsage.IndexBuffer)
            {
                vkUsage |= BufferUsageFlags.IndexBufferBit;
            }
            if ((usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer)
            {
                vkUsage |= BufferUsageFlags.UniformBufferBit;
            }
            if ((usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite
                || (usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly)
            {
                vkUsage |= BufferUsageFlags.StorageBufferBit;
            }
            if ((usage & BufferUsage.IndirectBuffer) == BufferUsage.IndirectBuffer)
            {
                vkUsage |= BufferUsageFlags.IndirectBufferBit;
            }

            BufferCreateInfo bufferCI = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = sizeInBytes,
                Usage = vkUsage
            };
            Result result = _gd.Vk.CreateBuffer(gd.Device, in bufferCI, null, out _deviceBuffer);
            CheckResult(result);

            bool prefersDedicatedAllocation;
            if (_gd.GetBufferMemoryRequirements2 != null)
            {
                BufferMemoryRequirementsInfo2KHR memReqInfo2 = new BufferMemoryRequirementsInfo2KHR
                {
                    SType = StructureType.BufferMemoryRequirementsInfo2Khr,
                    Buffer = _deviceBuffer
                };
                MemoryRequirements2KHR memReqs2 = new MemoryRequirements2KHR
                {
                    SType = StructureType.MemoryRequirements2Khr
                };
                MemoryDedicatedRequirementsKHR dedicatedReqs = new MemoryDedicatedRequirementsKHR
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
                _gd.Vk.GetBufferMemoryRequirements(gd.Device, _deviceBuffer, out _bufferMemoryRequirements);
                prefersDedicatedAllocation = false;
            }

            var isStaging = (usage & BufferUsage.Staging) == BufferUsage.Staging;
            var hostVisible = isStaging || (usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;

            MemoryPropertyFlags memoryPropertyFlags =
                hostVisible
                ? MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
                : MemoryPropertyFlags.DeviceLocalBit;
            if (isStaging)
            {
                // Use "host cached" memory for staging when available, for better performance of GPU -> CPU transfers
                var hostCachedAvailable = TryFindMemoryType(
                    gd.PhysicalDeviceMemProperties,
                    _bufferMemoryRequirements.MemoryTypeBits,
                    memoryPropertyFlags | MemoryPropertyFlags.HostCachedBit,
                    out _);
                if (hostCachedAvailable)
                {
                    memoryPropertyFlags |= MemoryPropertyFlags.HostCachedBit;
                }
            }

            VkMemoryBlock memoryToken = gd.MemoryManager.Allocate(
                gd.PhysicalDeviceMemProperties,
                _bufferMemoryRequirements.MemoryTypeBits,
                memoryPropertyFlags,
                hostVisible,
                _bufferMemoryRequirements.Size,
                _bufferMemoryRequirements.Alignment,
                prefersDedicatedAllocation,
                default(Image),
                _deviceBuffer);
            _memory = memoryToken;
            result = _gd.Vk.BindBufferMemory(gd.Device, _deviceBuffer, _memory.DeviceMemory, _memory.Offset);
            CheckResult(result);

            RefCount = new ResourceRefCount(DisposeCore);
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
            }
        }
    }
}
