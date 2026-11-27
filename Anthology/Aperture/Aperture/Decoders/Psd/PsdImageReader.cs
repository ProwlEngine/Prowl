// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.IO.Compression;

namespace Prowl.Aperture.Decoders.Psd;

/// <summary>
/// Turns the flattened image into pixels. Channels are stored one whole plane after another
/// rather than interleaved, which is what makes the run length form worth having: a plane of one
/// colour compresses far better on its own than the same pixels do with two other channels
/// between each of them.
/// </summary>
internal static class PsdImageReader
{
    /// <summary>Colour modes this reader turns into colour.</summary>
    public static bool IsSupported(in PsdComposite composite) =>
        composite.ColorMode is 0 or 1 or 2 or 3 or 4 or 7 or 8 or 9 &&
        composite.Compression is 0 or 1 or 2 or 3;

    /// <summary>Channels the output carries, which is not always the number the file stores.</summary>
    public static int OutputChannels(in PsdComposite composite)
    {
        // The multichannel mode names no colour, so its planes are shown as they lie.
        int colour = composite.ColorMode switch
        {
            0 or 1 or 8 => 1,
            7 => Math.Min(composite.Channels, 3),
            _ => 3,
        };

        return HasAlpha(composite) ? colour + 1 : colour;
    }

    /// <summary>
    /// Whether a plane past the colour ones is transparency. Anything further than the first is a
    /// mask or a spot ink, and a mode that names no colour at all has none of either.
    /// </summary>
    public static bool HasAlpha(in PsdComposite composite)
    {
        int colour = composite.ColorMode switch
        {
            0 or 1 or 8 => 1,
            7 => Math.Min(composite.Channels, 3),
            _ => 3,
        };

        return composite.Channels > (composite.ColorMode == 4 ? 4 : colour);
    }

    public static PixelFormat NaturalFormat(in PsdComposite composite)
    {
        int channels = OutputChannels(composite);
        return composite.Depth switch
        {
            32 => channels >= 4 ? PixelFormat.RgbaF32
                : channels == 3 ? PixelFormat.RgbF32
                : PixelFormat.LF32,
            16 => channels switch
            {
                1 => PixelFormat.L16,
                2 => PixelFormat.La16,
                3 => PixelFormat.Rgb16,
                _ => PixelFormat.Rgba16,
            },
            _ => channels switch
            {
                1 => PixelFormat.L8,
                2 => PixelFormat.La8,
                3 => PixelFormat.Rgb8,
                _ => PixelFormat.Rgba8,
            },
        };
    }

    /// <summary>Whether the data present could hold the planes the header declares.</summary>
    public static bool CanDescribe(int available, in PsdComposite composite)
    {
        long planes = (long)composite.RowBytes * composite.Height * composite.Channels;
        if (composite.Compression == 0)
            return planes <= available;

        // A run of pixels costs two bytes at least and covers at most 128 of them.
        return composite.IsRunLength ? (long)available / 2 * 128 >= planes : available > 0;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, in PsdComposite composite, PixelFormat target,
                                 Span<byte> destination, int stride, bool flip, out ApertureError error)
    {
        int rowBytes = composite.RowBytes;
        int stored = Math.Min(composite.Channels, 5);
        long planeSize = (long)rowBytes * composite.Height;

        if (planeSize * stored > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] planes = BufferPool.Bytes.Rent((int)(planeSize * stored));
        try
        {
            Span<byte> raw = planes.AsSpan(0, (int)(planeSize * stored));
            raw.Clear();

            if (!Gather(data, composite, raw, rowBytes, stored, out error))
                return false;

            Convert(composite, raw, rowBytes, stored, target, destination, stride, flip);
            error = ApertureError.None;
            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(planes);
        }
    }

    private static bool Gather(ReadOnlySpan<byte> data, in PsdComposite composite, Span<byte> raw,
                               int rowBytes, int stored, out ApertureError error)
    {
        error = ApertureError.None;
        int at = composite.DataOffset;

        switch (composite.Compression)
        {
            case 0:
                if (at + raw.Length > data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                data.Slice(at, raw.Length).CopyTo(raw);
                return true;

            case 1:
                return GatherRuns(data, composite, raw, rowBytes, stored, out error);

            default:
            {
                // The predicted form is not written for a composite.
                try
                {
                    using MemoryStream input = new(data[at..].ToArray(), writable: false);
                    using ZLibStream inflate = new(input, CompressionMode.Decompress);

                    int written = 0;
                    while (written < raw.Length)
                    {
                        int read = inflate.Read(raw[written..]);
                        if (read <= 0)
                            break;

                        written += read;
                    }

                    return written > 0;
                }
                catch (InvalidDataException)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// The run length form. Every row of every plane has its packed length written out in front,
    /// which lets a reader jump to a row without decoding the ones before it, and lets this one
    /// carry on past a row that decodes short.
    /// </summary>
    private static bool GatherRuns(ReadOnlySpan<byte> data, in PsdComposite composite, Span<byte> raw,
                                   int rowBytes, int stored, out ApertureError error)
    {
        error = ApertureError.None;

        int rows = composite.Height * composite.Channels;
        int width = composite.Large ? 4 : 2;
        int at = composite.DataOffset;

        if (at + ((long)rows * width) > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        int counts = at;
        at += rows * width;

        for (int plane = 0; plane < composite.Channels; plane++)
        {
            for (int y = 0; y < composite.Height; y++)
            {
                int index = (plane * composite.Height) + y;
                long packed = width == 4
                    ? BinaryPrimitives.ReadUInt32BigEndian(data[(counts + (index * 4))..])
                    : BinaryPrimitives.ReadUInt16BigEndian(data[(counts + (index * 2))..]);

                if (packed < 0 || at + packed > data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                if (plane < stored)
                {
                    Span<byte> row = raw.Slice(((plane * composite.Height) + y) * rowBytes, rowBytes);
                    Unpack(data.Slice(at, (int)packed), row);
                }

                at += (int)packed;
            }
        }

        return true;
    }

    /// <summary>A signed count, positive for literals and negative for a repeated byte.</summary>
    private static void Unpack(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int at = 0;
        int written = 0;

        while (at < source.Length && written < destination.Length)
        {
            sbyte count = (sbyte)source[at++];
            if (count == -128)
                continue;

            if (count >= 0)
            {
                int run = Math.Min(count + 1, Math.Min(source.Length - at, destination.Length - written));
                if (run <= 0)
                    break;

                source.Slice(at, run).CopyTo(destination[written..]);
                at += count + 1;
                written += run;
                continue;
            }

            if (at >= source.Length)
                break;

            int repeat = Math.Min(1 - count, destination.Length - written);
            destination.Slice(written, repeat).Fill(source[at++]);
            written += repeat;
        }
    }

    private static void Convert(in PsdComposite composite, ReadOnlySpan<byte> raw, int rowBytes, int stored,
                                PixelFormat target, Span<byte> destination, int stride, bool flip)
    {
        int width = composite.Width;
        int height = composite.Height;
        int channels = target.ChannelCount();
        int outputBytes = target.BytesPerChannel();
        long planeSize = (long)rowBytes * height;

        // The ordinary shape, which is three or four rows to interleave rather than a pixel at
        // a time through the general path below.
        if (composite.Depth == 8 && composite.ColorMode == 3 && stored >= 3 &&
            outputBytes == 1 && channels is 3 or 4)
        {
            for (int y = 0; y < height; y++)
            {
                int line = flip ? height - 1 - y : y;
                long start = (long)y * rowBytes;

                Interleave(raw, (int)start, (int)(planeSize + start), (int)((planeSize * 2) + start),
                           stored > 3 ? (int)((planeSize * 3) + start) : -1,
                           destination.Slice(line * stride, width * channels), width, channels);
            }

            return;
        }

        Span<int> pixel = stackalloc int[8];

        for (int y = 0; y < height; y++)
        {
            int to = flip ? height - 1 - y : y;
            Span<byte> row = destination.Slice(to * stride, width * channels * outputBytes);

            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < stored; c++)
                    pixel[c] = Sample(composite, raw, (int)((c * planeSize) + ((long)y * rowBytes)), x);

                Write(composite, pixel, stored, row, x, channels, outputBytes);
            }
        }
    }

    /// <summary>
    /// Three or four separate rows of one channel each, woven into one row of pixels. A file
    /// with no alpha channel gets an opaque one.
    /// </summary>
    private static void Interleave(ReadOnlySpan<byte> raw, int red, int green, int blue, int alpha,
                                   Span<byte> row, int width, int channels)
    {
        if (channels == 3)
        {
            for (int x = 0; x < width; x++)
            {
                int at = x * 3;
                row[at] = raw[red + x];
                row[at + 1] = raw[green + x];
                row[at + 2] = raw[blue + x];
            }

            return;
        }

        Span<uint> output = MemoryMarshal.Cast<byte, uint>(row)[..width];

        for (int x = 0; x < width; x++)
        {
            uint colour = raw[red + x] | ((uint)raw[green + x] << 8) | ((uint)raw[blue + x] << 16);
            output[x] = colour | (alpha < 0 ? 0xFF000000u : (uint)raw[alpha + x] << 24);
        }
    }

    private static int Sample(in PsdComposite composite, ReadOnlySpan<byte> raw, int rowStart, int x)
    {
        switch (composite.Depth)
        {
            case 1:
            {
                int at = rowStart + (x >> 3);
                // A set bit is white here, the opposite way round from the other one bit formats.
                return at < raw.Length && (raw[at] & (0x80 >> (x & 7))) != 0 ? 255 : 0;
            }

            case 16:
                return BinaryPrimitives.ReadUInt16BigEndian(raw[(rowStart + (x * 2))..]);

            case 32:
                return unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(raw[(rowStart + (x * 4))..]));

            default:
                return raw[rowStart + x];
        }
    }

    private static void Write(in PsdComposite composite, ReadOnlySpan<int> pixel, int stored,
                              Span<byte> row, int x, int channels, int outputBytes)
    {
        int at = x * channels * outputBytes;

        if (composite.Depth == 32)
        {
            for (int c = 0; c < channels; c++)
            {
                int source = Math.Min(c, stored - 1);
                float value = c < stored ? BitConverter.Int32BitsToSingle(pixel[source]) : 1f;
                BinaryPrimitives.WriteSingleLittleEndian(row[(at + (c * 4))..], value);
            }

            return;
        }

        int ceiling = composite.Depth == 16 ? 65535 : 255;
        int outputMax = outputBytes == 2 ? 65535 : 255;

        Span<int> colour = stackalloc int[4];
        int produced = Resolve(composite, pixel, stored, ceiling, colour);

        for (int c = 0; c < channels; c++)
        {
            int value = c < produced ? colour[c] : ceiling;
            if (ceiling != outputMax)
                value = (int)((((long)value * outputMax) + (ceiling / 2)) / ceiling);

            if (outputBytes == 2)
                BinaryPrimitives.WriteUInt16LittleEndian(row[(at + (c * 2))..], (ushort)value);
            else
                row[at + c] = (byte)value;
        }
    }

    /// <summary>
    /// Takes a lightness and its two colour axes through the intermediate space and out into the
    /// primaries a display uses, adapted to the white point the format is defined against.
    /// </summary>
    private static void ToDisplay(double lightness, double a, double b, int ceiling, Span<int> colour)
    {
        double fy = (lightness + 16.0) / 116.0;
        double fx = fy + (a / 500.0);
        double fz = fy - (b / 200.0);

        double x = 0.96422 * Expand(fx);
        double y = Expand(fy);
        double z = 0.82521 * Expand(fz);

        double red = (3.1338561 * x) - (1.6168667 * y) - (0.4906146 * z);
        double green = (-0.9787684 * x) + (1.9161415 * y) + (0.0334540 * z);
        double blue = (0.0719453 * x) - (0.2289914 * y) + (1.4052427 * z);

        colour[0] = Encode(red, ceiling);
        colour[1] = Encode(green, ceiling);
        colour[2] = Encode(blue, ceiling);
    }

    private static double Expand(double value)
    {
        double cubed = value * value * value;
        return cubed > 0.008856 ? cubed : (value - (16.0 / 116.0)) / 7.787;
    }

    /// <summary>Applies the transfer curve a display expects and puts the result in range.</summary>
    private static int Encode(double value, int ceiling)
    {
        value = Math.Clamp(value, 0.0, 1.0);
        value = value <= 0.0031308 ? 12.92 * value : (1.055 * Math.Pow(value, 1.0 / 2.4)) - 0.055;
        return (int)Math.Clamp((value * ceiling) + 0.5, 0, ceiling);
    }

    /// <summary>Turns the stored planes into colour, in the file's own range.</summary>
    private static int Resolve(in PsdComposite composite, ReadOnlySpan<int> pixel, int stored,
                               int ceiling, Span<int> colour)
    {
        switch (composite.ColorMode)
        {
            case 2 or 8 when composite.Palette is not null:
            {
                // The table is three runs of one channel rather than one run of triples.
                byte[] palette = composite.Palette;
                int index = Math.Clamp(pixel[0], 0, 255);
                colour[0] = palette[index];
                colour[1] = palette[256 + index];
                colour[2] = palette[512 + index];
                colour[3] = stored > 1 ? pixel[1] : ceiling;
                return stored > 1 ? 4 : 3;
            }

            case 9:
            {
                // Lightness with two opposing axes, which describes colour rather than makes it,
                // so it goes through the space a display works in.
                double lightness = pixel[0] * 100.0 / ceiling;
                double green = (pixel[1] * 255.0 / ceiling) - 128.0;
                double blue = (pixel[2] * 255.0 / ceiling) - 128.0;

                ToDisplay(lightness, green, blue, ceiling, colour);
                colour[3] = stored > 3 ? pixel[3] : ceiling;
                return stored > 3 ? 4 : 3;
            }

            case 4:
            {
                // Ink stored inverted, so the channels already read as light.
                int key = stored > 3 ? pixel[3] : ceiling;
                for (int c = 0; c < 3; c++)
                    colour[c] = (int)((((long)pixel[c] * key) + (ceiling / 2)) / ceiling);

                colour[3] = stored > 4 ? pixel[4] : ceiling;
                return stored > 4 ? 4 : 3;
            }

            default:
            {
                int count = composite.ColorMode switch
                {
                    0 or 1 or 8 => 1,
                    7 => Math.Min(stored, 3),
                    _ => 3,
                };
                for (int c = 0; c < count; c++)
                    colour[c] = c < stored ? pixel[c] : 0;

                colour[count] = stored > count ? pixel[count] : ceiling;
                return stored > count ? count + 1 : count;
            }
        }
    }
}
