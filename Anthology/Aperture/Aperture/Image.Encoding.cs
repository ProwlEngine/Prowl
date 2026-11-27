// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

public sealed partial class Image
{
    // ---- Building ---------------------------------------------------------------------

    /// <summary>
    /// Wraps pixels a caller already holds, so they can be written or resized. The bytes are
    /// copied, so the source may be reused or freed straight afterwards.
    /// </summary>
    /// <param name="pixels">The pixels, top row first unless <paramref name="stride"/> says otherwise.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="format">Layout of <paramref name="pixels"/>.</param>
    /// <param name="stride">
    /// Bytes between rows in <paramref name="pixels"/>, or zero when the rows are packed.
    /// </param>
    /// <param name="usePooledMemory">
    /// Whether the copy is rented from the pool, which is cheaper but has to be returned by
    /// <see cref="Dispose"/> before the caller lets go of any span taken from it.
    /// </param>
    public static Image FromPixels(ReadOnlySpan<byte> pixels, int width, int height, PixelFormat format,
                                   int stride = 0, bool usePooledMemory = true)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "An image is at least one pixel each way.");

        int packed = width * format.BytesPerPixel();
        if (stride == 0)
            stride = packed;

        if (stride < packed)
            throw new ArgumentOutOfRangeException(nameof(stride), "A row cannot be shorter than its pixels.");

        long total = (long)stride * height;
        if (pixels.Length < total)
            throw new ArgumentException("Fewer pixels than the geometry describes.", nameof(pixels));

        byte[] buffer = usePooledMemory ? BufferPool.Bytes.Rent((int)total) : new byte[total];
        pixels[..(int)total].CopyTo(buffer);

        ImageFrame frame = new(buffer, (int)total, width, height, stride, format, usePooledMemory);

        return new Image
        {
            Format = ImageFormat.Unknown,
            Width = width,
            Height = height,
            PixelFormat = format,
            Frames = [frame],
            Info = new ImageInfo
            {
                Format = ImageFormat.Unknown,
                Width = width,
                Height = height,
                BitsPerChannel = format.BytesPerChannel() * 8,
                Channels = format.ChannelCount(),
                HasAlpha = format.HasAlpha(),
                IsHdr = format.IsFloatingPoint(),
                ColorModel = format.ChannelCount() <= 2 ? ColorModel.Grayscale : ColorModel.Rgb,
                PreferredPixelFormat = format,
            },
        };
    }

    // ---- Writing ----------------------------------------------------------------------

    /// <summary>
    /// Writes the image to a file, choosing the format from the extension.
    /// </summary>
    /// <exception cref="ApertureException">Nothing can write that format, or the file cannot be written.</exception>
    public void Save(string path, EncodeOptions? options = null)
    {
        if (!TrySave(path, options, out ApertureError error))
            throw ApertureException.ForSave(error, options?.Format ?? ImageFormat.Unknown);
    }

    /// <summary>
    /// Writes the image to a stream. <see cref="EncodeOptions.Format"/> has to name the format,
    /// since a stream carries no name to take it from.
    /// </summary>
    /// <exception cref="ApertureException">Nothing can write that format.</exception>
    public void Save(Stream destination, EncodeOptions options)
    {
        if (!TrySave(destination, options, out ApertureError error))
            throw ApertureException.ForSave(error, options.Format);
    }

    /// <summary>Encodes the image and hands back the bytes.</summary>
    /// <exception cref="ApertureException">Nothing can write that format.</exception>
    public byte[] Encode(EncodeOptions options)
    {
        using MemoryStream buffer = new();
        Save(buffer, options);
        return buffer.ToArray();
    }

    /// <summary>Writes the image to a file, reporting a failure rather than throwing.</summary>
    public bool TrySave(string path, EncodeOptions? options, out ApertureError error)
    {
        ArgumentNullException.ThrowIfNull(path);

        options = options?.Clone() ?? new EncodeOptions();
        if (options.Format == ImageFormat.Unknown)
            options.Format = FormatDetector.FromExtension(path);

        if (options.Format == ImageFormat.Unknown)
        {
            error = ApertureError.UnknownFormat;
            return false;
        }

        try
        {
            using MemoryStream buffer = new();
            if (!TrySave(buffer, options, out error))
                return false;

            using FileStream file = File.Create(path);
            buffer.Position = 0;
            buffer.CopyTo(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ApertureError.IoError;
            return false;
        }
    }

    /// <summary>Writes the image to a stream, reporting a failure rather than throwing.</summary>
    public bool TrySave(Stream destination, EncodeOptions options, out ApertureError error)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        if (!ImageEncoderRegistry.TryGet(options.Format, out IImageEncoder? encoder) || encoder is null)
        {
            error = ApertureError.NotSupported;
            return false;
        }

        ImageFrame frame = RootFrame;
        PixelFormat target = options.TargetPixelFormat
            ?? (encoder.SupportedPixelFormats.Contains(frame.PixelFormat)
                ? frame.PixelFormat
                : encoder.PreferredPixelFormat);

        if (target == frame.PixelFormat)
            return encoder.TryEncode(frame, options, destination, out error);

        if (!PixelConverter.CanConvert(frame.PixelFormat, target))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        using Image converted = Convert(frame, target);
        return encoder.TryEncode(converted.RootFrame, options, destination, out error);
    }

    /// <summary>Copies one frame into another layout, a row at a time.</summary>
    private static Image Convert(ImageFrame frame, PixelFormat target)
    {
        int stride = frame.Width * target.BytesPerPixel();
        byte[] buffer = BufferPool.Bytes.Rent(stride * frame.Height);

        for (int y = 0; y < frame.Height; y++)
        {
            PixelConverter.ConvertRow(frame.GetRow(y), frame.PixelFormat,
                                      buffer.AsSpan(y * stride, stride), target, frame.Width);
        }

        ImageFrame result = new(buffer, stride * frame.Height, frame.Width, frame.Height, stride, target, true);

        return new Image
        {
            Format = ImageFormat.Unknown,
            Width = frame.Width,
            Height = frame.Height,
            PixelFormat = target,
            Frames = [result],
            Info = new ImageInfo
            {
                Format = ImageFormat.Unknown,
                Width = frame.Width,
                Height = frame.Height,
                BitsPerChannel = target.BytesPerChannel() * 8,
                Channels = target.ChannelCount(),
                HasAlpha = target.HasAlpha(),
                PreferredPixelFormat = target,
            },
        };
    }
}
