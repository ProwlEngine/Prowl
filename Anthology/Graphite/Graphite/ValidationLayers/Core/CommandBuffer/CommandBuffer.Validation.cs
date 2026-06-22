using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
#if VALIDATE_USAGE
    private readonly List<DeviceBuffer> _referencedBuffers = new();
#endif

    [Conditional("VALIDATE_USAGE")]
    private protected void TrackReferencedBuffer(DeviceBuffer buffer)
    {
#if VALIDATE_USAGE
        if (buffer != null)
            _referencedBuffers.Add(buffer);
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private protected void SetProperties_TrackReferencedBuffers(PropertySet properties)
    {
#if VALIDATE_USAGE
        foreach (KeyValuePair<PropertyID, PropertyEntry> entry in properties.Entries)
        {
            DeviceBufferRange? range = entry.Value.Buffer;
            if (range.HasValue && range.Value.Buffer != null)
                _referencedBuffers.Add(range.Value.Buffer);
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private protected void ClearCachedState_ClearReferencedBuffers()
    {
#if VALIDATE_USAGE
        _referencedBuffers.Clear();
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    internal void SubmitCommands_MarkReferencedBuffersInFlight(GraphicsDevice device, ulong frameId)
    {
#if VALIDATE_USAGE
        foreach (DeviceBuffer buffer in _referencedBuffers)
            buffer.SubmitCommands_MarkInFlight(device, frameId);
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void SetVertexSource_CheckNonNull(IVertexSource source)
    {
#if VALIDATE_USAGE
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source),
                "IVertexSource must be non-null. Bind an empty implementation if a vertex-source-free draw is intended.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private protected void CheckVertexBindingUsage(in VertexBinding binding, uint layoutSlot)
    {
#if VALIDATE_USAGE
        if (binding.Buffer == null)
        {
            throw new RenderException(
                $"IVertexSource.ResolveSlot returned a null Buffer for layout slot {layoutSlot}.");
        }
        if ((binding.Buffer.Usage & BufferUsage.VertexBuffer) == 0)
        {
            throw new RenderException(
                $"Buffer for layout slot {layoutSlot} cannot be bound as a vertex buffer because it was not created with BufferUsage.VertexBuffer.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private protected void CheckIndexBufferUsage(DeviceBuffer buffer)
    {
#if VALIDATE_USAGE
        if (buffer == null)
        {
            throw new RenderException(
                "IVertexSource.TryGetIndexBuffer returned true but the index buffer is null.");
        }
        if ((buffer.Usage & BufferUsage.IndexBuffer) == 0)
        {
            throw new RenderException(
                "Buffer cannot be bound as an index buffer because it was not created with BufferUsage.IndexBuffer.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void ClearColorTarget_CheckFramebuffer(uint index)
    {
#if VALIDATE_USAGE
        CheckFramebuffer(nameof(ClearColorTarget));
        if (_framebuffer!.ColorTargets.Count <= index)
        {
            throw new RenderException(
                $"{nameof(ClearColorTarget)} index must be less than the current Framebuffer's color target count.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void ClearDepthStencil_CheckFramebuffer()
    {
#if VALIDATE_USAGE
        CheckFramebuffer(nameof(ClearDepthStencil));
        if (_framebuffer!.DepthTarget == null)
        {
            throw new RenderException(
                $"The current Framebuffer has no depth target, so {nameof(ClearDepthStencil)} cannot be used.");
        }
#endif
    }


    [Conditional("VALIDATE_USAGE")]
    private void CheckFramebuffer(string name)
    {
#if VALIDATE_USAGE
        if (_framebuffer == null)
            throw new RenderException($"Cannot use {name}. There is no Framebuffer bound.");
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void DrawIndexed_CheckBaseVertexInstance(int vertexOffset, uint instanceStart)
    {
#if VALIDATE_USAGE
        if (!_features.DrawBaseVertex && vertexOffset != 0)
        {
            throw new RenderException("Drawing with a non-zero base vertex is not supported on this device.");
        }
        if (!_features.DrawBaseInstance && instanceStart != 0)
        {
            throw new RenderException("Drawing with a non-zero base instance is not supported on this device.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void DrawIndirect_CheckOffset(uint offset)
    {
#if VALIDATE_USAGE
        if ((offset % 4) != 0)
        {
            throw new RenderException($"{nameof(offset)} must be a multiple of 4.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void DrawIndirect_CheckSupport()
    {
#if VALIDATE_USAGE
        if (!_features.DrawIndirect)
        {
            throw new RenderException($"Indirect drawing is not supported by this device.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void DrawIndirect_CheckBuffer(DeviceBuffer indirectBuffer)
    {
#if VALIDATE_USAGE
        if ((indirectBuffer.Usage & BufferUsage.IndirectBuffer) != BufferUsage.IndirectBuffer)
        {
            throw new RenderException(
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
            throw new RenderException(
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
            throw new RenderException(
                $"The {nameof(source)} parameter of {nameof(ResolveTexture)} must be a multisample texture.");
        }
        if (destination.SampleCount != TextureSampleCount.Count1)
        {
            throw new RenderException(
                $"The {nameof(destination)} parameter of {nameof(ResolveTexture)} must be a non-multisample texture. Instead, it is a texture with {FormatHelpers.GetSampleCountUInt32(source.SampleCount)} samples.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void CopyTexture_CheckDimensionsCompatible(Texture source, Texture destination)
    {
#if VALIDATE_USAGE
        if (source.SampleCount != destination.SampleCount || source.Width != destination.Width
            || source.Height != destination.Height || source.Depth != destination.Depth
            || source.Format != destination.Format)
        {
            throw new RenderException($"Source and destination Textures are not compatible to be copied in {nameof(CopyTexture)}.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void CopyTexture_CheckCompatibilityAll(Texture source, Texture destination, uint effectiveSrcArrayLayers)
    {
#if VALIDATE_USAGE
        uint effectiveDstArrayLayers = GetEffectiveArrayLayers(destination);
        if (effectiveSrcArrayLayers != effectiveDstArrayLayers || source.MipLevels != destination.MipLevels)
        {
            throw new RenderException($"Source and destination Textures are not compatible to be copied in {nameof(CopyTexture)}.");
        }
        CopyTexture_CheckDimensionsCompatible(source, destination);
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void CopyTexture_CheckCompatibilityForSubresource(Texture source, Texture destination, uint mipLevel, uint arrayLayer)
    {
#if VALIDATE_USAGE
        uint effectiveSrcArrayLayers = GetEffectiveArrayLayers(source);
        uint effectiveDstArrayLayers = GetEffectiveArrayLayers(destination);
        CopyTexture_CheckDimensionsCompatible(source, destination);
        if (mipLevel >= source.MipLevels || mipLevel >= destination.MipLevels || arrayLayer >= effectiveSrcArrayLayers || arrayLayer >= effectiveDstArrayLayers)
        {
            throw new RenderException(
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
            throw new RenderException($"The given copy region is empty.");
        }
        if (layerCount == 0)
        {
            throw new RenderException($"{nameof(layerCount)} must be greater than 0.");
        }
        Util.GetMipDimensions(source, srcMipLevel, out uint srcWidth, out uint srcHeight, out uint srcDepth);
        uint srcBlockSize = FormatHelpers.IsCompressedFormat(source.Format) ? 4u : 1u;
        uint roundedSrcWidth = (srcWidth + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
        uint roundedSrcHeight = (srcHeight + srcBlockSize - 1) / srcBlockSize * srcBlockSize;
        if (srcX + width > roundedSrcWidth || srcY + height > roundedSrcHeight || srcZ + depth > srcDepth)
        {
            throw new RenderException($"The given copy region is not valid for the source Texture.");
        }
        Util.GetMipDimensions(destination, dstMipLevel, out uint dstWidth, out uint dstHeight, out uint dstDepth);
        uint dstBlockSize = FormatHelpers.IsCompressedFormat(destination.Format) ? 4u : 1u;
        uint roundedDstWidth = (dstWidth + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
        uint roundedDstHeight = (dstHeight + dstBlockSize - 1) / dstBlockSize * dstBlockSize;
        if (dstX + width > roundedDstWidth || dstY + height > roundedDstHeight || dstZ + depth > dstDepth)
        {
            throw new RenderException($"The given copy region is not valid for the destination Texture.");
        }
        if (srcMipLevel >= source.MipLevels)
        {
            throw new RenderException($"{nameof(srcMipLevel)} must be less than the number of mip levels in the source Texture.");
        }
        uint effectiveSrcArrayLayers = ValidationHelpers.GetEffectiveArrayLayers(source);
        if (srcBaseArrayLayer + layerCount > effectiveSrcArrayLayers)
        {
            throw new RenderException($"An invalid mip range was given for the source Texture.");
        }
        if (dstMipLevel >= destination.MipLevels)
        {
            throw new RenderException($"{nameof(dstMipLevel)} must be less than the number of mip levels in the destination Texture.");
        }
        uint effectiveDstArrayLayers = ValidationHelpers.GetEffectiveArrayLayers(destination);
        if (dstBaseArrayLayer + layerCount > effectiveDstArrayLayers)
        {
            throw new RenderException($"An invalid mip range was given for the destination Texture.");
        }
#endif
    }

    private protected static void DrawIndexed_AssertIndexBufferResolved(bool resolved)
    {
        Debug.Assert(resolved,
            $"Validation in {nameof(DrawIndexed)} must have already trapped a missing index buffer on indexed-draw paths.");
    }

    [Conditional("VALIDATE_USAGE")]
    private void DrawIndexed_CheckIndexBuffer()
    {
#if VALIDATE_USAGE
        if (_currentVertexSource == null)
        {
            return;
        }
        if (!_currentVertexSource.TryGetIndexBuffer(out DeviceBuffer ib, out IndexFormat fmt, out uint indexCount))
        {
            throw new RenderException(
                "DrawIndexed/DrawIndexedIndirect requires the bound IVertexSource to supply an index buffer, " +
                "but TryGetIndexBuffer returned false.");
        }

        uint indexFormatSize = fmt == IndexFormat.UInt16 ? 2u : 4u;
        uint bytesNeeded = indexCount * indexFormatSize;
        if (ib.SizeInBytes < bytesNeeded)
        {
            throw new RenderException(
                $"The active index buffer does not contain enough data to satisfy the given draw command. {bytesNeeded} bytes are needed, but the buffer only contains {ib.SizeInBytes}.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private void DrawIndexedIndirect_CheckIndexBuffer()
    {
#if VALIDATE_USAGE
        if (_currentVertexSource != null
            && !_currentVertexSource.TryGetIndexBuffer(out _, out _, out _))
        {
            throw new RenderException(
                "DrawIndexed/DrawIndexedIndirect requires the bound IVertexSource to supply an index buffer, " +
                "but TryGetIndexBuffer returned false.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void CopyBuffer_CheckRange(
        DeviceBuffer source, uint sourceOffset,
        DeviceBuffer destination, uint destinationOffset,
        uint sizeInBytes)
    {
#if VALIDATE_USAGE
        if (sourceOffset + sizeInBytes > source.SizeInBytes)
        {
            throw new RenderException(
                $"The source DeviceBuffer's capacity ({source.SizeInBytes}) is not large enough to read {sizeInBytes} bytes at offset {sourceOffset}.");
        }
        if (destinationOffset + sizeInBytes > destination.SizeInBytes)
        {
            throw new RenderException(
                $"The destination DeviceBuffer's capacity ({destination.SizeInBytes}) is not large enough to write {sizeInBytes} bytes at offset {destinationOffset}.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private static void CopyTexture_CheckNotNull(Texture source, Texture destination)
    {
        ValidationHelpers.RequireNotNull(source, nameof(source), nameof(CopyTexture));
        ValidationHelpers.RequireNotNull(destination, nameof(destination), nameof(CopyTexture));
    }

    [Conditional("VALIDATE_USAGE")]
    private void Draw_PreDrawValidation()
    {
#if VALIDATE_USAGE
        if (_shaderProgram == null)
        {
            throw new RenderException($"A graphics GraphicsProgram must be set in order to issue draw commands.");
        }
        if (_framebuffer == null)
        {
            throw new RenderException($"A {nameof(Framebuffer)} must be set in order to issue draw commands.");
        }
        if (_currentVertexSource == null)
        {
            throw new RenderException(
                "An IVertexSource must be set via SetVertexSource before issuing draw commands. " +
                "Bind an empty IVertexSource implementation if no vertex data is required.");
        }
#endif
    }
}
