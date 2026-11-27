// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Bmp;
using Prowl.Aperture.Decoders.Ico;

namespace Prowl.Aperture.Decoders;

/// <summary>
/// Reads Windows icons and cursors. Each directory entry holds either a headerless BMP or a
/// complete PNG, so an ICO is a container of other formats rather than a codec of its own.
/// </summary>
public sealed class IcoDecoder : DecoderBase
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Ico;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".ico", ".cur"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!IcoDirectory.TryRead(data, out List<IcoEntry> entries, out error))
            return false;

        IcoEntry entry = IcoDirectory.Best(entries);

        // The largest is what comes back by default and what fixes the layout for the rest.
        if (options.DecodeAllFrames && entries.Count > 1)
            return TryDecodeEveryEntry(data, entries, entry, options, info, out image, out error);

        ReadOnlySpan<byte> payload = data.Slice(entry.Offset, entry.Length);

        if (entry.IsPng)
        {
            if (!new PngDecoder().TryDecode(payload, options, out Image? inner, out error))
                return false;

            if (inner!.Width != entry.Width || inner.Height != entry.Height)
            {
                inner.Dispose();
                error = ApertureError.InvalidData;
                return false;
            }

            // The inner image is left undisposed on purpose: it no longer owns the frames.
            image = new Image
            {
                Format = ImageFormat.Ico,
                Width = inner.Width,
                Height = inner.Height,
                PixelFormat = inner.PixelFormat,
                Frames = inner.Frames,
                Info = info,
            };
            return true;
        }

        return TryDecodeBitmap(payload, entry, options, info, out image, out error);
    }

    /// <summary>
    /// Reads every size the icon holds, largest first. The entries are independent pictures that
    /// need agree on nothing, so the largest settles the layout and the rest are read into it.
    /// </summary>
    private bool TryDecodeEveryEntry(ReadOnlySpan<byte> data, List<IcoEntry> entries, IcoEntry best,
                                     DecodeOptions options, ImageInfo info, out Image? image,
                                     out ApertureError error)
    {
        image = null;

        if (!TryDecodeEntry(data, best, options, info, out Image? first, out error))
            return false;

        PixelFormat target = first!.PixelFormat;
        List<ImageFrame> frames = [.. first.Frames];

        DecodeOptions rest = options.Clone();
        rest.TargetPixelFormat = target;

        // Largest first, and the deeper of two the same size first.
        List<IcoEntry> ordered = [.. entries];
        ordered.Sort((a, b) =>
        {
            long areaA = (long)a.Width * a.Height;
            long areaB = (long)b.Width * b.Height;
            return areaA != areaB ? areaB.CompareTo(areaA) : b.Bits.CompareTo(a.Bits);
        });

        foreach (IcoEntry other in ordered)
        {
            if (other.Offset == best.Offset && other.Length == best.Length)
                continue;

            // One unreadable size does not condemn the sizes that did read.
            if (!TryDecodeEntry(data, other, rest, info, out Image? decoded, out _))
                continue;

            frames.AddRange(decoded!.Frames);
        }

        image = new Image
        {
            Format = ImageFormat.Ico,
            Width = first.Width,
            Height = first.Height,
            PixelFormat = target,
            Frames = [.. frames],
            Info = info,
        };
        error = ApertureError.None;
        return true;
    }

    /// <summary>Reads one entry of the directory, whichever of the two encodings it is in.</summary>
    private bool TryDecodeEntry(ReadOnlySpan<byte> data, IcoEntry entry, DecodeOptions options,
                                ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;
        if (entry.Offset < 0 || entry.Length <= 0 || entry.Offset + entry.Length > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        ReadOnlySpan<byte> payload = data.Slice(entry.Offset, entry.Length);

        if (!entry.IsPng)
            return TryDecodeBitmap(payload, entry, options, info, out image, out error);

        if (!new PngDecoder().TryDecode(payload, options, out Image? inner, out error))
            return false;

        if (inner!.Width != entry.Width || inner.Height != entry.Height)
        {
            inner.Dispose();
            error = ApertureError.InvalidData;
            return false;
        }

        image = new Image
        {
            Format = ImageFormat.Ico,
            Width = inner.Width,
            Height = inner.Height,
            PixelFormat = inner.PixelFormat,
            Frames = inner.Frames,
            Info = info,
        };
        return true;
    }

    /// <summary>
    /// A bitmap entry carries the header and pixels but not the file header, and states twice its
    /// height: the colour rows are followed by a one bit mask saying which pixels are see through.
    /// That mask still decides a thirty two bit icon whose alpha its author never filled in.
    /// </summary>
    private static bool TryDecodeBitmap(ReadOnlySpan<byte> payload, in IcoEntry entry, DecodeOptions options,
                                        ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!BmpHeader.TryReadDib(payload, 0, -1, out BmpHeader header, out error))
            return false;

        if (header.Height % 2 != 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        int maskOffset = header.PixelOffset + (header.Stride * (header.Height / 2));
        header.Height /= 2;

        // The directory and the image it points at have to agree about the size.
        if (header.Width != entry.Width || header.Height != entry.Height)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        if (!BmpImageReader.CanDescribe(payload.Length - header.PixelOffset, header))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        PixelFormat target = options.TargetPixelFormat ?? PixelFormat.Rgba8;
        if (target != PixelFormat.Rgba8 && target != PixelFormat.Rgb8)
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        int channels = target == PixelFormat.Rgba8 ? 4 : 3;
        int stride = options.GetStride(header.Width, target);
        long total = (long)stride * header.Height;

        if (total > options.MaxAllocationBytes || total > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] buffer = options.UsePooledMemory ? BufferPool.Bytes.Rent((int)total) : new byte[(int)total];
        try
        {
            Span<byte> pixels = buffer.AsSpan(0, (int)total);
            pixels.Clear();

            if (!BmpImageReader.TryDecode(payload, header, channels, pixels, stride,
                                          options.FlipVertically, out error))
                return false;

            // The mask decides transparency only where the colour data does not, so a thirty
            // two bit entry whose alpha was filled in keeps it.
            if (channels == 4 && IsOpaque(pixels, stride, header.Width, header.Height))
                ApplyMask(payload, header, maskOffset, pixels, stride, options.FlipVertically);

            ImageFrame frame = new(buffer, (int)total, header.Width, header.Height, stride, target,
                                   options.UsePooledMemory);

            image = new Image
            {
                Format = ImageFormat.Ico,
                Width = header.Width,
                Height = header.Height,
                PixelFormat = target,
                Frames = new[] { frame },
                Info = info,
            };
            buffer = [];
            error = ApertureError.None;
            return true;
        }
        finally
        {
            if (buffer.Length != 0 && options.UsePooledMemory)
                BufferPool.Bytes.Return(buffer);
        }
    }

    private static bool IsOpaque(ReadOnlySpan<byte> pixels, int stride, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = pixels.Slice(y * stride, width * 4);
            for (int at = 3; at < row.Length; at += 4)
            {
                if (row[at] != 255)
                    return false;
            }
        }

        return true;
    }

    /// <summary>Clears the alpha of every pixel the mask marks as see through.</summary>
    private static void ApplyMask(ReadOnlySpan<byte> payload, in BmpHeader header, int maskOffset,
                                  Span<byte> pixels, int stride, bool flip)
    {
        int maskStride = ((header.Width + 31) / 32) * 4;
        if (maskOffset < 0 || maskOffset + ((long)maskStride * header.Height) > payload.Length)
            return;

        for (int y = 0; y < header.Height; y++)
        {
            // The mask is stored bottom up alongside the colour rows it belongs to.
            ReadOnlySpan<byte> line = payload.Slice(maskOffset + ((header.Height - 1 - y) * maskStride), maskStride);
            Span<byte> row = pixels.Slice((flip ? header.Height - 1 - y : y) * stride, header.Width * 4);

            for (int x = 0; x < header.Width; x++)
            {
                if ((line[x >> 3] & (0x80 >> (x & 7))) != 0)
                    row[(x * 4) + 3] = 0;
            }
        }
    }

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;

        if (!IcoDirectory.TryRead(data, out List<IcoEntry> entries, out error))
            return false;

        IcoEntry best = IcoDirectory.Best(entries);

        // A PNG entry comes back in the layout the PNG reader chooses. A bitmap entry always
        // gains an alpha channel, since an icon carries a mask alongside its colour.
        ImageInfo? inner = null;
        if (best.IsPng && best.Offset >= 0 && best.Length > 0 &&
            best.Offset + best.Length <= data.Length)
        {
            new PngDecoder().TryIdentify(data.Slice(best.Offset, best.Length), out inner, out _);
        }

        info = new ImageInfo
        {
            Format = ImageFormat.Ico,
            Width = best.Width,
            Height = best.Height,
            BitsPerChannel = 8,
            Channels = inner?.Channels ?? 4,
            HasAlpha = inner?.HasAlpha ?? true,
            ColorModel = best.Bits is > 0 and <= 8 ? ColorModel.Indexed : ColorModel.Rgb,
            PreferredPixelFormat = inner?.PreferredPixelFormat ?? PixelFormat.Rgba8,
            FrameCount = entries.Count,
            Compression = best.IsPng ? "PNG entries" : "BMP entries",
        };
        error = ApertureError.None;
        return true;
    }
}
