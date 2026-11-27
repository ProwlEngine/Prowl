// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Jpeg;

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// The bridge to the JPEG reader for strips and tiles compressed with it. The tables are usually
/// written once in a tag of their own, leaving each strip holding nothing but the scan, so a strip
/// is read by joining the two: the tables without their end marker, then the strip without its
/// start marker.
/// </summary>
internal static class TiffJpeg
{
    /// <summary>Cap on markers walked while looking for the frame header.</summary>
    private const int MaxMarkers = 1024;

    public static bool TryDecode(ReadOnlySpan<byte> tables, ReadOnlySpan<byte> strip, int channels,
                                 Span<byte> destination, int stride, int rows)
    {
        if (tables.Length >= 2 && tables[^2] == 0xFF && tables[^1] == 0xD9)
            tables = tables[..^2];

        if (tables.Length > 0 && strip.Length >= 2 && strip[0] == 0xFF && strip[1] == 0xD8)
            strip = strip[2..];

        int total = tables.Length + strip.Length;
        if (total < 4)
            return false;

        byte[] stream = BufferPool.Bytes.Rent(total);
        try
        {
            Span<byte> whole = stream.AsSpan(0, total);
            tables.CopyTo(whole);
            strip.CopyTo(whole[tables.Length..]);

            if (whole[0] != 0xFF || whole[1] != 0xD8)
                return false;

            if (!TryDescribe(whole, out int width, out int height, out int components))
                return false;

            // The strip's own frame header governs how much is written, so a stream claiming more
            // than the block holds is refused rather than clipped.
            if (components != channels || width * channels > stride || height > rows)
                return false;

            DecodeOptions options = new() { MaxAllocationBytes = long.MaxValue };
            ImageInfo info = new()
            {
                Format = ImageFormat.Jpeg,
                Width = width,
                Height = height,
                Channels = components,
            };

            return JpegImageReader.TryDecode(whole, options, info, channels, destination, stride,
                                             flip: false, out _);
        }
        finally
        {
            BufferPool.Bytes.Return(stream);
        }
    }

    /// <summary>Reads the frame header, which is the only part of the stream this side needs.</summary>
    private static bool TryDescribe(ReadOnlySpan<byte> stream, out int width, out int height, out int components)
    {
        width = height = components = 0;
        int at = 2;

        for (int scanned = 0; scanned < MaxMarkers; scanned++)
        {
            while (at < stream.Length && stream[at] != 0xFF)
                at++;

            while (at < stream.Length && stream[at] == 0xFF)
                at++;

            if (at >= stream.Length)
                return false;

            byte marker = stream[at++];
            if (marker is 0x01 or 0xD8 or 0xD9 or (>= 0xD0 and <= 0xD7))
                continue;

            if (at + 2 > stream.Length)
                return false;

            int length = (stream[at] << 8) | stream[at + 1];
            if (length < 2 || at + length > stream.Length)
                return false;

            if (marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7)
                       or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF))
            {
                if (length < 8)
                    return false;

                height = (stream[at + 3] << 8) | stream[at + 4];
                width = (stream[at + 5] << 8) | stream[at + 6];
                components = stream[at + 7];
                return width > 0 && height > 0 && components is 1 or 3;
            }

            at += length;
        }

        return false;
    }
}
