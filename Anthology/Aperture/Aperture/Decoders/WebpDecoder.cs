// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using Prowl.Aperture.Decoders.WebP;
using Prowl.Aperture.Metadata;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads WebP: the simple VP8 and VP8L layouts and the extended VP8X container.</summary>
public sealed class WebpDecoder : DecoderBase
{
    /// <summary>Cap on RIFF chunks walked, so a chain of empty chunks cannot stall the parse.</summary>
    private const int MaxChunksScanned = 4096;

    private const byte ExtendedFlagAnimation = 0x02;
    private const byte ExtendedFlagAlpha = 0x10;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Webp;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".webp"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!WebPContainer.TryRead(data, options.MaxFrames, out int width, out int height,
                                   out bool hasAlpha, out List<WebPFrame> frames, out error))
            return false;

        PixelFormat natural = hasAlpha ? PixelFormat.Rgba8 : PixelFormat.Rgb8;
        PixelFormat target = options.TargetPixelFormat ?? natural;
        if (!PixelConverter.CanConvert(natural, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        int stride = options.GetStride(width, target);
        long total = (long)stride * height;
        if (total > options.MaxAllocationBytes || total > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        List<ImageFrame> output = [];
        uint[]? canvas = null;

        try
        {
            foreach (WebPFrame frame in frames)
            {
                if (!TryReadFrame(data, frame, out uint[]? pixels, out bool rented,
                                  out int frameWidth, out int frameHeight, out error))
                    return false;

                try
                {
                    // One frame covering the whole picture is the picture, with nothing under
                    // it to blend with, so it goes out without a canvas of its own size.
                    if (frames.Count == 1 && frame.Left == 0 && frame.Top == 0 &&
                        frameWidth == width && frameHeight == height)
                    {
                        output.Add(Copy(pixels!, width, height, stride, target, frame, options));
                        continue;
                    }

                    // Allocated only once a frame has come back, so a file naming the largest
                    // picture the format allows and holding nothing costs nothing.
                    if (canvas is null)
                    {
                        if ((long)width * height > int.MaxValue)
                        {
                            error = ApertureError.LimitExceeded;
                            return false;
                        }

                        canvas = BufferPool.Uints.Rent(width * height);
                        canvas.AsSpan(0, width * height).Clear();
                    }

                    Draw(pixels!, frameWidth, frameHeight, frame, canvas, width, height,
                         output.Count == 0);

                    output.Add(Copy(canvas, width, height, stride, target, frame, options));

                    if (frame.DisposeToBackground)
                        Clear(canvas, width, height, frame);
                }
                                finally
                {
                    if (rented)
                        BufferPool.Uints.Return(pixels!);
                }
            }

            image = new Image
            {
                Format = ImageFormat.Webp,
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
            if (canvas is not null)
                BufferPool.Uints.Return(canvas);

            foreach (ImageFrame frame in output)
                frame.Release();
        }
    }

    /// <summary>Decodes one frame's own picture, before anything is drawn anywhere.</summary>
    private static bool TryReadFrame(ReadOnlySpan<byte> data, WebPFrame frame, out uint[]? pixels,
                                     out bool rented, out int frameWidth, out int frameHeight,
                                     out ApertureError error)
    {
        error = ApertureError.None;
        pixels = null;
        rented = false;
        frameWidth = 0;
        frameHeight = 0;

        if (frame.Offset < 0 || frame.Length <= 0 || frame.Offset + frame.Length > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (!WebPContainer.TryReadSize(data, frame, out frameWidth, out frameHeight, out _))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        ReadOnlySpan<byte> body = data.Slice(frame.Offset, frame.Length);

        if (frame.Lossless)
        {
            if (!Vp8LDecoder.TryDecode(body, frameWidth, frameHeight, out pixels))
            {
                error = ApertureError.InvalidData;
                return false;
            }
        }
        else
        {
            if (!Vp8FrameDecoder.TryDecode(body, out Vp8Frame? lossy))
            {
                error = ApertureError.InvalidData;
                return false;
            }

            pixels = BufferPool.Uints.Rent(frameWidth * frameHeight);
            rented = true;
            Vp8Yuv.ToColour(lossy!, pixels, frameWidth, frameHeight);

            // The lossy codec has no alpha, so transparency arrives in its own chunk.
            if (frame.AlphaLength > 0)
            {
                if (!WebPAlpha.TryDecode(data.Slice(frame.AlphaOffset - 1, frame.AlphaLength + 1),
                                         frameWidth, frameHeight, out byte[]? alpha))
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                // A rented buffer is longer than the picture, which is what bounds this.
                for (int i = 0; i < frameWidth * frameHeight; i++)
                    pixels[i] = (pixels[i] & 0x00FFFFFFu) | ((uint)alpha![i] << 24);
            }
        }

        return true;
    }

    /// <summary>Puts a frame's picture onto the canvas at the place it names.</summary>
    private static void Draw(uint[] pixels, int frameWidth, int frameHeight, WebPFrame frame,
                             uint[] canvas, int width, int height, bool first)
    {
        int left = frame.Left;
        int top = frame.Top;

        for (int y = 0; y < frameHeight; y++)
        {
            int row = top + y;
            if ((uint)row >= (uint)height)
                continue;

            for (int x = 0; x < frameWidth; x++)
            {
                int column = left + x;
                if ((uint)column >= (uint)width)
                    continue;

                uint colour = pixels[(y * frameWidth) + x];
                int to = (row * width) + column;

                // Nothing lies under the first frame; a later one blends over what was left.
                canvas[to] = first || !frame.Blend ? colour : Blend(canvas[to], colour);
            }
        }
    }

    /// <summary>Puts one frame's pixel over what is beneath it, in the usual source over rule.</summary>
    private static uint Blend(uint under, uint over)
    {
        // Anything over nothing is itself, colour included.
        uint alpha = over >> 24;
        if (alpha == 255 || (under >> 24) == 0)
            return over;

        if (alpha == 0)
            return under;

        uint result = 0;
        uint combined = alpha + (((under >> 24) * (255 - alpha)) / 255);

        for (int shift = 0; shift <= 16; shift += 8)
        {
            uint top = (over >> shift) & 0xFF;
            uint bottom = (under >> shift) & 0xFF;
            uint mixed = ((top * alpha) + (bottom * (under >> 24) * (255 - alpha) / 255)) /
                         Math.Max(combined, 1);
            result |= Math.Min(mixed, 255) << shift;
        }

        return result | (Math.Min(combined, 255) << 24);
    }

    /// <summary>Clears a frame's rectangle, which is what disposing to the background means.</summary>
    private static void Clear(uint[] canvas, int width, int height, WebPFrame frame)
    {
        for (int y = 0; y < frame.Height; y++)
        {
            int row = frame.Top + y;
            if ((uint)row >= (uint)height)
                continue;

            for (int x = 0; x < frame.Width; x++)
            {
                int column = frame.Left + x;
                if ((uint)column < (uint)width)
                    canvas[(row * width) + column] = 0;
            }
        }
    }

    /// <summary>Takes a copy of the canvas as one frame, in whatever layout the caller asked for.</summary>
    private static ImageFrame Copy(uint[] canvas, int width, int height, int stride,
                                   PixelFormat target, WebPFrame frame, DecodeOptions options)
    {
        long total = (long)stride * height;
        byte[] buffer = options.UsePooledMemory ? BufferPool.Bytes.Rent((int)total) : new byte[(int)total];
        Span<byte> pixels = buffer.AsSpan(0, (int)total);

        int channels = target.ChannelCount();
        bool wide = target.BytesPerChannel() == 2;
        int pixelBytes = channels * target.BytesPerChannel();

        // Every byte of a row is written, so only what an alignment adds past the row needs it.
        if (stride != width * pixelBytes)
            pixels.Clear();

        Span<byte> row = stackalloc byte[4];

        for (int y = 0; y < height; y++)
        {
            int to = options.FlipVertically ? height - 1 - y : y;
            Span<byte> line = pixels.Slice(to * stride, width * pixelBytes);
            ReadOnlySpan<uint> source = canvas.AsSpan(y * width, width);

            if (target == PixelFormat.Rgba8)
            {
                WriteRgba(source, line);
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                uint colour = source[x];
                row[0] = (byte)(colour >> 16);
                row[1] = (byte)(colour >> 8);
                row[2] = (byte)colour;
                row[3] = (byte)(colour >> 24);

                for (int c = 0; c < channels; c++)
                {
                    byte value = row[Math.Min(c, 3)];
                    if (wide)
                    {
                        line[((x * channels) + c) * 2] = value;
                        line[(((x * channels) + c) * 2) + 1] = value;
                    }
                    else
                    {
                        line[(x * channels) + c] = value;
                    }
                }
            }
        }

        return new ImageFrame(buffer, (int)total, width, height, stride, target, options.UsePooledMemory)
        {
            Delay = frame.Delay,
            Disposal = frame.DisposeToBackground ? FrameDisposal.RestoreBackground : FrameDisposal.None,
        };
    }

    /// <summary>Where the red and blue of each pixel swap places on the way out.</summary>
    private static ReadOnlySpan<byte> Swapped =>
        [2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15];

    /// <summary>
    /// Writes one row as eight bit colour with alpha, which is the layout a texture upload wants
    /// and the one this decoder is nearly always asked for. The canvas holds the same four
    /// channels in the other order, so the whole of the conversion is a swap of two of them.
    /// </summary>
    private static void WriteRgba(ReadOnlySpan<uint> source, Span<byte> destination)
    {
        int x = 0;

        if (Ssse3.IsSupported)
        {
            ref byte from = ref Unsafe.As<uint, byte>(ref MemoryMarshal.GetReference(source));
            ref byte to = ref MemoryMarshal.GetReference(destination);
            Vector128<byte> order = Vector128.Create(Swapped);

            for (; x + 4 <= source.Length; x += 4)
            {
                Vector128<byte> packed = Vector128.LoadUnsafe(ref from, (nuint)(x * 4));
                Ssse3.Shuffle(packed, order).StoreUnsafe(ref to, (nuint)(x * 4));
            }
        }

        for (; x < source.Length; x++)
        {
            uint colour = source[x];
            int at = x * 4;
            destination[at] = (byte)(colour >> 16);
            destination[at + 1] = (byte)(colour >> 8);
            destination[at + 2] = (byte)colour;
            destination[at + 3] = (byte)(colour >> 24);
        }
    }

    /// <inheritdoc />
    protected override ImageMetadata ReadMetadata(ReadOnlySpan<byte> data)
    {
        MetadataBuilder builder = new();

        // The ancillary blocks are chunks like any other, found by the same walk.
        int offset = 12;

        for (int scanned = 0; scanned < MaxMetadataChunks; scanned++)
        {
            if (offset + 8 > data.Length)
                break;

            ReadOnlySpan<byte> type = data.Slice(offset, 4);
            uint length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            if (length > int.MaxValue)
                break;

            long next = (long)offset + 8 + length + (length & 1);
            if (offset + 8 + (long)length > data.Length)
                break;

            ReadOnlySpan<byte> payload = data.Slice(offset + 8, (int)length);

            if (type.SequenceEqual("ICCP"u8))
                builder.SetProfile(payload);
            else if (type.SequenceEqual("EXIF"u8))
                builder.SetExif(payload);
            else if (type.SequenceEqual("XMP "u8))
                builder.SetXmp(payload);

            if (next > data.Length)
                break;

            offset = (int)next;
        }

        return builder.Build();
    }

    /// <summary>Cap on chunks walked for metadata, well past any real file's count.</summary>
    private const int MaxMetadataChunks = 1 << 16;

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (data.Length < 12)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (!data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WEBP"u8))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        SpanReader reader = new(data);
        reader.Skip(4);
        reader.TryReadUInt32(out uint riffSize);
        reader.Skip(4);

        // The RIFF size covers everything after the size field itself.
        long declaredEnd = 8L + riffSize;
        int end = (int)Math.Min(declaredEnd <= 8 ? data.Length : declaredEnd, data.Length);

        int width = 0, height = 0;
        bool hasAlpha = false, lossless = false, extended = false, animated = false;
        int animationFrames = 0;
        string compression = "Unknown";

        for (int scanned = 0; scanned < MaxChunksScanned && reader.Position + 8 <= end; scanned++)
        {
            if (!reader.TryReadBytes(4, out ReadOnlySpan<byte> tag) || !reader.TryReadUInt32(out uint size))
                break;

            int payloadStart = reader.Position;
            if (size > (uint)(end - payloadStart))
            {
                // A final chunk that overruns the file still counts if the geometry is known.
                if (width == 0)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }
                break;
            }

            ReadOnlySpan<byte> payload = data.Slice(payloadStart, (int)size);

            if (tag.SequenceEqual("VP8X"u8) && payload.Length >= 10)
            {
                extended = true;
                hasAlpha |= (payload[0] & ExtendedFlagAlpha) != 0;
                animated = (payload[0] & ExtendedFlagAnimation) != 0;
                width = ReadUInt24(payload[4..]) + 1;
                height = ReadUInt24(payload[7..]) + 1;
                compression = "Extended";
            }
            else if (tag.SequenceEqual("VP8 "u8))
            {
                if (TryReadLossy(payload, out int w, out int h))
                {
                    if (!extended)
                    {
                        width = w;
                        height = h;
                    }
                    compression = extended ? "Extended, VP8 lossy" : "VP8 lossy";
                }
                else if (width == 0)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
            }
            else if (tag.SequenceEqual("VP8L"u8))
            {
                if (TryReadLossless(payload, out int w, out int h, out bool alpha))
                {
                    lossless = true;
                    hasAlpha |= alpha;
                    if (!extended)
                    {
                        width = w;
                        height = h;
                    }
                    compression = extended ? "Extended, VP8L lossless" : "VP8L lossless";
                }
                else if (width == 0)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
            }
            else if (tag.SequenceEqual("ALPH"u8))
            {
                hasAlpha = true;
            }
            else if (tag.SequenceEqual("ANMF"u8))
            {
                animationFrames++;
            }

            // Chunk payloads are padded to an even length.
            if (!reader.Seek(payloadStart + size + (size & 1)))
                break;
        }

        if (width <= 0 || height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        info = new ImageInfo
        {
            Format = ImageFormat.Webp,
            Width = width,
            Height = height,
            BitsPerChannel = 8,
            Channels = hasAlpha ? 4 : 3,
            HasAlpha = hasAlpha,
            ColorModel = lossless ? ColorModel.Rgb : ColorModel.YCbCr,
            PreferredPixelFormat = hasAlpha ? PixelFormat.Rgba8 : PixelFormat.Rgb8,
            FrameCount = Math.Max(animationFrames, 1),
            IsAnimated = animated,
            Compression = compression,
        };
        error = ApertureError.None;
        return true;
    }

    /// <summary>Reads the VP8 keyframe header: a three byte frame tag, a sync code, then the geometry.</summary>
    private static bool TryReadLossy(ReadOnlySpan<byte> payload, out int width, out int height)
    {
        width = height = 0;
        if (payload.Length < 10)
            return false;

        // Bit 0 of the frame tag is the frame type; only a keyframe carries dimensions.
        if ((payload[0] & 0x01) != 0)
            return false;

        if (payload[3] != 0x9D || payload[4] != 0x01 || payload[5] != 0x2A)
            return false;

        // The top two bits of each 16 bit field are an upscaling hint, not part of the size.
        width = (payload[6] | (payload[7] << 8)) & 0x3FFF;
        height = (payload[8] | (payload[9] << 8)) & 0x3FFF;
        return width > 0 && height > 0;
    }

    /// <summary>Reads the VP8L header: a signature byte then 14 bit dimensions packed little endian.</summary>
    private static bool TryReadLossless(ReadOnlySpan<byte> payload, out int width, out int height, out bool hasAlpha)
    {
        width = height = 0;
        hasAlpha = false;
        if (payload.Length < 5 || payload[0] != 0x2F)
            return false;

        uint bits = (uint)(payload[1] | (payload[2] << 8) | (payload[3] << 16) | (payload[4] << 24));
        width = (int)(bits & 0x3FFF) + 1;
        height = (int)((bits >> 14) & 0x3FFF) + 1;
        hasAlpha = ((bits >> 28) & 1) != 0;
        return true;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);
}
