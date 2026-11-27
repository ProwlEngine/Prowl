// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Prowl.Aperture;

/// <summary>
/// Converts pixels between the layouts in <see cref="PixelFormat"/>. Widening a channel
/// replicates its bits rather than shifting, so full intensity stays full, and narrowing is the
/// exact inverse and therefore rounds. A missing alpha reads as opaque, and greyscale expands by
/// copying its one channel rather than by any weighting.
/// </summary>
public static class PixelConverter
{
    /// <summary>Whether a conversion between two layouts is implemented.</summary>
    public static bool CanConvert(PixelFormat source, PixelFormat destination) =>
        source != PixelFormat.Unknown && destination != PixelFormat.Unknown;

    /// <summary>
    /// Converts one row. The spans must hold <paramref name="width"/> pixels of their respective
    /// formats.
    /// </summary>
    public static void ConvertRow(ReadOnlySpan<byte> source, PixelFormat sourceFormat,
                                  Span<byte> destination, PixelFormat destinationFormat, int width)
    {
        if (sourceFormat == destinationFormat)
        {
            source[..(width * sourceFormat.BytesPerPixel())].CopyTo(destination);
            return;
        }

        if (sourceFormat.IsFloatingPoint() || destinationFormat.IsFloatingPoint())
        {
            ConvertFloating(source, sourceFormat, destination, destinationFormat, width);
            return;
        }

        bool sourceWide = sourceFormat.BytesPerChannel() == 2;
        bool destinationWide = destinationFormat.BytesPerChannel() == 2;

        if (!sourceWide && !destinationWide)
            ConvertNarrow(source, sourceFormat, destination, destinationFormat, width);
        else
            ConvertWide(source, sourceFormat, destination, destinationFormat, width, sourceWide, destinationWide);
    }

    private static void ConvertNarrow(ReadOnlySpan<byte> source, PixelFormat sourceFormat,
                                      Span<byte> destination, PixelFormat destinationFormat, int width)
    {
        // The handful of pairings a decoder actually asks for, each a flat loop. The general
        // path below is correct for all of them but re-decides the work on every pixel.
        if (BitConverter.IsLittleEndian)
        {
            switch (sourceFormat)
            {
                case PixelFormat.L8 when destinationFormat == PixelFormat.Rgba8:
                    GrayToRgba(source, destination, width);
                    return;

                case PixelFormat.La8 when destinationFormat == PixelFormat.Rgba8:
                    GrayAlphaToRgba(source, destination, width);
                    return;

                case PixelFormat.Rgb8 when destinationFormat == PixelFormat.Rgba8:
                    RgbToRgba(source, destination, width);
                    return;

                case PixelFormat.Rgba8 when destinationFormat == PixelFormat.Rgb8:
                    RgbaToRgb(source, destination, width);
                    return;
            }
        }

        if (sourceFormat == PixelFormat.L8 && destinationFormat == PixelFormat.Rgb8)
        {
            GrayToRgb(source, destination, width);
            return;
        }

        int sourceChannels = sourceFormat.ChannelCount();
        int destinationChannels = destinationFormat.ChannelCount();
        bool sourceHasAlpha = sourceFormat.HasAlpha();
        bool destinationHasAlpha = destinationFormat.HasAlpha();
        int sourceColour = sourceHasAlpha ? sourceChannels - 1 : sourceChannels;
        int destinationColour = destinationHasAlpha ? destinationChannels - 1 : destinationChannels;

        for (int x = 0; x < width; x++)
        {
            int from = x * sourceChannels;
            int to = x * destinationChannels;

            if (destinationColour == sourceColour)
            {
                for (int c = 0; c < destinationColour; c++)
                    destination[to + c] = source[from + c];
            }
            else if (sourceColour == 1)
            {
                byte grey = source[from];
                for (int c = 0; c < destinationColour; c++)
                    destination[to + c] = grey;
            }
            else
            {
                // Colour down to grey keeps the red channel: a decoded image reaching here is
                // already grey, and averaging would only introduce rounding.
                destination[to] = source[from];
            }

            if (destinationHasAlpha)
                destination[to + destinationColour] = sourceHasAlpha ? source[from + sourceColour] : (byte)255;
        }
    }

    private static void ConvertWide(ReadOnlySpan<byte> source, PixelFormat sourceFormat,
                                    Span<byte> destination, PixelFormat destinationFormat,
                                    int width, bool sourceWide, bool destinationWide)
    {
        int sourceChannels = sourceFormat.ChannelCount();
        int destinationChannels = destinationFormat.ChannelCount();
        bool sourceHasAlpha = sourceFormat.HasAlpha();
        bool destinationHasAlpha = destinationFormat.HasAlpha();
        int sourceColour = sourceHasAlpha ? sourceChannels - 1 : sourceChannels;
        int destinationColour = destinationHasAlpha ? destinationChannels - 1 : destinationChannels;

        ReadOnlySpan<ushort> wideSource = sourceWide ? MemoryMarshal.Cast<byte, ushort>(source) : default;
        Span<ushort> wideDestination = destinationWide ? MemoryMarshal.Cast<byte, ushort>(destination) : default;

        // What a texture pipeline asks of every format storing more than a byte a channel, so
        // it is a flat loop rather than the general path.
        if (destinationFormat == PixelFormat.Rgba8 && sourceWide && BitConverter.IsLittleEndian &&
            sourceColour == 3)
        {
            WideToRgba(wideSource, destination, width, sourceHasAlpha);
            return;
        }

        for (int x = 0; x < width; x++)
        {
            int from = x * sourceChannels;
            int to = x * destinationChannels;

            for (int c = 0; c < destinationColour; c++)
            {
                int channel = destinationColour == sourceColour ? c : sourceColour == 1 ? 0 : Math.Min(c, sourceColour - 1);
                ushort value = sourceWide ? wideSource[from + channel] : Widen(source[from + channel]);
                Write(destination, wideDestination, to + c, value, destinationWide);
            }

            if (!destinationHasAlpha)
                continue;

            ushort alpha = ushort.MaxValue;
            if (sourceHasAlpha)
                alpha = sourceWide ? wideSource[from + sourceColour] : Widen(source[from + sourceColour]);

            Write(destination, wideDestination, to + destinationColour, alpha, destinationWide);
        }
    }

    /// <summary>
    /// Packs one opaque pixel. On a little endian machine the low byte of the word is red, so a
    /// grey level multiplied by this constant lands in all three colour channels at once.
    /// </summary>
    private const uint ColourReplicate = 0x00010101;

    private const uint Opaque = 0xFF000000;

    /// <summary>Copies a grey level across three colour channels and fills in an opaque alpha.</summary>
    internal static void GrayToRgba(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        Span<uint> output = MemoryMarshal.Cast<byte, uint>(destination)[..width];
        int x = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<uint> replicate = Vector256.Create(ColourReplicate);
            Vector256<uint> opaque = Vector256.Create(Opaque);

            for (; x <= width - 8; x += 8)
            {
                ulong packed = BinaryPrimitives.ReadUInt64LittleEndian(source[x..]);
                Vector128<ushort> words = Vector128.WidenLower(Vector128.CreateScalar(packed).AsByte());
                (Vector128<uint> low, Vector128<uint> high) = Vector128.Widen(words);

                ((Vector256.Create(low, high) * replicate) | opaque).CopyTo(output[x..]);
            }
        }

        for (; x < width; x++)
        {
            uint grey = source[x];
            output[x] = Opaque | (grey * ColourReplicate);
        }
    }

    private static void GrayAlphaToRgba(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        Span<uint> output = MemoryMarshal.Cast<byte, uint>(destination)[..width];
        for (int x = 0; x < width; x++)
        {
            int from = x * 2;
            output[x] = ((uint)source[from + 1] << 24) | (source[from] * ColourReplicate);
        }
    }

    /// <summary>Where a three byte pixel's channels land once a fourth is made room for.</summary>
    private static ReadOnlySpan<byte> Spread =>
        [0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255];

    private static void RgbToRgba(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        int x = 0;

        // Four pixels is one shuffle, named rather than reached through the portable one, which
        // answers for out of range indices and measured slower than the scalar loop.
        if (Ssse3.IsSupported)
        {
            ref byte from = ref MemoryMarshal.GetReference(source);
            ref byte to = ref MemoryMarshal.GetReference(destination);
            Vector128<byte> order = Vector128.Create(Spread);
            Vector128<uint> opaque = Vector128.Create(Opaque);

            for (; (x * 3) + 16 <= source.Length && x + 4 <= width; x += 4)
            {
                Vector128<byte> packed = Vector128.LoadUnsafe(ref from, (nuint)(x * 3));
                (Ssse3.Shuffle(packed, order).AsUInt32() | opaque).AsByte().StoreUnsafe(ref to, (nuint)(x * 4));
            }
        }

        Span<uint> output = MemoryMarshal.Cast<byte, uint>(destination)[..width];
        for (; x < width; x++)
        {
            int at = x * 3;
            output[x] = Opaque | source[at] | ((uint)source[at + 1] << 8) | ((uint)source[at + 2] << 16);
        }
    }

    private static void RgbaToRgb(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        ReadOnlySpan<uint> input = MemoryMarshal.Cast<byte, uint>(source)[..width];
        for (int x = 0; x < width; x++)
        {
            uint pixel = input[x];
            int to = x * 3;
            destination[to] = (byte)pixel;
            destination[to + 1] = (byte)(pixel >> 8);
            destination[to + 2] = (byte)(pixel >> 16);
        }
    }

    private static void GrayToRgb(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        for (int x = 0; x < width; x++)
        {
            byte grey = source[x];
            int to = x * 3;
            destination[to] = grey;
            destination[to + 1] = grey;
            destination[to + 2] = grey;
        }
    }

    /// <summary>
    /// Conversions with a floating point layout on one side. Everything crosses through a value
    /// where one means full intensity, and nothing applies a transfer function, so a high dynamic
    /// range image narrowed to bytes is clipped rather than tone mapped.
    /// </summary>
    private static void ConvertFloating(ReadOnlySpan<byte> source, PixelFormat sourceFormat,
                                        Span<byte> destination, PixelFormat destinationFormat, int width)
    {
        int sourceChannels = sourceFormat.ChannelCount();
        int destinationChannels = destinationFormat.ChannelCount();
        bool sourceHasAlpha = sourceFormat.HasAlpha();
        bool destinationHasAlpha = destinationFormat.HasAlpha();
        int sourceColour = sourceHasAlpha ? sourceChannels - 1 : sourceChannels;
        int destinationColour = destinationHasAlpha ? destinationChannels - 1 : destinationChannels;

        // The common request, so it is a flat loop rather than the general path, which decides
        // the shape of the work again for every channel of every pixel.
        if (destinationFormat == PixelFormat.Rgba8 && BitConverter.IsLittleEndian &&
            sourceFormat is PixelFormat.RgbF32 or PixelFormat.RgbaF32)
        {
            FloatToRgba(MemoryMarshal.Cast<byte, float>(source), destination, width, sourceHasAlpha);
            return;
        }

        for (int x = 0; x < width; x++)
        {
            int from = x * sourceChannels;
            int to = x * destinationChannels;

            for (int c = 0; c < destinationColour; c++)
            {
                int channel = destinationColour == sourceColour ? c
                    : sourceColour == 1 ? 0
                    : Math.Min(c, sourceColour - 1);

                Write(destination, destinationFormat, to + c, Read(source, sourceFormat, from + channel));
            }

            if (destinationHasAlpha)
            {
                float alpha = sourceHasAlpha ? Read(source, sourceFormat, from + sourceColour) : 1f;
                Write(destination, destinationFormat, to + destinationColour, alpha);
            }
        }
    }

    /// <summary>Float colour, with or without alpha, pinned and quantised to one word a pixel.</summary>
    private static void FloatToRgba(ReadOnlySpan<float> source, Span<byte> destination, int width, bool hasAlpha)
    {
        Span<uint> output = MemoryMarshal.Cast<byte, uint>(destination)[..width];
        int channels = hasAlpha ? 4 : 3;

        for (int x = 0; x < width; x++)
        {
            int at = x * channels;
            uint red = Quantise(source[at]);
            uint green = Quantise(source[at + 1]);
            uint blue = Quantise(source[at + 2]);
            uint alpha = hasAlpha ? Quantise(source[at + 3]) : 255u;

            output[x] = red | (green << 8) | (blue << 16) | (alpha << 24);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Quantise(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

    /// <summary>One channel as a value where one means full intensity.</summary>
    private static float Read(ReadOnlySpan<byte> source, PixelFormat format, int index) => format switch
    {
        PixelFormat.RgbF16 or PixelFormat.RgbaF16 =>
            (float)MemoryMarshal.Read<Half>(source[(index * 2)..]),
        PixelFormat.LF32 or PixelFormat.RgbF32 or PixelFormat.RgbaF32 =>
            MemoryMarshal.Read<float>(source[(index * 4)..]),
        _ when format.BytesPerChannel() == 2 => MemoryMarshal.Read<ushort>(source[(index * 2)..]) / 65535f,
        _ => source[index] / 255f,
    };

    private static void Write(Span<byte> destination, PixelFormat format, int index, float value)
    {
        switch (format)
        {
            case PixelFormat.RgbF16 or PixelFormat.RgbaF16:
                MemoryMarshal.Write(destination[(index * 2)..], (Half)value);
                return;

            case PixelFormat.LF32 or PixelFormat.RgbF32 or PixelFormat.RgbaF32:
                MemoryMarshal.Write(destination[(index * 4)..], value);
                return;
        }

        float clamped = Math.Clamp(value, 0f, 1f);
        if (format.BytesPerChannel() == 2)
            MemoryMarshal.Write(destination[(index * 2)..], (ushort)((clamped * 65535f) + 0.5f));
        else
            destination[index] = (byte)((clamped * 255f) + 0.5f);
    }

    /// <summary>Stretches a byte across sixteen bits by replicating it, so 0xFF maps to 0xFFFF.</summary>
    private static ushort Widen(byte value) => (ushort)((value << 8) | value);

    private static void Write(Span<byte> narrow, Span<ushort> wide, int index, ushort value, bool destinationWide)
    {
        if (destinationWide)
            wide[index] = value;
        else
            narrow[index] = Narrow(value);
    }

    /// <summary>
    /// Undoes the bit replication, rounding to the nearer byte value. The multiply and shift is
    /// exactly equivalent to the division it is defined by, checked over all 65,536 inputs.
    /// </summary>
    private static byte Narrow(ushort value) => (byte)(((value * 255) + 32895) >> 16);

    /// <summary>Sixteen bit colour, with or without alpha, narrowed to one opaque word a pixel.</summary>
    private static void WideToRgba(ReadOnlySpan<ushort> source, Span<byte> destination, int width, bool hasAlpha)
    {
        Span<uint> output = MemoryMarshal.Cast<byte, uint>(destination)[..width];
        int channels = hasAlpha ? 4 : 3;

        for (int x = 0; x < width; x++)
        {
            int at = x * channels;
            uint red = Narrow(source[at]);
            uint green = Narrow(source[at + 1]);
            uint blue = Narrow(source[at + 2]);
            uint alpha = hasAlpha ? Narrow(source[at + 3]) : 255u;

            output[x] = red | (green << 8) | (blue << 16) | (alpha << 24);
        }
    }
}
