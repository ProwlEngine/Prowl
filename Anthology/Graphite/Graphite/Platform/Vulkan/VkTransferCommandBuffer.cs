using System;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Vulkan TransferCommandBuffer. Owns its own CommandPool so it never touches the frame ring-buffer or its fences. Submit blocks until GPU finishes.
/// </summary>
internal sealed unsafe class VkTransferCommandBuffer : TransferCommandBuffer
{
    private readonly VkGraphicsDevice _gd;
    private readonly CommandPool _pool;
    private Silk.NET.Vulkan.CommandBuffer _cb;
    private bool _destroyed;
    private string _name;
    private QueryPool? _pendingTimingPool;

    public override GraphicsDevice Device => _gd;
    public override bool IsDisposed => _destroyed;

    internal Silk.NET.Vulkan.CommandBuffer CommandBuffer => _cb;

    public override string Name
    {
        get => _name;
        set => _name = value;
    }

    public VkTransferCommandBuffer(VkGraphicsDevice gd)
    {
        _gd = gd;

        CommandPoolCreateInfo poolCI = new(sType: StructureType.CommandPoolCreateInfo)
        {
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit | CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = gd.GraphicsQueueIndex
        };
        gd.Vk.CreateCommandPool(gd.Device, in poolCI, null, out _pool).CheckResult();

        CommandBufferAllocateInfo cbAI = new(sType: StructureType.CommandBufferAllocateInfo)
        {
            CommandPool = _pool,
            CommandBufferCount = 1,
            Level = CommandBufferLevel.Primary
        };
        gd.Vk.AllocateCommandBuffers(gd.Device, in cbAI, out _cb).CheckResult();
    }

    public override void Begin()
    {
        _gd.Vk.ResetCommandBuffer(_cb, 0).CheckResult();

        CommandBufferBeginInfo beginInfo = new(sType: StructureType.CommandBufferBeginInfo)
        {
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        _gd.Vk.BeginCommandBuffer(_cb, in beginInfo).CheckResult();
        HasEnded = false;
        _pendingTimingPool = _gd.BeginTiming(_cb);
    }

    public override void End()
    {
        _gd.EndTiming(_cb, _pendingTimingPool);
        _gd.Vk.EndCommandBuffer(_cb).CheckResult();
        HasEnded = true;
    }

    // Reads and clears the timing pool End() wrote into, for the submission path to attach to
    // this specific submission.
    internal QueryPool? TakePendingTimingPool()
    {
        QueryPool? pool = _pendingTimingPool;
        _pendingTimingPool = null;
        return pool;
    }

    internal void SubmitAndWait()
    {
        _gd.SubmitAndWaitTransfer(_cb, TakePendingTimingPool(), _name);
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        _gd.UpdateBuffer(buffer, bufferOffsetInBytes, source, sizeInBytes);
    }

    private protected override void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        _gd.UpdateTexture(texture, source, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
    }

    private protected override void CopyBufferCore(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)
    {
        VkBuffer srcVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(source);
        VkBuffer dstVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);

        BufferCopy region = new()
        {
            SrcOffset = sourceOffset,
            DstOffset = destinationOffset,
            Size = sizeInBytes
        };

        _gd.Vk.CmdCopyBuffer(_cb, srcVkBuffer.DeviceBuffer, dstVkBuffer.DeviceBuffer, 1, in region);
        destination.MarkContentChanged();

        bool needToProtectUniform = destination.Usage.HasFlag(BufferUsage.UniformBuffer);
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = needToProtectUniform ? AccessFlags.UniformReadBit : AccessFlags.VertexAttributeReadBit
        };
        _gd.Vk.CmdPipelineBarrier(
            _cb,
            PipelineStageFlags.TransferBit, needToProtectUniform ?
                PipelineStageFlags.VertexShaderBit | PipelineStageFlags.ComputeShaderBit |
                PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.GeometryShaderBit |
                PipelineStageFlags.TessellationControlShaderBit | PipelineStageFlags.TessellationEvaluationShaderBit
                : PipelineStageFlags.VertexInputBit,
            0,
            1, in barrier,
            0, null,
            0, null);
        _gd.Profiler?.RecordBarrier(BarrierBin.BufferTransition, 1);
    }

    private protected override void CopyTextureCore(
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount)
    {
        VkCommandBuffer.CopyTextureCore_VkCommandBuffer(
            _gd.Vk,
            _cb,
            source, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
            destination, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
            width, height, depth, layerCount);
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        VkCommandBuffer.GenerateMipmapsCore_VkCommandBuffer(_gd, _cb, vkTex);
    }

    public override void Dispose()
    {
        if (_destroyed)
            return;

        _destroyed = true;
        _gd.Vk.DestroyCommandPool(_gd.Device, _pool, null);
    }
}
