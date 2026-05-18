using System.Diagnostics;

namespace Prowl.Veldrid;

public abstract partial class CommandBuffer
{
#if VALIDATE_USAGE
    private DeviceBuffer _indexBuffer;
    private IndexFormat _indexFormat;
#endif

    [Conditional("VALIDATE_USAGE")]
    private void ClearCachedState_ClearIndexBuffer()
    {
#if VALIDATE_USAGE
        _indexBuffer = null;
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void SetVertexBuffer_CheckUsage(DeviceBuffer buffer)
    {
#if VALIDATE_USAGE
        if ((buffer.Usage & BufferUsage.VertexBuffer) == 0)
        {
            throw new VeldridException(
                $"Buffer cannot be bound as a vertex buffer because it was not created with BufferUsage.VertexBuffer.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void SetIndexBuffer_CheckUsageAndStore(DeviceBuffer buffer, IndexFormat format)
    {
#if VALIDATE_USAGE
        if ((buffer.Usage & BufferUsage.IndexBuffer) == 0)
        {
            throw new VeldridException(
                $"Buffer cannot be bound as an index buffer because it was not created with BufferUsage.IndexBuffer.");
        }
        _indexBuffer = buffer;
        _indexFormat = format;
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void SetGraphicsResourceSet_CheckLayoutCompatibility(uint slot, ResourceSet rs)
    {
#if VALIDATE_USAGE
        if (_graphicsPipeline == null)
        {
            throw new VeldridException($"A graphics Pipeline must be active before {nameof(SetGraphicsResourceSet)} can be called.");
        }

        int layoutsCount = _graphicsPipeline.ResourceLayouts.Length;
        if (layoutsCount <= slot)
        {
            throw new VeldridException(
                $"Failed to bind ResourceSet to slot {slot}. The active graphics Pipeline only contains {layoutsCount} ResourceLayouts.");
        }

        ResourceLayout layout = _graphicsPipeline.ResourceLayouts[slot];
        int pipelineLength = layout.Description.Elements.Length;
        ResourceLayoutDescription layoutDesc = rs.Layout.Description;
        int setLength = layoutDesc.Elements.Length;
        if (pipelineLength != setLength)
        {
            throw new VeldridException($"Failed to bind ResourceSet to slot {slot}. The number of resources in the ResourceSet ({setLength}) does not match the number expected by the active Pipeline ({pipelineLength}).");
        }

        for (int i = 0; i < pipelineLength; i++)
        {
            ResourceKind pipelineKind = layout.Description.Elements[i].Kind;
            ResourceKind setKind = layoutDesc.Elements[i].Kind;
            if (pipelineKind != setKind)
            {
                throw new VeldridException(
                    $"Failed to bind ResourceSet to slot {slot}. Resource element {i} was of the incorrect type. The bound Pipeline expects {pipelineKind}, but the ResourceSet contained {setKind}.");
            }
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void SetComputeResourceSet_CheckLayoutCompatibility(uint slot, ResourceSet rs)
    {
#if VALIDATE_USAGE
        if (_computePipeline == null)
        {
            throw new VeldridException($"A compute Pipeline must be active before {nameof(SetComputeResourceSet)} can be called.");
        }

        int layoutsCount = _computePipeline.ResourceLayouts.Length;
        if (layoutsCount <= slot)
        {
            throw new VeldridException(
                $"Failed to bind ResourceSet to slot {slot}. The active compute Pipeline only contains {layoutsCount} ResourceLayouts.");
        }

        ResourceLayout layout = _computePipeline.ResourceLayouts[slot];
        int pipelineLength = layout.Description.Elements.Length;
        int setLength = rs.Layout.Description.Elements.Length;
        if (pipelineLength != setLength)
        {
            throw new VeldridException($"Failed to bind ResourceSet to slot {slot}. The number of resources in the ResourceSet ({setLength}) does not match the number expected by the active Pipeline ({pipelineLength}).");
        }

        for (int i = 0; i < pipelineLength; i++)
        {
            ResourceKind pipelineKind = layout.Description.Elements[i].Kind;
            ResourceKind setKind = rs.Layout.Description.Elements[i].Kind;
            if (pipelineKind != setKind)
            {
                throw new VeldridException(
                    $"Failed to bind ResourceSet to slot {slot}. Resource element {i} was of the incorrect type. The bound Pipeline expects {pipelineKind}, but the ResourceSet contained {setKind}.");
            }
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void ClearColorTarget_CheckFramebuffer(uint index)
    {
#if VALIDATE_USAGE
        if (_framebuffer == null)
        {
            throw new VeldridException($"Cannot use ClearColorTarget. There is no Framebuffer bound.");
        }
        if (_framebuffer.ColorTargets.Count <= index)
        {
            throw new VeldridException(
                "ClearColorTarget index must be less than the current Framebuffer's color target count.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void ClearDepthStencil_CheckFramebuffer()
    {
#if VALIDATE_USAGE
        if (_framebuffer == null)
        {
            throw new VeldridException($"Cannot use ClearDepthStencil. There is no Framebuffer bound.");
        }
        if (_framebuffer.DepthTarget == null)
        {
            throw new VeldridException(
                "The current Framebuffer has no depth target, so ClearDepthStencil cannot be used.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void DrawIndexed_CheckBaseVertexInstance(int vertexOffset, uint instanceStart)
    {
#if VALIDATE_USAGE
        if (!_features.DrawBaseVertex && vertexOffset != 0)
        {
            throw new VeldridException("Drawing with a non-zero base vertex is not supported on this device.");
        }
        if (!_features.DrawBaseInstance && instanceStart != 0)
        {
            throw new VeldridException("Drawing with a non-zero base instance is not supported on this device.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void DrawIndirect_CheckOffset(uint offset)
    {
#if VALIDATE_USAGE
        if ((offset % 4) != 0)
        {
            throw new VeldridException($"{nameof(offset)} must be a multiple of 4.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void DrawIndirect_CheckSupport()
    {
#if VALIDATE_USAGE
        if (!_features.DrawIndirect)
        {
            throw new VeldridException($"Indirect drawing is not supported by this device.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void DrawIndirect_CheckBuffer(DeviceBuffer indirectBuffer)
    {
#if VALIDATE_USAGE
        if ((indirectBuffer.Usage & BufferUsage.IndirectBuffer) != BufferUsage.IndirectBuffer)
        {
            throw new VeldridException(
                $"{nameof(indirectBuffer)} parameter must have been created with BufferUsage.IndirectBuffer. Instead, it was {indirectBuffer.Usage}.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void DrawIndirect_CheckStride(uint stride, int argumentSize)
    {
#if VALIDATE_USAGE
        if (stride < argumentSize || ((stride % 4) != 0))
        {
            throw new VeldridException(
                $"{nameof(stride)} parameter must be a multiple of 4, and must be larger than the size of the corresponding argument structure.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void ResolveTexture_CheckSampleCounts(Texture source, Texture destination)
    {
#if VALIDATE_USAGE
        if (source.SampleCount == TextureSampleCount.Count1)
        {
            throw new VeldridException(
                $"The {nameof(source)} parameter of {nameof(ResolveTexture)} must be a multisample texture.");
        }
        if (destination.SampleCount != TextureSampleCount.Count1)
        {
            throw new VeldridException(
                $"The {nameof(destination)} parameter of {nameof(ResolveTexture)} must be a non-multisample texture. Instead, it is a texture with {FormatHelpers.GetSampleCountUInt32(source.SampleCount)} samples.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void CopyTexture_CheckCompatibilityAll(Texture source, Texture destination, uint effectiveSrcArrayLayers)
    {
#if VALIDATE_USAGE
        uint effectiveDstArrayLayers = (destination.Usage & TextureUsage.Cubemap) != 0
            ? destination.ArrayLayers * 6
            : destination.ArrayLayers;
        if (effectiveSrcArrayLayers != effectiveDstArrayLayers || source.MipLevels != destination.MipLevels
            || source.SampleCount != destination.SampleCount || source.Width != destination.Width
            || source.Height != destination.Height || source.Depth != destination.Depth
            || source.Format != destination.Format)
        {
            throw new VeldridException("Source and destination Textures are not compatible to be copied.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void CopyTexture_CheckCompatibilityForSubresource(Texture source, Texture destination, uint mipLevel, uint arrayLayer)
    {
#if VALIDATE_USAGE
        uint effectiveSrcArrayLayers = (source.Usage & TextureUsage.Cubemap) != 0
            ? source.ArrayLayers * 6
            : source.ArrayLayers;
        uint effectiveDstArrayLayers = (destination.Usage & TextureUsage.Cubemap) != 0
            ? destination.ArrayLayers * 6
            : destination.ArrayLayers;
        if (source.SampleCount != destination.SampleCount || source.Width != destination.Width
            || source.Height != destination.Height || source.Depth != destination.Depth
            || source.Format != destination.Format)
        {
            throw new VeldridException("Source and destination Textures are not compatible to be copied.");
        }
        if (mipLevel >= source.MipLevels || mipLevel >= destination.MipLevels || arrayLayer >= effectiveSrcArrayLayers || arrayLayer >= effectiveDstArrayLayers)
        {
            throw new VeldridException(
                $"{nameof(mipLevel)} and {nameof(arrayLayer)} must be less than the given Textures' mip level count and array layer count.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void CopyTexture_CheckRegion(
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
#if VALIDATE_USAGE
        if (width == 0 || height == 0 || depth == 0)
        {
            throw new VeldridException($"The given copy region is empty.");
        }
        if (layerCount == 0)
        {
            throw new VeldridException($"{nameof(layerCount)} must be greater than 0.");
        }
        Util.GetMipDimensions(source, srcMipLevel, out uint srcWidth, out uint srcHeight, out uint srcDepth);
        uint srcBlockSize = FormatHelpers.IsCompressedFormat(source.Format) ? 4u : 1u;
        uint roundedSrcWidth = (srcWidth + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
        uint roundedSrcHeight = (srcHeight + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
        if (srcX + width > roundedSrcWidth || srcY + height > roundedSrcHeight || srcZ + depth > srcDepth)
        {
            throw new VeldridException($"The given copy region is not valid for the source Texture.");
        }
        Util.GetMipDimensions(destination, dstMipLevel, out uint dstWidth, out uint dstHeight, out uint dstDepth);
        uint dstBlockSize = FormatHelpers.IsCompressedFormat(destination.Format) ? 4u : 1u;
        uint roundedDstWidth = (dstWidth + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
        uint roundedDstHeight = (dstHeight + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
        if (dstX + width > roundedDstWidth || dstY + height > roundedDstHeight || dstZ + depth > dstDepth)
        {
            throw new VeldridException($"The given copy region is not valid for the destination Texture.");
        }
        if (srcMipLevel >= source.MipLevels)
        {
            throw new VeldridException($"{nameof(srcMipLevel)} must be less than the number of mip levels in the source Texture.");
        }
        uint effectiveSrcArrayLayers = (source.Usage & TextureUsage.Cubemap) != 0
            ? source.ArrayLayers * 6
            : source.ArrayLayers;
        if (srcBaseArrayLayer + layerCount > effectiveSrcArrayLayers)
        {
            throw new VeldridException($"An invalid mip range was given for the source Texture.");
        }
        if (dstMipLevel >= destination.MipLevels)
        {
            throw new VeldridException($"{nameof(dstMipLevel)} must be less than the number of mip levels in the destination Texture.");
        }
        uint effectiveDstArrayLayers = (destination.Usage & TextureUsage.Cubemap) != 0
            ? destination.ArrayLayers * 6
            : destination.ArrayLayers;
        if (dstBaseArrayLayer + layerCount > effectiveDstArrayLayers)
        {
            throw new VeldridException($"An invalid mip range was given for the destination Texture.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void DrawIndexed_CheckIndexBuffer(uint indexCount)
    {
#if VALIDATE_USAGE
        if (_indexBuffer == null)
        {
            throw new VeldridException($"An index buffer must be bound before {nameof(CommandBuffer)}.{nameof(DrawIndexed)} can be called.");
        }

        uint indexFormatSize = _indexFormat == IndexFormat.UInt16 ? 2u : 4u;
        uint bytesNeeded = indexCount * indexFormatSize;
        if (_indexBuffer.SizeInBytes < bytesNeeded)
        {
            throw new VeldridException(
                $"The active index buffer does not contain enough data to satisfy the given draw command. {bytesNeeded} bytes are needed, but the buffer only contains {_indexBuffer.SizeInBytes}.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void Draw_PreDrawValidation()
    {
#if VALIDATE_USAGE
        if (_graphicsPipeline == null)
        {
            throw new VeldridException($"A graphics {nameof(Pipeline)} must be set in order to issue draw commands.");
        }
        if (_framebuffer == null)
        {
            throw new VeldridException($"A {nameof(Framebuffer)} must be set in order to issue draw commands.");
        }
        if (!_graphicsPipeline.GraphicsOutputDescription.Equals(_framebuffer.OutputDescription))
        {
            throw new VeldridException($"The {nameof(OutputDescription)} of the current graphics {nameof(Pipeline)} is not compatible with the current {nameof(Framebuffer)}.");
        }
#endif
    }
}
