using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal sealed unsafe class SharedCommandPool
{
    private readonly VkGraphicsDevice _gd;
    private readonly CommandPool _pool;
    private readonly Silk.NET.Vulkan.CommandBuffer _cb;

    public bool IsCached { get; }

    public SharedCommandPool(VkGraphicsDevice gd, bool isCached)
    {
        _gd = gd;
        IsCached = isCached;

        CommandPoolCreateInfo commandPoolCI = new(sType: StructureType.CommandPoolCreateInfo);
        commandPoolCI.Flags = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit;
        commandPoolCI.QueueFamilyIndex = _gd.GraphicsQueueIndex;
        _gd.Vk.CreateCommandPool(_gd.Device, in commandPoolCI, null, out _pool).CheckResult();

        CommandBufferAllocateInfo allocateInfo = new(sType: StructureType.CommandBufferAllocateInfo);
        allocateInfo.CommandBufferCount = 1;
        allocateInfo.Level = CommandBufferLevel.Primary;
        allocateInfo.CommandPool = _pool;
        fixed (Silk.NET.Vulkan.CommandBuffer* cbPtr = &_cb)
            _gd.Vk.AllocateCommandBuffers(_gd.Device, &allocateInfo, cbPtr).CheckResult();
    }

    public Silk.NET.Vulkan.CommandBuffer BeginNewCommandBuffer()
    {
        CommandBufferBeginInfo beginInfo = new(sType: StructureType.CommandBufferBeginInfo)
        {
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _gd.Vk.BeginCommandBuffer(_cb, in beginInfo).CheckResult();

        return _cb;
    }

    public void EndAndSubmit(Silk.NET.Vulkan.CommandBuffer cb)
    {
        _gd.Vk.EndCommandBuffer(cb).CheckResult();
        _gd.SubmitCommandBuffer(null, cb, 0, null, 0, null, null);
        lock (_gd._stagingResourcesLock)
        {
            _gd._submittedSharedCommandPools.Add(cb, this);
        }
    }

    internal void Destroy()
    {
        _gd.Vk.DestroyCommandPool(_gd.Device, _pool, null);
    }
}
