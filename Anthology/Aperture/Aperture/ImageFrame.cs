// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Aperture;

/// <summary>
/// One decoded raster: one per animation frame, icon entry, or mipmap level and array slice. The
/// pixel memory is owned by the <see cref="Image"/> that produced it, so a span taken from a
/// frame must not outlive it. Use <see cref="CopyTo(Span{byte}, int)"/> when it has to.
/// </summary>
public sealed class ImageFrame
{
    private byte[] _buffer;
    private int _length;
    private readonly bool _pooled;

    internal ImageFrame(byte[] buffer, int length, int width, int height, int stride,
                        PixelFormat pixelFormat, bool pooled)
    {
        _buffer = buffer;
        _length = length;
        _pooled = pooled;
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
    }

    /// <summary>Width of this frame in pixels, which for an animation may be smaller than the canvas.</summary>
    public int Width { get; }

    /// <summary>Height of this frame in pixels, which for an animation may be smaller than the canvas.</summary>
    public int Height { get; }

    /// <summary>
    /// Bytes between the start of one row and the start of the next. Equal to the packed row
    /// length unless <see cref="DecodeOptions.RowAlignment"/> asked for padding.
    /// </summary>
    public int Stride { get; }

    /// <summary>Layout of the pixel data.</summary>
    public PixelFormat PixelFormat { get; }

    /// <summary>Left edge of this frame on the parent canvas.</summary>
    public int OffsetX { get; init; }

    /// <summary>Top edge of this frame on the parent canvas.</summary>
    public int OffsetY { get; init; }

    /// <summary>How long this frame is shown, zero for a still image.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>What happens to this frame's region before the next frame is drawn.</summary>
    public FrameDisposal Disposal { get; init; }

    /// <summary>How this frame combines with the canvas underneath it.</summary>
    public FrameBlend Blend { get; init; }

    /// <summary>Mipmap level, zero being the full resolution image.</summary>
    public int MipLevel { get; init; }

    /// <summary>Array slice or cube map face, zero for a plain 2D texture.</summary>
    public int ArraySlice { get; init; }

    /// <summary>Total bytes of pixel data, which is <see cref="Stride"/> times <see cref="Height"/>.</summary>
    public int ByteLength => _length;

    /// <summary>
    /// The pixel data, top row first. Valid only until the owning <see cref="Image"/> is disposed.
    /// </summary>
    public Span<byte> Pixels
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_buffer, 0, _length);
    }

    /// <summary>
    /// The pixel data as a <see cref="Memory{T}"/>, for the asynchronous and buffer-writer APIs
    /// that cannot take a span. Valid only until the owning <see cref="Image"/> is disposed.
    /// </summary>
    public Memory<byte> PixelMemory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_buffer, 0, _length);
    }

    /// <summary>One row of pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetRow(int y)
    {
        if ((uint)y >= (uint)Height)
            ThrowRowOutOfRange();
        return new Span<byte>(_buffer, y * Stride, Stride);
    }

    /// <summary>
    /// One row reinterpreted as pixels of type <typeparamref name="T"/>, whose size the caller is
    /// trusted to match to <see cref="PixelFormat"/>.
    /// </summary>
    public Span<T> GetRowAs<T>(int y) where T : unmanaged =>
        MemoryMarshal.Cast<byte, T>(GetRow(y)[..(Width * PixelFormat.BytesPerPixel())]);

    /// <summary>The whole frame reinterpreted as pixels of type <typeparamref name="T"/>.</summary>
    /// <remarks>Only meaningful when <see cref="Stride"/> has no row padding.</remarks>
    public Span<T> AsSpan<T>() where T : unmanaged => MemoryMarshal.Cast<byte, T>(Pixels);

    /// <summary>
    /// Copies the pixels into a caller owned buffer, re-striding rows on the way. Pass the row
    /// pitch a graphics API reported and the rows land where its driver expects them.
    /// </summary>
    /// <param name="destination">Where to write. Must hold <see cref="GetRequiredBytes"/> bytes.</param>
    /// <param name="destinationStride">
    /// Bytes between rows in <paramref name="destination"/>, or zero to use this frame's stride.
    /// </param>
    /// <returns>False when the destination is too small; nothing is written in that case.</returns>
    public bool CopyTo(Span<byte> destination, int destinationStride = 0)
    {
        int rowBytes = Width * PixelFormat.BytesPerPixel();
        int stride = destinationStride <= 0 ? Stride : destinationStride;
        if (stride < rowBytes)
            return false;

        long required = (long)stride * (Height - 1) + rowBytes;
        if (destination.Length < required)
            return false;

        ReadOnlySpan<byte> source = Pixels;
        if (stride == Stride && Stride == rowBytes)
        {
            source[..(rowBytes * Height)].CopyTo(destination);
            return true;
        }

        for (int y = 0; y < Height; y++)
            source.Slice(y * Stride, rowBytes).CopyTo(destination[(y * stride)..]);

        return true;
    }

    /// <summary>Bytes a <see cref="CopyTo(Span{byte}, int)"/> at the given row pitch needs.</summary>
    public long GetRequiredBytes(int destinationStride = 0)
    {
        int rowBytes = Width * PixelFormat.BytesPerPixel();
        int stride = destinationStride <= 0 ? Stride : destinationStride;
        return (long)stride * (Height - 1) + rowBytes;
    }

    /// <summary>Hands the pixel memory back to the pool. Called by the owning image.</summary>
    internal void Release()
    {
        byte[] buffer = Interlocked.Exchange(ref _buffer, [])!;

        // The length goes with the buffer. Leaving it behind would make every accessor below
        // index an empty array and throw instead of reporting an empty frame.
        _length = 0;

        if (_pooled && buffer.Length != 0)
            BufferPool.Bytes.Return(buffer);
    }

    private static void ThrowRowOutOfRange() =>
        throw new ArgumentOutOfRangeException("y", "The row is outside the frame.");
}
