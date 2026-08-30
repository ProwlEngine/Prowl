// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// Turns a directory's strips or tiles into pixels, in two passes: gather every block into one
/// flat surface laid out the way the file stores it, then read that surface once into the layout
/// the caller asked for. The buffer that costs buys one place where the tiling, the planes and the
/// compressions stop mattering.
/// </summary>
internal static class TiffImageReader
{
    private const int CompressionNone = 1;
    private const int CompressionLzw = 5;
    private const int CompressionJpeg = 7;
    private const int CompressionFaxModified = 2;
    private const int CompressionFaxGroup3 = 3;
    private const int CompressionFaxGroup4 = 4;
    private const int CompressionWebp = 50001;
    private const int CompressionOldJpeg = 6;
    private const int CompressionThunder = 32809;
    private const int CompressionLogLuv = 34676;
    private const int CompressionLogLuv24 = 34677;
    private const int CompressionDeflateAdobe = 8;
    private const int CompressionPackBits = 32773;
    private const int CompressionDeflate = 32946;

    /// <summary>Whether the compression is one this reader implements.</summary>
    public static bool IsSupported(TiffImage image)
    {
        if (image.Compression is not (CompressionNone or CompressionLzw or CompressionPackBits
            or CompressionDeflateAdobe or CompressionDeflate or CompressionJpeg
            or CompressionFaxModified or CompressionFaxGroup3 or CompressionFaxGroup4
            or CompressionWebp or CompressionThunder or CompressionLogLuv or CompressionLogLuv24
            or CompressionOldJpeg))
            return false;

        // The older JPEG compression produces eight bit colour whatever else the tags claim.
        if (image.Compression == CompressionOldJpeg)
        {
            return image.BitsPerSample == 8 && image.SamplesPerPixel is 1 or 3 &&
                   image.QuantisationTables.Length >= image.SamplesPerPixel &&
                   image.DcTables.Length >= image.SamplesPerPixel &&
                   image.AcTables.Length >= image.SamplesPerPixel;
        }

        // The scanner compression is four bit grey and nothing else.
        if (image.Compression == CompressionThunder &&
            (image.BitsPerSample != 4 || image.SamplesPerPixel != 1))
            return false;

        // The log forms produce floating point whatever sample width the container states.
        if (image.LogLuv)
            return image.SamplesPerPixel is 1 or 3;

        // A WebP strip is a whole picture, so it arrives as eight bit colour already.
        if (image.Compression == CompressionWebp &&
            (image.BitsPerSample != 8 || image.SamplesPerPixel is not (3 or 4)))
            return false;

        // A fax picture is one bit a pixel and nothing else, which is what the coding assumes.
        if (image.Compression is CompressionFaxModified or CompressionFaxGroup3 or CompressionFaxGroup4 &&
            (image.BitsPerSample != 1 || image.SamplesPerPixel != 1))
            return false;

        // The JPEG reader has already applied the stream's own colour transform.
        if (image.Compression == CompressionJpeg &&
            (image.BitsPerSample != 8 || image.SampleFormat != 1 || image.SamplesPerPixel is not (1 or 3)))
            return false;

        if (image.SampleFormat is not (1 or 2 or 3))
            return false;

        // A signed sample is biased into the unsigned range of the same width.
        if (image.SampleFormat == 2 && image.BitsPerSample is not (8 or 16 or 32))
            return false;

        // Floating point samples come in all three widths the language has a type for.
        if (image.SampleFormat == 3 && image.BitsPerSample is not (16 or 32 or 64))
            return false;

        // Shared chroma changes the storage layout, which the expander below undoes first.
        if (image.Photometric == 6)
        {
            return image.BitsPerSample == 8 && image.SamplesPerPixel == 3 && image.Planar == 1 &&
                   image.ChromaAcross is 1 or 2 or 4 && image.ChromaDown is 1 or 2 or 4;
        }

        // A sensor image measures one colour a site and has to be filled out to three.
        if (image.Photometric == 32803)
        {
            return image.Cfa && image.SamplesPerPixel == 1 && image.SampleFormat == 1 &&
                   image.Planar == 1 && image.WhiteLevel > image.BlackLevel;
        }

        // Already through the camera's own demosaic, so it is ordinary colour.
        if (image.Photometric == 34892)
            return image.SamplesPerPixel is 3 or 4;

        // Anything needing a second plane or a table this reader does not build is refused.
        return image.Photometric is 0 or 1 or 2 or 3 or 5;
    }

    /// <summary>The layout the file's own samples map onto most directly.</summary>
    public static PixelFormat NaturalFormat(TiffImage image)
    {
        if (image.SampleFormat == 3)
            return image.ColourSamples >= 3 ? PixelFormat.RgbF32 : PixelFormat.LF32;

        if (image.Cfa)
            return image.BitsPerSample >= 16 ? PixelFormat.Rgb16 : PixelFormat.Rgb8;

        // Computed colour needs the wider intermediate: a table holds sixteen bit channels
        // whatever the index width, and ink resolves by arithmetic eight bits would round away.
        bool wide = image.BitsPerSample > 8 || image.Photometric is 3 or 5;
        bool alpha = image.AlphaSample >= 0;

        // A palette turns one sample into three, and ink turns four into three.
        int channels = image.Photometric switch
        {
            3 => 3,
            5 => 3,
            _ => image.ColourSamples,
        };

        return (channels, alpha) switch
        {
            (>= 3, true) => wide ? PixelFormat.Rgba16 : PixelFormat.Rgba8,
            (>= 3, false) => wide ? PixelFormat.Rgb16 : PixelFormat.Rgb8,
            (_, true) => wide ? PixelFormat.La16 : PixelFormat.La8,
            _ => wide ? PixelFormat.L16 : PixelFormat.L8,
        };
    }

    /// <summary>Bytes one row of one plane occupies in the file's own layout.</summary>
    private static int RowBytes(TiffImage image, int width)
    {
        int samples = image.Planar == 2 ? 1 : image.SamplesPerPixel;
        long bits = (long)width * samples * image.BitsPerSample;
        return bits > int.MaxValue - 7 ? 0 : (int)((bits + 7) / 8);
    }

    /// <summary>Whether the blocks the directory names lie inside the file at all.</summary>
    public static bool CanDescribe(ReadOnlySpan<byte> data, TiffImage image)
    {
        long total = 0;
        for (int i = 0; i < image.Offsets.Length; i++)
        {
            long offset = image.Offsets[i];
            long count = image.Counts[i];
            if (offset < 0 || count < 0 || offset + count > data.Length)
                return false;

            total += count;
        }

        if (total <= 0)
            return false;

        // Uncompressed data has a known length, so short blocks describe no picture.
        if (image.Compression != CompressionNone)
            return true;

        // Shared chroma stores units rather than rows, so that is what the length covers.
        if (image.Subsampled)
            return total >= TiffChroma.PackedBytes(image, image.Width, image.Height);

        int planes = image.Planar == 2 ? image.SamplesPerPixel : 1;
        return total >= (long)RowBytes(image, image.Width) * image.Height * planes;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, TiffImage image, PixelFormat target,
                                 Span<byte> destination, int stride, bool flip, out ApertureError error)
    {
        int planes = image.Planar == 2 ? image.SamplesPerPixel : 1;
        int rowBytes = RowBytes(image, image.Width);
        long surfaceSize = (long)rowBytes * image.Height * planes;

        if (surfaceSize <= 0 || surfaceSize > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] surface = BufferPool.Bytes.Rent((int)surfaceSize);
        try
        {
            Span<byte> samples = surface.AsSpan(0, (int)surfaceSize);
            samples.Clear();

            if (!Gather(data, image, samples, rowBytes, planes, out error))
                return false;

            Convert(image, samples, rowBytes, planes, target, destination, stride, flip);
            error = ApertureError.None;
            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(surface);
        }
    }

    /// <summary>Decompresses every block into its place on the flat surface.</summary>
    private static bool Gather(ReadOnlySpan<byte> data, TiffImage image, Span<byte> samples,
                               int rowBytes, int planes, out ApertureError error)
    {
        error = ApertureError.None;

        int blockWidth = image.IsTiled ? image.TileWidth : image.Width;
        int blockHeight = image.IsTiled ? image.TileLength : image.RowsPerStrip;
        int blockRowBytes = RowBytes(image, blockWidth);
        int across = image.IsTiled ? (image.Width + blockWidth - 1) / blockWidth : 1;
        int down = (image.Height + blockHeight - 1) / blockHeight;
        int perPlane = across * down;

        if (blockRowBytes <= 0 || perPlane <= 0)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        byte[] block = BufferPool.Bytes.Rent(blockRowBytes * blockHeight);
        try
        {
            for (int index = 0; index < image.Offsets.Length && index < perPlane * planes; index++)
            {
                int plane = index / perPlane;
                int within = index % perPlane;
                int column = within % across;
                int row = within / across;

                // A strip or tile at the right or bottom edge is stored full size and read short.
                int rows = Math.Min(blockHeight, image.Height - (row * blockHeight));
                if (rows <= 0)
                    continue;

                Span<byte> raw = block.AsSpan(0, blockRowBytes * blockHeight);
                raw.Clear();

                ReadOnlySpan<byte> source = data.Slice((int)image.Offsets[index], (int)image.Counts[index]);

                if (image.Compression == CompressionOldJpeg)
                {
                    if (!TiffOldJpeg.TryDecode(data, source, image.Offsets[index], image, blockWidth,
                                              rows, raw, blockRowBytes))
                    {
                        error = ApertureError.InvalidData;
                        return false;
                    }

                    CopyBlock(image, raw, samples, rowBytes, blockRowBytes, blockWidth, blockHeight,
                              plane, column, row, rows);
                    continue;
                }

                if (image.Compression is CompressionLogLuv or CompressionLogLuv24)
                {
                    if (!TiffLogLuv.TryDecode(source, raw, blockWidth, rows, image.Compression,
                                              image.SamplesPerPixel, image.LittleEndian))
                    {
                        error = ApertureError.InvalidData;
                        return false;
                    }

                    CopyBlock(image, raw, samples, rowBytes, blockRowBytes, blockWidth, blockHeight,
                              plane, column, row, rows);
                    continue;
                }

                if (image.Compression == CompressionThunder)
                {
                    if (!TiffThunder.TryDecode(source, raw, blockWidth, rows, blockRowBytes))
                    {
                        error = ApertureError.InvalidData;
                        return false;
                    }

                    CopyBlock(image, raw, samples, rowBytes, blockRowBytes, blockWidth, blockHeight,
                              plane, column, row, rows);
                    continue;
                }

                if (image.Compression == CompressionWebp)
                {
                    if (!TiffWebp.TryDecode(source, raw, blockWidth, rows, blockRowBytes,
                                            image.SamplesPerPixel))
                    {
                        error = ApertureError.InvalidData;
                        return false;
                    }

                    CopyBlock(image, raw, samples, rowBytes, blockRowBytes, blockWidth, blockHeight,
                              plane, column, row, rows);
                    continue;
                }

                if (image.Compression is CompressionFaxModified or CompressionFaxGroup3
                                        or CompressionFaxGroup4)
                {
                    // Bit order matters while the codes are read, so the decoder handles it.
                    if (!TiffFax.TryDecode(source, raw, blockWidth, rows, blockRowBytes,
                                           image.Compression, image.FaxOptions, image.FillOrder == 2))
                    {
                        error = ApertureError.InvalidData;
                        return false;
                    }

                    CopyBlock(image, raw, samples, rowBytes, blockRowBytes, blockWidth, blockHeight,
                              plane, column, row, rows);
                    continue;
                }

                if (image.Compression == CompressionJpeg)
                {
                    ReadOnlySpan<byte> tables = image.JpegTablesLength > 0
                        ? data.Slice(image.JpegTablesOffset, image.JpegTablesLength)
                        : default;

                    if (!TiffJpeg.TryDecode(tables, source, image.SamplesPerPixel, raw, blockRowBytes, blockHeight))
                    {
                        error = ApertureError.InvalidData;
                        return false;
                    }

                    CopyBlock(image, raw, samples, rowBytes, blockRowBytes, blockWidth, blockHeight,
                              plane, column, row, rows);
                    continue;
                }

                if (image.Subsampled)
                {
                    if (!GatherSubsampled(image, source, raw, blockWidth, rows, blockRowBytes))
                    {
                        error = ApertureError.InvalidData;
                        return false;
                    }

                    CopyBlock(image, raw, samples, rowBytes, blockRowBytes, blockWidth, blockHeight,
                              plane, column, row, rows);
                    continue;
                }

                if (!Decompress(image.Compression, source, raw, out int written) || written == 0)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                if (image.FillOrder == 2)
                    ReverseBits(raw[..written]);

                if (image.Predictor != 1)
                    ApplyPredictor(image, raw[..written], blockRowBytes, blockWidth, rows);

                CopyBlock(image, raw, samples, rowBytes, blockRowBytes, blockWidth, blockHeight,
                          plane, column, row, rows);
            }

            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(block);
        }
    }

    /// <summary>
    /// Reads a band whose chroma is shared between pixels. The data comes out in units rather
    /// than rows, so it is unpacked into a scratch of the size it actually occupies and then
    /// spread across the rows the rest of the reader expects.
    /// </summary>
    private static bool GatherSubsampled(TiffImage image, ReadOnlySpan<byte> source, Span<byte> raw,
                                         int width, int rows, int rowBytes)
    {
        long packed = TiffChroma.PackedBytes(image, width, rows);
        if (packed <= 0 || packed > int.MaxValue)
            return false;

        byte[] scratch = BufferPool.Bytes.Rent((int)packed);
        try
        {
            Span<byte> units = scratch.AsSpan(0, (int)packed);
            units.Clear();

            if (!Decompress(image.Compression, source, units, out int written) || written == 0)
                return false;

            if (image.FillOrder == 2)
                ReverseBits(units[..written]);

            return TiffChroma.TryExpand(units, raw, image, width, rows, rowBytes);
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
        }
    }

    private static bool Decompress(int compression, ReadOnlySpan<byte> source, Span<byte> destination,
                                   out int written)
    {
        switch (compression)
        {
            case CompressionNone:
                written = Math.Min(source.Length, destination.Length);
                source[..written].CopyTo(destination);
                return written > 0;

            case CompressionLzw:
                return TiffCodecs.TryLzw(source, destination, out written);

            case CompressionPackBits:
                return TiffCodecs.TryPackBits(source, destination, out written);

            default:
                return TiffCodecs.TryDeflate(source, destination, out written);
        }
    }

    /// <summary>
    /// Undoes the differencing a file may apply before compressing, which turns a smooth gradient
    /// into a run of equal small numbers and so gives the compressor something to work with.
    /// </summary>
    private static void ApplyPredictor(TiffImage image, Span<byte> raw, int rowBytes, int width, int rows)
    {
        int channels = image.Planar == 2 ? 1 : image.SamplesPerPixel;

        if (image.Predictor == 3)
        {
            ApplyFloatPredictor(image, raw, rowBytes, channels, rows);
            return;
        }

        for (int y = 0; y < rows; y++)
        {
            Span<byte> row = raw.Slice(y * rowBytes, Math.Min(rowBytes, raw.Length - (y * rowBytes)));

            switch (image.BitsPerSample)
            {
                case 8:
                    for (int i = channels; i < row.Length; i++)
                        row[i] += row[i - channels];
                    break;

                case 16:
                {
                    int count = row.Length / 2;
                    for (int i = channels; i < count; i++)
                    {
                        ushort previous = ReadUInt16(row[((i - channels) * 2)..], image.LittleEndian);
                        ushort current = ReadUInt16(row[(i * 2)..], image.LittleEndian);
                        WriteUInt16(row[(i * 2)..], (ushort)(current + previous), image.LittleEndian);
                    }
                    break;
                }

                case 32:
                {
                    int count = row.Length / 4;
                    for (int i = channels; i < count; i++)
                    {
                        uint previous = ReadUInt32(row[((i - channels) * 4)..], image.LittleEndian);
                        uint current = ReadUInt32(row[(i * 4)..], image.LittleEndian);
                        WriteUInt32(row[(i * 4)..], current + previous, image.LittleEndian);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The predictor for floating point samples, which differences bytes rather than values. It
    /// splits each row into byte planes first so the exponents of neighbouring samples sit
    /// together, since subtracting one float from the next gives a number no smaller than either.
    /// </summary>
    private static void ApplyFloatPredictor(TiffImage image, Span<byte> raw, int rowBytes, int channels, int rows)
    {
        int size = image.BitsPerSample / 8;
        if (size is not (2 or 4 or 8))
            return;

        byte[] scratch = BufferPool.Bytes.Rent(rowBytes);

        try
        {
            for (int y = 0; y < rows; y++)
            {
                int start = y * rowBytes;
                if (start >= raw.Length)
                    break;

                Span<byte> row = raw.Slice(start, Math.Min(rowBytes, raw.Length - start));
                int samples = row.Length / size;
                if (samples == 0)
                    continue;

                for (int i = channels; i < row.Length; i++)
                    row[i] += row[i - channels];

                Span<byte> planes = scratch.AsSpan(0, row.Length);
                row.CopyTo(planes);

                // The planes run from the most significant byte down.
                for (int i = 0; i < samples; i++)
                {
                    for (int b = 0; b < size; b++)
                    {
                        int from = image.LittleEndian ? ((size - b - 1) * samples) + i : (b * samples) + i;
                        row[(i * size) + b] = planes[from];
                    }
                }
            }
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
        }
    }

    /// <summary>Reverses the bits of every byte, for the files that store them the other way up.</summary>
    private static void ReverseBits(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            uint v = data[i];
            v = ((v & 0xF0) >> 4) | ((v & 0x0F) << 4);
            v = ((v & 0xCC) >> 2) | ((v & 0x33) << 2);
            data[i] = (byte)(((v & 0xAA) >> 1) | ((v & 0x55) << 1));
        }
    }

    private static void CopyBlock(TiffImage image, ReadOnlySpan<byte> raw, Span<byte> samples,
                                  int rowBytes, int blockRowBytes, int blockWidth, int blockHeight,
                                  int plane, int column, int row, int rows)
    {
        int planeOffset = plane * rowBytes * image.Height;
        int channels = image.Planar == 2 ? 1 : image.SamplesPerPixel;
        int bits = image.BitsPerSample;

        for (int y = 0; y < rows; y++)
        {
            int target = planeOffset + (((row * blockHeight) + y) * rowBytes);
            ReadOnlySpan<byte> line = raw.Slice(y * blockRowBytes, blockRowBytes);

            if (!image.IsTiled)
            {
                line[..Math.Min(rowBytes, line.Length)].CopyTo(samples[target..]);
                continue;
            }

            // A tile's left edge need not land on a byte, so the copy is measured in bits.
            int startBit = column * blockWidth * channels * bits;
            int width = Math.Min(blockWidth, image.Width - (column * blockWidth));
            int lengthBits = width * channels * bits;

            if ((startBit & 7) == 0 && (lengthBits & 7) == 0)
            {
                int bytes = Math.Min(lengthBits / 8, rowBytes - (startBit / 8));
                if (bytes > 0)
                    line[..bytes].CopyTo(samples[(target + (startBit / 8))..]);

                continue;
            }

            for (int i = 0; i < lengthBits; i++)
            {
                int to = startBit + i;
                if ((to >> 3) >= rowBytes)
                    break;

                int bit = (line[i >> 3] >> (7 - (i & 7))) & 1;
                ref byte slot = ref samples[target + (to >> 3)];
                int mask = 0x80 >> (to & 7);
                slot = (byte)(bit != 0 ? slot | mask : slot & ~mask);
            }
        }
    }

    /// <summary>Reads the flat surface once into the layout the caller asked for.</summary>
    private static void Convert(TiffImage image, ReadOnlySpan<byte> samples, int rowBytes, int planes,
                                PixelFormat target, Span<byte> destination, int stride, bool flip)
    {
        int width = image.Width;
        int height = image.Height;
        int channels = target.ChannelCount();
        int outputBytes = target.BytesPerChannel();

        if (image.Cfa)
        {
            ConvertCfa(image, samples, rowBytes, target, destination, stride, flip);
            return;
        }

        // The ordinary shape, which is a row to widen rather than a pixel at a time.
        if (image.BitsPerSample == 8 && planes == 1 && image.SampleFormat != 3 &&
            image.SamplesPerPixel == 3 && image.Photometric == 2 && image.AlphaSample < 0 &&
            outputBytes == 1 && channels is 3 or 4)
        {
            for (int y = 0; y < height; y++)
            {
                int line = flip ? height - 1 - y : y;
                ReadOnlySpan<byte> from = samples.Slice(y * rowBytes, width * 3);
                Span<byte> to = destination.Slice(line * stride, width * channels);

                if (channels == 3)
                    from.CopyTo(to);
                else
                    PixelConverter.ConvertRow(from, PixelFormat.Rgb8, to, PixelFormat.Rgba8, width);
            }

            return;
        }

        // Float samples in the machine's own order and channel count are the output already.
        if (image.SampleFormat == 3 && image.BitsPerSample == 32 && planes == 1 &&
            image.LittleEndian == BitConverter.IsLittleEndian &&
            channels == image.SamplesPerPixel && outputBytes == 4)
        {
            for (int y = 0; y < height; y++)
            {
                int line = flip ? height - 1 - y : y;
                samples.Slice(y * rowBytes, width * channels * 4)
                       .CopyTo(destination.Slice(line * stride, width * channels * 4));
            }

            return;
        }

        Span<int> pixel = stackalloc int[TiffImage.MaxSamples];

        for (int y = 0; y < height; y++)
        {
            int to = flip ? height - 1 - y : y;
            Span<byte> row = destination.Slice(to * stride, width * channels * outputBytes);

            for (int x = 0; x < width; x++)
            {
                if (image.SampleFormat == 3)
                {
                    WriteFloats(image, samples, rowBytes, planes, y, x, row, channels);
                    continue;
                }

                ReadSamples(image, samples, rowBytes, planes, y, x, pixel);
                WritePixel(image, pixel, row, x, channels, outputBytes, target);
            }
        }
    }

    /// <summary>
    /// The sensor path, which needs the whole picture in hand at once: what a site did not measure
    /// comes from the sites around it, so no row can be finished before its neighbours are read.
    /// </summary>
    private static void ConvertCfa(TiffImage image, ReadOnlySpan<byte> samples, int rowBytes,
                                   PixelFormat target, Span<byte> destination, int stride, bool flip)
    {
        int width = image.Width;
        int height = image.Height;
        int outputBytes = target.BytesPerChannel();
        int outputMax = outputBytes == 2 ? Full : 255;

        int range = image.WhiteLevel - image.BlackLevel;
        int[] plane = new int[width * height];
        Span<int> pixel = stackalloc int[TiffImage.MaxSamples];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ReadSamples(image, samples, rowBytes, 1, y, x, pixel);

                // The two levels say where black and full sit among the readings.
                long above = Math.Clamp(pixel[0] - image.BlackLevel, 0, range);
                plane[(y * width) + x] = (int)(((above * outputMax) + (range / 2)) / range);
            }
        }

        int[] row = new int[width * 3];

        for (int y = 0; y < height; y++)
        {
            int to = flip ? height - 1 - y : y;
            Span<byte> line = destination.Slice(to * stride, width * 3 * outputBytes);
            TiffCfa.Interpolate(image, plane, width, height, y, row);

            for (int c = 0; c < width * 3; c++)
            {
                if (outputBytes == 2)
                    BinaryPrimitives.WriteUInt16LittleEndian(line[(c * 2)..], (ushort)row[c]);
                else
                    line[c] = (byte)row[c];
            }
        }
    }

    /// <summary>
    /// The floating point path, which has nothing to normalise: the samples already say what they
    /// mean, and the only work is widening whichever of the three stored widths the file used.
    /// </summary>
    private static void WriteFloats(TiffImage image, ReadOnlySpan<byte> samples, int rowBytes, int planes,
                                    int y, int x, Span<byte> row, int channels)
    {
        int count = image.SamplesPerPixel;
        int size = image.BitsPerSample / 8;
        int to = x * channels * sizeof(float);

        for (int c = 0; c < channels; c++)
        {
            int source = Math.Min(c, count - 1);
            int plane = planes > 1 ? source : 0;
            int index = planes > 1 ? x : (x * count) + source;
            int at = (plane * rowBytes * image.Height) + (y * rowBytes) + (index * size);

            float value = image.BitsPerSample switch
            {
                16 => (float)BitConverter.UInt16BitsToHalf(ReadUInt16(samples[at..], image.LittleEndian)),
                64 => (float)BitConverter.UInt64BitsToDouble(ReadUInt64(samples[at..], image.LittleEndian)),
                _ => BitConverter.UInt32BitsToSingle(ReadUInt32(samples[at..], image.LittleEndian)),
            };

            // A fourth channel with nothing behind it is opaque, not zero.
            if (c >= count)
                value = 1f;

            BinaryPrimitives.WriteSingleLittleEndian(row[(to + (c * 4))..], value);
        }
    }

    /// <summary>Shifts a two's complement sample into the unsigned range of the same width.</summary>
    private static int Bias(int value, int bits) =>
        bits >= 32 ? value ^ int.MinValue : (value ^ (1 << (bits - 1))) & ((1 << bits) - 1);

    private static void ReadSamples(TiffImage image, ReadOnlySpan<byte> samples, int rowBytes, int planes,
                                    int y, int x, Span<int> pixel)
    {
        int bits = image.BitsPerSample;
        int count = image.SamplesPerPixel;

        for (int c = 0; c < count; c++)
        {
            int plane = planes > 1 ? c : 0;
            int index = planes > 1 ? x : (x * count) + c;
            int rowStart = (plane * rowBytes * image.Height) + (y * rowBytes);

            int value = bits switch
            {
                8 => samples[rowStart + index],
                16 => ReadUInt16(samples[(rowStart + (index * 2))..], image.LittleEndian),
                32 => unchecked((int)ReadUInt32(samples[(rowStart + (index * 4))..], image.LittleEndian)),
                64 => unchecked((int)(ReadUInt64(samples[(rowStart + (index * 8))..], image.LittleEndian) >> 32)),
                _ => ReadPacked(samples, rowStart, index, bits),
            };

            // A signed sample is shown by shifting zero to the middle of the range.
            pixel[c] = image.SampleFormat == 2 ? Bias(value, bits) : value;
        }
    }

    /// <summary>
    /// A sample of any width, read most significant bit first from wherever in the row it starts.
    /// The format allows any whole number of bits, and ten, twelve and fourteen are as common in
    /// scientific and scanned images as eight is everywhere else.
    /// </summary>
    private static int ReadPacked(ReadOnlySpan<byte> samples, int rowStart, int index, int bits)
    {
        long bit = (long)index * bits;
        int at = rowStart + (int)(bit >> 3);
        int offset = (int)(bit & 7);

        // Any sample up to thirty two bits spans at most five bytes once it is unaligned.
        ulong window = 0;
        for (int i = 0; i < 5; i++)
        {
            int from = at + i;
            window = (window << 8) | (from < samples.Length ? samples[from] : 0u);
        }

        return (int)((window >> (40 - offset - bits)) & ((1UL << bits) - 1));
    }

    /// <summary>Full intensity in the internal range every path below works in.</summary>
    private const int Full = 65535;

    private static void WritePixel(TiffImage image, ReadOnlySpan<int> pixel, Span<byte> row, int x,
                                   int channels, int outputBytes, PixelFormat target)
    {
        int at = x * channels * outputBytes;


        // Everything crosses through one range, so a table's sixteen bit channels stay wide
        // however narrow the index that found them was.
        Span<int> normal = stackalloc int[TiffImage.MaxSamples];
        int bits = image.BitsPerSample;
        long ceiling = bits >= 32 ? uint.MaxValue : (1L << bits) - 1;

        for (int c = 0; c < image.SamplesPerPixel; c++)
        {
            // Wider than the internal range, so it rounds rather than truncates.
            long raw = bits >= 32 ? (uint)pixel[c] : pixel[c];
            normal[c] = ceiling == Full ? (int)raw : (int)(((raw * Full) + (ceiling / 2)) / ceiling);
        }

        Span<int> colour = stackalloc int[4];
        int produced = Resolve(image, normal, colour);

        // An associated alpha is already multiplied into its colour, so it is divided out.
        if (image.AlphaIsAssociated && produced > 1)
        {
            int alpha = colour[produced - 1];
            if (alpha == 0)
            {
                for (int c = 0; c < produced - 1; c++)
                    colour[c] = 0;
            }
            else if (alpha < Full)
            {
                for (int c = 0; c < produced - 1; c++)
                    colour[c] = (int)Math.Min(Full, (((long)colour[c] * Full) + (alpha / 2)) / alpha);
            }
        }

        int outputMax = outputBytes == 2 ? Full : 255;

        for (int c = 0; c < channels; c++)
        {
            int value = c < produced ? colour[c] : Full;
            if (outputMax != Full)
                value = Rescale(value, Full, outputMax);

            if (outputBytes == 2)
                BinaryPrimitives.WriteUInt16LittleEndian(row[(at + (c * 2))..], (ushort)value);
            else
                row[at + c] = (byte)value;
        }
    }

    /// <summary>
    /// Turns the file's samples into colour. Which samples mean what is the photometric tag's job:
    /// it decides whether a single channel counts up from black or down from white, whether one
    /// sample is an index into a table, and whether the rest are ink rather than light.
    /// </summary>
    private static int Resolve(TiffImage image, ReadOnlySpan<int> pixel, Span<int> colour)
    {
        bool alphaPresent = image.AlphaSample >= 0;
        int alpha = alphaPresent ? pixel[image.AlphaSample] : Full;

        switch (image.Photometric)
        {
            case 0:
                colour[0] = Full - pixel[0];
                colour[1] = alpha;
                return alphaPresent ? 2 : 1;

            case 3:
            {
                // Three runs of one channel rather than triples, read at the index's own range.
                ushort[] palette = image.Palette!;
                int entries = palette.Length / 3;
                int width = image.BitsPerSample >= 16 ? Full : (1 << image.BitsPerSample) - 1;
                int index = Math.Clamp(Rescale(pixel[0], Full, width), 0, entries - 1);

                colour[0] = palette[index];
                colour[1] = palette[entries + index];
                colour[2] = palette[(entries * 2) + index];
                colour[3] = alpha;
                return alphaPresent ? 4 : 3;
            }

            case 5:
            {
                // Ink covers light rather than emitting it.
                int colourants = Math.Min(image.ColourSamples, 3);
                int key = image.ColourSamples >= 4 ? Full - pixel[3] : Full;

                for (int c = 0; c < 3; c++)
                {
                    int coverage = c < colourants ? pixel[c] : 0;
                    colour[c] = (int)((long)(Full - coverage) * key / Full);
                }

                colour[3] = alpha;
                return alphaPresent ? 4 : 3;
            }

            case 6:
            {
                // Luma with two differences. The weights and range come from the file, so one
                // pass covers every set of primaries.
                double[] range = image.ReferenceBlackWhite;
                double y = Stretch(pixel[0], range[0], range[1], 255);
                double cb = Stretch(pixel[1], range[2], range[3], 127.5);
                double cr = Stretch(pixel[2], range[4], range[5], 127.5);

                double red = (cr * (2 - (2 * image.LumaRed))) + y;
                double blue = (cb * (2 - (2 * image.LumaBlue))) + y;
                double green = (y - (image.LumaBlue * blue) - (image.LumaRed * red)) / image.LumaGreen;

                colour[0] = Clamp(red);
                colour[1] = Clamp(green);
                colour[2] = Clamp(blue);
                colour[3] = alpha;
                return alphaPresent ? 4 : 3;
            }

            default:
            {
                int count = Math.Min(image.ColourSamples, 3);
                for (int c = 0; c < count; c++)
                    colour[c] = pixel[c];

                colour[count] = alpha;
                return alphaPresent ? count + 1 : count;
            }
        }
    }

    /// <summary>
    /// Moves a value between two ranges, rounding rather than truncating. Half a step matters
    /// here: narrowing sixteen bits to eight by truncation reads a step low on most values, which
    /// is visible against any reader that rounds.
    /// </summary>
    /// <summary>Puts one stored channel back on the scale its two reference points describe.</summary>
    private static double Stretch(int value, double black, double white, double span) =>
        ((value * 255.0 / Full) - black) * span / (white - black);

    /// <summary>Takes a channel on the nought to two hundred and fifty five scale into the internal one.</summary>
    private static int Clamp(double value) =>
        (int)Math.Clamp((value * Full / 255.0) + 0.5, 0, Full);

    private static int Rescale(int value, int from, int to) =>
        from == to ? value : (int)((((long)value * to) + (from / 2)) / from);

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(data)
        : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(data)
        : BinaryPrimitives.ReadUInt32BigEndian(data);

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, bool little) => little
        ? BinaryPrimitives.ReadUInt64LittleEndian(data)
        : BinaryPrimitives.ReadUInt64BigEndian(data);

    private static void WriteUInt16(Span<byte> data, ushort value, bool little)
    {
        if (little)
            BinaryPrimitives.WriteUInt16LittleEndian(data, value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(data, value);
    }

    private static void WriteUInt32(Span<byte> data, uint value, bool little)
    {
        if (little)
            BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(data, value);
    }
}
