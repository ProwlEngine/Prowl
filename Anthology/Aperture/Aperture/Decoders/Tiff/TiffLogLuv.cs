// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// The compressions for pictures of light rather than of colour. Brightness is stored as its
/// logarithm and colour, where it is stored at all, apart from it and at lower precision, so the
/// result holds real luminances rather than numbers scaled to a display.
/// </summary>
internal static class TiffLogLuv
{
    private const double ChromaScale = 410.0;

    public static bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination, int width,
                                 int rows, int compression, int samples, bool littleEndian)
    {
        int photometric = samples == 1 ? 32844 : 32845;

        int pixels = width * rows;
        if (pixels <= 0 || (long)pixels * samples * sizeof(float) > destination.Length)
            return false;

        uint[] values = new uint[pixels];

        // The twenty four bit form is stored outright; the other two split each value into byte
        // planes and code each on its own, since neighbouring high bytes are alike.
        bool packed = compression == 34677;
        int planes = photometric == 32844 ? 2 : 4;

        if (packed)
        {
            if (pixels * 3 > source.Length)
                return false;

            for (int i = 0; i < pixels; i++)
            {
                int at = i * 3;
                values[i] = ((uint)source[at] << 16) | ((uint)source[at + 1] << 8) | source[at + 2];
            }
        }
        else if (!TryExpandPlanes(source, values, width, rows, planes))
        {
            return false;
        }

        int channels = samples;

        for (int i = 0; i < pixels; i++)
        {
            Span<byte> to = destination[(i * channels * 4)..];

            if (photometric == 32844)
            {
                Write(to, 0, (float)Luminance((int)(short)values[i]), littleEndian);
                continue;
            }

            double x, y, z;
            if (packed)
                ToColour24(values[i], out x, out y, out z);
            else
                ToColour32(values[i], out x, out y, out z);

            // The values name a colour by where it sits rather than by how much of each primary
            // it holds, so they are turned into primaries before anything can show them.
            Write(to, 0, (float)((3.2404542 * x) - (1.5371385 * y) - (0.4985314 * z)), littleEndian);
            Write(to, 1, (float)((-0.9692660 * x) + (1.8760108 * y) + (0.0415560 * z)), littleEndian);
            Write(to, 2, (float)((0.0556434 * x) - (0.2040259 * y) + (1.0572252 * z)), littleEndian);
        }

        return true;
    }

    private static void Write(Span<byte> to, int channel, float value, bool littleEndian)
    {
        Span<byte> at = to[(channel * 4)..];

        if (littleEndian)
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(at, value);
        else
            System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(at, value);
    }

    /// <summary>
    /// Undoes the run length coding, one byte plane at a time from the most significant down. A
    /// count with its top bit set names a run of one byte; anything else names a literal run.
    /// </summary>
    private static bool TryExpandPlanes(ReadOnlySpan<byte> source, Span<uint> values, int width,
                                        int rows, int planes)
    {
        int at = 0;

        for (int row = 0; row < rows; row++)
        {
            Span<uint> line = values.Slice(row * width, width);
            line.Clear();

            for (int plane = 0; plane < planes; plane++)
            {
                int shift = (planes - 1 - plane) * 8;
                int written = 0;

                while (written < width && at < source.Length)
                {
                    if (source[at] >= 128)
                    {
                        if (at + 2 > source.Length)
                            return row > 0;

                        int run = source[at] - 126;
                        uint value = (uint)source[at + 1] << shift;
                        at += 2;

                        while (run-- > 0 && written < width)
                            line[written++] |= value;

                        continue;
                    }

                    int count = source[at++];
                    while (count-- > 0 && written < width && at < source.Length)
                        line[written++] |= (uint)source[at++] << shift;
                }

                if (written != width)
                    return row > 0;
            }
        }

        return true;
    }

    /// <summary>The luminance a sixteen bit logarithm stands for.</summary>
    private static double Luminance(int packed)
    {
        int magnitude = packed & 0x7FFF;
        if (magnitude == 0)
            return 0.0;

        double value = Math.Exp((Math.Log(2.0) / 256.0 * (magnitude + 0.5)) - (Math.Log(2.0) * 64.0));
        return (packed & 0x8000) == 0 ? value : -value;
    }

    /// <summary>The luminance a ten bit logarithm stands for, which the shorter form uses.</summary>
    private static double ShortLuminance(int packed) =>
        packed == 0 ? 0.0 : Math.Exp((Math.Log(2.0) / 64.0 * (packed + 0.5)) - (Math.Log(2.0) * 12.0));

    private static void ToColour32(uint packed, out double x, out double y, out double z)
    {
        double luminance = Luminance((int)(packed >> 16));
        if (luminance <= 0.0)
        {
            x = y = z = 0.0;
            return;
        }

        double u = 1.0 / ChromaScale * (((packed >> 8) & 0xFF) + 0.5);
        double v = 1.0 / ChromaScale * ((packed & 0xFF) + 0.5);
        Expand(u, v, luminance, out x, out y, out z);
    }

    private static void ToColour24(uint packed, out double x, out double y, out double z)
    {
        double luminance = ShortLuminance((int)((packed >> 14) & 0x3FF));
        if (luminance <= 0.0)
        {
            x = y = z = 0.0;
            return;
        }

        if (!TryCell((int)(packed & 0x3FFF), out double u, out double v))
        {
            u = 0.210526316;
            v = 0.473684211;
        }

        Expand(u, v, luminance, out x, out y, out z);
    }

    private static void Expand(double u, double v, double luminance, out double x, out double y, out double z)
    {
        double scale = 1.0 / ((6.0 * u) - (16.0 * v) + 12.0);
        double cx = 9.0 * u * scale;
        double cy = 4.0 * v * scale;

        x = cx / cy * luminance;
        y = luminance;
        z = (1.0 - cx - cy) / cy * luminance;
    }

    /// <summary>Finds which cell of the chromaticity grid a stored number names.</summary>
    private static bool TryCell(int index, out double u, out double v)
    {
        u = v = 0.0;
        if (index < 0 || index >= LogLuvTable.Cells)
            return false;

        ReadOnlySpan<short> running = LogLuvTable.Running;

        int lower = 0;
        int upper = running.Length;

        while (upper - lower > 1)
        {
            int middle = (lower + upper) >> 1;
            int offset = index - running[middle];

            if (offset > 0)
                lower = middle;
            else if (offset < 0)
                upper = middle;
            else
            {
                lower = middle;
                break;
            }
        }

        int across = index - running[lower];
        u = LogLuvTable.Starts[lower] + ((across + 0.5) * LogLuvTable.CellSize);
        v = LogLuvTable.FirstRow + ((lower + 0.5) * LogLuvTable.CellSize);
        return true;
    }
}
