using System;
using System.Diagnostics;

using Silk.NET.Vulkan;

using VkApi = Silk.NET.Vulkan.Vk;
using VkBufferHandle = Silk.NET.Vulkan.Buffer;
using VkImageHandle = Silk.NET.Vulkan.Image;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        VkBuffer stagingBuffer = GetStagingBuffer(sizeInBytes);
        _gd.UpdateBuffer(stagingBuffer, 0, source, sizeInBytes);
        CopyBuffer(stagingBuffer, 0, buffer, bufferOffsetInBytes, sizeInBytes);
        buffer.MarkInFlight(_gd, ExecutionId);
    }

    private protected override void CopyBufferCore(
        DeviceBuffer source,
        uint sourceOffset,
        DeviceBuffer destination,
        uint destinationOffset,
        uint sizeInBytes)
    {
        EnsureNoRenderPass();

        source.MarkInFlight(_gd, ExecutionId);
        destination.MarkInFlight(_gd, ExecutionId);
        destination.MarkContentChanged();

        VkBuffer srcVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(source);
        AddStagingResource(srcVkBuffer.RefCount);
        VkBuffer dstVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);
        AddStagingResource(dstVkBuffer.RefCount);

        BufferCopy region = new()
        {
            SrcOffset = sourceOffset,
            DstOffset = destinationOffset,
            Size = sizeInBytes
        };

        _gd.Vk.CmdCopyBuffer(_cb, srcVkBuffer.DeviceBuffer, dstVkBuffer.DeviceBuffer, 1, in region);
        _gd.Profiler?.Record(BufferOpBin.Copy, sizeInBytes);

        EmitPostCopyBufferBarrier(destination.Usage.HasFlag(BufferUsage.UniformBuffer));
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
        EnsureNoRenderPass();
        CopyTextureCore_VkCommandBuffer(
            _gd.Vk,
            _cb,
            source, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
            destination, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
            width, height, depth, layerCount);

        VkTexture srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
        AddStagingResource(srcVkTexture.RefCount);
        VkTexture dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);
        AddStagingResource(dstVkTexture.RefCount);
    }

    internal static void CopyTextureCore_VkCommandBuffer(
        VkApi vk,
        Silk.NET.Vulkan.CommandBuffer cb,
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
        VkTexture src = Util.AssertSubtype<Texture, VkTexture>(source);
        VkTexture dst = Util.AssertSubtype<Texture, VkTexture>(destination);

        bool sourceIsStaging = (source.Usage & TextureUsage.Staging) == TextureUsage.Staging;
        bool destIsStaging = (destination.Usage & TextureUsage.Staging) == TextureUsage.Staging;

        if (!sourceIsStaging && !destIsStaging)
        {
            CopyImageToImage(
                vk, cb,
                src, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
                dst, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
                width, height, depth, layerCount);
        }
        else if (sourceIsStaging && !destIsStaging)
        {
            CopyStagingToImage(
                vk, cb,
                src, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
                dst, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
                width, height, depth, layerCount);
        }
        else if (!sourceIsStaging && destIsStaging)
        {
            CopyImageToStaging(
                vk, cb,
                src, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
                dst, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
                width, height, depth, layerCount);
        }
        else
        {
            CopyStagingToStaging(
                vk, cb,
                src, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
                dst, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
                width, height, depth, layerCount);
        }
    }

    private static void CopyImageToImage(
        VkApi vk,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture src, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer,
        VkTexture dst, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer,
        uint width, uint height, uint depth, uint layerCount)
    {
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

        src.TransitionImageLayout(cb, srcMipLevel, 1, srcBaseArrayLayer, layerCount, ImageLayout.TransferSrcOptimal);
        dst.TransitionImageLayout(cb, dstMipLevel, 1, dstBaseArrayLayer, layerCount, ImageLayout.TransferDstOptimal);

        vk.CmdCopyImage(
            cb,
            src.OptimalDeviceImage,
            ImageLayout.TransferSrcOptimal,
            dst.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            1,
            in region);

        RestoreSampledLayout(cb, src, srcMipLevel, srcBaseArrayLayer, layerCount);
        RestoreSampledLayout(cb, dst, dstMipLevel, dstBaseArrayLayer, layerCount);
    }

    private static void CopyStagingToImage(
        VkApi vk,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture src, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer,
        VkTexture dst, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer,
        uint width, uint height, uint depth, uint layerCount)
    {
        SubresourceLayout srcLayout = src.GetSubresourceLayout(src.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
        dst.TransitionImageLayout(cb, dstMipLevel, 1, dstBaseArrayLayer, layerCount, ImageLayout.TransferDstOptimal);

        StagingImageLayout layout = new(src, srcMipLevel, src.Format, src.Format);

        BufferImageCopy region = new()
        {
            BufferOffset = layout.BufferOffset(srcLayout.Offset, srcX, srcY, srcZ),
            BufferRowLength = layout.RowLength,
            BufferImageHeight = layout.ImageHeight,
            ImageExtent = new Extent3D
            {
                Width = Math.Min(width, layout.MipWidth),
                Height = Math.Min(height, layout.MipHeight),
                Depth = depth
            },
            ImageOffset = new Offset3D { X = (int)dstX, Y = (int)dstY, Z = (int)dstZ },
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LayerCount = layerCount,
                MipLevel = dstMipLevel,
                BaseArrayLayer = dstBaseArrayLayer
            }
        };

        vk.CmdCopyBufferToImage(cb, src.StagingBuffer, dst.OptimalDeviceImage, ImageLayout.TransferDstOptimal, 1, in region);

        RestoreSampledLayout(cb, dst, dstMipLevel, dstBaseArrayLayer, layerCount);
    }

    private static void CopyImageToStaging(
        VkApi vk,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture src, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer,
        VkTexture dst, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer,
        uint width, uint height, uint depth, uint layerCount)
    {
        VkImageHandle srcImage = src.OptimalDeviceImage;
        src.TransitionImageLayout(cb, srcMipLevel, 1, srcBaseArrayLayer, layerCount, ImageLayout.TransferSrcOptimal);

        ImageAspectFlags aspect = (src.Usage & TextureUsage.DepthStencil) != 0
            ? ImageAspectFlags.DepthBit
            : ImageAspectFlags.ColorBit;

        StagingImageLayout layout = new(dst, dstMipLevel, src.Format, dst.Format);

        BufferImageCopy* layers = stackalloc BufferImageCopy[(int)layerCount];
        for (uint layer = 0; layer < layerCount; layer++)
        {
            SubresourceLayout dstLayout = dst.GetSubresourceLayout(
                dst.CalculateSubresource(dstMipLevel, dstBaseArrayLayer + layer));

            layers[layer] = new BufferImageCopy
            {
                BufferRowLength = layout.RowLength,
                BufferImageHeight = layout.ImageHeight,
                BufferOffset = layout.BufferOffset(dstLayout.Offset, dstX, dstY, dstZ),
                ImageExtent = new Extent3D { Width = width, Height = height, Depth = depth },
                ImageOffset = new Offset3D { X = (int)srcX, Y = (int)srcY, Z = (int)srcZ },
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = aspect,
                    LayerCount = 1,
                    MipLevel = srcMipLevel,
                    BaseArrayLayer = srcBaseArrayLayer + layer
                }
            };
        }

        vk.CmdCopyImageToBuffer(cb, srcImage, ImageLayout.TransferSrcOptimal, dst.StagingBuffer, layerCount, layers);

        RestoreSampledLayout(cb, src, srcMipLevel, srcBaseArrayLayer, layerCount);
    }

    private static void CopyStagingToStaging(
        VkApi vk,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture src, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer,
        VkTexture dst, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer,
        uint width, uint height, uint depth, uint layerCount)
    {
        VkBufferHandle srcBuffer = src.StagingBuffer;
        SubresourceLayout srcLayout = src.GetSubresourceLayout(src.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
        VkBufferHandle dstBuffer = dst.StagingBuffer;
        SubresourceLayout dstLayout = dst.GetSubresourceLayout(dst.CalculateSubresource(dstMipLevel, dstBaseArrayLayer));

        PixelFormat format = src.Format;
        uint blockSize = FormatHelpers.IsCompressedFormat(format) ? 4u : 1u;
        uint blockSizeInBytes = blockSize == 1
            ? format.GetSizeInBytes()
            : FormatHelpers.GetBlockSizeInBytes(format);
        uint rowSize = FormatHelpers.GetRowPitch(width, format);
        uint numRows = FormatHelpers.GetNumRows(height, format);

        uint zLimit = Math.Max(depth, layerCount);
        for (uint zz = 0; zz < zLimit; zz++)
        {
            for (uint row = 0; row < numRows; row++)
            {
                BufferCopy region = new()
                {
                    SrcOffset = srcLayout.Offset
                        + srcLayout.DepthPitch * (zz + srcZ)
                        + srcLayout.RowPitch * (row + srcY / blockSize)
                        + blockSizeInBytes * (srcX / blockSize),
                    DstOffset = dstLayout.Offset
                        + dstLayout.DepthPitch * (zz + dstZ)
                        + dstLayout.RowPitch * (row + dstY / blockSize)
                        + blockSizeInBytes * (dstX / blockSize),
                    Size = rowSize,
                };

                vk.CmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, in region);
            }
        }
    }

    private static void RestoreSampledLayout(
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture texture,
        uint mipLevel,
        uint baseArrayLayer,
        uint layerCount)
    {
        if ((texture.Usage & TextureUsage.Sampled) == 0) return;

        texture.TransitionImageLayout(cb, mipLevel, 1, baseArrayLayer, layerCount, ImageLayout.ShaderReadOnlyOptimal);
    }

    private static ImageAspectFlags CopyAspectMask(VkTexture texture)
    {
        if ((texture.Usage & TextureUsage.DepthStencil) == 0)
            return ImageAspectFlags.ColorBit;

        return FormatHelpers.IsStencilFormat(texture.Format)
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.DepthBit;
    }

    /// <summary>
    /// Buffer-side addressing for a staging texture subresource: the row/depth pitches a
    /// <see cref="BufferImageCopy"/> needs, and the byte offset of a texel within them.
    /// </summary>
    private readonly struct StagingImageLayout
    {
        public readonly uint MipWidth;
        public readonly uint MipHeight;
        public readonly uint RowLength;
        public readonly uint ImageHeight;
        public readonly uint RowPitch;
        public readonly uint DepthPitch;
        public readonly uint BlockSize;
        public readonly uint BlockSizeInBytes;

        /// <param name="mipSource">Texture whose mip dimensions define the buffer extent.</param>
        /// <param name="blockFormat">Format deciding whether addressing is per-block or per-texel.</param>
        /// <param name="pitchFormat">Format the pitches are computed in.</param>
        public StagingImageLayout(VkTexture mipSource, uint mipLevel, PixelFormat blockFormat, PixelFormat pitchFormat)
        {
            Util.GetMipDimensions(mipSource, mipLevel, out MipWidth, out MipHeight, out _);
            BlockSize = FormatHelpers.IsCompressedFormat(blockFormat) ? 4u : 1u;
            RowLength = Math.Max(MipWidth, BlockSize);
            ImageHeight = Math.Max(MipHeight, BlockSize);
            BlockSizeInBytes = BlockSize == 1
                ? pitchFormat.GetSizeInBytes()
                : FormatHelpers.GetBlockSizeInBytes(pitchFormat);
            RowPitch = FormatHelpers.GetRowPitch(RowLength, pitchFormat);
            DepthPitch = FormatHelpers.GetDepthPitch(RowPitch, ImageHeight, pitchFormat);
        }

        public readonly ulong BufferOffset(ulong baseOffset, uint x, uint y, uint z)
            => baseOffset
                + (z * DepthPitch)
                + ((y / BlockSize) * RowPitch)
                + ((x / BlockSize) * BlockSizeInBytes);
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        EnsureNoRenderPass();
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        AddStagingResource(vkTex.RefCount);

        GenerateMipmapsCore_VkCommandBuffer(_gd, _cb, vkTex);
    }

    internal static void GenerateMipmapsCore_VkCommandBuffer(VkGraphicsDevice gd, Silk.NET.Vulkan.CommandBuffer cb, VkTexture vkTex)
    {
        uint layerCount = vkTex.ArrayLayers;
        if ((vkTex.Usage & TextureUsage.Cubemap) != 0)
        {
            layerCount *= 6;
        }

        uint width = vkTex.Width;
        uint height = vkTex.Height;
        uint depth = vkTex.Depth;
        for (uint level = 1; level < vkTex.MipLevels; level++)
        {
            uint mipWidth = Math.Max(width >> 1, 1);
            uint mipHeight = Math.Max(height >> 1, 1);
            uint mipDepth = Math.Max(depth >> 1, 1);

            BlitMipLevel(gd, cb, vkTex, level, layerCount, width, height, depth, mipWidth, mipHeight, mipDepth);

            width = mipWidth;
            height = mipHeight;
            depth = mipDepth;
        }

        if ((vkTex.Usage & TextureUsage.Sampled) != 0)
        {
            vkTex.TransitionImageLayoutNonmatching(cb, 0, vkTex.MipLevels, 0, layerCount, ImageLayout.ShaderReadOnlyOptimal);
        }
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
        vkTex.TransitionImageLayoutNonmatching(cb, level - 1, 1, 0, layerCount, ImageLayout.TransferSrcOptimal);
        vkTex.TransitionImageLayoutNonmatching(cb, level, 1, 0, layerCount, ImageLayout.TransferDstOptimal);

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
        AddStagingResource(vkSource.RefCount);
        VkTexture vkDestination = Util.AssertSubtype<Texture, VkTexture>(destination);
        AddStagingResource(vkDestination.RefCount);

        ImageAspectFlags aspectFlags = ((source.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.ColorBit;
        ImageResolve region = new()
        {
            Extent = new Extent3D { Width = source.Width, Height = source.Height, Depth = source.Depth },
            SrcSubresource = new ImageSubresourceLayers { LayerCount = 1, AspectMask = aspectFlags },
            DstSubresource = new ImageSubresourceLayers { LayerCount = 1, AspectMask = aspectFlags }
        };

        vkSource.TransitionImageLayout(_cb, 0, 1, 0, 1, ImageLayout.TransferSrcOptimal);
        vkDestination.TransitionImageLayout(_cb, 0, 1, 0, 1, ImageLayout.TransferDstOptimal);

        _gd.Vk.CmdResolveImage(
            _cb,
            vkSource.OptimalDeviceImage,
            ImageLayout.TransferSrcOptimal,
            vkDestination.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            1,
            in region);

        RestoreSampledLayout(_cb, vkDestination, 0, 0, 1);
    }
}
