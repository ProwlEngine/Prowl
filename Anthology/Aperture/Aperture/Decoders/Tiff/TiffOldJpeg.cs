// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Jpeg;

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// The first attempt at putting JPEG inside TIFF, which the format later replaced. It scatters the
/// pieces of a stream across half a dozen tags and leaves only the entropy coded data in the
/// strips, so nothing in the file is a stream a JPEG reader would accept until it is rebuilt.
/// </summary>
internal static class TiffOldJpeg
{
    public static bool TryDecode(ReadOnlySpan<byte> file, ReadOnlySpan<byte> scan, long scanOffset,
                                 TiffImage image, int width, int rows, Span<byte> destination, int rowBytes)
    {
        // Some files kept a whole stream beside the tags with the strips pointing into it.
        // Where that stream already holds the scan there is nothing to rebuild.
        long start = image.JpegStreamOffset;
        long end = start + image.JpegStreamLength;

        if (image.JpegStreamLength > 0 && start >= 0 && end <= file.Length &&
            start <= scanOffset && end >= scanOffset + scan.Length)
        {
            return TryRun(file.Slice((int)start, (int)image.JpegStreamLength), image, width, rows,
                          destination, rowBytes);
        }

        if (!TryBuildHeader(file, image, width, rows, out byte[]? header))
            return false;

        int total = header!.Length + scan.Length + 2;
        byte[] stream = BufferPool.Bytes.Rent(total);

        try
        {
            Span<byte> whole = stream.AsSpan(0, total);
            header.CopyTo(whole);
            scan.CopyTo(whole[header.Length..]);

            whole[^2] = 0xFF;
            whole[^1] = 0xD9;

            return TryRun(whole, image, width, rows, destination, rowBytes);
        }
        finally
        {
            BufferPool.Bytes.Return(stream);
        }
    }

    private static bool TryRun(ReadOnlySpan<byte> stream, TiffImage image, int width, int rows,
                               Span<byte> destination, int rowBytes)
    {
        DecodeOptions options = new() { MaxAllocationBytes = long.MaxValue };
        ImageInfo info = new()
        {
            Format = ImageFormat.Jpeg,
            Width = width,
            Height = rows,
            Channels = image.SamplesPerPixel,
        };

        return JpegImageReader.TryDecode(stream, options, info, image.SamplesPerPixel,
                                         destination, rowBytes, flip: false, out _);
    }

    /// <summary>
    /// Puts the scattered tables back into the segments a JPEG stream begins with, and states the
    /// picture's shape from the container's own tags since the stream carries none.
    /// </summary>
    private static bool TryBuildHeader(ReadOnlySpan<byte> file, TiffImage image, int width, int rows,
                                       out byte[]? header)
    {
        header = null;

        int components = image.SamplesPerPixel;
        if (components is not (1 or 3))
            return false;

        if (image.QuantisationTables.Length < components ||
            image.DcTables.Length < components ||
            image.AcTables.Length < components)
            return false;

        List<byte> stream = [0xFF, 0xD8];

        for (int i = 0; i < components; i++)
        {
            long at = image.QuantisationTables[i];
            if (at < 0 || at + 64 > file.Length)
                return false;

            stream.AddRange([0xFF, 0xDB, 0x00, 0x43, (byte)i]);
            stream.AddRange(file.Slice((int)at, 64).ToArray());
        }

        for (int table = 0; table < 2; table++)
        {
            long[] pointers = table == 0 ? image.DcTables : image.AcTables;

            for (int i = 0; i < components; i++)
            {
                long at = pointers[i];
                if (at < 0 || at + 16 > file.Length)
                    return false;

                int values = 0;
                for (int k = 0; k < 16; k++)
                    values += file[(int)at + k];

                if (at + 16 + values > file.Length)
                    return false;

                int length = 19 + values;
                stream.AddRange([0xFF, 0xC4, (byte)(length >> 8), (byte)length, (byte)((table << 4) | i)]);
                stream.AddRange(file.Slice((int)at, 16 + values).ToArray());
            }
        }

        int sofLength = 8 + (3 * components);
        stream.AddRange([0xFF, 0xC0, (byte)(sofLength >> 8), (byte)sofLength, 8,
                         (byte)(rows >> 8), (byte)rows, (byte)(width >> 8), (byte)width,
                         (byte)components]);

        for (int i = 0; i < components; i++)
        {
            // Only the first component carries the chroma sharing, since it is the one the other
            // two are shared across.
            int across = i == 0 ? image.ChromaAcross : 1;
            int down = i == 0 ? image.ChromaDown : 1;

            stream.AddRange([(byte)(i + 1), (byte)((across << 4) | down), (byte)i]);
        }

        if (image.RestartInterval > 0)
        {
            stream.AddRange([0xFF, 0xDD, 0x00, 0x04,
                             (byte)(image.RestartInterval >> 8), (byte)image.RestartInterval]);
        }

        int sosLength = 6 + (2 * components);
        stream.AddRange([0xFF, 0xDA, (byte)(sosLength >> 8), (byte)sosLength, (byte)components]);

        for (int i = 0; i < components; i++)
            stream.AddRange([(byte)(i + 1), (byte)((i << 4) | i)]);

        stream.AddRange([0x00, 0x3F, 0x00]);

        header = [.. stream];
        return true;
    }
}
