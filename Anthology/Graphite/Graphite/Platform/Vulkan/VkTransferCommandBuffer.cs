using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

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
    private QueryPool? _pendingTimingPool;
    private readonly List<VkBuffer> _stagingInUse = [];
    private readonly List<VkBuffer> _stagingFree = [];

    public override GraphicsDevice Device => _gd;

    internal Silk.NET.Vulkan.CommandBuffer CommandBuffer => _cb;

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
        _stagingFree.AddRange(_stagingInUse);
        _stagingInUse.Clear();
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
        _gd.SubmitAndWaitTransfer(_cb, TakePendingTimingPool(), Name, Id);
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        VkBuffer staging = RentStaging(source, sizeInBytes);

        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.TransferWriteBit
        };
        _gd.Vk.CmdPipelineBarrier(_cb, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.TransferBit, 0, 1, in barrier, 0, null, 0, null);

        CopyBufferCore(staging, 0, buffer, bufferOffsetInBytes, sizeInBytes);
        _gd.Profiler?.Record(BufferOpBin.Update, sizeInBytes);
    }

    private protected override void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        VkBuffer staging = RentStaging(source, sizeInBytes);
        if (vkTex.IsStaging)
        {
            CopyToStagingTexture(staging, vkTex, x, y, z, width, height, depth, mipLevel, arrayLayer);
            _gd.Profiler?.Record(BufferOpBin.Update, sizeInBytes);
            return;
        }

        ImageLayout layout = VkBarriers.CurrentLayout(this, vkTex);

        VkBarriers.Transition(_gd, _cb, vkTex, layout, ImageLayout.TransferDstOptimal, mipLevel, 1, arrayLayer, 1);

        BufferImageCopy region = new()
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = mipLevel,
                BaseArrayLayer = arrayLayer,
                LayerCount = 1
            },
            ImageOffset = new Offset3D { X = (int)x, Y = (int)y, Z = (int)z },
            ImageExtent = new Extent3D { Width = width, Height = height, Depth = depth }
        };
        _gd.Vk.CmdCopyBufferToImage(_cb, staging.DeviceBuffer, vkTex.OptimalDeviceImage, ImageLayout.TransferDstOptimal, 1, in region);

        VkBarriers.Transition(_gd, _cb, vkTex, ImageLayout.TransferDstOptimal, layout, mipLevel, 1, arrayLayer, 1);
        _gd.Profiler?.Record(BufferOpBin.Update, sizeInBytes);
    }

    private void CopyToStagingTexture(
        VkBuffer staging, VkTexture texture,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        PixelFormat format = texture.Format;
        SubresourceLayout layout = texture.GetSubresourceLayout(texture.CalculateSubresource(mipLevel, arrayLayer));
        uint blockSize = FormatHelpers.IsCompressedFormat(format) ? 4u : 1u;
        uint elementBytes = blockSize > 1 ? FormatHelpers.GetBlockSizeInBytes(format) : format.GetSizeInBytes();
        uint rowBytes = FormatHelpers.GetRowPitch(width, format);
        uint rows = FormatHelpers.GetNumRows(height, format);

        BufferCopy[] regions = new BufferCopy[rows * depth];
        for (uint slice = 0; slice < depth; slice++)
        {
            for (uint row = 0; row < rows; row++)
            {
                regions[slice * rows + row] = new BufferCopy
                {
                    SrcOffset = (slice * rows + row) * (ulong)rowBytes,
                    DstOffset = layout.Offset
                        + (z + slice) * layout.DepthPitch
                        + (y / blockSize + row) * layout.RowPitch
                        + x / blockSize * elementBytes,
                    Size = rowBytes
                };
            }
        }

        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.TransferWriteBit
        };
        _gd.Vk.CmdPipelineBarrier(_cb, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.TransferBit, 0, 1, in barrier, 0, null, 0, null);

        fixed (BufferCopy* regionsPtr = regions)
            _gd.Vk.CmdCopyBuffer(_cb, staging.DeviceBuffer, texture.StagingBuffer, (uint)regions.Length, regionsPtr);

        barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
        barrier.DstAccessMask = AccessFlags.TransferReadBit | AccessFlags.HostReadBit;
        _gd.Vk.CmdPipelineBarrier(_cb, PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit | PipelineStageFlags.HostBit, 0, 1, in barrier, 0, null, 0, null);
    }

    private VkBuffer RentStaging(IntPtr source, uint sizeInBytes)
    {
        VkBuffer? staging = null;
        for (int i = 0; i < _stagingFree.Count; i++)
        {
            if (_stagingFree[i].SizeInBytes >= sizeInBytes)
            {
                staging = _stagingFree[i];
                _stagingFree.RemoveAt(i);
                break;
            }
        }

        staging ??= (VkBuffer)_gd.ResourceFactory.CreateBuffer(
            new BufferDescription(Math.Max(64u, BitOperations.RoundUpToPowerOf2(sizeInBytes)), BufferUsage.Staging));
        _stagingInUse.Add(staging);

        Unsafe.CopyBlock(staging.Memory.BlockMappedPointer, source.ToPointer(), sizeInBytes);
        return staging;
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
            _gd,
            _cb,
            source, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
            destination, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
            width, height, depth, layerCount,
            VkBarriers.CurrentLayout(this, Util.AssertSubtype<Texture, VkTexture>(source)),
            VkBarriers.CurrentLayout(this, Util.AssertSubtype<Texture, VkTexture>(destination)));
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        VkCommandBuffer.GenerateMipmapsCore_VkCommandBuffer(_gd, _cb, vkTex, VkBarriers.CurrentLayout(this, vkTex));
    }

    private protected override void DisposeCore()
    {
        foreach (VkBuffer staging in _stagingInUse)
            staging.Dispose();
        foreach (VkBuffer staging in _stagingFree)
            staging.Dispose();
        _stagingInUse.Clear();
        _stagingFree.Clear();

        _gd.Vk.DestroyCommandPool(_gd.Device, _pool, null);
    }
}
