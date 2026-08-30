// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Prowl.Aperture.Decoders;

namespace Prowl.Aperture.Encoders;

/// <summary>
/// Writes PNG. Greyscale, greyscale with alpha, truecolour and truecolour with alpha, at eight or
/// sixteen bits a channel, which is every layout the format defines that does not need a palette.
/// </summary>
internal sealed class PngEncoder : IImageEncoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <inheritdoc />
    public ImageFormat Format => ImageFormat.Png;

    /// <inheritdoc />
    public string FileExtension => ".png";

    /// <inheritdoc />
    public IReadOnlyList<PixelFormat> SupportedPixelFormats { get; } =
    [
        PixelFormat.L8, PixelFormat.La8, PixelFormat.Rgb8, PixelFormat.Rgba8,
        PixelFormat.L16, PixelFormat.La16, PixelFormat.Rgb16, PixelFormat.Rgba16,
    ];

    /// <inheritdoc />
    public PixelFormat PreferredPixelFormat => PixelFormat.Rgba8;

    /// <inheritdoc />
    public bool TryEncode(ImageFrame frame, EncodeOptions options, Stream destination, out ApertureError error)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(destination);

        if (!TryDescribe(frame.PixelFormat, out byte colourType, out byte bitDepth))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        if (frame.Width <= 0 || frame.Height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        int width = frame.Width;
        int height = frame.Height;
        int bytesPerPixel = frame.PixelFormat.BytesPerPixel();
        int rowBytes = width * bytesPerPixel;

        destination.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = bitDepth;
        header[9] = colourType;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(destination, "IHDR"u8, header);

        WriteText(destination, options.Png.TextEntries);
        WriteImageData(destination, frame, options, rowBytes, bytesPerPixel, bitDepth);
        WriteChunk(destination, "IEND"u8, default);

        error = ApertureError.None;
        return true;
    }

    /// <summary>The colour type and sample width that stand for a layout, if the format has one.</summary>
    private static bool TryDescribe(PixelFormat format, out byte colourType, out byte bitDepth)
    {
        int type = format switch
        {
            PixelFormat.L8 or PixelFormat.L16 => 0,
            PixelFormat.Rgb8 or PixelFormat.Rgb16 => 2,
            PixelFormat.La8 or PixelFormat.La16 => 4,
            PixelFormat.Rgba8 or PixelFormat.Rgba16 => 6,
            _ => -1,
        };

        colourType = (byte)Math.Max(type, 0);
        bitDepth = (byte)(format.BytesPerChannel() == 2 ? 16 : 8);
        return type >= 0;
    }

    /// <summary>
    /// Filters every row and deflates the result into one IDAT. The rows go through the filter a
    /// row at a time and straight into the compressor, so nothing the size of the image is held
    /// beyond the two rows the filters need and whatever the compressor buffers.
    /// </summary>
    private static void WriteImageData(Stream destination, ImageFrame frame, EncodeOptions options,
                                       int rowBytes, int bytesPerPixel, int bitDepth)
    {
        using MemoryStream compressed = new();
        using (ZLibStream deflate = new(compressed, Level(options.Effort), leaveOpen: true))
        {
            byte[] current = BufferPool.Bytes.Rent(rowBytes);
            byte[] previous = BufferPool.Bytes.Rent(rowBytes);
            byte[] filtered = BufferPool.Bytes.Rent(rowBytes + 1);
            byte[] scratch = BufferPool.Bytes.Rent(rowBytes);

            try
            {
                Array.Clear(previous, 0, rowBytes);

                for (int y = 0; y < frame.Height; y++)
                {
                    int from = options.FlipVertically ? frame.Height - 1 - y : y;
                    frame.GetRow(from)[..rowBytes].CopyTo(current);

                    // Sixteen bit samples are written most significant byte first whatever the
                    // machine holds them as.
                    if (bitDepth == 16 && BitConverter.IsLittleEndian)
                        SwapPairs(current.AsSpan(0, rowBytes));

                    int length = Filter(options.Png.Filter, current, previous, filtered, scratch,
                                        rowBytes, bytesPerPixel);
                    deflate.Write(filtered, 0, length);

                    (previous, current) = (current, previous);
                }
            }
            finally
            {
                BufferPool.Bytes.Return(scratch);
                BufferPool.Bytes.Return(filtered);
                BufferPool.Bytes.Return(previous);
                BufferPool.Bytes.Return(current);
            }
        }

        if (!compressed.TryGetBuffer(out ArraySegment<byte> payload))
            payload = compressed.ToArray();

        WriteChunk(destination, "IDAT"u8, payload.AsSpan());
    }

    private static CompressionLevel Level(CompressionEffort effort) => effort switch
    {
        CompressionEffort.None => CompressionLevel.NoCompression,
        CompressionEffort.Fastest => CompressionLevel.Fastest,
        CompressionEffort.Smallest => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal,
    };

    private static void SwapPairs(Span<byte> row)
    {
        for (int i = 0; i + 1 < row.Length; i += 2)
            (row[i], row[i + 1]) = (row[i + 1], row[i]);
    }

    /// <summary>
    /// Writes the filter byte and the filtered row into <paramref name="destination"/>, returning
    /// how much of it was used. Adaptive tries all five and keeps the smallest.
    /// </summary>
    private static int Filter(PngFilterStrategy strategy, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous,
                              Span<byte> destination, Span<byte> scratch, int rowBytes, int bytesPerPixel)
    {
        if (strategy != PngFilterStrategy.Adaptive)
        {
            destination[0] = (byte)FixedFilter(strategy);
            Apply(destination[0], row, previous, destination[1..], rowBytes, bytesPerPixel);
            return rowBytes + 1;
        }

        int best = -1;
        long bestScore = long.MaxValue;

        for (int filter = 0; filter <= 4; filter++)
        {
            Apply((byte)filter, row, previous, scratch, rowBytes, bytesPerPixel);
            long score = Deviation(scratch[..rowBytes]);

            if (score >= bestScore)
                continue;

            bestScore = score;
            best = filter;
            scratch[..rowBytes].CopyTo(destination[1..]);
        }

        destination[0] = (byte)best;
        return rowBytes + 1;
    }

    private static int FixedFilter(PngFilterStrategy strategy) => strategy switch
    {
        PngFilterStrategy.Sub => 1,
        PngFilterStrategy.Up => 2,
        PngFilterStrategy.Average => 3,
        PngFilterStrategy.Paeth => 4,
        _ => 0,
    };

    /// <summary>
    /// How far a filtered row sits from nothing, reading each byte as a signed difference. The
    /// row that deflate can say most about is the one whose bytes are nearest zero.
    /// </summary>
    private static long Deviation(ReadOnlySpan<byte> row)
    {
        long total = 0;
        foreach (byte value in row)
            total += value < 128 ? value : 256 - value;

        return total;
    }

    private static void Apply(byte filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous,
                              Span<byte> destination, int rowBytes, int bytesPerPixel)
    {
        switch (filter)
        {
            case 0:
                row[..rowBytes].CopyTo(destination);
                break;

            case 1:
                for (int i = 0; i < bytesPerPixel; i++)
                    destination[i] = row[i];
                for (int i = bytesPerPixel; i < rowBytes; i++)
                    destination[i] = (byte)(row[i] - row[i - bytesPerPixel]);
                break;

            case 2:
                for (int i = 0; i < rowBytes; i++)
                    destination[i] = (byte)(row[i] - previous[i]);
                break;

            case 3:
                for (int i = 0; i < bytesPerPixel; i++)
                    destination[i] = (byte)(row[i] - (previous[i] >> 1));
                for (int i = bytesPerPixel; i < rowBytes; i++)
                    destination[i] = (byte)(row[i] - ((row[i - bytesPerPixel] + previous[i]) >> 1));
                break;

            default:
                for (int i = 0; i < bytesPerPixel; i++)
                    destination[i] = (byte)(row[i] - previous[i]);
                for (int i = bytesPerPixel; i < rowBytes; i++)
                {
                    destination[i] = (byte)(row[i] - Paeth(row[i - bytesPerPixel], previous[i],
                                                           previous[i - bytesPerPixel]));
                }

                break;
        }
    }

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;
        int fromLeft = Math.Abs(estimate - left);
        int fromAbove = Math.Abs(estimate - above);
        int fromUpperLeft = Math.Abs(estimate - upperLeft);

        if (fromLeft <= fromAbove && fromLeft <= fromUpperLeft)
            return left;

        return fromAbove <= fromUpperLeft ? above : upperLeft;
    }

    private static void WriteText(Stream destination, IReadOnlyDictionary<string, string>? entries)
    {
        if (entries is null)
            return;

        foreach ((string keyword, string text) in entries)
        {
            if (keyword.Length is 0 or > 79)
                continue;

            byte[] key = Encoding.Latin1.GetBytes(keyword);
            byte[] value = Encoding.Latin1.GetBytes(text);
            byte[] payload = new byte[key.Length + 1 + value.Length];

            key.CopyTo(payload, 0);
            payload[key.Length] = 0;
            value.CopyTo(payload, key.Length + 1);

            WriteChunk(destination, "tEXt"u8, payload);
        }
    }

    /// <summary>Writes one chunk: its length, its type, its payload, and the CRC of the last two.</summary>
    private static void WriteChunk(Stream destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)payload.Length);
        destination.Write(length);
        destination.Write(type);
        destination.Write(payload);

        byte[] covered = BufferPool.Bytes.Rent(type.Length + payload.Length);
        try
        {
            type.CopyTo(covered);
            payload.CopyTo(covered.AsSpan(type.Length));

            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(covered.AsSpan(0, type.Length + payload.Length)));
            destination.Write(crc);
        }
        finally
        {
            BufferPool.Bytes.Return(covered);
        }
    }
}
