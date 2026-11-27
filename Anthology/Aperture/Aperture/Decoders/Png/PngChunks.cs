// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Png;

/// <summary>
/// The parts of a PNG a decode needs, found by one walk of the chunk list, which is also where the
/// file is checked for structural sense. The palette and transparency tables are spans into the
/// source rather than copies, and the compressed stream is measured here and copied out later.
/// </summary>
internal readonly ref struct PngChunks
{
    /// <summary>Cap on chunks walked, so a file of empty chunks cannot stall the decode.</summary>
    private const int MaxChunks = 1 << 20;

    /// <summary>The palette, three bytes per entry, empty when the file has none.</summary>
    public ReadOnlySpan<byte> Palette { get; init; }

    /// <summary>The transparency table, whose meaning depends on the colour type.</summary>
    public ReadOnlySpan<byte> Transparency { get; init; }

    /// <summary>Total bytes across every image data chunk.</summary>
    public int CompressedLength { get; init; }

    /// <summary>Colour type from the header.</summary>
    public byte ColourType { get; init; }

    /// <summary>Bit depth from the header.</summary>
    public byte BitDepth { get; init; }

    /// <summary>Whether the image is stored as seven interlaced passes.</summary>
    public bool Interlaced { get; init; }

    /// <summary>Whether a stream that ends early should still yield the rows it produced.</summary>
    public bool AllowTruncated { get; init; }

    /// <summary>The animation's frames in order, or nothing when the file carries none.</summary>
    public PngAnimationFrame[]? Frames { get; init; }

    /// <summary>
    /// Whether the default image is the animation's first frame. A file that opens a frame before
    /// its image data says so; one that does not is holding a still picture that the animation
    /// never shows, and that picture is a frame of the file even though it is not one of the
    /// animation.
    /// </summary>
    public bool DefaultIsFrame { get; init; }

    /// <summary>Number of palette entries.</summary>
    public int PaletteLength => Palette.Length / 3;

    /// <summary>
    /// Walks the chunk list, measuring the compressed stream, finding the tables and rejecting a
    /// file whose structure does not hold together.
    /// </summary>
    public static bool TryScan(ReadOnlySpan<byte> data, DecodeOptions options,
                               int canvasWidth, int canvasHeight,
                               out PngChunks chunks, out ApertureError error)
    {
        chunks = default;
        error = ApertureError.None;

        if (data.Length < 8 + 25)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        byte bitDepth = data[24];
        byte colourType = data[25];
        bool interlaced = data[28] == 1;
        bool allowTruncated = options.AllowTruncated;
        bool validate = options.ValidateChecksums;

        ReadOnlySpan<byte> palette = default;
        ReadOnlySpan<byte> transparency = default;
        long compressed = 0;
        int offset = 8;

        bool sawEnd = false;
        bool sawAnimationControl = false;
        bool sawFrameData = false;
        long declaredFrames = 0;
        int frameControlCount = 0;
        long nextSequence = 0;

        List<PngAnimationFrame> frames = [];
        bool defaultIsFrame = false;
        bool sawData = false;

        for (int scanned = 0; scanned < MaxChunks; scanned++)
        {
            if (offset + 8 > data.Length)
                break;

            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > int.MaxValue)
                break;

            ReadOnlySpan<byte> type = data.Slice(offset + 4, 4);
            long next = (long)offset + 12 + length;

            // A final chunk that runs past the end still contributes whatever bytes are there,
            // which is what lets a truncated download decode as far as it got.
            if (next > data.Length)
            {
                if (type.SequenceEqual("IDAT"u8) && offset + 8 < data.Length)
                    compressed += data.Length - (offset + 8);
                break;
            }

            ReadOnlySpan<byte> payload = data.Slice(offset + 8, (int)length);

            if (validate && !IsChecksumValid(data, offset, (int)length))
            {
                error = ApertureError.ChecksumMismatch;
                return false;
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                compressed += payload.Length;
                sawData = true;

                // An fcTL ahead of the default image makes that image the animation's first
                // frame, so its data belongs to the frame that fcTL opened.
                if (defaultIsFrame && frames.Count == 1)
                    frames[0].Data.Add((offset + 8, payload.Length));
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                // A palette is a whole number of three byte entries and indexes at most 256.
                if (payload.Length % 3 != 0 || payload.Length > 256 * 3)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
                palette = payload;
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (!IsTransparencyValid(payload, colourType, palette.Length / 3))
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
                transparency = payload;
            }
            else if (type.SequenceEqual("acTL"u8))
            {
                if (payload.Length < 8)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
                declaredFrames = BinaryPrimitives.ReadUInt32BigEndian(payload);
                sawAnimationControl = true;
            }
            else if (type.SequenceEqual("fcTL"u8))
            {
                if (!IsFrameControlValid(payload, canvasWidth, canvasHeight, ref nextSequence))
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
                frameControlCount++;

                if (frames.Count == 0 && !sawData)
                    defaultIsFrame = true;

                frames.Add(ReadFrameControl(payload));
            }
            else if (type.SequenceEqual("fdAT"u8))
            {
                if (payload.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(payload) != nextSequence)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
                nextSequence++;
                sawFrameData = true;

                // The first four bytes are the ordering number rather than image data.
                if (frames.Count > 0)
                    frames[^1].Data.Add((offset + 12, payload.Length - 4));
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                sawEnd = true;
                break;
            }

            offset = (int)next;
        }

        if (compressed <= 0)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        // A file with no end marker was cut short, whatever else survived.
        if (!sawEnd && !allowTruncated)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (!ValidateAnimation(sawAnimationControl, sawFrameData, declaredFrames, frameControlCount, allowTruncated))
        {
            error = ApertureError.InvalidData;
            return false;
        }

        chunks = new PngChunks
        {
            Palette = palette,
            Transparency = transparency,
            CompressedLength = (int)Math.Min(compressed, int.MaxValue),
            ColourType = colourType,
            BitDepth = bitDepth,
            Interlaced = interlaced,
            AllowTruncated = allowTruncated,
            Frames = frames.Count > 0 ? [.. frames] : null,
            DefaultIsFrame = defaultIsFrame,
        };
        return true;
    }

    /// <summary>Checks a chunk's trailing CRC against its type and payload.</summary>
    private static bool IsChecksumValid(ReadOnlySpan<byte> data, int offset, int length)
    {
        int end = offset + 8 + length;
        if (end + 4 > data.Length)
            return false;

        uint stored = BinaryPrimitives.ReadUInt32BigEndian(data[end..]);
        return Crc32.Compute(data.Slice(offset + 4, 4 + length)) == stored;
    }

    /// <summary>
    /// A transparency chunk means something different for each colour type: one sample for
    /// greyscale, three for truecolour, and one alpha byte per palette entry.
    /// </summary>
    private static bool IsTransparencyValid(ReadOnlySpan<byte> payload, byte colourType, int paletteEntries) =>
        colourType switch
        {
            0 => payload.Length == 2,
            2 => payload.Length == 6,
            3 => payload.Length <= Math.Max(paletteEntries, 0) && payload.Length <= 256,
            // Colour types that already carry alpha may not also carry a transparency chunk.
            _ => false,
        };

    /// <summary>
    /// Checks one frame control block: the sequence numbers across the animation chunks form one
    /// run starting at zero, and every frame rectangle lies inside the canvas.
    /// </summary>
    private static bool IsFrameControlValid(ReadOnlySpan<byte> payload, int canvasWidth, int canvasHeight,
                                            ref long nextSequence)
    {
        if (payload.Length < 26)
            return false;

        if (BinaryPrimitives.ReadUInt32BigEndian(payload) != nextSequence)
            return false;

        uint width = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(payload[8..]);
        uint offsetX = BinaryPrimitives.ReadUInt32BigEndian(payload[12..]);
        uint offsetY = BinaryPrimitives.ReadUInt32BigEndian(payload[16..]);

        if (width == 0 || height == 0)
            return false;

        if ((long)offsetX + width > canvasWidth || (long)offsetY + height > canvasHeight)
            return false;

        // Dispose is clear, background or previous; blend is source or over. A delay
        // denominator of zero is legal and means hundredths of a second.
        if (payload[24] > 2 || payload[25] > 1)
            return false;

        nextSequence++;
        return true;
    }

    /// <summary>Reads a frame's geometry and timing out of its control chunk.</summary>
    private static PngAnimationFrame ReadFrameControl(ReadOnlySpan<byte> payload) => new()
    {
        Width = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[4..]),
        Height = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[8..]),
        Left = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[12..]),
        Top = (int)BinaryPrimitives.ReadUInt32BigEndian(payload[16..]),
        DelayNumerator = BinaryPrimitives.ReadUInt16BigEndian(payload[20..]),
        DelayDenominator = BinaryPrimitives.ReadUInt16BigEndian(payload[22..]),
        Dispose = payload[24],
        Blend = payload[25],
    };

    /// <summary>Copies one frame's data payloads into a buffer of their own.</summary>
    public static int CopyFrameTo(ReadOnlySpan<byte> data, PngAnimationFrame frame, Span<byte> destination)
    {
        int written = 0;
        foreach ((int offset, int length) in frame.Data)
        {
            if (offset < 0 || length <= 0 || offset + length > data.Length)
                continue;

            int room = Math.Min(length, destination.Length - written);
            data.Slice(offset, room).CopyTo(destination[written..]);
            written += room;
        }

        return written;
    }

    /// <summary>Checks that the animation control chunk and the frames it promises agree.</summary>
    private static bool ValidateAnimation(bool sawAnimationControl, bool sawFrameData, long declaredFrames,
                                          int frameControlCount, bool allowTruncated)
    {
        // Frame chunks without an animation control chunk have nothing to belong to.
        if (!sawAnimationControl)
            return frameControlCount == 0 && !sawFrameData;

        if (declaredFrames is < 1 or > int.MaxValue)
            return false;

        if (allowTruncated)
            return true;

        if (frameControlCount != declaredFrames)
            return false;

        // More than one frame means at least one of them is not the default image, and a frame
        // that is not the default image has to bring its own data.
        return declaredFrames <= 1 || sawFrameData;
    }

    /// <summary>Copies every image data payload into one contiguous buffer.</summary>
    public int CopyCompressedTo(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        int written = 0;
        int offset = 8;

        for (int scanned = 0; scanned < MaxChunks; scanned++)
        {
            if (offset + 8 > data.Length)
                break;

            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > int.MaxValue)
                break;

            ReadOnlySpan<byte> type = data.Slice(offset + 4, 4);
            long next = (long)offset + 12 + length;

            if (next > data.Length)
            {
                if (type.SequenceEqual("IDAT"u8) && offset + 8 < data.Length)
                {
                    ReadOnlySpan<byte> tail = data[(offset + 8)..];
                    int room = Math.Min(tail.Length, destination.Length - written);
                    tail[..room].CopyTo(destination[written..]);
                    written += room;
                }
                break;
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                ReadOnlySpan<byte> payload = data.Slice(offset + 8, (int)length);
                int room = Math.Min(payload.Length, destination.Length - written);
                payload[..room].CopyTo(destination[written..]);
                written += room;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset = (int)next;
        }

        return written;
    }
}
