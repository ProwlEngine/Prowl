using System;
using System.Diagnostics;

using Silk.NET.Vulkan;

using VkImageHandle = Silk.NET.Vulkan.Image;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        VkBuffer stagingBuffer = GetFilledStagingBuffer(source, sizeInBytes);
        CopyBuffer(stagingBuffer, 0, buffer, bufferOffsetInBytes, sizeInBytes);
    }

    private protected override void CopyBufferCore(
        DeviceBuffer source,
        uint sourceOffset,
        DeviceBuffer destination,
        uint destinationOffset,
        uint sizeInBytes)
    {
        EnsureNoRenderPass();

        destination.MarkContentChanged();

        VkBuffer srcVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(source);
        VkBuffer dstVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);

        BufferCopy region = new()
        {
            SrcOffset = sourceOffset,
            DstOffset = destinationOffset,
            Size = sizeInBytes
        };

        _gd.Vk.CmdCopyBuffer(_cb, srcVkBuffer.DeviceBuffer, dstVkBuffer.DeviceBuffer, 1, in region);
        _gd.Counters.RecordBufferOp(BufferOpBin.Copy, sizeInBytes);

        EmitPostCopyBufferBarrier(destination.Usage.HasFlag(BufferUsage.UniformBuffer));
    }

    internal override void RecordFullBarrier()
    {
        EnsureNoRenderPass();
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit
        };
        _gd.Vk.CmdPipelineBarrier(_cb, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit, 0, 1, in barrier, 0, null, 0, null);
    }

    private protected override void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        in TextureRegion region)
    {
        EnsureNoRenderPass();
        uint x = region.X, y = region.Y, z = region.Z;
        uint width = region.Width, height = region.Height, depth = region.Depth;
        uint mipLevel = region.MipLevel, arrayLayer = region.ArrayLayer;
        Util.GetMipDimensions(texture, mipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
        width = Math.Min(width, mipWidth - x);
        height = Math.Min(height, mipHeight - y);
        depth = Math.Min(depth, mipDepth - z);
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        VkBuffer staging = GetFilledStagingBuffer(source, sizeInBytes);

        ImageLayout layout = VkBarriers.CurrentLayout(this, vkTex);

        VkBarriers.Transition(_gd, _cb, vkTex, layout, ImageLayout.TransferDstOptimal, mipLevel, 1, arrayLayer, 1);

        BufferImageCopy copy = new()
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
        _gd.Vk.CmdCopyBufferToImage(_cb, staging.DeviceBuffer, vkTex.OptimalDeviceImage, ImageLayout.TransferDstOptimal, 1, in copy);

        VkBarriers.Transition(_gd, _cb, vkTex, ImageLayout.TransferDstOptimal, layout, mipLevel, 1, arrayLayer, 1);
        _gd.Counters.RecordBufferOp(BufferOpBin.Update, sizeInBytes);
    }

    private void EmitPostCopyBufferBarrier(bool needToProtectUniform)
    {
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = needToProtectUniform ? AccessFlags.UniformReadBit : AccessFlags.VertexAttributeReadBit
        };

        PipelineStageFlags dstStage = needToProtectUniform
            ? PipelineStageFlags.VertexShaderBit | PipelineStageFlags.ComputeShaderBit |
              PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.GeometryShaderBit |
              PipelineStageFlags.TessellationControlShaderBit | PipelineStageFlags.TessellationEvaluationShaderBit
            : PipelineStageFlags.VertexInputBit;

        _gd.Vk.CmdPipelineBarrier(
            _cb,
            PipelineStageFlags.TransferBit, dstStage,
            0,
            1, in barrier,
            0, null,
            0, null);
        _gd.Counters.RecordBarrier(BarrierBin.BufferTransition, 1);
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
        EnsureNoRenderPass();
        VkTexture src = Util.AssertSubtype<Texture, VkTexture>(source);
        VkTexture dst = Util.AssertSubtype<Texture, VkTexture>(destination);
        ImageLayout srcLayout = VkBarriers.CurrentLayout(this, src);
        ImageLayout dstLayout = VkBarriers.CurrentLayout(this, dst);

        ImageCopy region = new()
        {
            SrcOffset = new Offset3D { X = (int)srcX, Y = (int)srcY, Z = (int)srcZ },
            DstOffset = new Offset3D { X = (int)dstX, Y = (int)dstY, Z = (int)dstZ },
            SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = CopyAspectMask(src),
                LayerCount = layerCount,
                MipLevel = srcMipLevel,
                BaseArrayLayer = srcBaseArrayLayer
            },
            DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = CopyAspectMask(dst),
                LayerCount = layerCount,
                MipLevel = dstMipLevel,
                BaseArrayLayer = dstBaseArrayLayer
            },
            Extent = new Extent3D { Width = width, Height = height, Depth = depth }
        };

        VkBarriers.Transition(_gd, _cb, src, srcLayout, ImageLayout.TransferSrcOptimal, srcMipLevel, 1, srcBaseArrayLayer, layerCount);
        VkBarriers.Transition(_gd, _cb, dst, dstLayout, ImageLayout.TransferDstOptimal, dstMipLevel, 1, dstBaseArrayLayer, layerCount);

        _gd.Vk.CmdCopyImage(
            _cb,
            src.OptimalDeviceImage,
            ImageLayout.TransferSrcOptimal,
            dst.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            1,
            in region);

        VkBarriers.Transition(_gd, _cb, src, ImageLayout.TransferSrcOptimal, srcLayout, srcMipLevel, 1, srcBaseArrayLayer, layerCount);
        VkBarriers.Transition(_gd, _cb, dst, ImageLayout.TransferDstOptimal, dstLayout, dstMipLevel, 1, dstBaseArrayLayer, layerCount);
    }

    private protected override void CopyTextureToBufferCore(
        Texture source,
        DeviceBuffer destination,
        uint destinationOffset,
        in TextureRegion region)
    {
        EnsureNoRenderPass();
        destination.MarkContentChanged();
        VkTexture src = Util.AssertSubtype<Texture, VkTexture>(source);
        VkBuffer dst = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);
        ImageLayout layout = VkBarriers.CurrentLayout(this, src);

        BufferImageCopy copy = new()
        {
            BufferOffset = destinationOffset,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = (source.Usage & TextureUsage.DepthStencil) != 0 ? ImageAspectFlags.DepthBit : ImageAspectFlags.ColorBit,
                MipLevel = region.MipLevel,
                BaseArrayLayer = region.ArrayLayer,
                LayerCount = 1
            },
            ImageOffset = new Offset3D { X = (int)region.X, Y = (int)region.Y, Z = (int)region.Z },
            ImageExtent = new Extent3D { Width = region.Width, Height = region.Height, Depth = region.Depth }
        };

        VkBarriers.Transition(_gd, _cb, src, layout, ImageLayout.TransferSrcOptimal, region.MipLevel, 1, region.ArrayLayer, 1);
        _gd.Vk.CmdCopyImageToBuffer(_cb, src.OptimalDeviceImage, ImageLayout.TransferSrcOptimal, dst.DeviceBuffer, 1, in copy);
        VkBarriers.Transition(_gd, _cb, src, ImageLayout.TransferSrcOptimal, layout, region.MipLevel, 1, region.ArrayLayer, 1);
        _gd.Counters.RecordBufferOp(BufferOpBin.Copy, FormatHelpers.GetRegionSize(region.Width, region.Height, region.Depth, source.Format));
    }

    private static ImageAspectFlags CopyAspectMask(VkTexture texture)
    {
        if ((texture.Usage & TextureUsage.DepthStencil) == 0)
            return ImageAspectFlags.ColorBit;

        return FormatHelpers.IsStencilFormat(texture.Format)
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.DepthBit;
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        EnsureNoRenderPass();
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);

        GenerateMipmapsCore_VkCommandBuffer(_gd, _cb, vkTex, VkBarriers.CurrentLayout(this, vkTex));
    }

    internal static void GenerateMipmapsCore_VkCommandBuffer(VkGraphicsDevice gd, Silk.NET.Vulkan.CommandBuffer cb, VkTexture vkTex, ImageLayout layout)
    {
        uint layerCount = vkTex.ActualArrayLayers;

        uint width = vkTex.Width;
        uint height = vkTex.Height;
        uint depth = vkTex.Depth;
        for (uint level = 1; level < vkTex.MipLevels; level++)
        {
            uint mipWidth = Math.Max(width >> 1, 1);
            uint mipHeight = Math.Max(height >> 1, 1);
            uint mipDepth = Math.Max(depth >> 1, 1);

            ImageLayout sourceLayout = level == 1 ? layout : ImageLayout.TransferDstOptimal;
            VkBarriers.Transition(gd, cb, vkTex, sourceLayout, ImageLayout.TransferSrcOptimal, level - 1, 1, 0, layerCount);
            VkBarriers.Transition(gd, cb, vkTex, layout, ImageLayout.TransferDstOptimal, level, 1, 0, layerCount);
            BlitMipLevel(gd, cb, vkTex, level, layerCount, width, height, depth, mipWidth, mipHeight, mipDepth);

            width = mipWidth;
            height = mipHeight;
            depth = mipDepth;
        }

        uint last = vkTex.MipLevels - 1;
        VkBarriers.Transition(gd, cb, vkTex, ImageLayout.TransferSrcOptimal, layout, 0, last, 0, layerCount);
        VkBarriers.Transition(gd, cb, vkTex, ImageLayout.TransferDstOptimal, layout, last, 1, 0, layerCount);
    }

    private static void BlitMipLevel(
        VkGraphicsDevice gd,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture vkTex,
        uint level,
        uint layerCount,
        uint width, uint height, uint depth,
        uint mipWidth, uint mipHeight, uint mipDepth)
    {
        ImageBlit region = new()
        {
            SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                LayerCount = layerCount,
                MipLevel = level - 1
            },
            DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                LayerCount = layerCount,
                MipLevel = level
            }
        };
        region.SrcOffsets.Element0 = new Offset3D();
        region.SrcOffsets.Element1 = new Offset3D { X = (int)width, Y = (int)height, Z = (int)depth };
        region.DstOffsets.Element0 = new Offset3D();
        region.DstOffsets.Element1 = new Offset3D { X = (int)mipWidth, Y = (int)mipHeight, Z = (int)mipDepth };

        VkImageHandle deviceImage = vkTex.OptimalDeviceImage;
        gd.Vk.CmdBlitImage(
            cb,
            deviceImage, ImageLayout.TransferSrcOptimal,
            deviceImage, ImageLayout.TransferDstOptimal,
            1, &region,
            gd.GetFormatFilter(vkTex.VkFormat));
    }

    protected override void ResolveTextureCore(Texture source, Texture destination)
    {
        EnsureNoRenderPass();

        VkTexture vkSource = Util.AssertSubtype<Texture, VkTexture>(source);
        VkTexture vkDestination = Util.AssertSubtype<Texture, VkTexture>(destination);

        ImageAspectFlags aspectFlags = ((source.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.ColorBit;
        ImageResolve region = new()
        {
            Extent = new Extent3D { Width = source.Width, Height = source.Height, Depth = source.Depth },
            SrcSubresource = new ImageSubresourceLayers { LayerCount = 1, AspectMask = aspectFlags },
            DstSubresource = new ImageSubresourceLayers { LayerCount = 1, AspectMask = aspectFlags }
        };

        ImageLayout sourceLayout = VkBarriers.CurrentLayout(this, vkSource);
        ImageLayout destinationLayout = VkBarriers.CurrentLayout(this, vkDestination);
        VkBarriers.Transition(_gd, _cb, vkSource, sourceLayout, ImageLayout.TransferSrcOptimal, 0, 1, 0, 1);
        VkBarriers.Transition(_gd, _cb, vkDestination, destinationLayout, ImageLayout.TransferDstOptimal, 0, 1, 0, 1);

        _gd.Vk.CmdResolveImage(
            _cb,
            vkSource.OptimalDeviceImage,
            ImageLayout.TransferSrcOptimal,
            vkDestination.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            1,
            in region);

        VkBarriers.Transition(_gd, _cb, vkSource, ImageLayout.TransferSrcOptimal, sourceLayout, 0, 1, 0, 1);
        VkBarriers.Transition(_gd, _cb, vkDestination, ImageLayout.TransferDstOptimal, destinationLayout, 0, 1, 0, 1);
    }
}
