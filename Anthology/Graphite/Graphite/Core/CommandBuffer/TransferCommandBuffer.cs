using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

/// <summary>
/// Records buffer/texture transfer commands (update, copy, mipmap gen), submittable with or without an open frame.
/// <para>
/// No draw/dispatch, no framebuffer state, no property binding - just moves data outside the frame ring-buffer.
/// Good for one-off stuff like readback or streaming uploads without opening a throwaway frame.
/// </para>
/// <para>
/// Get one from ResourceFactory.CreateTransferCommandBuffer. Submit with GraphicsDevice.SubmitAndWait, which
/// blocks until the GPU finishes. Reusable across multiple Begin/End/SubmitAndWait cycles.
/// </para>
/// Not thread-safe, sync externally.
/// </summary>
public abstract partial class TransferCommandBuffer : DeviceResource, IDisposable
{
    /// <summary>
    /// True if End was called since the last Begin. Used by SubmitAndWait to validate before submission.
    /// </summary>
    internal bool HasEnded { get; private protected set; }

    /// <summary>
    /// Owning device.
    /// </summary>
    public abstract GraphicsDevice Device { get; }

    /// <summary>
    /// Resets to initial state. Call before issuing other commands. Only valid if never called before, or after End
    /// or SubmitAndWait.
    /// </summary>
    public abstract void Begin();

    /// <summary>
    /// Finishes recording, makes the command list executable. Must be called after Begin.
    /// </summary>
    public abstract void End();

    /// <summary>
    /// Updates a buffer region. T must be blittable.
    /// </summary>
    public unsafe void UpdateBuffer<T>(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        T source) where T : unmanaged
    {
        ref byte sourceByteRef = ref Unsafe.AsRef<byte>(Unsafe.AsPointer(ref source));
        fixed (byte* ptr = &sourceByteRef)
        {
            UpdateBuffer(buffer, bufferOffsetInBytes, (IntPtr)ptr, (uint)sizeof(T));
        }
    }

    /// <summary>
    /// Updates a buffer region. T must be blittable.
    /// </summary>
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

    /// <summary>
    /// Updates a buffer region.
    /// </summary>
    public void UpdateBuffer(
        DeviceBuffer buffer,
        uint bufferOffsetInBytes,
        IntPtr source,
        uint sizeInBytes)
    {
        if (bufferOffsetInBytes + sizeInBytes > buffer.SizeInBytes)
        {
            throw new RenderException(
                $"The data size given to UpdateBuffer is too large. The given buffer can only hold {buffer.SizeInBytes} total bytes. The requested update would require {bufferOffsetInBytes + sizeInBytes} bytes.");
        }
        if (sizeInBytes == 0)
        {
            return;
        }

        UpdateBufferCore(buffer, bufferOffsetInBytes, source, sizeInBytes);
    }

    private protected abstract void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes);

    /// <summary>
    /// Updates part of a texture.
    /// </summary>
    public unsafe void UpdateTexture<T>(
        Texture texture,
        ReadOnlySpan<T> source,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer) where T : unmanaged
    {
        uint sizeInBytes = (uint)(sizeof(T) * source.Length);
        fixed (void* pin = &MemoryMarshal.GetReference(source))
        {
            UpdateTexture(texture, (IntPtr)pin, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
        }
    }

    /// <summary>
    /// Updates part of a texture.
    /// </summary>
    public void UpdateTexture(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        UpdateTextureCore(texture, source, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
    }

    private protected abstract void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer);

    /// <summary>
    /// Copies a region from one buffer to another.
    /// </summary>
    public void CopyBuffer(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)
    {
        ValidationHelpers.RequireNotNull(source, nameof(source), nameof(CopyBuffer));
        ValidationHelpers.RequireNotNull(destination, nameof(destination), nameof(CopyBuffer));
        if (sizeInBytes == 0)
        {
            return;
        }
        ValidationHelpers.CopyBufferCheckRange(source, sourceOffset, destination, destinationOffset, sizeInBytes);

        CopyBufferCore(source, sourceOffset, destination, destinationOffset, sizeInBytes);
    }

    private protected abstract void CopyBufferCore(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes);

    /// <summary>
    /// Copies all subresources from one texture to another.
    /// </summary>
    public void CopyTexture(Texture source, Texture destination)
    {
        ValidationHelpers.CopyTextureCheckNotNull(source, destination);
        uint effectiveSrcArrayLayers = ValidationHelpers.GetEffectiveArrayLayers(source);
        ValidationHelpers.CopyTextureCheckCompatibilityAll(source, destination, effectiveSrcArrayLayers);

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

    /// <summary>
    /// Copies one subresource from one texture to another.
    /// </summary>
    public void CopyTexture(Texture source, Texture destination, uint mipLevel, uint arrayLayer)
    {
        ValidationHelpers.CopyTextureCheckNotNull(source, destination);
        ValidationHelpers.CopyTextureCheckCompatibilityForSubresource(source, destination, mipLevel, arrayLayer);

        Util.GetMipDimensions(source, mipLevel, out uint width, out uint height, out uint depth);
        CopyTexture(
            source, 0, 0, 0, mipLevel, arrayLayer,
            destination, 0, 0, 0, mipLevel, arrayLayer,
            width, height, depth,
            1);
    }

    /// <summary>
    /// Copies a region from one texture into another.
    /// </summary>
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
        ValidationHelpers.CopyTextureCheckNotNull(source, destination);
        ValidationHelpers.CopyTextureCheckRegion(
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

    /// <summary>
    /// Generates lower mip levels from the largest mip. Texture must be created with TextureUsage.GenerateMipmaps.
    /// </summary>
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

    /// <summary>
    /// Debug name, shows up in graphics debuggers.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// True if disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources.
    /// </summary>
    public abstract void Dispose();
}
