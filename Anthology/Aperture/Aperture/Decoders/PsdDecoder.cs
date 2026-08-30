// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Psd;
using Prowl.Aperture.Metadata;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads the Photoshop document header for both PSD and the large document PSB variant.</summary>
public sealed class PsdDecoder : DecoderBase
{
    private const int HeaderSize = 26;

    /// <summary>Photoshop's own limit for a .psd; a .psb goes ten times further.</summary>
    private const int MaxPsdDimension = 30000;
    private const int MaxPsbDimension = 300000;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Psd;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".psd", ".psb"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!PsdComposite.TryRead(data, out PsdComposite composite, out error))
            return false;

        if (!PsdImageReader.IsSupported(composite))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        if (!PsdImageReader.CanDescribe(data.Length - composite.DataOffset, composite))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        PixelFormat natural = PsdImageReader.NaturalFormat(composite);
        PixelFormat target = options.TargetPixelFormat ?? natural;
        if (!PixelConverter.CanConvert(natural, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        bool direct = target.ChannelCount() == natural.ChannelCount() &&
                      target.BytesPerChannel() == natural.BytesPerChannel() &&
                      target.IsFloatingPoint() == natural.IsFloatingPoint();

        int naturalStride = composite.Width * natural.BytesPerPixel();
        int stride = options.GetStride(composite.Width, target);
        long total = (long)stride * composite.Height;

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
                scratch = BufferPool.Bytes.Rent(naturalStride * composite.Height);
                surface = scratch.AsSpan(0, naturalStride * composite.Height);
                surface.Clear();
                surfaceStride = naturalStride;
                surfaceFormat = natural;
            }

            if (!PsdImageReader.TryDecode(data, composite, surfaceFormat, surface, surfaceStride,
                                          options.FlipVertically, out error))
                return false;

            if (!direct)
            {
                for (int y = 0; y < composite.Height; y++)
                {
                    PixelConverter.ConvertRow(surface.Slice(y * surfaceStride, naturalStride), natural,
                                              pixels[(y * stride)..], target, composite.Width);
                }
            }

            ImageFrame frame = new(buffer, (int)total, composite.Width, composite.Height, stride, target,
                                   options.UsePooledMemory);

            image = new Image
            {
                Format = ImageFormat.Psd,
                Width = composite.Width,
                Height = composite.Height,
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
        // The colour mode data comes first and the image resources follow it, each resource a
        // signature, a numbered kind, a name padded to an even length, then its bytes.
        if (data.Length < 30)
            return ImageMetadata.Empty;

        uint colourLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data[26..]);
        long at = 30L + colourLength;
        if (at + 4 > data.Length)
            return ImageMetadata.Empty;

        uint resourceLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data[(int)at..]);
        long end = at + 4 + resourceLength;
        if (end > data.Length)
            return ImageMetadata.Empty;

        MetadataBuilder builder = new();
        at += 4;

        for (int scanned = 0; scanned < MaxResources && at + 12 <= end; scanned++)
        {
            if (!data.Slice((int)at, 4).SequenceEqual("8BIM"u8))
                break;

            int kind = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data[((int)at + 4)..]);

            // A Pascal string padded so the whole of it takes an even number of bytes.
            int nameLength = data[(int)at + 6];
            long after = at + 6 + 1 + nameLength;
            if ((nameLength & 1) == 0)
                after++;

            if (after + 4 > end)
                break;

            uint size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data[(int)after..]);
            long body = after + 4;
            if (body + size > end)
                break;

            ReadOnlySpan<byte> payload = data.Slice((int)body, (int)size);

            switch (kind)
            {
                case 1039: builder.SetProfile(payload); break;
                case 1058: builder.SetExif(payload); break;
                case 1060: builder.SetXmp(payload); break;
            }

            at = body + size + (size & 1);
        }

        return builder.Build();
    }

    /// <summary>Cap on resources walked, well past what a document carries.</summary>
    private const int MaxResources = 4096;

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (data.Length < HeaderSize)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (!data[..4].SequenceEqual("8BPS"u8))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        SpanReader reader = new(data, littleEndian: false);
        reader.Skip(4);
        reader.TryReadUInt16(out ushort version);
        reader.TryReadBytes(6, out ReadOnlySpan<byte> reserved);
        reader.TryReadUInt16(out ushort channels);
        reader.TryReadUInt32(out uint height);
        reader.TryReadUInt32(out uint width);
        reader.TryReadUInt16(out ushort depth);
        reader.TryReadUInt16(out ushort colorMode);

        if (version is not (1 or 2))
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        foreach (byte b in reserved)
        {
            if (b == 0)
                continue;
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (channels is < 1 or > 56)
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        if (depth is not (1 or 8 or 16 or 32))
        {
            error = ApertureError.InvalidBitDepth;
            return false;
        }

        int limit = version == 2 ? MaxPsbDimension : MaxPsdDimension;
        if (width == 0 || height == 0 || width > limit || height > limit)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        ColorModel model = colorMode switch
        {
            0 => ColorModel.Grayscale,
            1 => ColorModel.Grayscale,
            2 => ColorModel.Indexed,
            3 => ColorModel.Rgb,
            4 => ColorModel.Cmyk,
            7 => ColorModel.Unknown,
            8 => ColorModel.Grayscale,
            9 => ColorModel.Lab,
            _ => ColorModel.Unknown,
        };

        if (model == ColorModel.Unknown && colorMode is not 7)
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        int colorChannels = colorMode switch
        {
            3 => 3,
            4 => 4,
            9 => 3,
            _ => 1,
        };
        bool hasAlpha = channels > colorChannels;
        int outputChannels = Math.Min(colorChannels + (hasAlpha ? 1 : 0), 4);

        // The reader decides this: a mode resolving to something other than what it stores,
        // such as an index or a fourth ink, is not readable from the header alone.
        bool readable = PsdComposite.TryRead(data, out PsdComposite composite, out _);

        PixelFormat natural = readable
            ? PsdImageReader.NaturalFormat(composite)
            : ChoosePixelFormat(outputChannels, depth == 1 ? 8 : depth, depth == 32, hasAlpha);

        if (readable)
            hasAlpha = PsdImageReader.HasAlpha(composite);

        info = new ImageInfo
        {
            Format = ImageFormat.Psd,
            Width = (int)width,
            Height = (int)height,
            BitsPerChannel = depth,
            Channels = channels,
            HasAlpha = hasAlpha,
            IsHdr = depth == 32,
            ColorModel = model,
            PreferredPixelFormat = natural,
            FrameCount = 1,
            Compression = version == 2 ? "Large document (PSB)" : "Photoshop document (PSD)",
        };
        error = ApertureError.None;
        return true;
    }
}
