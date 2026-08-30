// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Utilities;

/// <summary>
/// Resampling. Every method hands back a new <see cref="Image"/> and leaves its source untouched,
/// so the caller disposes both.
/// </summary>
/// <remarks>
/// The filter is a box average: each destination pixel is the mean of the source pixels its own
/// footprint covers, weighted by how much of each it covers. For the thumbnails and mip levels
/// this exists to serve, that is the right answer and a cheap one; it has no ringing to guard
/// against and reads every source pixel exactly once.
/// </remarks>
public static class ImageResize
{
    /// <summary>
    /// Resamples to an exact size, which stretches the picture where the aspect ratio differs.
    /// </summary>
    /// <param name="source">The image to read. Left untouched.</param>
    /// <param name="width">Width to produce, at least one.</param>
    /// <param name="height">Height to produce, at least one.</param>
    public static Image Resize(Image source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Resize(source.RootFrame, width, height);
    }

    /// <summary>Resamples one frame to an exact size.</summary>
    public static Image Resize(ImageFrame source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A resize produces at least one pixel each way.");

        PixelFormat format = source.PixelFormat;
        if (format.IsFloatingPoint() || format.BytesPerChannel() != 1)
            throw new ArgumentException("Resizing reads eight bit channels; convert first.", nameof(source));

        int channels = format.ChannelCount();
        int stride = width * channels;
        byte[] pixels = new byte[stride * height];

        // The two axes are separable, so the horizontal weights are worked out once for the whole
        // image rather than once for every row.
        (int Start, int End)[] columns = Spans(source.Width, width);
        (int Start, int End)[] rows = Spans(source.Height, height);

        Span<int> totals = channels <= 8 ? stackalloc int[channels] : new int[channels];

        for (int y = 0; y < height; y++)
        {
            (int top, int bottom) = rows[y];

            for (int x = 0; x < width; x++)
            {
                (int left, int right) = columns[x];
                totals.Clear();

                for (int sy = top; sy < bottom; sy++)
                {
                    ReadOnlySpan<byte> row = source.GetRow(sy);
                    for (int sx = left; sx < right; sx++)
                    {
                        int at = sx * channels;
                        for (int c = 0; c < channels; c++)
                            totals[c] += row[at + c];
                    }
                }

                int count = (bottom - top) * (right - left);
                int to = (y * stride) + (x * channels);
                for (int c = 0; c < channels; c++)
                    pixels[to + c] = (byte)((totals[c] + (count / 2)) / count);
            }
        }

        return Image.FromPixels(pixels, width, height, format, stride);
    }

    /// <summary>
    /// Resamples to fit inside a box without distorting the picture, so the result is as large as
    /// it can be while both sides stay within the box.
    /// </summary>
    public static Image Fit(Image source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A box is at least one pixel each way.");

        double scale = Math.Min((double)width / source.Width, (double)height / source.Height);
        int fitted = Math.Max(1, (int)Math.Round(source.Width * scale));
        int tall = Math.Max(1, (int)Math.Round(source.Height * scale));

        return Resize(source, fitted, tall);
    }

    /// <summary>
    /// Places the picture on a larger canvas without resampling it, leaving the rest at
    /// <paramref name="fill"/>. A size smaller than the picture crops it instead.
    /// </summary>
    /// <param name="source">The image to place. Left untouched.</param>
    /// <param name="width">Canvas width.</param>
    /// <param name="height">Canvas height.</param>
    /// <param name="fill">The channel values every uncovered pixel takes, or nothing for zero.</param>
    public static Image Pad(Image source, int width, int height, ReadOnlySpan<byte> fill = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A canvas is at least one pixel each way.");

        ImageFrame frame = source.RootFrame;
        PixelFormat format = frame.PixelFormat;
        int pixel = format.BytesPerPixel();
        int stride = width * pixel;
        byte[] pixels = new byte[stride * height];

        if (fill.Length == pixel)
        {
            for (int at = 0; at < pixels.Length; at += pixel)
                fill.CopyTo(pixels.AsSpan(at, pixel));
        }
        else if (!fill.IsEmpty)
        {
            throw new ArgumentException("A fill states one whole pixel or nothing at all.", nameof(fill));
        }

        int offsetX = (width - frame.Width) / 2;
        int offsetY = (height - frame.Height) / 2;

        int copyWidth = Math.Min(frame.Width, width - Math.Max(offsetX, 0));
        int copyHeight = Math.Min(frame.Height, height - Math.Max(offsetY, 0));

        for (int y = 0; y < copyHeight; y++)
        {
            int destinationY = offsetY + y;
            int sourceY = y;

            if (offsetY < 0)
            {
                destinationY = y;
                sourceY = y - offsetY;
                if (sourceY >= frame.Height)
                    break;
            }

            if ((uint)destinationY >= (uint)height)
                continue;

            int fromX = offsetX < 0 ? -offsetX : 0;
            int toX = Math.Max(offsetX, 0);
            int run = Math.Min(copyWidth, Math.Min(frame.Width - fromX, width - toX));
            if (run <= 0)
                continue;

            frame.GetRow(sourceY).Slice(fromX * pixel, run * pixel)
                 .CopyTo(pixels.AsSpan((destinationY * stride) + (toX * pixel), run * pixel));
        }

        return Image.FromPixels(pixels, width, height, format, stride);
    }

    /// <summary>
    /// Resamples to fit the box and then centres the result on a canvas of exactly that size,
    /// which is the shape a square thumbnail of a picture of any proportion takes.
    /// </summary>
    public static Image FitAndPad(Image source, int width, int height, ReadOnlySpan<byte> fill = default)
    {
        using Image fitted = Fit(source, width, height);
        return Pad(fitted, width, height, fill);
    }

    /// <summary>
    /// Which source pixels each destination pixel averages. The boundaries are worked out in
    /// whole pixels, so every source pixel lands in exactly one span and none is read twice.
    /// </summary>
    private static (int Start, int End)[] Spans(int source, int destination)
    {
        (int Start, int End)[] spans = new (int, int)[destination];

        for (int i = 0; i < destination; i++)
        {
            int start = (int)((long)i * source / destination);
            int end = (int)((long)(i + 1) * source / destination);
            spans[i] = (start, Math.Max(end, start + 1));
        }

        return spans;
    }
}
