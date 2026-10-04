using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

/// <summary>
/// Transfer surface of a command buffer: buffer and texture updates, copies and mipmap generation.
/// These work both inside a graph and in work recorded through <see cref="GraphicsDevice.Record"/>.
/// </summary>
public abstract class CommandBufferBase : GraphicsResource
{
    /// <summary>True if End was called since last Begin.</summary>
    internal bool HasEnded { get; private protected set; }

    internal GraphicsDevice Device { get; }

    private protected CommandBufferBase(GraphicsDevice device)
    {
        Device = device;
    }

    internal IGraphStateSource? GraphState { get; set; }

    internal int GraphStateVersion => GraphState?.StateVersion ?? 0;

    internal TextureState StateOf(Texture texture)
        => GraphState != null ? GraphState.StateOf(texture) : TextureState.Resting;

    /// <summary>Updates buffer region with a single value. T must be blittable.</summary>
    /// <typeparam name="T">Upload type.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Value to upload.</param>
    public unsafe void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        in T source) where T : unmanaged
    {
        fixed (byte* ptr = &Unsafe.As<T, byte>(ref Unsafe.AsRef(in source)))
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)ptr, (uint)sizeof(T));
        }
    }

    /// <summary>Updates buffer region with new data. T must be blittable. Arrays and Span convert implicitly.</summary>
    /// <typeparam name="T">Upload type.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Read-only span to upload.</param>
    public unsafe void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        ReadOnlySpan<T> source) where T : unmanaged
    {
        fixed (void* pin = &MemoryMarshal.GetReference(source))
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)pin, (uint)(sizeof(T) * source.Length));
        }
    }

    /// <summary>Updates buffer region.</summary>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset.</param>
    /// <param name="source">Pointer to data.</param>
    /// <param name="sizeInBytes">Total upload bytes.</param>
    public void UpdateBuffer(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        IntPtr source,
        uint sizeInBytes)
    {
        if (bufferOffsetInBytes + sizeInBytes > buffer.SizeInBytes)
        {
            throw new RenderException(
                $"The DeviceBuffer's capacity ({buffer.SizeInBytes}) is not large enough to store the amount of " +
                $"data specified ({sizeInBytes}) at the given offset ({bufferOffsetInBytes}).");
        }
        if (sizeInBytes == 0)
        {
            return;
        }

        UpdateBufferCore(buffer, bufferOffsetInBytes, source, sizeInBytes);
    }

    private protected abstract void UpdateBufferCore(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        IntPtr source,
        uint sizeInBytes);

    /// <summary>Copies a region between buffers.</summary>
    /// <param name="source">Source buffer.</param>
    /// <param name="sourceOffset">Source start offset.</param>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="destinationOffset">Destination start offset.</param>
    /// <param name="sizeInBytes">Bytes to copy.</param>
    public void CopyBuffer(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)
    {
        ValidationHelpers.RequireNotNull(Device, source, nameof(source), nameof(CopyBuffer));
        ValidationHelpers.RequireNotNull(Device, destination, nameof(destination), nameof(CopyBuffer));
        if (sizeInBytes == 0)
        {
            return;
        }
        ValidationHelpers.CopyBufferCheckRange(Device, source, sourceOffset, destination, destinationOffset, sizeInBytes);

        CopyBufferCore(source, sourceOffset, destination, destinationOffset, sizeInBytes);
    }

    private protected abstract void CopyBufferCore(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes);

    /// <summary>Copies all subresources between textures.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="destination">Destination texture.</param>
    public void CopyTexture(Texture source, Texture destination)
    {
        ValidationHelpers.CopyTextureCheckNotNull(Device, source, destination);
        uint effectiveSrcArrayLayers = ValidationHelpers.GetEffectiveArrayLayers(source);
        ValidationHelpers.CopyTextureCheckCompatibilityAll(Device, source, destination, effectiveSrcArrayLayers);

        for (uint level = 0; level < source.MipLevels; level++)
        {
            Util.GetMipDimensions(source, level, out uint mipWidth, out uint mipHeight, out uint mipDepth);
            CopyTexture(
                source, 0, 0, 0, level, 0,
                destination, 0, 0, 0, level, 0,
                mipWidth, mipHeight, mipDepth,
                effectiveSrcArrayLayers);
        }
    }

    /// <summary>Copies one subresource between textures.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="destination">Destination texture.</param>
    /// <param name="mipLevel">Mip level.</param>
    /// <param name="arrayLayer">Array layer.</param>
    public void CopyTexture(Texture source, Texture destination, uint mipLevel, uint arrayLayer)
    {
        ValidationHelpers.CopyTextureCheckNotNull(Device, source, destination);
        ValidationHelpers.CopyTextureCheckCompatibilityForSubresource(Device, source, destination, mipLevel, arrayLayer);

        Util.GetMipDimensions(source, mipLevel, out uint width, out uint height, out uint depth);
        CopyTexture(
            source, 0, 0, 0, mipLevel, arrayLayer,
            destination, 0, 0, 0, mipLevel, arrayLayer,
            width, height, depth,
            1);
    }

    /// <summary>Copies a region between textures.</summary>
    /// <param name="source">Source texture.</param>
    /// <param name="srcX">Source X.</param>
    /// <param name="srcY">Source Y.</param>
    /// <param name="srcZ">Source Z.</param>
    /// <param name="srcMipLevel">Source mip level.</param>
    /// <param name="srcBaseArrayLayer">First source layer.</param>
    /// <param name="destination">Destination texture.</param>
    /// <param name="dstX">Destination X.</param>
    /// <param name="dstY">Destination Y.</param>
    /// <param name="dstZ">Destination Z.</param>
    /// <param name="dstMipLevel">Destination mip level.</param>
    /// <param name="dstBaseArrayLayer">First destination layer.</param>
    /// <param name="width">Region width, texels.</param>
    /// <param name="height">Region height, texels.</param>
    /// <param name="depth">Region depth, texels.</param>
    /// <param name="layerCount">Layers to copy.</param>
    public void CopyTexture(
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
        ValidationHelpers.CopyTextureCheckNotNull(Device, source, destination);
        ValidationHelpers.CopyTextureCheckRegion(Device, 
            source,
            srcX, srcY, srcZ,
            srcMipLevel,
            srcBaseArrayLayer,
            destination,
            dstX, dstY, dstZ,
            dstMipLevel,
            dstBaseArrayLayer,
            width, height, depth,
            layerCount);
        CopyTextureCore(
            source,
            srcX, srcY, srcZ,
            srcMipLevel,
            srcBaseArrayLayer,
            destination,
            dstX, dstY, dstZ,
            dstMipLevel,
            dstBaseArrayLayer,
            width, height, depth,
            layerCount);
    }

    private protected abstract void CopyTextureCore(
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount);

    /// <summary>Generates lower mip levels from the largest mip. Needs the GenerateMipmaps usage flag.</summary>
    /// <param name="texture">Texture to mipmap.</param>
    public void GenerateMipmaps(Texture texture)
    {
        if ((texture.Usage & TextureUsage.GenerateMipmaps) == 0)
        {
            throw new RenderException(
                $"{nameof(GenerateMipmaps)} requires a target Texture with {nameof(TextureUsage)}.{nameof(TextureUsage.GenerateMipmaps)}");
        }

        if (texture.MipLevels > 1)
        {
            GenerateMipmapsCore(texture);
        }
    }

    private protected abstract void GenerateMipmapsCore(Texture texture);

    /// <summary>Updates a texture region from a pointer.</summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Pointer to data.</param>
    /// <param name="sizeInBytes">Total upload bytes.</param>
    /// <param name="region">Region to write.</param>
    public void UpdateTexture(Texture texture, IntPtr source, uint sizeInBytes, in TextureRegion region)
    {
        Device.UpdateTexture_CheckParameters(texture, sizeInBytes, region);
        UpdateTextureCore(texture, source, sizeInBytes, region);
    }

    /// <summary>Updates a texture region from a span.</summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Data to upload.</param>
    /// <param name="region">Region to write.</param>
    public unsafe void UpdateTexture<T>(Texture texture, ReadOnlySpan<T> source, in TextureRegion region) where T : unmanaged
    {
        fixed (void* pin = &MemoryMarshal.GetReference(source))
        {
            UpdateTexture(texture, (IntPtr)pin, (uint)(sizeof(T) * source.Length), region);
        }
    }

    /// <summary>Replaces all of mip 0, layer 0 with the span.</summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Data to upload.</param>
    public void UpdateTexture<T>(Texture texture, ReadOnlySpan<T> source) where T : unmanaged
    {
        UpdateTexture(texture, source, TextureRegion.Whole(texture));
    }

    private protected abstract void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes, in TextureRegion region);
}
