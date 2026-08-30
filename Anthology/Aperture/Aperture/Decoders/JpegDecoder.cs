// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers;
using Prowl.Aperture.Decoders.Jpeg;
using Prowl.Aperture.Metadata;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads JPEG in JFIF and Exif framing, baseline through progressive and lossless.</summary>
public sealed class JpegDecoder : DecoderBase
{
    /// <summary>Cap on markers walked before the file is treated as corrupt.</summary>
    private const int MaxMarkersScanned = 4096;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Jpeg;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".jpg", ".jpeg", ".jpe", ".jfif"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool OrientationIsPending => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

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

        // The reader interleaves whatever channel count it is given, so an alpha channel costs
        // nothing beyond writing the opaque byte, and the usual upload layout needs no second pass.
        bool grayscale = info.Channels == 1;
        int channels = target switch
        {
            PixelFormat.L8 when grayscale => 1,
            PixelFormat.Rgb8 => 3,
            PixelFormat.Rgba8 => 4,
            _ => 0,
        };

        bool direct = channels != 0;
        int naturalChannels = grayscale ? 1 : 3;
        int naturalStride = direct ? stride : info.Width * naturalChannels;

        byte[] buffer = options.UsePooledMemory
            ? BufferPool.Bytes.Rent((int)total)
            : new byte[(int)total];

        byte[]? scratch = null;
        try
        {
            Span<byte> pixels = buffer.AsSpan(0, (int)total);

            // Every row is written in full, so only the padding an alignment adds needs zeroing.
            if (stride != info.Width * target.BytesPerPixel())
                pixels.Clear();

            Span<byte> surface = pixels;
            int surfaceStride = stride;

            if (!direct)
            {
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
                channels = naturalChannels;
            }

            if (!JpegImageReader.TryDecode(data, options, info, channels, surface, surfaceStride,
                                           options.FlipVertically, out error))
                return false;

            if (!direct)
            {
                for (int y = 0; y < info.Height; y++)
                {
                    PixelConverter.ConvertRow(surface.Slice(y * surfaceStride, naturalStride), natural,
                                              pixels[(y * stride)..], target, info.Width);
                }
            }

            ImageFrame frame = new(buffer, (int)total, info.Width, info.Height, stride, target,
                                   options.UsePooledMemory);

            image = new Image
            {
                Format = ImageFormat.Jpeg,
                Width = info.Width,
                Height = info.Height,
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
            if (scratch is not null)
                BufferPool.Bytes.Return(scratch);
            if (buffer.Length != 0 && options.UsePooledMemory)
                BufferPool.Bytes.Return(buffer);
        }
    }

    /// <inheritdoc />
    protected override ImageMetadata ReadMetadata(ReadOnlySpan<byte> data)
    {
        MetadataBuilder builder = new();

        // A colour profile larger than one segment holds is split across several, each numbered,
        // so the pieces are gathered first and joined once they have all been seen.
        List<(int Index, int Start, int Length)>? profile = null;
        int offset = 2;

        for (int scanned = 0; scanned < MaxMetadataSegments; scanned++)
        {
            if (offset + 4 > data.Length || data[offset] != 0xFF)
                break;

            byte marker = data[offset + 1];
            if (marker is 0xD9 or 0xDA)
                break;

            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            int length = (data[offset + 2] << 8) | data[offset + 3];
            if (length < 2 || offset + 2 + length > data.Length)
                break;

            int start = offset + 4;
            ReadOnlySpan<byte> payload = data.Slice(start, length - 2);
            offset += 2 + length;

            if (marker == 0xE1)
            {
                if (payload.Length > 6 && payload[..6].SequenceEqual(ExifName))
                    builder.SetExif(payload[6..]);
                else if (payload.Length > 29 && payload[..29].SequenceEqual(XmpName))
                    builder.SetXmp(payload[29..]);
            }
            else if (marker == 0xE2 && payload.Length > 14 && payload[..12].SequenceEqual(ProfileName))
            {
                (profile ??= []).Add((payload[12], start + 14, payload.Length - 14));
            }
        }

        if (profile is not null)
            JoinProfile(data, profile, builder);

        return builder.Build();
    }

    /// <summary>Cap on segments walked for metadata, well past any real file's count.</summary>
    private const int MaxMetadataSegments = 4096;

    private static ReadOnlySpan<byte> ExifName => "Exif\0\0"u8;

    private static ReadOnlySpan<byte> ProfileName => "ICC_PROFILE\0"u8;

    private static ReadOnlySpan<byte> XmpName => "http://ns.adobe.com/xap/1.0/\0"u8;

    /// <summary>Puts the numbered pieces of a split colour profile back in order.</summary>
    private static void JoinProfile(ReadOnlySpan<byte> data,
                                    List<(int Index, int Start, int Length)> pieces,
                                    MetadataBuilder builder)
    {
        if (pieces.Count == 0)
            return;

        pieces.Sort((a, b) => a.Index.CompareTo(b.Index));

        long total = 0;
        foreach ((int _, int _, int length) in pieces)
            total += length;

        if (total is <= 0 or > int.MaxValue)
            return;

        byte[] joined = new byte[total];
        int written = 0;

        foreach ((int _, int start, int length) in pieces)
        {
            if (start < 0 || length < 0 || start + length > data.Length)
                return;

            data.Slice(start, length).CopyTo(joined.AsSpan(written));
            written += length;
        }

        builder.SetProfile(joined);
    }

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (data.Length < 4)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (data[0] != 0xFF || data[1] != 0xD8)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        int offset = 2;
        bool sawFrame = false;
        int width = 0, height = 0, precision = 0, components = 0;
        byte frameMarker = 0;
        ExifOrientation orientation = ExifOrientation.Unspecified;
        double horizontalDpi = 0, verticalDpi = 0;
        int adobeTransform = -1;

        for (int scanned = 0; scanned < MaxMarkersScanned; scanned++)
        {
            if (!TryReadMarker(data, ref offset, out byte marker))
                break;

            // Markers with no payload: RSTn, SOI, EOI, TEM.
            if (marker is 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7))
                continue;

            if (marker == 0xD9)
                break;

            if (offset + 2 > data.Length)
            {
                error = sawFrame ? ApertureError.None : ApertureError.UnexpectedEndOfData;
                if (!sawFrame)
                    return false;
                break;
            }

            int segmentLength = (data[offset] << 8) | data[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                if (!sawFrame)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }
                break;
            }

            ReadOnlySpan<byte> payload = data.Slice(offset + 2, segmentLength - 2);

            if (IsStartOfFrame(marker))
            {
                if (payload.Length < 6)
                {
                    error = ApertureError.InvalidHeader;
                    return false;
                }

                precision = payload[0];
                height = (payload[1] << 8) | payload[2];
                width = (payload[3] << 8) | payload[4];
                components = payload[5];
                frameMarker = marker;
                sawFrame = true;
            }
            else if (marker == 0xE0 && payload.Length >= 12 && payload[..5].SequenceEqual("JFIF\0"u8))
            {
                ReadJfifDensity(payload, ref horizontalDpi, ref verticalDpi);
            }
            else if (marker == 0xE1 && payload.Length > 6 && payload[..6].SequenceEqual("Exif\0\0"u8))
            {
                ReadExif(payload[6..], ref orientation, ref horizontalDpi, ref verticalDpi);
            }
            else if (marker == 0xEE && payload.Length >= 12 && payload[..5].SequenceEqual("Adobe"u8))
            {
                adobeTransform = payload[11];
            }

            offset += segmentLength;

            // Entropy coded data follows the scan header, so stop parsing markers once the
            // frame is known and the first scan has started.
            if (marker == 0xDA && sawFrame)
                break;
        }

        if (!sawFrame)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        // A height of zero is legal in the frame header only if a DNL marker supplies it later.
        if (width <= 0 || height < 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (precision is not (8 or 12 or 16))
        {
            error = ApertureError.InvalidBitDepth;
            return false;
        }

        if (components is < 1 or > 4)
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        ColorModel colorModel = components switch
        {
            1 => ColorModel.Grayscale,
            4 => adobeTransform == 2 ? ColorModel.YCbCr : ColorModel.Cmyk,
            _ => ColorModel.YCbCr,
        };

        // Four component files carry ink, which this library resolves to colour on the way out.
        int outputChannels = components == 1 ? 1 : 3;
        info = new ImageInfo
        {
            Format = ImageFormat.Jpeg,
            Width = width,
            Height = height,
            BitsPerChannel = precision,
            Channels = components,
            HasAlpha = false,
            ColorModel = colorModel,
            PreferredPixelFormat = ChoosePixelFormat(outputChannels, precision > 8 ? 16 : 8, false),
            FrameCount = 1,
            Orientation = orientation,
            HorizontalDpi = horizontalDpi,
            VerticalDpi = verticalDpi,
            Compression = DescribeFrame(frameMarker),
        };
        error = ApertureError.None;
        return true;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF);

    private static string DescribeFrame(byte marker) => marker switch
    {
        0xC0 => "Baseline",
        0xC1 => "Extended sequential",
        0xC2 => "Progressive",
        0xC3 => "Lossless",
        0xC5 => "Differential sequential",
        0xC6 => "Differential progressive",
        0xC7 => "Differential lossless",
        0xC9 => "Arithmetic extended sequential",
        0xCA => "Arithmetic progressive",
        0xCB => "Arithmetic lossless",
        0xCD => "Arithmetic differential sequential",
        0xCE => "Arithmetic differential progressive",
        0xCF => "Arithmetic differential lossless",
        _ => "Unknown",
    };

    /// <summary>
    /// Advances to the next marker. Any number of 0xFF fill bytes may precede it, and 0xFF00 is
    /// a stuffed data byte rather than a marker.
    /// </summary>
    private static bool TryReadMarker(ReadOnlySpan<byte> data, ref int offset, out byte marker)
    {
        marker = 0;
        while (offset < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            int probe = offset;
            while (probe < data.Length && data[probe] == 0xFF)
                probe++;

            if (probe >= data.Length)
                return false;

            byte candidate = data[probe];
            offset = probe + 1;
            if (candidate == 0x00)
                continue;

            marker = candidate;
            return true;
        }
        return false;
    }

    private static void ReadJfifDensity(ReadOnlySpan<byte> payload, ref double horizontalDpi, ref double verticalDpi)
    {
        byte units = payload[7];
        int x = (payload[8] << 8) | payload[9];
        int y = (payload[10] << 8) | payload[11];
        if (x == 0 || y == 0)
            return;

        // Unit 1 is dots per inch, unit 2 dots per centimetre, unit 0 an aspect ratio only.
        switch (units)
        {
            case 1:
                horizontalDpi = x;
                verticalDpi = y;
                break;
            case 2:
                horizontalDpi = x * 2.54;
                verticalDpi = y * 2.54;
                break;
        }
    }

    private static void ReadExif(ReadOnlySpan<byte> tiff, ref ExifOrientation orientation,
                                 ref double horizontalDpi, ref double verticalDpi)
    {
        if (!TiffDirectory.TryReadHeader(tiff, out bool little, out bool big, out ulong first) ||
            !TiffDirectory.TryOpen(tiff, little, big, first, out TiffDirectory ifd0))
            return;

        if (ifd0.TryGetInteger(TiffTag.Orientation, out long value) && value is >= 1 and <= 8)
            orientation = (ExifOrientation)value;

        if (horizontalDpi != 0 || verticalDpi != 0)
            return;

        if (!ifd0.TryGetInteger(TiffTag.ResolutionUnit, out long unit))
            unit = 2;

        if (ifd0.TryGetRational(TiffTag.XResolution, out double x) &&
            ifd0.TryGetRational(TiffTag.YResolution, out double y))
        {
            double scale = unit == 3 ? 2.54 : 1.0;
            horizontalDpi = x * scale;
            verticalDpi = y * scale;
        }
    }
}
