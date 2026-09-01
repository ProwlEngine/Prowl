// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Gif;
using Prowl.Aperture.Metadata;

using System.Runtime.InteropServices;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads GIF87a and GIF89a, including multi frame animations.</summary>
public sealed class GifDecoder : DecoderBase
{
    /// <summary>Cap on blocks walked while counting frames, so a crafted block chain cannot spin.</summary>
    private const int MaxBlocksScanned = 1 << 16;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Gif;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".gif"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        int wanted = options.DecodeAllFrames ? Math.Max(1, options.MaxFrames) : 1;
        if (!GifReader.TryRead(data, wanted, out int width, out int height, out uint background,
                               out List<GifFrame> frames, out error))
            return false;

        PixelFormat target = options.TargetPixelFormat ?? PixelFormat.Rgba8;
        int channels = target switch
        {
            PixelFormat.Rgb8 => 3,
            PixelFormat.Rgba8 => 4,
            _ => 0,
        };

        if (channels == 0)
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        int stride = options.GetStride(width, target);
        long total = (long)stride * height;

        if (total * frames.Count > options.MaxAllocationBytes || total > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        // Every frame is a rectangle drawn over what the last one left, so the canvas is carried
        // between them and each finished state is copied out as its own picture.
        byte[] canvas = BufferPool.Bytes.Rent(width * height * 4);
        byte[]? saved = null;
        List<ImageFrame> output = [];

        try
        {
            Span<byte> surface = canvas.AsSpan(0, width * height * 4);
            Fill(surface, background);

            bool first = true;

            foreach (GifFrame frame in frames)
            {
                // Nothing lies under the first frame, so where it is transparent the picture is
                // transparent rather than showing the colour the canvas was filled with.
                if (first)
                {
                    Clear(surface, width, frame);
                    first = false;
                }

                if (frame.Disposal == FrameDisposal.RestorePrevious)
                {
                    saved ??= BufferPool.Bytes.Rent(surface.Length);
                    surface.CopyTo(saved);
                }

                Draw(surface, width, height, frame);
                output.Add(Copy(surface, width, height, stride, channels, target, frame, options));

                switch (frame.Disposal)
                {
                    case FrameDisposal.RestoreBackground:
                        Clear(surface, width, frame);
                        break;

                    case FrameDisposal.RestorePrevious when saved is not null:
                        saved.AsSpan(0, surface.Length).CopyTo(surface);
                        break;
                }
            }

            image = new Image
            {
                Format = ImageFormat.Gif,
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
            BufferPool.Bytes.Return(canvas);
            if (saved is not null)
                BufferPool.Bytes.Return(saved);
            // Only reached when something threw, in which case the frames built so far are the
            // only holders of their buffers.
            foreach (ImageFrame frame in output)
                frame.Release();
        }
    }

    /// <summary>
    /// A stored colour as the word whose bytes are the four the canvas holds, in order. Writing
    /// a whole pixel at once is worth the swap, and on a machine that stores words the other way
    /// round there is nothing to swap.
    /// </summary>
    private static uint Pack(uint colour) => BitConverter.IsLittleEndian
        ? ((colour >> 16) & 0xFF) | (colour & 0x0000FF00) | ((colour & 0xFF) << 16) | (colour & 0xFF000000)
        : colour;

    private static void Fill(Span<byte> canvas, uint colour) =>
        MemoryMarshal.Cast<byte, uint>(canvas).Fill(Pack(colour));

    /// <summary>Draws one frame's rectangle over the canvas, leaving its transparent pixels alone.</summary>
    private static void Draw(Span<byte> canvas, int width, int height, GifFrame frame)
    {
        Span<uint> pixels = MemoryMarshal.Cast<byte, uint>(canvas);

        // The palette is at most two hundred and fifty six entries, so packing it once a frame
        // takes the swap out of the pixel loop entirely.
        Span<uint> palette = stackalloc uint[256];
        for (int i = 0; i < palette.Length; i++)
            palette[i] = Pack(i < frame.Palette.Length ? frame.Palette[i] : 0xFF000000u);

        // The part of the frame that falls on the canvas is the same on every row.
        int first = Math.Max(0, -frame.Left);
        int last = Math.Min(frame.Width, width - frame.Left);
        int transparent = frame.TransparentIndex;

        for (int y = 0; y < frame.Height; y++)
        {
            int row = frame.Top + y;
            if ((uint)row >= (uint)height)
                continue;

            ReadOnlySpan<byte> indices = frame.Indices.AsSpan(y * frame.Width, frame.Width);
            int at = (row * width) + frame.Left;

            for (int x = first; x < last; x++)
            {
                int index = indices[x];
                if (index != transparent)
                    pixels[at + x] = palette[index];
            }
        }
    }

    private static void Clear(Span<byte> canvas, int width, GifFrame frame)
    {
        for (int y = 0; y < frame.Height; y++)
        {
            int row = frame.Top + y;
            int at = ((row * width) + frame.Left) * 4;
            if (at < 0 || at + (frame.Width * 4) > canvas.Length)
                continue;

            canvas.Slice(at, frame.Width * 4).Clear();
        }
    }

    private static ImageFrame Copy(ReadOnlySpan<byte> canvas, int width, int height, int stride,
                                   int channels, PixelFormat target, GifFrame frame, DecodeOptions options)
    {
        long total = (long)stride * height;
        byte[] buffer = options.UsePooledMemory ? BufferPool.Bytes.Rent((int)total) : new byte[(int)total];
        Span<byte> pixels = buffer.AsSpan(0, (int)total);
        pixels.Clear();

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> source = canvas.Slice(y * width * 4, width * 4);
            Span<byte> row = pixels.Slice((options.FlipVertically ? height - 1 - y : y) * stride,
                                          width * channels);

            if (channels == 4)
            {
                source.CopyTo(row);
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                row[x * 3] = source[x * 4];
                row[(x * 3) + 1] = source[(x * 4) + 1];
                row[(x * 3) + 2] = source[(x * 4) + 2];
            }
        }

        return new ImageFrame(buffer, (int)total, width, height, stride, target, options.UsePooledMemory)
        {
            Delay = frame.Delay,
            Disposal = frame.Disposal,
        };
    }

    /// <inheritdoc />
    protected override ImageMetadata ReadMetadata(ReadOnlySpan<byte> data)
    {
        if (!GifReader.TryReadComment(data, out string comment))
            return ImageMetadata.Empty;

        MetadataBuilder builder = new();
        builder.AddText("Comment", comment);
        return builder.Build();
    }

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (data.Length < 13)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        bool gif87 = data[..6].SequenceEqual("GIF87a"u8);
        if (!gif87 && !data[..6].SequenceEqual("GIF89a"u8))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        SpanReader reader = new(data);
        reader.Skip(6);
        reader.TryReadUInt16(out ushort width);
        reader.TryReadUInt16(out ushort height);
        reader.TryReadByte(out byte packed);
        reader.Skip(2);

        if (width == 0 || height == 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        int bitsPerColor = (packed & 0x07) + 1;
        bool hasGlobalTable = (packed & 0x80) != 0;
        if (hasGlobalTable && !reader.Skip(3 * (1 << bitsPerColor)))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        CountFrames(ref reader, out int frameCount, out bool sawTransparency, out bool truncated);
        if (frameCount == 0)
        {
            error = truncated ? ApertureError.UnexpectedEndOfData : ApertureError.NoImageData;
            return false;
        }

        info = new ImageInfo
        {
            Format = ImageFormat.Gif,
            Width = width,
            Height = height,
            BitsPerChannel = 8,
            Channels = sawTransparency ? 4 : 3,
            HasAlpha = sawTransparency,
            ColorModel = ColorModel.Indexed,
            PreferredPixelFormat = PixelFormat.Rgba8,
            FrameCount = frameCount,
            IsAnimated = frameCount > 1,
            Compression = gif87 ? "LZW, GIF87a" : "LZW, GIF89a",
        };
        error = ApertureError.None;
        return true;
    }

    /// <summary>
    /// Walks the block stream counting image descriptors. Every sub-block chain is bounded by
    /// the remaining span, so a length byte that points past the end ends the walk rather than
    /// looping.
    /// </summary>
    private static void CountFrames(ref SpanReader reader, out int frameCount,
                                    out bool sawTransparency, out bool truncated)
    {
        frameCount = 0;
        sawTransparency = false;
        truncated = false;

        for (int scanned = 0; scanned < MaxBlocksScanned; scanned++)
        {
            if (!reader.TryReadByte(out byte introducer))
            {
                truncated = true;
                return;
            }

            switch (introducer)
            {
                case 0x3B: // trailer
                    return;

                case 0x21: // extension
                    if (!reader.TryReadByte(out byte label))
                    {
                        truncated = true;
                        return;
                    }
                    if (label == 0xF9 && reader.TryPeekAt(reader.Position, 2, out ReadOnlySpan<byte> gce) &&
                        gce[0] >= 4 && (gce[1] & 0x01) != 0)
                    {
                        sawTransparency = true;
                    }
                    if (!SkipSubBlocks(ref reader))
                    {
                        truncated = true;
                        return;
                    }
                    break;

                case 0x2C: // image descriptor
                    if (!reader.Skip(8) || !reader.TryReadByte(out byte localPacked))
                    {
                        truncated = true;
                        return;
                    }
                    if ((localPacked & 0x80) != 0 && !reader.Skip(3 * (1 << ((localPacked & 0x07) + 1))))
                    {
                        truncated = true;
                        return;
                    }
                    if (!reader.Skip(1) || !SkipSubBlocks(ref reader)) // LZW minimum code size, then data
                    {
                        truncated = true;
                        frameCount++;
                        return;
                    }
                    frameCount++;
                    break;

                default:
                    // Unknown introducer: the stream is corrupt from here on.
                    truncated = true;
                    return;
            }
        }
    }

    private static bool SkipSubBlocks(ref SpanReader reader)
    {
        while (true)
        {
            if (!reader.TryReadByte(out byte size))
                return false;
            if (size == 0)
                return true;
            if (!reader.Skip(size))
                return false;
        }
    }
}
