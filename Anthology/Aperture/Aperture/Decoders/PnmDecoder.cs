// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Pnm;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads the Netpbm family: PBM, PGM and PPM in both encodings, plus PAM.</summary>
public sealed class PnmDecoder : DecoderBase
{
    /// <summary>Cap on header bytes scanned, so a file of nothing but comments cannot stall.</summary>
    private const int MaxHeaderBytes = 64 * 1024;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Pnm;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".pnm", ".pbm", ".pgm", ".ppm", ".pam"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!PnmHeader.TryRead(data, out PnmHeader header, out error))
            return false;

        if (!PnmImageReader.CanDescribe(data.Length - header.DataOffset, header))
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

        // The file's own layout is what the reader writes, so anything else is a second pass.
        bool direct = target == natural;
        int naturalStride = info.Width * natural.BytesPerPixel();
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

            // Every row is written in full, so only the padding an alignment adds needs zeroing.
            if (stride != info.Width * target.BytesPerPixel())
                pixels.Clear();

            Span<byte> surface = pixels;
            int surfaceStride = stride;

            if (!direct)
            {
                scratch = BufferPool.Bytes.Rent(naturalStride * info.Height);
                surface = scratch.AsSpan(0, naturalStride * info.Height);
                surfaceStride = naturalStride;
            }

            if (!PnmImageReader.TryDecode(data, header, surface, surfaceStride,
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
                Format = ImageFormat.Pnm,
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

        if (!PnmHeader.TryRead(data, out PnmHeader header, out error))
            return false;

        int channels = header.Channels;
        bool hasAlpha = channels is 2 or 4;
        int bits = header.IsBitmap ? 1 : header.BitsPerChannel;

        info = new ImageInfo
        {
            Format = ImageFormat.Pnm,
            Width = header.Width,
            Height = header.Height,
            BitsPerChannel = bits,
            Channels = channels,
            HasAlpha = hasAlpha,
            ColorModel = channels >= 3 ? ColorModel.Rgb : ColorModel.Grayscale,
            PreferredPixelFormat = ChoosePixelFormat(channels, header.BitsPerChannel, false, hasAlpha),
            FrameCount = 1,
            Compression = header.Variant == 7 ? "Uncompressed, PAM"
                : header.IsAscii ? "Uncompressed, ASCII" : "Uncompressed, binary",
        };
        error = ApertureError.None;
        return true;
    }
}
