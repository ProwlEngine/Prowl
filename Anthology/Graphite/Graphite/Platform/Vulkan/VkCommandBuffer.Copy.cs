using System;
using System.Diagnostics;

using Silk.NET.Vulkan;

using VkApi = Silk.NET.Vulkan.Vk;
using VkBufferHandle = Silk.NET.Vulkan.Buffer;
using VkImageHandle = Silk.NET.Vulkan.Image;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
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
        VkTexture srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
        VkTexture dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);

        bool sourceIsStaging = (source.Usage & TextureUsage.Staging) == TextureUsage.Staging;
        bool destIsStaging = (destination.Usage & TextureUsage.Staging) == TextureUsage.Staging;

        if (!sourceIsStaging && !destIsStaging)
        {
            ImageSubresourceLayers srcSubresource = new()
            {
                AspectMask = CopyAspectMask(srcVkTexture),
                LayerCount = layerCount,
                MipLevel = srcMipLevel,
                BaseArrayLayer = srcBaseArrayLayer
            };

            ImageSubresourceLayers dstSubresource = new()
            {
                AspectMask = CopyAspectMask(dstVkTexture),
                LayerCount = layerCount,
                MipLevel = dstMipLevel,
                BaseArrayLayer = dstBaseArrayLayer
            };

            ImageCopy region = new()
            {
                SrcOffset = new Offset3D { X = (int)srcX, Y = (int)srcY, Z = (int)srcZ },
                DstOffset = new Offset3D { X = (int)dstX, Y = (int)dstY, Z = (int)dstZ },
                SrcSubresource = srcSubresource,
                DstSubresource = dstSubresource,
                Extent = new Extent3D { Width = width, Height = height, Depth = depth }
            };

            srcVkTexture.TransitionImageLayout(
                cb,
                srcMipLevel,
                1,
                srcBaseArrayLayer,
                layerCount,
                ImageLayout.TransferSrcOptimal);

            dstVkTexture.TransitionImageLayout(
                cb,
                dstMipLevel,
                1,
                dstBaseArrayLayer,
                layerCount,
                ImageLayout.TransferDstOptimal);

            vk.CmdCopyImage(
                cb,
                srcVkTexture.OptimalDeviceImage,
                ImageLayout.TransferSrcOptimal,
                dstVkTexture.OptimalDeviceImage,
                ImageLayout.TransferDstOptimal,
                1,
                in region);

            if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    ImageLayout.ShaderReadOnlyOptimal);
            }

            if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    ImageLayout.ShaderReadOnlyOptimal);
            }
        }
        else if (sourceIsStaging && !destIsStaging)
        {
            VkBufferHandle srcBuffer = srcVkTexture.StagingBuffer;
            SubresourceLayout srcLayout = srcVkTexture.GetSubresourceLayout(
                srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
            VkImageHandle dstImage = dstVkTexture.OptimalDeviceImage;
            dstVkTexture.TransitionImageLayout(
                cb,
                dstMipLevel,
                1,
                dstBaseArrayLayer,
                layerCount,
                ImageLayout.TransferDstOptimal);

            ImageSubresourceLayers dstSubresource = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LayerCount = layerCount,
                MipLevel = dstMipLevel,
                BaseArrayLayer = dstBaseArrayLayer
            };

            Util.GetMipDimensions(srcVkTexture, srcMipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
            uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
            uint bufferRowLength = Math.Max(mipWidth, blockSize);
            uint bufferImageHeight = Math.Max(mipHeight, blockSize);
            uint compressedX = srcX / blockSize;
            uint compressedY = srcY / blockSize;
            uint blockSizeInBytes = blockSize == 1
                ? srcVkTexture.Format.GetSizeInBytes()
                : FormatHelpers.GetBlockSizeInBytes(srcVkTexture.Format);
            uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, srcVkTexture.Format);
            uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, srcVkTexture.Format);

            uint copyWidth = Math.Min(width, mipWidth);
            uint copyheight = Math.Min(height, mipHeight);

            BufferImageCopy regions = new()
            {
                BufferOffset = srcLayout.Offset
                    + (srcZ * depthPitch)
                    + (compressedY * rowPitch)
                    + (compressedX * blockSizeInBytes),
                BufferRowLength = bufferRowLength,
                BufferImageHeight = bufferImageHeight,
                ImageExtent = new Extent3D { Width = copyWidth, Height = copyheight, Depth = depth },
                ImageOffset = new Offset3D { X = (int)dstX, Y = (int)dstY, Z = (int)dstZ },
                ImageSubresource = dstSubresource
            };

            vk.CmdCopyBufferToImage(cb, srcBuffer, dstImage, ImageLayout.TransferDstOptimal, 1, in regions);

            if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    ImageLayout.ShaderReadOnlyOptimal);
            }
        }
        else if (!sourceIsStaging && destIsStaging)
        {
            VkImageHandle srcImage = srcVkTexture.OptimalDeviceImage;
            srcVkTexture.TransitionImageLayout(
                cb,
                srcMipLevel,
                1,
                srcBaseArrayLayer,
                layerCount,
                ImageLayout.TransferSrcOptimal);

            VkBufferHandle dstBuffer = dstVkTexture.StagingBuffer;

            ImageAspectFlags aspect = (srcVkTexture.Usage & TextureUsage.DepthStencil) != 0
                ? ImageAspectFlags.DepthBit
                : ImageAspectFlags.ColorBit;

            Util.GetMipDimensions(dstVkTexture, dstMipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
            uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
            uint bufferRowLength = Math.Max(mipWidth, blockSize);
            uint bufferImageHeight = Math.Max(mipHeight, blockSize);
            uint compressedDstX = dstX / blockSize;
            uint compressedDstY = dstY / blockSize;
            uint blockSizeInBytes = blockSize == 1
                ? dstVkTexture.Format.GetSizeInBytes()
                : FormatHelpers.GetBlockSizeInBytes(dstVkTexture.Format);
            uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, dstVkTexture.Format);
            uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, dstVkTexture.Format);

            BufferImageCopy* layers = stackalloc BufferImageCopy[(int)layerCount];
            for (uint layer = 0; layer < layerCount; layer++)
            {
                SubresourceLayout dstLayout = dstVkTexture.GetSubresourceLayout(
                    dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer + layer));

                ImageSubresourceLayers srcSubresource = new()
                {
                    AspectMask = aspect,
                    LayerCount = 1,
                    MipLevel = srcMipLevel,
                    BaseArrayLayer = srcBaseArrayLayer + layer
                };

                BufferImageCopy region = new()
                {
                    BufferRowLength = bufferRowLength,
                    BufferImageHeight = bufferImageHeight,
                    BufferOffset = dstLayout.Offset
                        + (dstZ * depthPitch)
                        + (compressedDstY * rowPitch)
                        + (compressedDstX * blockSizeInBytes),
                    ImageExtent = new Extent3D { Width = width, Height = height, Depth = depth },
                    ImageOffset = new Offset3D { X = (int)srcX, Y = (int)srcY, Z = (int)srcZ },
                    ImageSubresource = srcSubresource
                };

                layers[layer] = region;
            }

            vk.CmdCopyImageToBuffer(cb, srcImage, ImageLayout.TransferSrcOptimal, dstBuffer, layerCount, layers);

            if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    ImageLayout.ShaderReadOnlyOptimal);
            }
        }
        else
        {
            Debug.Assert(sourceIsStaging && destIsStaging);
            VkBufferHandle srcBuffer = srcVkTexture.StagingBuffer;
            SubresourceLayout srcLayout = srcVkTexture.GetSubresourceLayout(
                srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
            VkBufferHandle dstBuffer = dstVkTexture.StagingBuffer;
            SubresourceLayout dstLayout = dstVkTexture.GetSubresourceLayout(
                dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer));

            uint zLimit = Math.Max(depth, layerCount);
            if (!FormatHelpers.IsCompressedFormat(source.Format))
            {
                uint pixelSize = srcVkTexture.Format.GetSizeInBytes();
                for (uint zz = 0; zz < zLimit; zz++)
                {
                    for (uint yy = 0; yy < height; yy++)
                    {
                        BufferCopy region = new()
                        {
                            SrcOffset = srcLayout.Offset
                                + srcLayout.DepthPitch * (zz + srcZ)
                                + srcLayout.RowPitch * (yy + srcY)
                                + pixelSize * srcX,
                            DstOffset = dstLayout.Offset
                                + dstLayout.DepthPitch * (zz + dstZ)
                                + dstLayout.RowPitch * (yy + dstY)
                                + pixelSize * dstX,
                            Size = width * pixelSize,
                        };

                        vk.CmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, in region);
                    }
                }
            }
            else // IsCompressedFormat
            {
                uint denseRowSize = FormatHelpers.GetRowPitch(width, source.Format);
                uint numRows = FormatHelpers.GetNumRows(height, source.Format);
                uint compressedSrcX = srcX / 4;
                uint compressedSrcY = srcY / 4;
                uint compressedDstX = dstX / 4;
                uint compressedDstY = dstY / 4;
                uint blockSizeInBytes = FormatHelpers.GetBlockSizeInBytes(source.Format);

                for (uint zz = 0; zz < zLimit; zz++)
                {
                    for (uint row = 0; row < numRows; row++)
                    {
                        BufferCopy region = new()
                        {
                            SrcOffset = srcLayout.Offset
                                + srcLayout.DepthPitch * (zz + srcZ)
                                + srcLayout.RowPitch * (row + compressedSrcY)
                                + blockSizeInBytes * compressedSrcX,
                            DstOffset = dstLayout.Offset
                                + dstLayout.DepthPitch * (zz + dstZ)
                                + dstLayout.RowPitch * (row + compressedDstY)
                                + blockSizeInBytes * compressedDstX,
                            Size = denseRowSize,
                        };

                        vk.CmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, in region);
                    }
                }

            }
        }
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
        AddStagingResource(vkTex.RefCount);

        GenerateMipmapsCore_VkCommandBuffer(_gd, _cb, vkTex);
    }

    internal static unsafe void GenerateMipmapsCore_VkCommandBuffer(VkGraphicsDevice gd, Silk.NET.Vulkan.CommandBuffer cb, VkTexture vkTex)
    {
        uint layerCount = vkTex.ArrayLayers;
        if ((vkTex.Usage & TextureUsage.Cubemap) != 0)
        {
            layerCount *= 6;
        }

        ImageBlit region;

        uint width = vkTex.Width;
        uint height = vkTex.Height;
        uint depth = vkTex.Depth;
        for (uint level = 1; level < vkTex.MipLevels; level++)
        {
            vkTex.TransitionImageLayoutNonmatching(cb, level - 1, 1, 0, layerCount, ImageLayout.TransferSrcOptimal);
            vkTex.TransitionImageLayoutNonmatching(cb, level, 1, 0, layerCount, ImageLayout.TransferDstOptimal);

            VkImageHandle deviceImage = vkTex.OptimalDeviceImage;
            uint mipWidth = Math.Max(width >> 1, 1);
            uint mipHeight = Math.Max(height >> 1, 1);
            uint mipDepth = Math.Max(depth >> 1, 1);

            region.SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                LayerCount = layerCount,
                MipLevel = level - 1
            };
            region.SrcOffsets = default;
            region.SrcOffsets.Element0 = new Offset3D();
            region.SrcOffsets.Element1 = new Offset3D { X = (int)width, Y = (int)height, Z = (int)depth };
            region.DstOffsets = default;
            region.DstOffsets.Element0 = new Offset3D();

            region.DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                LayerCount = layerCount,
                MipLevel = level
            };

            region.DstOffsets.Element1 = new Offset3D { X = (int)mipWidth, Y = (int)mipHeight, Z = (int)mipDepth };
            gd.Vk.CmdBlitImage(
                cb,
                deviceImage, ImageLayout.TransferSrcOptimal,
                deviceImage, ImageLayout.TransferDstOptimal,
                1, &region,
                gd.GetFormatFilter(vkTex.VkFormat));

            width = mipWidth;
            height = mipHeight;
            depth = mipDepth;
        }

        if ((vkTex.Usage & TextureUsage.Sampled) != 0)
        {
            vkTex.TransitionImageLayoutNonmatching(cb, 0, vkTex.MipLevels, 0, layerCount, ImageLayout.ShaderReadOnlyOptimal);
        }
    }
}
