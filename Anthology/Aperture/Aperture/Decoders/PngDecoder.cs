// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Binary;
using Prowl.Aperture.Decoders.Png;
using Prowl.Aperture.Metadata;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads PNG and its APNG animation extension.</summary>
public sealed class PngDecoder : DecoderBase
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Largest number of chunks walked while identifying, so a crafted file cannot spin.</summary>
    private const int MaxChunksScanned = 4096;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Png;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".png", ".apng"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!PngChunks.TryScan(data, options, info.Width, info.Height, out PngChunks chunks, out error))
            return false;

        PixelFormat natural = info.PreferredPixelFormat;
        PixelFormat target = options.TargetPixelFormat ?? natural;
        if (!PixelConverter.CanConvert(natural, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        int stride = options.GetStride(info.Width, target);
        long total = (long)stride * info.Height;
        if (total > options.MaxAllocationBytes || total > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        // A default image outside the animation is one picture more than the animation counts.
        int frameCount = chunks.Frames is null ? 1
            : chunks.Frames.Length + (chunks.DefaultIsFrame ? 0 : 1);

        if (options.DecodeAllFrames && chunks.Frames is { Length: > 0 } frames && frameCount > 1)
            return TryDecodeAnimation(data, chunks, frames, options, info, target, stride, out image, out error);

        if (!TryDecodeStill(data, chunks, options, info, target, stride, out ImageFrame? still, out error))
            return false;

        image = new Image
        {
            Format = ImageFormat.Png,
            Width = info.Width,
            Height = info.Height,
            PixelFormat = target,
            Frames = new[] { still! },
            Info = info,
        };

        return true;
    }

    /// <summary>Decodes the default image, which is the whole of a file that holds one picture.</summary>
    private static bool TryDecodeStill(ReadOnlySpan<byte> data, in PngChunks chunks,
                                       DecodeOptions options, ImageInfo info, PixelFormat target,
                                       int stride, out ImageFrame? frame, out ApertureError error)
    {
        frame = null;

        PixelFormat natural = info.PreferredPixelFormat;
        long total = (long)stride * info.Height;

        // Only an unusual pairing, such as greyscale out of a truecolour file, needs the
        // file's own layout first.
        bool direct = PngScanline.CanWriteDirectly(chunks.ColourType, chunks.BitDepth, natural, target);
        int naturalStride = direct ? stride : info.Width * natural.BytesPerPixel();

        byte[] buffer = options.UsePooledMemory
            ? BufferPool.Bytes.Rent((int)total)
            : new byte[(int)total];

        byte[]? scratch = null;
        try
        {
            Span<byte> pixels = buffer.AsSpan(0, (int)total);
            pixels.Clear();

            Span<byte> surface = pixels;
            int surfaceStride = stride;

            if (!direct)
            {
                // Decode into the stored layout, then convert once per row.
                long naturalTotal = (long)naturalStride * info.Height;
                if (naturalTotal > options.MaxAllocationBytes || naturalTotal > int.MaxValue)
                {
                    error = ApertureError.LimitExceeded;
                    return false;
                }

                scratch = BufferPool.Bytes.Rent((int)naturalTotal);
                surface = scratch.AsSpan(0, (int)naturalTotal);
                surface.Clear();
                surfaceStride = naturalStride;
            }

            if (!PngImageReader.TryDecode(data, chunks, info, direct ? target : natural,
                                          surface, surfaceStride, options.FlipVertically, out error))
                return false;

            if (!direct)
            {
                for (int y = 0; y < info.Height; y++)
                {
                    PixelConverter.ConvertRow(surface.Slice(y * surfaceStride, naturalStride), natural,
                                              pixels[(y * stride)..], target, info.Width);
                }
            }

            frame = new ImageFrame(buffer, (int)total, info.Width, info.Height, stride, target,
                                   options.UsePooledMemory);
            buffer = [];
            error = ApertureError.None;
            return true;
        }
        finally
        {
            if (scratch is not null)
                BufferPool.Bytes.Return(scratch);
            if (buffer.Length != 0 && options.UsePooledMemory)
                BufferPool.Bytes.Return(buffer);
        }
    }

    /// <summary>
    /// Draws every frame onto a canvas in turn, which is what an animated file describes: each
    /// frame covers a rectangle of the picture and says what should be underneath it and what
    /// should be left behind once it has been shown.
    /// </summary>
    private static bool TryDecodeAnimation(ReadOnlySpan<byte> data, in PngChunks chunks,
                                           PngAnimationFrame[] frames, DecodeOptions options,
                                           ImageInfo info, PixelFormat target, int stride,
                                           out Image? image, out ApertureError error)
    {
        image = null;

        int width = info.Width;
        int height = info.Height;
        uint[] canvas = new uint[(long)width * height <= int.MaxValue ? width * height : 0];
        if (canvas.Length == 0)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        uint[] saved = new uint[canvas.Length];
        List<ImageFrame> output = [];

        try
        {
            if (!chunks.DefaultIsFrame)
            {
                if (!TryDecodeStill(data, chunks, options, info, target, stride,
                                    out ImageFrame? still, out error))
                    return false;

                output.Add(still!);
            }

            int count = Math.Min(frames.Length, options.MaxFrames - output.Count);
            for (int i = 0; i < count; i++)
            {
                PngAnimationFrame frame = frames[i];

                if (frame.Dispose == 2)
                    canvas.CopyTo(saved, 0);

                if (!TryDrawFrame(data, chunks, frame, options, canvas, width, i == 0, out error))
                    return false;

                output.Add(Snapshot(canvas, width, height, stride, target, frame, options));

                if (frame.Dispose == 1)
                    ClearRectangle(canvas, width, frame);
                else if (frame.Dispose == 2)
                    saved.CopyTo(canvas, 0);
            }

            if (output.Count == 0)
            {
                error = ApertureError.InvalidData;
                return false;
            }

            image = new Image
            {
                Format = ImageFormat.Png,
                Width = width,
                Height = height,
                PixelFormat = target,
                Frames = [.. output],
                Info = info,
            };

            output = [];
            error = ApertureError.None;
            return true;
        }
        finally
        {
            foreach (ImageFrame frame in output)
                frame.Release();
        }
    }

    /// <summary>Decodes one frame's own picture and puts it onto the canvas where it belongs.</summary>
    private static bool TryDrawFrame(ReadOnlySpan<byte> data, in PngChunks chunks,
                                     PngAnimationFrame frame, DecodeOptions options, uint[] canvas,
                                     int canvasWidth, bool first, out ApertureError error)
    {
        int frameStride = frame.Width * 4;
        long frameTotal = (long)frameStride * frame.Height;
        if (frameTotal <= 0 || frameTotal > options.MaxAllocationBytes || frameTotal > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        PngChunks own = new()
        {
            Palette = chunks.Palette,
            Transparency = chunks.Transparency,
            CompressedLength = frame.CompressedLength,
            ColourType = chunks.ColourType,
            BitDepth = chunks.BitDepth,
            Interlaced = chunks.Interlaced,
            AllowTruncated = chunks.AllowTruncated,
        };

        ImageInfo shape = new() { Format = ImageFormat.Png, Width = frame.Width, Height = frame.Height };
        byte[] pixels = BufferPool.Bytes.Rent((int)frameTotal);

        try
        {
            Span<byte> surface = pixels.AsSpan(0, (int)frameTotal);
            surface.Clear();

            if (!PngImageReader.TryDecodeFrame(data, own, frame, shape, PixelFormat.Rgba8,
                                               surface, frameStride, out error))
                return false;

            // Blending the first frame over an empty canvas would lose the colour it carries.
            bool replace = first || frame.Blend == 0;

            for (int y = 0; y < frame.Height; y++)
            {
                ReadOnlySpan<byte> row = surface.Slice(y * frameStride, frameStride);
                int to = ((frame.Top + y) * canvasWidth) + frame.Left;

                for (int x = 0; x < frame.Width; x++)
                {
                    uint colour = (uint)(row[x * 4] | (row[(x * 4) + 1] << 8) |
                                         (row[(x * 4) + 2] << 16) | (row[(x * 4) + 3] << 24));

                    canvas[to + x] = replace ? colour : Over(canvas[to + x], colour);
                }
            }

            error = ApertureError.None;
            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(pixels);
        }
    }

    /// <summary>The source over rule, on colours that are not multiplied by their alpha.</summary>
    private static uint Over(uint under, uint over)
    {
        uint alpha = over >> 24;
        if (alpha == 255 || (under >> 24) == 0)
            return over;

        if (alpha == 0)
            return under;

        uint beneath = under >> 24;
        uint combined = alpha + (beneath * (255 - alpha) / 255);
        uint result = combined << 24;

        for (int shift = 0; shift <= 16; shift += 8)
        {
            uint top = (over >> shift) & 0xFF;
            uint bottom = (under >> shift) & 0xFF;
            uint mixed = ((top * alpha) + (bottom * beneath * (255 - alpha) / 255)) / Math.Max(combined, 1);
            result |= Math.Min(mixed, 255) << shift;
        }

        return result;
    }

    /// <summary>Clears a frame's rectangle, which is what disposing to the background means.</summary>
    private static void ClearRectangle(uint[] canvas, int canvasWidth, PngAnimationFrame frame)
    {
        for (int y = 0; y < frame.Height; y++)
            canvas.AsSpan(((frame.Top + y) * canvasWidth) + frame.Left, frame.Width).Clear();
    }

    /// <summary>Takes the canvas as it stands into a frame in the layout the caller asked for.</summary>
    private static ImageFrame Snapshot(uint[] canvas, int width, int height, int stride,
                                       PixelFormat target, PngAnimationFrame frame, DecodeOptions options)
    {
        int total = stride * height;
        byte[] buffer = options.UsePooledMemory ? BufferPool.Bytes.Rent(total) : new byte[total];
        Span<byte> pixels = buffer.AsSpan(0, total);
        pixels.Clear();

        byte[] scratch = BufferPool.Bytes.Rent(width * 4);

        try
        {
            Span<byte> line = scratch.AsSpan(0, width * 4);

            for (int y = 0; y < height; y++)
            {
                int from = options.FlipVertically ? height - 1 - y : y;
                for (int x = 0; x < width; x++)
                {
                    uint colour = canvas[(from * width) + x];
                    line[x * 4] = (byte)colour;
                    line[(x * 4) + 1] = (byte)(colour >> 8);
                    line[(x * 4) + 2] = (byte)(colour >> 16);
                    line[(x * 4) + 3] = (byte)(colour >> 24);
                }

                PixelConverter.ConvertRow(line, PixelFormat.Rgba8, pixels[(y * stride)..], target, width);
            }
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
        }

        return new ImageFrame(buffer, total, width, height, stride, target, options.UsePooledMemory)
        {
            Delay = frame.Delay,
            Disposal = frame.Dispose == 1 ? FrameDisposal.RestoreBackground
                : frame.Dispose == 2 ? FrameDisposal.RestorePrevious
                : FrameDisposal.None,
        };
    }

    /// <inheritdoc />
    protected override ImageMetadata ReadMetadata(ReadOnlySpan<byte> data)
    {
        MetadataBuilder builder = new();
        int offset = 8;

        for (int scanned = 0; scanned < MaxMetadataChunks && offset + 8 <= data.Length; scanned++)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > int.MaxValue)
                break;

            ReadOnlySpan<byte> type = data.Slice(offset + 4, 4);
            long next = (long)offset + 12 + length;
            if (next > data.Length)
                break;

            ReadOnlySpan<byte> payload = data.Slice(offset + 8, (int)length);

            if (type.SequenceEqual("iCCP"u8))
            {
                // A name, then the method byte, then the profile itself under a zlib wrapper.
                ReadOnlySpan<byte> name = MetadataBuilder.UpToNull(payload);
                if (name.Length + 2 <= payload.Length && payload[name.Length + 1] == 0)
                    builder.SetDeflatedProfile(payload[(name.Length + 2)..]);
            }
            else if (type.SequenceEqual("eXIf"u8))
            {
                builder.SetExif(payload);
            }
            else if (type.SequenceEqual("tEXt"u8) || type.SequenceEqual("zTXt"u8) ||
                     type.SequenceEqual("iTXt"u8))
            {
                ReadText(type, payload, builder);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset = (int)next;
        }

        return builder.Build();
    }

    /// <summary>Cap on chunks walked for metadata, matching the one the pixel scan uses.</summary>
    private const int MaxMetadataChunks = 1 << 20;

    /// <summary>
    /// The three text chunks, which differ only in whether the text is compressed and whether it
    /// carries a language. An XMP packet lives in one of them under a known keyword.
    /// </summary>
    private static void ReadText(ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload, MetadataBuilder builder)
    {
        ReadOnlySpan<byte> keyword = MetadataBuilder.UpToNull(payload);
        if (keyword.Length >= payload.Length)
            return;

        ReadOnlySpan<byte> rest = payload[(keyword.Length + 1)..];
        byte[]? owned = null;

        if (type.SequenceEqual("zTXt"u8))
        {
            if (rest.Length < 1 || rest[0] != 0)
                return;

            owned = MetadataBuilder.TryInflate(rest[1..]);
            if (owned is null)
                return;

            rest = owned;
        }
        else if (type.SequenceEqual("iTXt"u8))
        {
            if (rest.Length < 2)
                return;

            bool compressed = rest[0] != 0;
            rest = rest[2..];

            // A language tag and a translated keyword sit between the flags and the text.
            for (int skipped = 0; skipped < 2; skipped++)
            {
                ReadOnlySpan<byte> field = MetadataBuilder.UpToNull(rest);
                if (field.Length >= rest.Length)
                    return;

                rest = rest[(field.Length + 1)..];
            }

            if (compressed)
            {
                owned = MetadataBuilder.TryInflate(rest);
                if (owned is null)
                    return;

                rest = owned;
            }
        }

        if (keyword.SequenceEqual("XML:com.adobe.xmp"u8))
        {
            builder.SetXmp(rest);
            return;
        }

        builder.AddText(System.Text.Encoding.Latin1.GetString(keyword),
                        System.Text.Encoding.UTF8.GetString(rest));
    }

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;

        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature))
        {
            error = data.Length < Signature.Length ? ApertureError.UnexpectedEndOfData : ApertureError.InvalidHeader;
            return false;
        }

        if (data.Length < 8 + 25)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        uint ihdrLength = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        if (ihdrLength != 13 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (Crc32.Compute(data.Slice(12, 4 + 13)) != BinaryPrimitives.ReadUInt32BigEndian(data[29..]))
        {
            error = ApertureError.ChecksumMismatch;
            return false;
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        byte bitDepth = data[24];
        byte colorType = data[25];
        byte compression = data[26];
        byte filter = data[27];
        byte interlace = data[28];

        // The spec caps dimensions at 2^31-1 and forbids zero.
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (!IsValidColorType(colorType))
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        if (!IsValidBitDepth(colorType, bitDepth))
        {
            error = ApertureError.InvalidBitDepth;
            return false;
        }

        if (compression != 0 || filter != 0 || interlace > 1)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        ScanChunks(data, out ChunkFacts facts);
        if (!facts.SawIdat)
        {
            error = facts.Truncated ? ApertureError.UnexpectedEndOfData : ApertureError.InvalidData;
            return false;
        }

        int channels = ChannelsFor(colorType);
        bool hasAlpha = colorType is 4 or 6 || facts.SawTrns;
        int storedBits = colorType == 3 ? 8 : bitDepth;

        info = new ImageInfo
        {
            Format = ImageFormat.Png,
            Width = (int)width,
            Height = (int)height,
            BitsPerChannel = bitDepth,
            Channels = channels,
            HasAlpha = hasAlpha,
            ColorModel = colorType switch
            {
                0 or 4 => ColorModel.Grayscale,
                3 => ColorModel.Indexed,
                _ => ColorModel.Rgb,
            },
            PreferredPixelFormat = ChoosePixelFormat(colorType == 3 ? 3 : channels, storedBits, false, hasAlpha),
            FrameCount = TotalFrames(facts),
            IsAnimated = TotalFrames(facts) > 1,
            Orientation = ExifOrientation.Unspecified,
            HorizontalDpi = facts.HorizontalDpi,
            VerticalDpi = facts.VerticalDpi,
            Compression = interlace == 1 ? "Deflate, Adam7 interlaced" : "Deflate",
        };
        error = ApertureError.None;
        return true;
    }

    private static bool IsValidColorType(byte colorType) => colorType is 0 or 2 or 3 or 4 or 6;

    private static bool IsValidBitDepth(byte colorType, byte bitDepth) => colorType switch
    {
        0 => bitDepth is 1 or 2 or 4 or 8 or 16,
        3 => bitDepth is 1 or 2 or 4 or 8,
        2 or 4 or 6 => bitDepth is 8 or 16,
        _ => false,
    };

    private static int ChannelsFor(byte colorType) => colorType switch
    {
        0 => 1,
        4 => 2,
        2 => 3,
        6 => 4,
        _ => 1,
    };

    /// <summary>
    /// Total decodable frames. The animation control chunk counts the animation only, so a
    /// default image that is not part of it is one more frame on top.
    /// </summary>
    private static int TotalFrames(in ChunkFacts facts)
    {
        if (facts.AnimationFrames <= 0)
            return 1;
        return facts.SawFrameControlBeforeData ? facts.AnimationFrames : facts.AnimationFrames + 1;
    }

    private struct ChunkFacts
    {
        public bool SawIdat;
        public bool SawTrns;
        public bool Truncated;
        public bool SawFrameControlBeforeData;
        public int AnimationFrames;
        public double HorizontalDpi;
        public double VerticalDpi;
    }

    /// <summary>
    /// Walks the chunk list for the facts that are not in IHDR: transparency, physical
    /// resolution and the APNG frame count. Stops at IDAT unless an acTL is still expected, and
    /// is capped at <see cref="MaxChunksScanned"/> so a file of empty chunks cannot stall.
    /// </summary>
    private static void ScanChunks(ReadOnlySpan<byte> data, out ChunkFacts facts)
    {
        facts = new ChunkFacts();
        int offset = 8;

        for (int scanned = 0; scanned < MaxChunksScanned; scanned++)
        {
            if (offset + 8 > data.Length)
            {
                facts.Truncated = true;
                return;
            }

            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            ReadOnlySpan<byte> type = data.Slice(offset + 4, 4);

            // Chunk lengths are capped at 2^31-1; anything larger is corruption, not a big chunk.
            if (length > int.MaxValue)
            {
                facts.Truncated = true;
                return;
            }

            long next = (long)offset + 12 + length;
            if (next > data.Length)
            {
                // A truncated final chunk still tells us the ones before it were real.
                facts.Truncated = true;
                if (type.SequenceEqual("IDAT"u8))
                    facts.SawIdat = true;
                return;
            }

            ReadOnlySpan<byte> payload = data.Slice(offset + 8, (int)length);

            if (type.SequenceEqual("IDAT"u8))
            {
                facts.SawIdat = true;
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                facts.SawTrns = true;
            }
            else if (type.SequenceEqual("acTL"u8) && payload.Length >= 8)
            {
                uint frames = BinaryPrimitives.ReadUInt32BigEndian(payload);
                facts.AnimationFrames = frames is > 0 and <= int.MaxValue ? (int)frames : 0;
            }
            else if (type.SequenceEqual("fcTL"u8) && !facts.SawIdat)
            {
                // An fcTL ahead of IDAT makes the default image the first animation frame.
                facts.SawFrameControlBeforeData = true;
            }
            else if (type.SequenceEqual("pHYs"u8) && payload.Length >= 9 && payload[8] == 1)
            {
                // Unit 1 is metres, so pixels per metre converts to dpi by 0.0254.
                facts.HorizontalDpi = BinaryPrimitives.ReadUInt32BigEndian(payload) * 0.0254;
                facts.VerticalDpi = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]) * 0.0254;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                return;
            }

            offset = (int)next;
        }
    }
}
