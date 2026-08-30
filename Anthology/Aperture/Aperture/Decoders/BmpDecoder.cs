// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Bmp;

namespace Prowl.Aperture.Decoders;

/// <summary>Reads Windows and OS/2 bitmaps, from the 12 byte core header through V5.</summary>
public sealed class BmpDecoder : DecoderBase
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Bmp;

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions { get; } = [".bmp", ".dib"];

    /// <inheritdoc />
    public override bool CanDecodePixels => true;

    /// <inheritdoc />
    protected override bool DecodePixels(ReadOnlySpan<byte> data, DecodeOptions options,
                                         ImageInfo info, out Image? image, out ApertureError error)
    {
        image = null;

        if (!BmpHeader.TryRead(data, out BmpHeader header, out error))
            return false;

        // A bitmap may carry a whole file of another format in place of its pixels, in which case
        // there is nothing here to decode and the right reader takes it from the same bytes.
        if (header.Compression is 4 or 5)
            return TryDecodeEmbedded(data, header, options, out image, out error);

        // Checked before anything is sized, so a header declaring a picture its data could never
        // fill costs nothing to refuse.
        if (!BmpImageReader.CanDescribe(data.Length - header.PixelOffset, header))
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

        int channels = target switch
        {
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

            if (!BmpImageReader.TryDecode(data, header, surfaceChannels, surface, surfaceStride,
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
                Format = ImageFormat.Bmp,
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

    private static bool TryDecodeEmbedded(ReadOnlySpan<byte> data, in BmpHeader header,
                                          DecodeOptions options, out Image? image, out ApertureError error)
    {
        image = null;

        if (header.PixelOffset < 0 || header.PixelOffset >= data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        ReadOnlySpan<byte> payload = data[header.PixelOffset..];
        IImageDecoder inner = header.Compression == 4 ? new JpegDecoder() : new PngDecoder();

        if (!inner.TryDecode(payload, options, out image, out error))
            return false;

        return true;
    }

    /// <inheritdoc />
    protected override bool ParseHeader(ReadOnlySpan<byte> data, out ImageInfo? info, out ApertureError error)
    {
        info = null;

        if (!BmpHeader.TryRead(data, out BmpHeader header, out error))
            return false;

        bool hasAlpha = header.HasAlpha;
        info = new ImageInfo
        {
            Format = ImageFormat.Bmp,
            Width = header.Width,
            Height = header.Height,
            BitsPerChannel = header.BitsPerPixel >= 24 ? 8 : header.BitsPerPixel,
            Channels = header.BitsPerPixel >= 24 ? header.BitsPerPixel / 8 : 1,
            HasAlpha = hasAlpha,
            ColorModel = header.IsIndexed ? ColorModel.Indexed : ColorModel.Rgb,
            PreferredPixelFormat = hasAlpha ? PixelFormat.Rgba8 : PixelFormat.Rgb8,
            FrameCount = 1,
            Orientation = header.TopDown ? ExifOrientation.TopLeft : ExifOrientation.Unspecified,
            HorizontalDpi = header.HorizontalDpi,
            VerticalDpi = header.VerticalDpi,
            Compression = DescribeCompression(header.Compression, header.DibSize),
        };
        error = ApertureError.None;
        return true;
    }

    private static string DescribeCompression(uint compression, uint dibSize) => compression switch
    {
        0 => dibSize == 12 ? "Uncompressed, core header" : "Uncompressed",
        1 => "RLE8",
        2 => "RLE4",
        3 => "Bitfields",
        4 => "Embedded JPEG",
        5 => "Embedded PNG",
        6 => "Alpha bitfields",
        _ => "Unknown",
    };
}
