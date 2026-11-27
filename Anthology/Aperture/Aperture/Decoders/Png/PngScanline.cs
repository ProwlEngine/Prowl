// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Prowl.Aperture.Decoders.Png;

/// <summary>
/// Turns one unfiltered PNG scanline into whole-byte pixels: sub-byte samples unpacked and scaled,
/// palette indices looked up, a transparency chunk turned into an alpha channel, sixteen bit
/// samples byte swapped. It writes the requested layout directly wherever it can, since a second
/// pass over a large image costs more than the whole of the filtering.
/// </summary>
internal static class PngScanline
{
    /// <summary>
    /// Scale factors that stretch a sub-byte sample to the full 0..255 range by bit replication,
    /// which is the mapping the format defines for displaying a lower depth image.
    /// </summary>
    private const byte OneBitScale = 255;
    private const byte TwoBitScale = 85;
    private const byte FourBitScale = 17;

    /// <summary>
    /// Everything a row conversion needs that does not change between rows. The transparency
    /// table is a span into the source file, and the palette has already been flattened into
    /// packed RGBA words so a lookup is one load rather than four.
    /// </summary>
    internal readonly ref struct Layout(byte colourType, byte bitDepth, PixelFormat target,
                                        ReadOnlySpan<uint> palette, ReadOnlySpan<byte> transparency)
    {
        public byte ColourType { get; } = colourType;
        public byte BitDepth { get; } = bitDepth;
        public PixelFormat Target { get; } = target;
        public ReadOnlySpan<uint> Palette { get; } = palette;
        public ReadOnlySpan<byte> Transparency { get; } = transparency;

        /// <summary>
        /// Whether a row needs nothing but copying to become the output. Where that holds, the
        /// unfiltering can write straight into the frame and the copy disappears.
        /// </summary>
        public bool ConversionIsCopy => BitDepth == 8 && ColourType switch
        {
            0 => Target == PixelFormat.L8,
            2 => Target == PixelFormat.Rgb8,
            4 => Target == PixelFormat.La8,
            6 => Target == PixelFormat.Rgba8,
            _ => false,
        };
    }

    /// <summary>Bytes one unfiltered scanline of <paramref name="width"/> pixels occupies.</summary>
    public static int GetRowBytes(int width, byte colourType, byte bitDepth)
    {
        int samples = GetSampleCount(colourType);
        long bits = (long)width * samples * bitDepth;
        return (int)((bits + 7) / 8);
    }

    /// <summary>Samples stored per pixel, before a palette or transparency chunk is applied.</summary>
    public static int GetSampleCount(byte colourType) => colourType switch
    {
        0 => 1,
        2 => 3,
        3 => 1,
        4 => 2,
        6 => 4,
        _ => 0,
    };

    /// <summary>Distance in bytes to the pixel on the left, which the filters need.</summary>
    public static int GetBytesPerPixel(byte colourType, byte bitDepth)
    {
        int bits = GetSampleCount(colourType) * bitDepth;
        return Math.Max(1, bits / 8);
    }

    /// <summary>
    /// Whether a row can be written straight into <paramref name="target"/>, skipping the
    /// intermediate buffer a general conversion would need.
    /// </summary>
    public static bool CanWriteDirectly(byte colourType, byte bitDepth, PixelFormat natural, PixelFormat target)
    {
        if (target == natural)
            return true;
        if (target == PixelFormat.Rgba8 && bitDepth <= 8)
            return true;
        return target == PixelFormat.Rgba16 && bitDepth == 16;
    }

    /// <summary>
    /// Flattens a palette and its transparency table into packed RGBA words. Doing this once per
    /// image turns the per-pixel palette lookup into a single indexed load and store.
    /// </summary>
    public static void BuildPalette(ReadOnlySpan<byte> palette, ReadOnlySpan<byte> alpha, Span<uint> destination)
    {
        int entries = palette.Length / 3;
        for (int i = 0; i < destination.Length; i++)
        {
            // An index with no palette entry is a broken file; opaque black is the least
            // surprising answer and keeps the per-pixel loop free of a bounds check.
            if (i >= entries)
            {
                destination[i] = 0xFF000000u;
                continue;
            }

            uint red = palette[i * 3];
            uint green = palette[(i * 3) + 1];
            uint blue = palette[(i * 3) + 2];
            uint opacity = i < alpha.Length ? alpha[i] : 255u;

            // Little endian byte order in memory is red, green, blue, alpha.
            destination[i] = red | (green << 8) | (blue << 16) | (opacity << 24);
        }
    }

    /// <summary>
    /// Converts one unfiltered row into <paramref name="destination"/>, writing
    /// <paramref name="width"/> pixels in the layout the caller asked for.
    /// </summary>
    public static void Convert(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        if (layout.Target == PixelFormat.Rgba8)
        {
            ToRgba8(source, destination, width, layout);
            return;
        }

        if (layout.Target == PixelFormat.Rgba16)
        {
            ToRgba16(source, destination, width, layout);
            return;
        }

        ToNatural(source, destination, width, layout);
    }

    // ---- direct paths to eight bit RGBA ----------------------------------------------

    private static void ToRgba8(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        Span<uint> output = MemoryMarshal.Cast<byte, uint>(destination);

        switch (layout.ColourType)
        {
            case 6 when layout.BitDepth == 8:
                source[..(width * 4)].CopyTo(destination);
                return;

            case 2 when layout.BitDepth == 8:
                TruecolourToRgba8(source, output, width, layout.Transparency);
                return;

            case 3:
                PaletteToRgba8(source, output, width, layout);
                return;

            case 0 when layout.BitDepth == 8:
                GreyscaleToRgba8(source, output, width, layout.Transparency);
                return;

            case 4 when layout.BitDepth == 8:
                GreyscaleAlphaToRgba8(source, output, width);
                return;

            default:
                LowDepthToRgba8(source, output, width, layout);
                return;
        }
    }

    /// <summary>Where each of three byte pixel's channels lands once a fourth is made room for.</summary>
    private static ReadOnlySpan<byte> Spread =>
        [0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255];

    private static void TruecolourToRgba8(ReadOnlySpan<byte> source, Span<uint> output, int width,
                                          ReadOnlySpan<byte> transparency)
    {
        int keyRed = -1, keyGreen = -1, keyBlue = -1;
        if (transparency.Length >= 6)
        {
            keyRed = BinaryPrimitives.ReadUInt16BigEndian(transparency);
            keyGreen = BinaryPrimitives.ReadUInt16BigEndian(transparency[2..]);
            keyBlue = BinaryPrimitives.ReadUInt16BigEndian(transparency[4..]);
        }

        ref byte src = ref MemoryMarshal.GetReference(source);
        ref uint dst = ref MemoryMarshal.GetReference(output);

        if (keyRed < 0)
        {
            // No colour key, so all that happens is a gap opening for the alpha byte. Four
            // pixels is one shuffle, named rather than reached through the portable one, which
            // guards against out of range indices and measured three times slower.
            int x = 0;

            if (Ssse3.IsSupported)
            {
                Vector128<byte> order = Vector128.Create(Spread);
                Vector128<uint> opaque = Vector128.Create(0xFF000000u);

                for (; (x * 3) + 16 <= source.Length; x += 4)
                {
                    Vector128<byte> packed = Vector128.LoadUnsafe(ref Unsafe.Add(ref src, x * 3));
                    (Ssse3.Shuffle(packed, order).AsUInt32() | opaque)
                        .StoreUnsafe(ref Unsafe.Add(ref dst, x));
                }
            }

            for (; x < width; x++)
            {
                int at = x * 3;
                uint red = Unsafe.Add(ref src, at);
                uint green = Unsafe.Add(ref src, at + 1);
                uint blue = Unsafe.Add(ref src, at + 2);
                Unsafe.Add(ref dst, x) = red | (green << 8) | (blue << 16) | 0xFF000000u;
            }
            return;
        }

        for (int x = 0; x < width; x++)
        {
            int at = x * 3;
            uint red = Unsafe.Add(ref src, at);
            uint green = Unsafe.Add(ref src, at + 1);
            uint blue = Unsafe.Add(ref src, at + 2);
            uint opacity = red == keyRed && green == keyGreen && blue == keyBlue ? 0u : 0xFF000000u;
            Unsafe.Add(ref dst, x) = red | (green << 8) | (blue << 16) | opacity;
        }
    }

    private static void PaletteToRgba8(ReadOnlySpan<byte> source, Span<uint> output, int width, in Layout layout)
    {
        ReadOnlySpan<uint> palette = layout.Palette;
        ref uint table = ref MemoryMarshal.GetReference(palette);
        ref uint dst = ref MemoryMarshal.GetReference(output);

        if (layout.BitDepth == 8)
        {
            ref byte src = ref MemoryMarshal.GetReference(source);
            for (int x = 0; x < width; x++)
                Unsafe.Add(ref dst, x) = Unsafe.Add(ref table, Unsafe.Add(ref src, x));
            return;
        }

        for (int x = 0; x < width; x++)
            Unsafe.Add(ref dst, x) = Unsafe.Add(ref table, ReadPackedSample(source, x, layout.BitDepth));
    }

    private static void GreyscaleToRgba8(ReadOnlySpan<byte> source, Span<uint> output, int width,
                                         ReadOnlySpan<byte> transparency)
    {
        int transparent = transparency.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(transparency) : -1;
        ref byte src = ref MemoryMarshal.GetReference(source);
        ref uint dst = ref MemoryMarshal.GetReference(output);

        for (int x = 0; x < width; x++)
        {
            uint grey = Unsafe.Add(ref src, x);
            uint opacity = (int)grey == transparent ? 0u : 0xFF000000u;
            Unsafe.Add(ref dst, x) = grey | (grey << 8) | (grey << 16) | opacity;
        }
    }

    private static void GreyscaleAlphaToRgba8(ReadOnlySpan<byte> source, Span<uint> output, int width)
    {
        ref byte src = ref MemoryMarshal.GetReference(source);
        ref uint dst = ref MemoryMarshal.GetReference(output);

        for (int x = 0; x < width; x++)
        {
            uint grey = Unsafe.Add(ref src, x * 2);
            uint opacity = Unsafe.Add(ref src, (x * 2) + 1);
            Unsafe.Add(ref dst, x) = grey | (grey << 8) | (grey << 16) | (opacity << 24);
        }
    }

    /// <summary>Greyscale at one, two or four bits per sample, scaled up to the full range.</summary>
    private static void LowDepthToRgba8(ReadOnlySpan<byte> source, Span<uint> output, int width, in Layout layout)
    {
        int transparent = layout.Transparency.Length >= 2
            ? BinaryPrimitives.ReadUInt16BigEndian(layout.Transparency)
            : -1;

        uint scale = layout.BitDepth switch
        {
            1 => OneBitScale,
            2 => TwoBitScale,
            4 => FourBitScale,
            _ => 1,
        };

        for (int x = 0; x < width; x++)
        {
            int sample = ReadPackedSample(source, x, layout.BitDepth);
            uint grey = (uint)sample * scale;
            uint opacity = sample == transparent ? 0u : 0xFF000000u;
            output[x] = grey | (grey << 8) | (grey << 16) | opacity;
        }
    }

    // ---- direct paths to sixteen bit RGBA --------------------------------------------

    private static void ToRgba16(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        Span<ushort> output = MemoryMarshal.Cast<byte, ushort>(destination);
        ReadOnlySpan<byte> transparency = layout.Transparency;

        switch (layout.ColourType)
        {
            case 6:
                for (int i = 0; i < width * 4; i++)
                    output[i] = BinaryPrimitives.ReadUInt16BigEndian(source[(i * 2)..]);
                return;

            case 4:
                for (int x = 0; x < width; x++)
                {
                    ushort grey = BinaryPrimitives.ReadUInt16BigEndian(source[(x * 4)..]);
                    ushort opacity = BinaryPrimitives.ReadUInt16BigEndian(source[((x * 4) + 2)..]);
                    int at = x * 4;
                    output[at] = grey;
                    output[at + 1] = grey;
                    output[at + 2] = grey;
                    output[at + 3] = opacity;
                }
                return;

            case 2:
            {
                int keyRed = -1, keyGreen = -1, keyBlue = -1;
                if (transparency.Length >= 6)
                {
                    keyRed = BinaryPrimitives.ReadUInt16BigEndian(transparency);
                    keyGreen = BinaryPrimitives.ReadUInt16BigEndian(transparency[2..]);
                    keyBlue = BinaryPrimitives.ReadUInt16BigEndian(transparency[4..]);
                }

                for (int x = 0; x < width; x++)
                {
                    ushort red = BinaryPrimitives.ReadUInt16BigEndian(source[(x * 6)..]);
                    ushort green = BinaryPrimitives.ReadUInt16BigEndian(source[((x * 6) + 2)..]);
                    ushort blue = BinaryPrimitives.ReadUInt16BigEndian(source[((x * 6) + 4)..]);
                    int at = x * 4;
                    output[at] = red;
                    output[at + 1] = green;
                    output[at + 2] = blue;
                    output[at + 3] = (ushort)(red == keyRed && green == keyGreen && blue == keyBlue
                        ? 0 : ushort.MaxValue);
                }
                return;
            }

            default:
            {
                int transparent = transparency.Length >= 2
                    ? BinaryPrimitives.ReadUInt16BigEndian(transparency)
                    : -1;

                for (int x = 0; x < width; x++)
                {
                    ushort grey = BinaryPrimitives.ReadUInt16BigEndian(source[(x * 2)..]);
                    int at = x * 4;
                    output[at] = grey;
                    output[at + 1] = grey;
                    output[at + 2] = grey;
                    output[at + 3] = (ushort)(grey == transparent ? 0 : ushort.MaxValue);
                }
                return;
            }
        }
    }

    // ---- the layout the file itself stores -------------------------------------------

    private static void ToNatural(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        switch (layout.ColourType)
        {
            case 0:
                NaturalGreyscale(source, destination, width, layout);
                break;
            case 2:
                NaturalTruecolour(source, destination, width, layout);
                break;
            case 3:
                NaturalPalette(source, destination, width, layout);
                break;
            case 4:
                NaturalGreyscaleAlpha(source, destination, width, layout);
                break;
            case 6:
                NaturalTruecolourAlpha(source, destination, width, layout);
                break;
        }
    }

    private static void NaturalGreyscale(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        ReadOnlySpan<byte> transparency = layout.Transparency;
        bool hasAlpha = layout.Target.HasAlpha();

        if (layout.BitDepth == 16)
        {
            int transparent = transparency.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(transparency) : -1;
            Span<ushort> output = MemoryMarshal.Cast<byte, ushort>(destination);
            for (int x = 0; x < width; x++)
            {
                ushort grey = BinaryPrimitives.ReadUInt16BigEndian(source[(x * 2)..]);
                if (hasAlpha)
                {
                    output[x * 2] = grey;
                    output[(x * 2) + 1] = (ushort)(grey == transparent ? 0 : ushort.MaxValue);
                }
                else
                {
                    output[x] = grey;
                }
            }
            return;
        }

        int transparentIndex = transparency.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(transparency) : -1;
        byte scale = layout.BitDepth switch
        {
            1 => OneBitScale,
            2 => TwoBitScale,
            4 => FourBitScale,
            _ => 1,
        };

        for (int x = 0; x < width; x++)
        {
            int sample = ReadPackedSample(source, x, layout.BitDepth);
            byte grey = layout.BitDepth == 8 ? (byte)sample : (byte)(sample * scale);

            if (hasAlpha)
            {
                destination[x * 2] = grey;
                destination[(x * 2) + 1] = (byte)(sample == transparentIndex ? 0 : 255);
            }
            else
            {
                destination[x] = grey;
            }
        }
    }

    private static void NaturalTruecolour(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        if (layout.BitDepth == 16)
        {
            // Without a transparency chunk the natural layout is three channels, not four, and
            // writing a fourth would run off the end of a frame sized for three.
            if (layout.Target.HasAlpha())
            {
                ToRgba16(source, destination, width, layout);
                return;
            }

            Span<ushort> output = MemoryMarshal.Cast<byte, ushort>(destination);
            for (int i = 0; i < width * 3; i++)
                output[i] = BinaryPrimitives.ReadUInt16BigEndian(source[(i * 2)..]);
            return;
        }

        if (!layout.Target.HasAlpha())
        {
            source[..(width * 3)].CopyTo(destination);
            return;
        }

        TruecolourToRgba8(source, MemoryMarshal.Cast<byte, uint>(destination), width, layout.Transparency);
    }

    private static void NaturalPalette(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        if (layout.Target.HasAlpha())
        {
            PaletteToRgba8(source, MemoryMarshal.Cast<byte, uint>(destination), width, layout);
            return;
        }

        ReadOnlySpan<uint> palette = layout.Palette;
        for (int x = 0; x < width; x++)
        {
            uint entry = palette[ReadPackedSample(source, x, layout.BitDepth)];
            int at = x * 3;
            destination[at] = (byte)entry;
            destination[at + 1] = (byte)(entry >> 8);
            destination[at + 2] = (byte)(entry >> 16);
        }
    }

    private static void NaturalGreyscaleAlpha(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        if (layout.BitDepth == 16)
        {
            Span<ushort> output = MemoryMarshal.Cast<byte, ushort>(destination);
            for (int i = 0; i < width * 2; i++)
                output[i] = BinaryPrimitives.ReadUInt16BigEndian(source[(i * 2)..]);
            return;
        }

        source[..(width * 2)].CopyTo(destination);
    }

    private static void NaturalTruecolourAlpha(ReadOnlySpan<byte> source, Span<byte> destination, int width, in Layout layout)
    {
        if (layout.BitDepth == 16)
        {
            Span<ushort> output = MemoryMarshal.Cast<byte, ushort>(destination);
            for (int i = 0; i < width * 4; i++)
                output[i] = BinaryPrimitives.ReadUInt16BigEndian(source[(i * 2)..]);
            return;
        }

        source[..(width * 4)].CopyTo(destination);
    }

    /// <summary>Reads sample <paramref name="index"/> from a row of packed sub-byte samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadPackedSample(ReadOnlySpan<byte> source, int index, byte bitDepth) => bitDepth switch
    {
        8 => source[index],
        4 => (source[index >> 1] >> ((index & 1) == 0 ? 4 : 0)) & 0x0F,
        2 => (source[index >> 2] >> (6 - ((index & 3) << 1))) & 0x03,
        1 => (source[index >> 3] >> (7 - (index & 7))) & 0x01,
        _ => 0,
    };
}
