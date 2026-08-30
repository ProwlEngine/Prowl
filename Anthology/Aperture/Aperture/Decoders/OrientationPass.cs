// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders;

/// <summary>
/// Turns decoded pixels the way round the file says they should be shown. Opt in, since it costs
/// a pass over the pixels and, for a quarter turn, a buffer of the other shape.
/// </summary>
internal static class OrientationPass
{
    /// <summary>Whether a recorded orientation asks for anything to be done.</summary>
    public static bool Turns(ExifOrientation orientation) =>
        orientation is > ExifOrientation.TopLeft and <= ExifOrientation.LeftBottom;

    /// <summary>Whether the transform trades the two axes, so the picture changes shape.</summary>
    private static bool Transposes(ExifOrientation orientation) =>
        orientation is ExifOrientation.LeftTop or ExifOrientation.RightTop
            or ExifOrientation.RightBottom or ExifOrientation.LeftBottom;

    /// <summary>Rebuilds an image with every frame turned, leaving the original released.</summary>
    public static bool TryApply(Image image, DecodeOptions options, ExifOrientation orientation,
                                out Image? turned, out ApertureError error)
    {
        turned = null;
        error = ApertureError.None;

        bool transposes = Transposes(orientation);
        List<ImageFrame> frames = new(image.Frames.Count);

        try
        {
            foreach (ImageFrame frame in image.Frames)
            {
                if (!TryTurn(frame, options, orientation, transposes, out ImageFrame? one, out error))
                    return false;

                frames.Add(one!);
            }

            turned = new Image
            {
                Format = image.Format,
                Width = transposes ? image.Height : image.Width,
                Height = transposes ? image.Width : image.Height,
                PixelFormat = image.PixelFormat,
                Frames = [.. frames],
                Info = image.Info,
                Metadata = image.Metadata,
            };

            frames.Clear();
            image.Dispose();
            return true;
        }
        finally
        {
            foreach (ImageFrame frame in frames)
                frame.Release();
        }
    }

    private static bool TryTurn(ImageFrame source, DecodeOptions options, ExifOrientation orientation,
                                bool transposes, out ImageFrame? turned, out ApertureError error)
    {
        turned = null;

        int width = transposes ? source.Height : source.Width;
        int height = transposes ? source.Width : source.Height;
        int pixel = source.PixelFormat.BytesPerPixel();
        int stride = options.GetStride(width, source.PixelFormat);
        long total = (long)stride * height;

        if (total > options.MaxAllocationBytes || total > int.MaxValue || total <= 0)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] buffer = options.UsePooledMemory
            ? BufferPool.Bytes.Rent((int)total)
            : new byte[(int)total];

        try
        {
            Span<byte> destination = buffer.AsSpan(0, (int)total);
            if (stride != width * pixel)
                destination.Clear();

            for (int y = 0; y < source.Height; y++)
            {
                ReadOnlySpan<byte> row = source.GetRow(y);

                for (int x = 0; x < source.Width; x++)
                {
                    Place(orientation, x, y, source.Width, source.Height, out int toX, out int toY);
                    row.Slice(x * pixel, pixel).CopyTo(destination[((toY * stride) + (toX * pixel))..]);
                }
            }

            turned = new ImageFrame(buffer, (int)total, width, height, stride, source.PixelFormat,
                                    options.UsePooledMemory)
            {
                Delay = source.Delay,
                Disposal = source.Disposal,
                MipLevel = source.MipLevel,
                ArraySlice = source.ArraySlice,
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

    /// <summary>Where a stored pixel belongs once the picture is the right way up.</summary>
    private static void Place(ExifOrientation orientation, int x, int y, int width, int height,
                              out int toX, out int toY)
    {
        switch (orientation)
        {
            case ExifOrientation.TopRight:
                toX = width - 1 - x;
                toY = y;
                return;

            case ExifOrientation.BottomRight:
                toX = width - 1 - x;
                toY = height - 1 - y;
                return;

            case ExifOrientation.BottomLeft:
                toX = x;
                toY = height - 1 - y;
                return;

            case ExifOrientation.LeftTop:
                toX = y;
                toY = x;
                return;

            case ExifOrientation.RightTop:
                toX = height - 1 - y;
                toY = x;
                return;

            case ExifOrientation.RightBottom:
                toX = height - 1 - y;
                toY = width - 1 - x;
                return;

            case ExifOrientation.LeftBottom:
                toX = y;
                toY = width - 1 - x;
                return;

            default:
                toX = x;
                toY = y;
                return;
        }
    }
}
