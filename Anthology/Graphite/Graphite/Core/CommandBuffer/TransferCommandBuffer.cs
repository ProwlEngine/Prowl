using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

/// <summary>
/// A restricted device resource for recording buffer/texture transfer commands (updates, copies, and
/// mipmap generation) that can be submitted whether or not a <see cref="Frame"/> is currently open.
/// <para>
/// Unlike <see cref="CommandBuffer"/>, a <see cref="TransferCommandBuffer"/> does not support draw/dispatch
/// commands, framebuffer state, or property binding: it exists solely to move data to and from
/// <see cref="DeviceBuffer"/> and <see cref="Texture"/> resources outside of the frame ring-buffer system.
/// This makes it safe to use for one-off operations such as texture read-back or streaming uploads that
/// would otherwise require opening a throwaway <see cref="Frame"/>.
/// </para>
/// <para>
/// Obtain one via <see cref="ResourceFactory.CreateTransferCommandBuffer"/>. Submit with
/// <see cref="GraphicsDevice.SubmitAndWait(TransferCommandBuffer)"/>, which blocks the calling thread until
/// the GPU has finished executing the recorded commands. A <see cref="TransferCommandBuffer"/> may be reused
/// for multiple Begin/End/SubmitAndWait cycles.
/// </para>
/// <see cref="TransferCommandBuffer"/> instances are not thread-safe; access must be externally synchronized.
/// </summary>
public abstract partial class TransferCommandBuffer : DeviceResource, IDisposable
{
    /// <summary>
    /// Gets whether <see cref="End"/> has been called on this instance since the last <see cref="Begin"/> call.
    /// Used by <see cref="GraphicsDevice.SubmitAndWait(TransferCommandBuffer)"/> to validate before submission.
    /// </summary>
    internal bool HasEnded { get; private protected set; }

    /// <summary>
    /// Gets the <see cref="GraphicsDevice"/> that owns this instance.
    /// </summary>
    public abstract GraphicsDevice Device { get; }

    /// <summary>
    /// Puts this <see cref="TransferCommandBuffer"/> into the initial state.
    /// This function must be called before other commands can be issued.
    /// Begin must only be called if it has not been previously called, if <see cref="End"/> has been called,
    /// or if <see cref="GraphicsDevice.SubmitAndWait(TransferCommandBuffer)"/> has been called on this instance.
    /// </summary>
    public abstract void Begin();

    /// <summary>
    /// Completes this list of transfer commands, putting it into an executable state.
    /// This function must only be called after <see cref="Begin"/> has been called.
    /// </summary>
    public abstract void End();

    /// <summary>
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// This function must be used with a blittable value type <typeparamref name="T"/>.
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
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
    /// This function must be used with a blittable value type <typeparamref name="T"/>.
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
    /// Updates a <see cref="DeviceBuffer"/> region with new data.
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
    /// Updates a portion of a <see cref="Texture"/> resource with new data.
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
    /// Updates a portion of a <see cref="Texture"/> resource with new data.
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
    /// Copies a region from the source <see cref="DeviceBuffer"/> to another region in the destination
    /// <see cref="DeviceBuffer"/>.
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
    /// Copies all subresources from one <see cref="Texture"/> to another.
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
    /// Copies one subresource from one <see cref="Texture"/> to another.
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
    /// Copies a region from one <see cref="Texture"/> into another.
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
    /// Generates mipmaps for the given <see cref="Texture"/>. The largest mipmap is used to generate all of the
    /// lower mipmap levels contained in the Texture. The target Texture must have been created with
    /// <see cref="TextureUsage"/>.<see cref="TextureUsage.GenerateMipmaps"/>.
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
    /// A string identifying this instance. Can be used to differentiate between objects in graphics debuggers and
    /// other tools.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// A bool indicating whether this instance has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources controlled by this instance.
    /// </summary>
    public abstract void Dispose();
}
