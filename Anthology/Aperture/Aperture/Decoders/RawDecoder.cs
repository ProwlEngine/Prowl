// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using Prowl.Aperture.Decoders.Raw;
using Prowl.Aperture.Metadata;

namespace Prowl.Aperture.Decoders;

/// <summary>
/// Reads camera raw headers. Most vendors build on TIFF, so those share the directory walk with
/// <see cref="TiffDecoder"/> and differ only in which flavour they report; Fujifilm RAF and Canon
/// CR3 use their own containers and are handled separately.
/// </summary>
public sealed class RawDecoder : DecoderBase
{
    /// <summary>Cap on sub-directories followed while hunting for the full resolution image.</summary>
    private const int MaxSubDirectories = 16;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Raw;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } =
        [".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".orf", ".rw2", ".raf", ".pef", ".srw"];

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (data.Length < 16)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (data[..15].SequenceEqual("FUJIFILMCCD-RAW"u8))
            return ParseRaf(data, out info, out error);

        if (data[4..8].SequenceEqual("ftyp"u8))
            return ParseCr3(data, out info, out error);

        return ParseTiffBased(data, out info, out error);
    }

    private static bool ParseTiffBased(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;

        // Olympus and Panasonic use their own version word in place of TIFF's 42, so the byte
        // order is taken from the first two bytes and the rest of the header is read as TIFF.
        bool little = data[0] == 'I' && data[1] == 'I';
        bool big = data[0] == 'M' && data[1] == 'M';
        if (!little && !big)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        ulong firstOffset = little
            ? BinaryPrimitives.ReadUInt32LittleEndian(data[4..])
            : BinaryPrimitives.ReadUInt32BigEndian(data[4..]);

        if (firstOffset < 8 || firstOffset >= (ulong)data.Length)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (!TiffDirectory.TryOpen(data, little, false, firstOffset, out TiffDirectory ifd0))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        RawVariant variant = IdentifyVariant(data, little, ifd0);

        if (variant == RawVariant.Rw2)
            return DescribePanasonic(data, little, ifd0, firstOffset, out info, out error);

        if (!SelectDirectory(data, little, ifd0, firstOffset, out TiffDirectory best, out ulong bestOffset))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        return TiffDecoder.DescribeDirectory(data, little, false, best, bestOffset, ImageFormat.Raw, variant, out info, out error);
    }

    /// <summary>
    /// IFD0 of a raw file usually describes a small preview; the full sensor image lives in a
    /// sub-directory. Takes whichever directory declares the largest frame.
    /// </summary>
    private static bool SelectDirectory(ReadOnlySpan<byte> data, bool little, in TiffDirectory ifd0,
                                        ulong firstOffset, out TiffDirectory best, out ulong bestOffset)
    {
        best = ifd0;
        bestOffset = firstOffset;
        long bestArea = ifd0.TryGetInteger(TiffTag.ImageWidth, out long w0) &&
                        ifd0.TryGetInteger(TiffTag.ImageLength, out long h0) ? w0 * h0 : 0;

        Span<long> subIfds = stackalloc long[MaxSubDirectories];
        int subCount = ifd0.GetIntegers(TiffTag.SubIfds, subIfds);
        for (int i = 0; i < subCount; i++)
        {
            ulong offset = (ulong)Math.Clamp(subIfds[i], 0, uint.MaxValue);
            if (offset < 8 || offset >= (ulong)data.Length)
                continue;
            if (!TiffDirectory.TryOpen(data, little, false, offset, out TiffDirectory sub))
                continue;
            if (!sub.TryGetInteger(TiffTag.ImageWidth, out long w) ||
                !sub.TryGetInteger(TiffTag.ImageLength, out long h))
                continue;
            if (w * h <= bestArea)
                continue;
            bestArea = w * h;
            best = sub;
            bestOffset = offset;
        }

        return bestArea > 0;
    }

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool OrientationIsPending => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        // A container of its own carries the sensor readings in a layout nothing here describes.
        if (info.RawVariant is RawVariant.Raf or RawVariant.Cr3 or RawVariant.Rw2)
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        // What decides the read is whether the container describes how the picture is stored,
        // not which maker wrote it.
        if (data.Length < 8)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        bool little = data[0] == 'I' && data[1] == 'I';
        if (!little && !(data[0] == 'M' && data[1] == 'M'))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        ulong first = little
            ? BinaryPrimitives.ReadUInt32LittleEndian(data[4..])
            : BinaryPrimitives.ReadUInt32BigEndian(data[4..]);

        if (first < 8 || first >= (ulong)data.Length ||
            !TiffDirectory.TryOpen(data, little, false, first, out TiffDirectory ifd0) ||
            !SelectDirectory(data, little, ifd0, first, out TiffDirectory best, out _))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        return TiffDecoder.TryDecodeDirectory(data, best, options, info, ImageFormat.Raw, out image, out error);
    }

    /// <summary>
    /// Panasonic replaces the standard geometry tags with its own private ones, so a RW2 read as
    /// an ordinary TIFF has no dimensions at all. Tags 2 and 3 hold the sensor frame.
    /// </summary>
    private static bool DescribePanasonic(ReadOnlySpan<byte> data, bool little, TiffDirectory ifd0,
                                          ulong firstOffset, out ImageInfo? info, out ApertureError error)
    {
        const int TagSensorWidth = 0x0002;
        const int TagSensorHeight = 0x0003;
        const int TagBitsPerSample = 0x000A;

        if (!ifd0.TryGetInteger(TagSensorWidth, out long width) ||
            !ifd0.TryGetInteger(TagSensorHeight, out long height))
        {
            // Some bodies use the ordinary tags after all, so fall back to the shared path.
            return TiffDecoder.DescribeDirectory(data, little, false, ifd0, firstOffset,
                                                 ImageFormat.Raw, RawVariant.Rw2, out info, out error);
        }

        info = null;
        if (!ValidateDimensions(width, height, out error))
            return false;

        if (!ifd0.TryGetInteger(TagBitsPerSample, out long bits) || bits is < 1 or > 16)
            bits = 12;

        info = new ImageInfo
        {
            Format = ImageFormat.Raw,
            Width = (int)width,
            Height = (int)height,
            BitsPerChannel = (int)bits,
            Channels = 1,
            HasAlpha = false,
            ColorModel = ColorModel.ColorFilterArray,
            PreferredPixelFormat = PixelFormat.Rgb16,
            FrameCount = 1,
            RawVariant = RawVariant.Rw2,
            Compression = "Panasonic RW2",
        };
        error = ApertureError.None;
        return true;
    }

    private static RawVariant IdentifyVariant(ReadOnlySpan<byte> data, bool little, TiffDirectory ifd0)
    {
        if (data.Length >= 4)
        {
            if (data[..4].SequenceEqual("IIRO"u8) || data[..4].SequenceEqual("IIRS"u8) || data[..4].SequenceEqual("MMOR"u8))
                return RawVariant.Orf;
            if (data[0] == 'I' && data[1] == 'I' && data[2] == 0x55 && data[3] == 0x00)
                return RawVariant.Rw2;
        }

        if (data.Length >= 10 && data[8] == (byte)'C' && data[9] == (byte)'R')
            return RawVariant.Cr2;

        if (ifd0.Contains(TiffTag.DngVersion))
            return RawVariant.Dng;

        if (ifd0.TryGetString(TiffTag.Make, 64, out string make))
        {
            if (make.StartsWith("NIKON", StringComparison.OrdinalIgnoreCase))
                return RawVariant.Nef;
            if (make.StartsWith("SONY", StringComparison.OrdinalIgnoreCase))
                return RawVariant.Arw;
            if (make.StartsWith("PENTAX", StringComparison.OrdinalIgnoreCase))
                return RawVariant.Pef;
            if (make.StartsWith("SAMSUNG", StringComparison.OrdinalIgnoreCase))
                return RawVariant.Srw;
            if (make.StartsWith("OLYMPUS", StringComparison.OrdinalIgnoreCase))
                return RawVariant.Orf;
            if (make.StartsWith("Panasonic", StringComparison.OrdinalIgnoreCase))
                return RawVariant.Rw2;
            if (make.StartsWith("Canon", StringComparison.OrdinalIgnoreCase))
                return RawVariant.Cr2;
        }

        _ = little;
        return RawVariant.None;
    }

    /// <summary>
    /// Reads the Fujifilm RAF header. The sensor geometry sits in a directory the header points
    /// at, but the embedded JPEG offset is enough to place the file and report its preview size.
    /// </summary>
    private static bool ParseRaf(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        const int JpegOffsetField = 84;

        if (data.Length < JpegOffsetField + 8)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        uint jpegOffset = BinaryPrimitives.ReadUInt32BigEndian(data[JpegOffsetField..]);
        uint jpegLength = BinaryPrimitives.ReadUInt32BigEndian(data[(JpegOffsetField + 4)..]);

        if (jpegOffset < JpegOffsetField || jpegLength == 0 || jpegOffset + (long)jpegLength > data.Length)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        JpegDecoder preview = new();
        if (!preview.TryIdentify(data.Slice((int)jpegOffset, (int)jpegLength), out ImageInfo? jpeg, out error))
            return false;

        info = new ImageInfo
        {
            Format = ImageFormat.Raw,
            Width = jpeg!.Width,
            Height = jpeg.Height,
            BitsPerChannel = 14,
            Channels = 1,
            HasAlpha = false,
            ColorModel = ColorModel.ColorFilterArray,
            PreferredPixelFormat = PixelFormat.Rgb16,
            FrameCount = 1,
            Orientation = jpeg.Orientation,
            RawVariant = RawVariant.Raf,
            Compression = "Fujifilm RAF",
        };
        error = ApertureError.None;
        return true;
    }

    /// <summary>Reads a Canon CR3, which wraps its raw data in the ISO base media container.</summary>
    private static bool ParseCr3(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (!Cr3Header.TryRead(data, out int width, out int height))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (!ValidateDimensions(width, height, out error))
            return false;

        info = new ImageInfo
        {
            Format = ImageFormat.Raw,
            Width = width,
            Height = height,

            // The readings are fourteen bits wide and stored in sixteen, which is the width
            // anything reading them would have to hand them back in.
            BitsPerChannel = 16,
            Channels = 1,
            HasAlpha = false,
            ColorModel = ColorModel.ColorFilterArray,
            PreferredPixelFormat = PixelFormat.Rgb16,
            FrameCount = 1,
            RawVariant = RawVariant.Cr3,
            Compression = "Canon CR3",
        };
        error = ApertureError.None;
        return true;
    }
}
