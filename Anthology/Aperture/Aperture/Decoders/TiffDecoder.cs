// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Tiff;
using Prowl.Aperture.Metadata;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads TIFF and BigTIFF in both byte orders.</summary>
public sealed class TiffDecoder : DecoderBase
{
    /// <summary>Cap on directories followed, so a self referencing IFD chain cannot loop forever.</summary>
    private const int MaxDirectories = 512;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Tiff;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".tif", ".tiff"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool OrientationIsPending => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!TiffDirectory.TryReadHeader(data, out bool little, out bool big, out ulong first) ||
            !TiffDirectory.TryOpen(data, little, big, first, out TiffDirectory ifd))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (!TryDecodeDirectory(data, ifd, options, info, Format, out image, out error))
            return false;

        if (options.DecodeAllFrames && ifd.NextDirectoryOffset != 0)
            AddLaterPages(data, little, big, ifd, options, info, ref image);

        return true;
    }

    /// <summary>
    /// Adds the pages that follow the first. They are independent pictures that need agree on
    /// nothing, so the first settles the layout and the rest are read into it. A page larger than
    /// the first is left out, since the frames of one image share one canvas.
    /// </summary>
    private static void AddLaterPages(ReadOnlySpan<byte> data, bool little, bool big,
                                      in TiffDirectory first, DecodeOptions options, ImageInfo info,
                                      ref Image? image)
    {
        List<ImageFrame> frames = [.. image!.Frames];

        DecodeOptions rest = options.Clone();
        rest.TargetPixelFormat = image.PixelFormat;

        HashSet<ulong> seen = [];
        ulong at = first.NextDirectoryOffset;

        for (int page = 1; page < options.MaxFrames && at != 0 && seen.Add(at); page++)
        {
            if (!TiffDirectory.TryOpen(data, little, big, at, out TiffDirectory next))
                break;

            at = next.NextDirectoryOffset;

            // One unreadable page does not condemn the pages that did read.
            if (!TryDecodeDirectory(data, next, rest, info, ImageFormat.Tiff, out Image? decoded, out _))
                continue;

            if (decoded!.Width > image.Width || decoded.Height > image.Height)
            {
                decoded.Dispose();
                break;
            }

            frames.AddRange(decoded.Frames);
        }

        if (frames.Count == image.Frames.Count)
            return;

        image = new Image
        {
            Format = ImageFormat.Tiff,
            Width = image.Width,
            Height = image.Height,
            PixelFormat = image.PixelFormat,
            Frames = [.. frames],
            Info = info,
            Metadata = image.Metadata,
        };
    }

    /// <summary>
    /// Reads the pixels one directory describes. A raw file's full resolution image sits in a
    /// sub-directory rather than the first one, so which directory to read is the caller's choice.
    /// </summary>
    internal static bool TryDecodeDirectory(ReadOnlySpan<byte> data, in TiffDirectory ifd,
                                            DecodeOptions options, ImageInfo info, ImageFormat format,
                                            out Image? image, out ApertureError error)
    {
        image = null;

        if (!TiffImage.TryRead(data, ifd, out TiffImage description, out error))
            return false;

        if (!TiffImageReader.IsSupported(description))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        if (!TiffImageReader.CanDescribe(data, description))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        PixelFormat natural = TiffImageReader.NaturalFormat(description);
        PixelFormat target = options.TargetPixelFormat ?? natural;
        if (!PixelConverter.CanConvert(natural, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        // The reader writes whichever layout it is handed, so a second pass is only needed when
        // the caller wants something the file's own samples do not map onto.
        bool direct = target.ChannelCount() == natural.ChannelCount() &&
                      target.BytesPerChannel() == natural.BytesPerChannel() &&
                      target.IsFloatingPoint() == natural.IsFloatingPoint();

        int naturalStride = description.Width * natural.BytesPerPixel();
        int stride = options.GetStride(description.Width, target);
        long total = (long)stride * description.Height;

        if (total > options.MaxAllocationBytes || total > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] buffer = options.UsePooledMemory ? BufferPool.Bytes.Rent((int)total) : new byte[(int)total];
        byte[]? scratch = null;

        try
        {
            Span<byte> pixels = buffer.AsSpan(0, (int)total);
            pixels.Clear();

            Span<byte> surface = pixels;
            int surfaceStride = stride;
            PixelFormat surfaceFormat = target;

            if (!direct)
            {
                scratch = BufferPool.Bytes.Rent(naturalStride * description.Height);
                surface = scratch.AsSpan(0, naturalStride * description.Height);
                surface.Clear();
                surfaceStride = naturalStride;
                surfaceFormat = natural;
            }

            if (!TiffImageReader.TryDecode(data, description, surfaceFormat, surface, surfaceStride,
                                           options.FlipVertically, out error))
                return false;

            if (!direct)
            {
                for (int y = 0; y < description.Height; y++)
                {
                    PixelConverter.ConvertRow(surface.Slice(y * surfaceStride, naturalStride), natural,
                                              pixels[(y * stride)..], target, description.Width);
                }
            }

            ImageFrame frame = new(buffer, (int)total, description.Width, description.Height, stride,
                                   target, options.UsePooledMemory);

            image = new Image
            {
                Format = format,
                Width = description.Width,
                Height = description.Height,
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
        if (!TiffDirectory.TryReadHeader(data, out bool little, out bool big, out ulong first) ||
            !TiffDirectory.TryOpen(data, little, big, first, out TiffDirectory ifd))
            return ImageMetadata.Empty;

        MetadataBuilder builder = new();

        if (ifd.TryGetByteRange(TiffTag.IccProfile, out int at, out int length))
            builder.SetProfile(data.Slice(at, length));

        if (ifd.TryGetByteRange(TiffTag.Xmp, out at, out length))
            builder.SetXmp(data.Slice(at, length));

        return builder.Build();
    }

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (data.Length < 8)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (!TiffDirectory.TryReadHeader(data, out bool little, out bool big, out ulong firstOffset))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (!TiffDirectory.TryOpen(data, little, big, firstOffset, out TiffDirectory ifd0))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        return DescribeDirectory(data, little, big, ifd0, firstOffset, ImageFormat.Tiff, RawVariant.None, out info, out error);
    }

    /// <summary>
    /// Turns an image file directory into an <see cref="ImageInfo"/>. Shared with
    /// <see cref="RawDecoder"/>, which reads the same structure and only differs in what it
    /// reports as the format.
    /// </summary>
    internal static bool DescribeDirectory(ReadOnlySpan<byte> data, bool little, bool big, TiffDirectory ifd,
                                           ulong firstOffset, ImageFormat format, RawVariant variant,
                                           out ImageInfo? info, out ApertureError error)
    {
        info = null;

        if (!ifd.TryGetInteger(TiffTag.ImageWidth, out long width) ||
            !ifd.TryGetInteger(TiffTag.ImageLength, out long height))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (!ValidateDimensions(width, height, out error))
            return false;

        if (!ifd.TryGetInteger(TiffTag.SamplesPerPixel, out long samples))
            samples = 1;

        if (samples is < 1 or > 64)
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        Span<long> bits = stackalloc long[8];
        int bitsRead = ifd.GetIntegers(TiffTag.BitsPerSample, bits);
        int bitsPerChannel = bitsRead > 0 ? (int)bits[0] : 1;
        if (bitsPerChannel is < 1 or > 64)
        {
            error = ApertureError.InvalidBitDepth;
            return false;
        }

        if (!ifd.TryGetInteger(TiffTag.PhotometricInterpretation, out long photometric))
            photometric = 1;

        if (!ifd.TryGetInteger(TiffTag.Compression, out long compression))
            compression = 1;

        if (!ifd.TryGetInteger(TiffTag.SampleFormat, out long sampleFormat))
            sampleFormat = 1;

        ifd.TryGetInteger(TiffTag.ExtraSamples, out long extraSamples);

        ExifOrientation orientation = ExifOrientation.Unspecified;
        if (ifd.TryGetInteger(TiffTag.Orientation, out long orientationValue) && orientationValue is >= 1 and <= 8)
            orientation = (ExifOrientation)orientationValue;

        double horizontalDpi = 0, verticalDpi = 0;
        if (!ifd.TryGetInteger(TiffTag.ResolutionUnit, out long unit))
            unit = 2;
        if (unit != 1 && ifd.TryGetRational(TiffTag.XResolution, out double x) &&
            ifd.TryGetRational(TiffTag.YResolution, out double y))
        {
            double scale = unit == 3 ? 2.54 : 1.0;
            horizontalDpi = x * scale;
            verticalDpi = y * scale;
        }

        ColorModel colorModel = photometric switch
        {
            0 or 1 => ColorModel.Grayscale,
            2 => ColorModel.Rgb,
            3 => ColorModel.Indexed,
            5 => ColorModel.Cmyk,
            6 => ColorModel.YCbCr,
            8 or 9 or 10 => ColorModel.Lab,
            32803 or 34892 => ColorModel.ColorFilterArray,
            _ => ColorModel.Unknown,
        };

        bool floating = sampleFormat == 3;
        bool hasAlpha = extraSamples != 0 ||
                        (colorModel == ColorModel.Rgb && samples >= 4) ||
                        (colorModel == ColorModel.Grayscale && samples >= 2);

        int outputChannels = colorModel switch
        {
            ColorModel.Grayscale => hasAlpha ? 2 : 1,
            ColorModel.Indexed => hasAlpha ? 4 : 3,
            ColorModel.Cmyk => 4,
            _ => (int)Math.Min(samples, 4),
        };

        int frames = CountDirectories(data, little, big, firstOffset, out bool looped);

        // A chain that returns to a directory it has already visited names no set of pages, and
        // following it would either loop or pick an arbitrary place to stop.
        if (looped)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        // The reader is asked rather than the tags read a second time into a different answer,
        // since ink and a colour table both resolve to something the tags do not say.
        PixelFormat natural = ChoosePixelFormat(outputChannels, bitsPerChannel, floating, hasAlpha);

        if (TiffDirectory.TryOpen(data, little, big, firstOffset, out TiffDirectory page) &&
            TiffImage.TryRead(data, page, out TiffImage description, out _))
        {
            natural = TiffImageReader.NaturalFormat(description);

            // An extra sample is only transparency when its own tag says it is; one of
            // unspecified purpose is a channel the file declines to describe.
            hasAlpha = description.AlphaSample >= 0;
        }

        info = new ImageInfo
        {
            Format = format,
            Width = (int)width,
            Height = (int)height,
            BitsPerChannel = bitsPerChannel,
            Channels = (int)samples,
            HasAlpha = hasAlpha,
            IsHdr = floating && bitsPerChannel >= 16,
            ColorModel = colorModel,
            PreferredPixelFormat = natural,
            FrameCount = frames,
            Orientation = orientation,
            HorizontalDpi = horizontalDpi,
            VerticalDpi = verticalDpi,
            RawVariant = variant,
            Compression = DescribeCompression(compression) + (big ? ", BigTIFF" : string.Empty),
        };
        error = ApertureError.None;
        return true;
    }

    /// <summary>
    /// Counts the pages by following the directory chain. Offsets already visited terminate the
    /// walk, which is what stops a file whose last IFD points back at the first.
    /// </summary>
    private static int CountDirectories(ReadOnlySpan<byte> data, bool little, bool big, ulong firstOffset,
                                        out bool looped)
    {
        HashSet<ulong> visited = [];
        ulong offset = firstOffset;
        int count = 0;
        looped = false;

        while (offset != 0 && count < MaxDirectories)
        {
            if (!visited.Add(offset))
            {
                looped = true;
                break;
            }

            if (!TiffDirectory.TryOpen(data, little, big, offset, out TiffDirectory directory))
                break;

            count++;
            offset = directory.NextDirectoryOffset;
        }

        return Math.Max(count, 1);
    }

    private static string DescribeCompression(long compression) => compression switch
    {
        1 => "Uncompressed",
        2 => "CCITT modified Huffman",
        3 => "CCITT Group 3 fax",
        4 => "CCITT Group 4 fax",
        5 => "LZW",
        6 => "JPEG, old style",
        7 => "JPEG",
        8 or 32946 => "Deflate",
        9 => "JBIG on black and white",
        10 => "JBIG on colour",
        32773 => "PackBits",
        33003 or 33005 or 34712 => "JPEG 2000",
        34887 => "LERC",
        34925 => "LZMA",
        50000 => "Zstandard",
        50001 => "WebP",
        50002 => "JPEG XL",
        _ => $"Unknown ({compression})",
    };
}
