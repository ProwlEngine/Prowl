using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// Updates a texture region from a pointer. Staged and submitted now.
    /// </summary>
    /// <param name="texture">Texture to update.</param>
    /// <param name="source">Pointer to packed pixel data for the region.</param>
    /// <param name="sizeInBytes">Bytes to upload. Must match region size.</param>
    /// <param name="region">Region to write.</param>
    public void UpdateTexture(Texture texture, IntPtr source, uint sizeInBytes, in TextureRegion region)
    {
        UpdateTexture_CheckParameters(texture, region);
        BoundsChecks.UpdateTexture(texture, sizeInBytes, region);
        TextureRegion copy = region;
        Record(cb => cb.UpdateTexture(texture, source, sizeInBytes, copy), "UpdateTexture");
    }

    /// <summary>
    /// Updates a texture region from a span.
    /// </summary>
    public unsafe void UpdateTexture<T>(Texture texture, ReadOnlySpan<T> source, in TextureRegion region) where T : unmanaged
    {
        fixed (void* pin = &MemoryMarshal.GetReference(source))
        {
            UpdateTexture(texture, (IntPtr)pin, (uint)(sizeof(T) * source.Length), region);
        }
    }

    /// <summary>
    /// Replaces all of mip 0, layer 0 with the span.
    /// </summary>
    public void UpdateTexture<T>(Texture texture, ReadOnlySpan<T> source) where T : unmanaged
    {
        UpdateTexture(texture, source, TextureRegion.Whole(texture));
    }

    /// <summary>
    /// Updates a buffer region with new data. Staged and submitted now; blocks only for Staging resources.
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
        buffer.MarkContentChanged();
        GpuSubmission submission = Record(cb => cb.UpdateBuffer(buffer, bufferOffsetInBytes, source, sizeInBytes), "UpdateBuffer");
        if ((buffer.Usage & BufferUsage.Staging) != 0)
            submission.Wait();
        Counters.RecordBufferOp(BufferOpBin.Update, sizeInBytes);
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
}
