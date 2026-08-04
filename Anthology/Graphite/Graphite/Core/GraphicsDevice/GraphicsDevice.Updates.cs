using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// Updates part of a texture with new data.
    /// </summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Pointer to packed pixel data for the region.</param>
    /// <param name="sizeInBytes">Bytes to upload. Must match region size.</param>
    /// <param name="x">Min X of the region.</param>
    /// <param name="y">Min Y of the region.</param>
    /// <param name="z">Min Z of the region.</param>
    /// <param name="width">Region width in texels.</param>
    /// <param name="height">Region height in texels.</param>
    /// <param name="depth">Region depth in texels.</param>
    /// <param name="mipLevel">Mip level. Under the texture's mip count.</param>
    /// <param name="arrayLayer">Array layer. Under the texture's layer count.</param>
    public void UpdateTexture(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        UpdateTexture_CheckParameters(texture, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
        UpdateTextureCore(texture, source, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
        Profiler?.Record(BufferOpBin.Update, sizeInBytes);
    }

    /// <summary>
    /// Updates part of a texture with data from a span. Arrays and Span convert implicitly.
    /// </summary>
    /// <typeparam name="T">Blittable pixel type.</typeparam>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Span with packed pixel data for the region.</param>
    /// <param name="x">Min X of the region.</param>
    /// <param name="y">Min Y of the region.</param>
    /// <param name="z">Min Z of the region.</param>
    /// <param name="width">Region width in texels.</param>
    /// <param name="height">Region height in texels.</param>
    /// <param name="depth">Region depth in texels.</param>
    /// <param name="mipLevel">Mip level. Under the texture's mip count.</param>
    /// <param name="arrayLayer">Array layer. Under the texture's layer count.</param>
    public unsafe void UpdateTexture<T>(
        Texture texture,
        ReadOnlySpan<T> source,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer) where T : unmanaged
    {
        fixed (void* pin = &MemoryMarshal.GetReference(source))
        {
            UpdateTexture(
                texture,
                (IntPtr)pin,
                (uint)(sizeof(T) * source.Length),
                x, y, z,
                width, height, depth,
                mipLevel, arrayLayer);
        }
    }

    /// <summary>
    /// Updates a buffer region with new data.
    /// </summary>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset to write at.</param>
    /// <param name="source">Pointer to the data.</param>
    /// <param name="sizeInBytes">Total upload size, bytes.</param>
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
        buffer.EnsureWritable();
        UpdateBufferCore(buffer, bufferOffsetInBytes, source, sizeInBytes);
        Profiler?.Record(BufferOpBin.Update, sizeInBytes);
    }

    /// <summary>
    /// Updates a buffer region with a single value. T must be blittable.
    /// </summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset to write at.</param>
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

    /// <summary>
    /// Updates a buffer region with new data. Arrays and Span convert implicitly.
    /// </summary>
    /// <typeparam name="T">Data type to upload.</typeparam>
    /// <param name="buffer">Buffer to update.</param>
    /// <param name="bufferOffsetInBytes">Byte offset to write at.</param>
    /// <param name="source">Span with the data.</param>
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

    private protected abstract void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer);

    private protected abstract void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes);
}
