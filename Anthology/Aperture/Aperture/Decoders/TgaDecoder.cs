// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Tga;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads Truevision TGA, both the original 1.0 layout and the 2.0 footer extension.</summary>
public sealed class TgaDecoder : DecoderBase
{
    private const int HeaderSize = 18;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Tga;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".tga", ".icb", ".vda", ".vst"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!TgaHeader.TryRead(data, out TgaHeader header, out error))
            return false;

        if (!TgaImageReader.CanDescribe(data.Length - header.PixelOffset, header))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        PixelFormat natural = info.PreferredPixelFormat;
        PixelFormat target = options.TargetPixelFormat ?? natural;
        if (!PixelConverter.CanConvert(natural, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        bool grayscale = header.IsGrayscale;
        int channels = target switch
        {
            PixelFormat.L8 when grayscale => 1,
            PixelFormat.Rgb8 => 3,
            PixelFormat.Rgba8 => 4,
            _ => 0,
        };

        bool direct = channels != 0;
        int naturalChannels = natural.BytesPerPixel();
        int stride = options.GetStride(info.Width, target);
        long total = (long)stride * info.Height;

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
            int surfaceChannels = channels;

            if (!direct)
            {
                int naturalStride = info.Width * naturalChannels;
                scratch = BufferPool.Bytes.Rent(naturalStride * info.Height);
                surface = scratch.AsSpan(0, naturalStride * info.Height);
                surface.Clear();
                surfaceStride = naturalStride;
                surfaceChannels = naturalChannels;
            }

            if (!TgaImageReader.TryDecode(data, header, surfaceChannels, surface, surfaceStride,
                                          options.FlipVertically, out error))
                return false;

            if (!direct)
            {
                for (int y = 0; y < info.Height; y++)
                {
                    PixelConverter.ConvertRow(surface.Slice(y * surfaceStride, surfaceStride), natural,
                                              pixels[(y * stride)..], target, info.Width);
                }
            }

            ImageFrame frame = new(buffer, (int)total, info.Width, info.Height, stride, target,
                                   options.UsePooledMemory);

            image = new Image
            {
                Format = ImageFormat.Tga,
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
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;
        if (data.Length < HeaderSize)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        SpanReader reader = new(data);
        reader.TryReadByte(out byte idLength);
        reader.TryReadByte(out byte colorMapType);
        reader.TryReadByte(out byte imageType);
        reader.TryReadUInt16(out ushort colorMapFirst);
        reader.TryReadUInt16(out ushort colorMapLength);
        reader.TryReadByte(out byte colorMapEntrySize);
        reader.Skip(4); // x and y origin
        reader.TryReadUInt16(out ushort width);
        reader.TryReadUInt16(out ushort height);
        reader.TryReadByte(out byte pixelDepth);
        reader.TryReadByte(out byte descriptor);

        if (colorMapType > 1 || (descriptor & 0xC0) != 0)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (imageType is not (0 or 1 or 2 or 3 or 9 or 10 or 11 or 32 or 33))
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        if (imageType == 0)
        {
            error = ApertureError.NoImageData;
            return false;
        }

        if (width == 0 || height == 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (pixelDepth is not (8 or 15 or 16 or 24 or 32))
        {
            error = ApertureError.InvalidBitDepth;
            return false;
        }

        bool paletted = imageType is 1 or 9;
        if (paletted)
        {
            if (colorMapType != 1 || colorMapLength == 0)
            {
                error = ApertureError.InvalidHeader;
                return false;
            }
            if (colorMapEntrySize is not (15 or 16 or 24 or 32))
            {
                error = ApertureError.InvalidHeader;
                return false;
            }
        }

        long colorMapBytes = colorMapType == 1 ? (long)colorMapLength * ((colorMapEntrySize + 7) / 8) : 0;
        long pixelStart = HeaderSize + idLength + colorMapBytes;
        if (pixelStart > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        // The low four bits of the descriptor hold the alpha channel depth.
        int attributeBits = descriptor & 0x0F;
        bool grayscale = imageType is 3 or 11;
        TgaHeader.TryRead(data, out TgaHeader parsed, out _);
        bool hasAlpha = parsed.AlphaUsed && !grayscale;
        bool rle = imageType is 9 or 10 or 11 or 32 or 33;

        int channels = grayscale ? 1 : hasAlpha ? 4 : 3;
        info = new ImageInfo
        {
            Format = ImageFormat.Tga,
            Width = width,
            Height = height,
            BitsPerChannel = 8,
            Channels = channels,
            HasAlpha = hasAlpha,
            ColorModel = paletted ? ColorModel.Indexed : grayscale ? ColorModel.Grayscale : ColorModel.Rgb,
            PreferredPixelFormat = ChoosePixelFormat(channels, 8, false, hasAlpha),
            FrameCount = 1,
            // Bit 5 set means the first row stored is the top row.
            Orientation = (descriptor & 0x20) != 0 ? ExifOrientation.TopLeft : ExifOrientation.BottomLeft,
            Compression = rle ? "RLE" : "Uncompressed",
        };
        _ = colorMapFirst;
        error = ApertureError.None;
        return true;
    }
}
