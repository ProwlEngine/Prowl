// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Aperture.Decoders.Png;

/// <summary>
/// Decodes PNG pixel data: gathers the compressed stream, inflates it, reverses the scanline
/// filters and expands each row into whole-byte pixels. Every intermediate buffer is rented, so a
/// decode allocates the frame and nothing else of consequence.
/// </summary>
internal static class PngImageReader
{
    /// <summary>Column each interlace pass starts at.</summary>
    private static ReadOnlySpan<byte> PassOriginX => [0, 4, 0, 2, 0, 1, 0];

    /// <summary>Row each interlace pass starts at.</summary>
    private static ReadOnlySpan<byte> PassOriginY => [0, 0, 4, 0, 2, 0, 1];

    /// <summary>Column stride of each interlace pass.</summary>
    private static ReadOnlySpan<byte> PassStepX => [8, 8, 4, 4, 2, 2, 1];

    /// <summary>Row stride of each interlace pass.</summary>
    private static ReadOnlySpan<byte> PassStepY => [8, 8, 8, 4, 4, 2, 2];

    /// <summary>
    /// Decodes into <paramref name="destination"/>, which must be
    /// <paramref name="destinationStride"/> times the image height bytes long.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, in PngChunks chunks, ImageInfo info,
                                 PixelFormat target, Span<byte> destination, int destinationStride,
                                 bool flipVertically, out ApertureError error) =>
        TryDecode(data, chunks, null, info, target, destination, destinationStride, flipVertically, out error);

    /// <summary>
    /// Decodes one frame of an animation, whose data lies in chunks of its own rather than in the
    /// ones the default image is built from.
    /// </summary>
    public static bool TryDecodeFrame(ReadOnlySpan<byte> data, in PngChunks chunks,
                                      PngAnimationFrame frame, ImageInfo info, PixelFormat target,
                                      Span<byte> destination, int destinationStride,
                                      out ApertureError error) =>
        TryDecode(data, chunks, frame, info, target, destination, destinationStride, false, out error);

    private static bool TryDecode(ReadOnlySpan<byte> data, in PngChunks chunks,
                                  PngAnimationFrame? frame, ImageInfo info,
                                  PixelFormat target, Span<byte> destination, int destinationStride,
                                  bool flipVertically, out ApertureError error)
    {
        byte colourType = chunks.ColourType;
        byte bitDepth = chunks.BitDepth;
        int width = info.Width;
        int height = info.Height;

        // Colour type 3 says the pixels are palette indices, so without a palette there is
        // nothing to look them up in.
        if (colourType == 3 && chunks.PaletteLength == 0)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        int rawLength = GetRawLength(width, height, colourType, bitDepth, chunks.Interlaced);
        if (rawLength <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        // A palette is at most 256 entries, so the flattened table is a fixed small buffer.
        Span<uint> palette = stackalloc uint[256];
        if (colourType == 3)
            PngScanline.BuildPalette(chunks.Palette, chunks.Transparency, palette);

        byte[] compressed = BufferPool.Bytes.Rent(Math.Max(1, chunks.CompressedLength));
        byte[] raw = BufferPool.Bytes.Rent(rawLength);
        try
        {
            int compressedLength = frame is null
                ? chunks.CopyCompressedTo(data, compressed)
                : PngChunks.CopyFrameTo(data, frame, compressed);
            if (!TryInflate(compressed, compressedLength, raw, rawLength, out int produced, out bool excess))
            {
                error = ApertureError.DecompressionFailed;
                return false;
            }

            // More pixel data than the header accounts for means the two disagree, and trusting
            // the header is what stops an over-long stream from being decoded at all.
            if (excess && !chunks.AllowTruncated)
            {
                error = ApertureError.InvalidData;
                return false;
            }

            // A stream that ran out early still decodes the rows it did produce; the rest of
            // the frame stays as the caller left it.
            if (produced < rawLength)
            {
                if (!chunks.AllowTruncated)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }
                raw.AsSpan(produced, rawLength - produced).Clear();
            }

            PngScanline.Layout layout = new(colourType, bitDepth, target, palette, chunks.Transparency);

            bool decoded = chunks.Interlaced
                ? TryDecodeInterlaced(raw.AsSpan(0, rawLength), layout, width, height,
                                      destination, destinationStride, flipVertically)
                : TryDecodeProgressive(raw.AsSpan(0, rawLength), layout, width, height,
                                       destination, destinationStride, flipVertically);

            if (!decoded)
            {
                error = ApertureError.InvalidData;
                return false;
            }

            error = ApertureError.None;
            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(raw);
            BufferPool.Bytes.Return(compressed);
        }
    }

    /// <summary>Unfilters and converts a non-interlaced image, one row at a time.</summary>
    private static bool TryDecodeProgressive(Span<byte> raw, in PngScanline.Layout layout,
                                             int width, int height, Span<byte> destination,
                                             int destinationStride, bool flipVertically)
    {
        int rowBytes = PngScanline.GetRowBytes(width, layout.ColourType, layout.BitDepth);
        int bytesPerPixel = PngScanline.GetBytesPerPixel(layout.ColourType, layout.BitDepth);

        // Where the row is already the layout the frame wants, unfiltering writes it straight
        // there, which saves a pass over every byte of the image.
        bool direct = layout.ConversionIsCopy && destinationStride >= rowBytes;

        ReadOnlySpan<byte> previous = default;
        int offset = 0;

        for (int y = 0; y < height; y++)
        {
            byte filter = raw[offset];
            Span<byte> row = raw.Slice(offset + 1, rowBytes);
            int target = flipVertically ? height - 1 - y : y;

            if (direct)
            {
                Span<byte> output = destination.Slice(target * destinationStride, rowBytes);
                if (!PngFilter.Apply(filter, row, output, previous, bytesPerPixel))
                    return false;

                previous = output;
            }
            else
            {
                if (!PngFilter.Apply(filter, row, previous, bytesPerPixel))
                    return false;

                PngScanline.Convert(row, destination[(target * destinationStride)..], width, layout);
                previous = row;
            }

            offset += rowBytes + 1;
        }

        return true;
    }

    /// <summary>
    /// Unfilters and converts the seven Adam7 passes, scattering each pass into the frame.
    /// </summary>
    private static bool TryDecodeInterlaced(Span<byte> raw, in PngScanline.Layout layout,
                                            int width, int height, Span<byte> destination,
                                            int destinationStride, bool flipVertically)
    {
        int pixelBytes = layout.Target.BytesPerPixel();
        int bytesPerPixel = PngScanline.GetBytesPerPixel(layout.ColourType, layout.BitDepth);

        int widest = 0;
        for (int pass = 0; pass < 7; pass++)
            widest = Math.Max(widest, GetPassWidth(width, pass));

        byte[] expanded = BufferPool.Bytes.Rent(Math.Max(1, widest * pixelBytes));
        try
        {
            int offset = 0;
            for (int pass = 0; pass < 7; pass++)
            {
                int passWidth = GetPassWidth(width, pass);
                int passHeight = GetPassHeight(height, pass);
                if (passWidth == 0 || passHeight == 0)
                    continue;

                int rowBytes = PngScanline.GetRowBytes(passWidth, layout.ColourType, layout.BitDepth);
                ReadOnlySpan<byte> previous = default;

                int originX = PassOriginX[pass];
                int originY = PassOriginY[pass];
                int stepX = PassStepX[pass];
                int stepY = PassStepY[pass];

                for (int y = 0; y < passHeight; y++)
                {
                    byte filter = raw[offset];
                    Span<byte> row = raw.Slice(offset + 1, rowBytes);

                    if (!PngFilter.Apply(filter, row, previous, bytesPerPixel))
                        return false;

                    Span<byte> pixels = expanded.AsSpan(0, passWidth * pixelBytes);
                    PngScanline.Convert(row, pixels, passWidth, layout);

                    int row1 = originY + (y * stepY);
                    if (flipVertically)
                        row1 = height - 1 - row1;
                    Scatter(pixels, destination[(row1 * destinationStride)..],
                            passWidth, originX, stepX, pixelBytes);

                    previous = row;
                    offset += rowBytes + 1;
                }
            }

            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(expanded);
        }
    }

    /// <summary>
    /// Spreads one pass of a row across the frame, a pixel every few columns. Naming the pixel
    /// sizes the format actually uses turns each of those into a single store, where asking the
    /// copy helper for one pixel at a time is a call for every pixel of the image.
    /// </summary>
    private static void Scatter(ReadOnlySpan<byte> pixels, Span<byte> target, int count,
                                int originX, int stepX, int pixelBytes)
    {
        ref byte from = ref MemoryMarshal.GetReference(pixels);
        ref byte to = ref MemoryMarshal.GetReference(target);

        switch (pixelBytes)
        {
            case 4:
                for (int x = 0; x < count; x++)
                {
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, (originX + (x * stepX)) * 4),
                        Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref from, x * 4)));
                }

                break;

            case 3:
                for (int x = 0; x < count; x++)
                {
                    ref byte at = ref Unsafe.Add(ref to, (originX + (x * stepX)) * 3);
                    at = Unsafe.Add(ref from, x * 3);
                    Unsafe.Add(ref at, 1) = Unsafe.Add(ref from, (x * 3) + 1);
                    Unsafe.Add(ref at, 2) = Unsafe.Add(ref from, (x * 3) + 2);
                }

                break;

            case 2:
                for (int x = 0; x < count; x++)
                {
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, (originX + (x * stepX)) * 2),
                        Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref from, x * 2)));
                }

                break;

            case 1:
                for (int x = 0; x < count; x++)
                    Unsafe.Add(ref to, originX + (x * stepX)) = Unsafe.Add(ref from, x);

                break;

            default:
                for (int x = 0; x < count; x++)
                {
                    pixels.Slice(x * pixelBytes, pixelBytes)
                          .CopyTo(target[((originX + (x * stepX)) * pixelBytes)..]);
                }

                break;
        }
    }

    /// <summary>Total unfiltered bytes the image occupies, filter bytes included.</summary>
    private static int GetRawLength(int width, int height, byte colourType, byte bitDepth, bool interlaced)
    {
        if (!interlaced)
        {
            long rowBytes = PngScanline.GetRowBytes(width, colourType, bitDepth);
            long total = (rowBytes + 1) * height;
            return total is > 0 and <= int.MaxValue ? (int)total : -1;
        }

        long sum = 0;
        for (int pass = 0; pass < 7; pass++)
        {
            int passWidth = GetPassWidth(width, pass);
            int passHeight = GetPassHeight(height, pass);
            if (passWidth == 0 || passHeight == 0)
                continue;

            sum += ((long)PngScanline.GetRowBytes(passWidth, colourType, bitDepth) + 1) * passHeight;
        }

        return sum is > 0 and <= int.MaxValue ? (int)sum : -1;
    }

    private static int GetPassWidth(int width, int pass)
    {
        int origin = PassOriginX[pass];
        int step = PassStepX[pass];
        return width > origin ? ((width - origin) + step - 1) / step : 0;
    }

    private static int GetPassHeight(int height, int pass)
    {
        int origin = PassOriginY[pass];
        int step = PassStepY[pass];
        return height > origin ? ((height - origin) + step - 1) / step : 0;
    }

    /// <summary>
    /// Inflates into <paramref name="raw"/>, then peeks one byte further to see whether the stream
    /// had more to give. The zlib wrapper is stepped over by hand and the payload inflated as raw
    /// deflate, which skips the Adler checksum; the chunk CRCs cover the same bytes.
    /// </summary>
    private static bool TryInflate(byte[] compressed, int compressedLength, byte[] raw, int rawLength,
                                   out int produced, out bool excess)
    {
        produced = 0;
        excess = false;

        if (compressedLength < 2 || !IsZLibHeader(compressed[0], compressed[1]))
            return false;

        try
        {
            using MemoryStream source = new(compressed, 2, compressedLength - 2, writable: false);
            using DeflateStream inflate = new(source, CompressionMode.Decompress);

            while (produced < rawLength)
            {
                int read = inflate.Read(raw, produced, rawLength - produced);
                if (read <= 0)
                    break;
                produced += read;
            }

            if (produced == rawLength)
            {
                Span<byte> probe = stackalloc byte[1];
                excess = inflate.Read(probe) > 0;
            }

            return produced > 0;
        }
        catch (InvalidDataException)
        {
            // Corrupt past the point it got to; whatever was produced is still usable.
            return produced > 0;
        }
    }

    /// <summary>
    /// Checks the two byte zlib wrapper: deflate compression, a window no larger than the format
    /// allows, a valid check value, and no preset dictionary.
    /// </summary>
    private static bool IsZLibHeader(byte first, byte second)
    {
        if ((first & 0x0F) != 8 || (first >> 4) > 7)
            return false;
        if ((second & 0x20) != 0)
            return false;
        return ((first << 8) | second) % 31 == 0;
    }
}
