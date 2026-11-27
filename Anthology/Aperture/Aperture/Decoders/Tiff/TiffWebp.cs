// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.WebP;

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// A strip compressed as a whole WebP picture rather than as a stream of samples. It is the one
/// compression that produces a finished picture with its own idea of colour, so the strip is
/// decoded as the file it is and written back out as the samples the container claimed.
/// </summary>
internal static class TiffWebp
{
    public static bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination, int width,
                                 int rows, int rowBytes, int channels)
    {
        if (!WebPContainer.TryRead(source, 1, out int pictureWidth, out int pictureHeight,
                                   out _, out List<WebPFrame> frames, out _))
            return false;

        if (frames.Count != 1 || pictureWidth != width || pictureHeight < rows)
            return false;

        WebPFrame frame = frames[0];
        if (!frame.Lossless)
            return false;

        ReadOnlySpan<byte> body = source.Slice(frame.Offset, frame.Length);
        if (!Vp8LDecoder.TryDecode(body, pictureWidth, pictureHeight, out uint[]? pixels))
            return false;

        for (int y = 0; y < rows; y++)
        {
            Span<byte> row = destination.Slice(y * rowBytes, rowBytes);

            for (int x = 0; x < width; x++)
            {
                uint colour = pixels![(y * pictureWidth) + x];
                int at = x * channels;

                row[at] = (byte)(colour >> 16);
                row[at + 1] = (byte)(colour >> 8);
                row[at + 2] = (byte)colour;

                if (channels == 4)
                    row[at + 3] = (byte)(colour >> 24);
            }
        }

        return true;
    }
}
