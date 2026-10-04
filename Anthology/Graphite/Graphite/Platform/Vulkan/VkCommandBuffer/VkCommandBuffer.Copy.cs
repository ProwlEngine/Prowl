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
        VkBuffer dstVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);

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
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        VkBuffer staging = GetStagingBuffer(sizeInBytes);
        _gd.UpdateBuffer(staging, 0, source, sizeInBytes);

        if (vkTex.IsStaging)
        {
            CopyToStagingTexture(staging, vkTex, x, y, z, width, height, depth, mipLevel, arrayLayer);
            _gd.Profiler?.Record(BufferOpBin.Update, sizeInBytes);
            return;
        }

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
        VkTexture srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
        VkTexture dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);
        CopyTextureCore_VkCommandBuffer(
            _gd,
            _cb,
            source, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
            destination, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
            width, height, depth, layerCount,
            VkBarriers.CurrentLayout(this, srcVkTexture),
            VkBarriers.CurrentLayout(this, dstVkTexture));

    }

    internal static void CopyTextureCore_VkCommandBuffer(
        VkGraphicsDevice gd,
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
        uint layerCount,
        ImageLayout srcLayout,
        ImageLayout dstLayout)
    {
        VkApi vk = gd.Vk;
        VkTexture src = Util.AssertSubtype<Texture, VkTexture>(source);
        VkTexture dst = Util.AssertSubtype<Texture, VkTexture>(destination);

        bool sourceIsStaging = (source.Usage & TextureUsage.Staging) == TextureUsage.Staging;
        bool destIsStaging = (destination.Usage & TextureUsage.Staging) == TextureUsage.Staging;

        if (!sourceIsStaging && !destIsStaging)
        {
            CopyImageToImage(
                gd, cb,
                src, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer, srcLayout,
                dst, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer, dstLayout,
                width, height, depth, layerCount);
        }
        else if (sourceIsStaging && !destIsStaging)
        {
            CopyStagingToImage(
                gd, cb,
                src, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
                dst, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer, dstLayout,
                width, height, depth, layerCount);
        }
        else if (!sourceIsStaging && destIsStaging)
        {
            CopyImageToStaging(
                gd, cb,
                src, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer, srcLayout,
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
        VkGraphicsDevice gd,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture src, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer, ImageLayout srcLayout,
        VkTexture dst, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer, ImageLayout dstLayout,
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

        VkBarriers.Transition(gd, cb, src, srcLayout, ImageLayout.TransferSrcOptimal, srcMipLevel, 1, srcBaseArrayLayer, layerCount);
        VkBarriers.Transition(gd, cb, dst, dstLayout, ImageLayout.TransferDstOptimal, dstMipLevel, 1, dstBaseArrayLayer, layerCount);

        gd.Vk.CmdCopyImage(
            cb,
            src.OptimalDeviceImage,
            ImageLayout.TransferSrcOptimal,
            dst.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            1,
            in region);

        VkBarriers.Transition(gd, cb, src, ImageLayout.TransferSrcOptimal, srcLayout, srcMipLevel, 1, srcBaseArrayLayer, layerCount);
        VkBarriers.Transition(gd, cb, dst, ImageLayout.TransferDstOptimal, dstLayout, dstMipLevel, 1, dstBaseArrayLayer, layerCount);
    }

    private static void CopyStagingToImage(
        VkGraphicsDevice gd,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture src, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer,
        VkTexture dst, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer, ImageLayout dstLayout,
        uint width, uint height, uint depth, uint layerCount)
    {
        SubresourceLayout srcLayout = src.GetSubresourceLayout(src.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
        VkBarriers.Transition(gd, cb, dst, dstLayout, ImageLayout.TransferDstOptimal, dstMipLevel, 1, dstBaseArrayLayer, layerCount);

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

        gd.Vk.CmdCopyBufferToImage(cb, src.StagingBuffer, dst.OptimalDeviceImage, ImageLayout.TransferDstOptimal, 1, in region);

        VkBarriers.Transition(gd, cb, dst, ImageLayout.TransferDstOptimal, dstLayout, dstMipLevel, 1, dstBaseArrayLayer, layerCount);
    }

    private static void CopyImageToStaging(
        VkGraphicsDevice gd,
        Silk.NET.Vulkan.CommandBuffer cb,
        VkTexture src, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer, ImageLayout srcLayout,
        VkTexture dst, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer,
        uint width, uint height, uint depth, uint layerCount)
    {
        VkImageHandle srcImage = src.OptimalDeviceImage;
        VkBarriers.Transition(gd, cb, src, srcLayout, ImageLayout.TransferSrcOptimal, srcMipLevel, 1, srcBaseArrayLayer, layerCount);

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

        gd.Vk.CmdCopyImageToBuffer(cb, srcImage, ImageLayout.TransferSrcOptimal, dst.StagingBuffer, layerCount, layers);

        VkBarriers.Transition(gd, cb, src, ImageLayout.TransferSrcOptimal, srcLayout, srcMipLevel, 1, srcBaseArrayLayer, layerCount);
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
