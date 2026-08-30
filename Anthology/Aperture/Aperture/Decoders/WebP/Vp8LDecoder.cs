// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// The lossless form, a dictionary coder rather than anything to do with a cosine transform:
/// entropy coded pixels, repeats named by how far back they start, recent colours named by a short
/// index. In front of that sit up to four reversible transforms, and undoing them is most of this.
/// </summary>
internal static class Vp8LDecoder
{
    private const int LiteralCodes = 256;
    private const int LengthCodes = 24;
    private const int DistanceCodes = 40;
    private const int CodesPerGroup = 5;
    private const int MaxCacheBits = 11;
    private const int CodeLengthCodes = 19;

    private const int PredictorTransform = 0;
    private const int CrossColorTransform = 1;
    private const int SubtractGreenTransform = 2;
    private const int ColorIndexingTransform = 3;

    private static ReadOnlySpan<byte> CodeLengthOrder =>
        [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    private static ReadOnlySpan<byte> CodeLengthExtraBits => [2, 3, 7];

    private static ReadOnlySpan<byte> CodeLengthRepeatOffsets => [3, 3, 11];

    /// <summary>
    /// How a short distance code names a nearby pixel. The first hundred and twenty codes stand
    /// for somewhere in the eight columns either side of the two rows above, which is where a
    /// repeat almost always starts, so the common case costs far fewer bits than a raw offset.
    /// </summary>
    private static ReadOnlySpan<byte> DistanceMap =>
    [
        0x18, 0x07, 0x17, 0x19, 0x28, 0x06, 0x27, 0x29, 0x16, 0x1a, 0x26, 0x2a,
        0x38, 0x05, 0x37, 0x39, 0x15, 0x1b, 0x36, 0x3a, 0x25, 0x2b, 0x48, 0x04,
        0x47, 0x49, 0x14, 0x1c, 0x35, 0x3b, 0x46, 0x4a, 0x24, 0x2c, 0x58, 0x45,
        0x4b, 0x34, 0x3c, 0x03, 0x57, 0x59, 0x13, 0x1d, 0x56, 0x5a, 0x23, 0x2d,
        0x44, 0x4c, 0x55, 0x5b, 0x33, 0x3d, 0x68, 0x02, 0x67, 0x69, 0x12, 0x1e,
        0x66, 0x6a, 0x22, 0x2e, 0x54, 0x5c, 0x43, 0x4d, 0x65, 0x6b, 0x32, 0x3e,
        0x78, 0x01, 0x77, 0x79, 0x53, 0x5d, 0x11, 0x1f, 0x64, 0x6c, 0x42, 0x4e,
        0x76, 0x7a, 0x21, 0x2f, 0x75, 0x7b, 0x31, 0x3f, 0x63, 0x6d, 0x52, 0x5e,
        0x00, 0x74, 0x7c, 0x41, 0x4f, 0x10, 0x20, 0x62, 0x6e, 0x30, 0x73, 0x7d,
        0x51, 0x5f, 0x40, 0x72, 0x7e, 0x61, 0x6f, 0x50, 0x71, 0x7f, 0x60, 0x70,
    ];

    private sealed class Transform
    {
        public int Type;
        public int Bits;
        public int Width;
        public int Height;
        public uint[] Data = [];
    }

    /// <summary>Decodes a whole lossless picture into rows of premultiplied nothing, as plain BGRA.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, int width, int height, out uint[]? pixels)
    {
        pixels = null;

        if (data.Length < 5 || data[0] != 0x2F)
            return false;

        Vp8LBitReader reader = new(data[5..]);
        return TryDecodeStream(ref reader, width, height, level0: true, out pixels);
    }

    /// <summary>Decodes the alpha plane a lossy picture may carry, which is a one channel stream.</summary>
    public static bool TryDecodeAlpha(ReadOnlySpan<byte> data, int width, int height, out uint[]? pixels)
    {
        pixels = null;
        Vp8LBitReader reader = new(data);
        return TryDecodeStream(ref reader, width, height, level0: true, out pixels);
    }

    private static bool TryDecodeStream(ref Vp8LBitReader reader, int width, int height, bool level0,
                                        out uint[]? pixels)
    {
        pixels = null;

        List<Transform> transforms = [];
        int transformWidth = width;
        int seen = 0;

        if (level0)
        {
            while (reader.Read(1) != 0)
            {
                if (!TryReadTransform(ref reader, ref transformWidth, height, ref seen, transforms))
                    return false;
            }
        }

        int cacheBits = 0;
        if (reader.Read(1) != 0)
        {
            cacheBits = (int)reader.Read(4);
            if (cacheBits is < 1 or > MaxCacheBits)
                return false;
        }

        if (!TryDecodeData(ref reader, transformWidth, height, cacheBits, level0, out uint[]? raw))
            return false;


        // They come off in reverse, and only the colour index one changes the row length.
        for (int i = transforms.Count - 1; i >= 0; i--)
        {
            if (!TryUndo(transforms[i], ref raw, ref transformWidth, height))
                return false;

        }

        if (transformWidth != width || raw!.Length < width * height)
            return false;

        pixels = raw;
        return true;
    }

    private static bool TryReadTransform(ref Vp8LBitReader reader, ref int width, int height,
                                         ref int seen, List<Transform> transforms)
    {
        int type = (int)reader.Read(2);

        // A file may apply each transform at most once, which is what keeps the chain finite.
        if ((seen & (1 << type)) != 0)
            return false;

        seen |= 1 << type;

        Transform transform = new() { Type = type, Width = width, Height = height };

        switch (type)
        {
            case PredictorTransform:
            case CrossColorTransform:
            {
                transform.Bits = 2 + (int)reader.Read(3);
                int across = SubSampleSize(width, transform.Bits);
                int down = SubSampleSize(height, transform.Bits);

                if (!TryDecodeStream(ref reader, across, down, level0: false, out uint[]? data))
                    return false;

                transform.Data = data!;
                break;
            }

            case ColorIndexingTransform:
            {
                int colours = (int)reader.Read(8) + 1;
                int bits = colours > 16 ? 0 : colours > 4 ? 1 : colours > 2 ? 2 : 3;

                if (!TryDecodeStream(ref reader, colours, 1, level0: false, out uint[]? table))
                    return false;

                transform.Bits = bits;
                transform.Data = ExpandPalette(table!, colours);
                width = SubSampleSize(transform.Width, bits);
                break;
            }

            case SubtractGreenTransform:
                break;

            default:
                return false;
        }

        transforms.Add(transform);
        return true;
    }

    /// <summary>
    /// A palette is stored as differences from the entry before it, so that a ramp costs almost
    /// nothing, and the table a reader indexes is the running total.
    /// </summary>
    private static uint[] ExpandPalette(uint[] table, int colours)
    {
        uint[] expanded = new uint[colours];
        uint running = 0;

        for (int i = 0; i < colours && i < table.Length; i++)
        {
            running = AddPixels(table[i], running);
            expanded[i] = running;
        }

        return expanded;
    }

    private static bool TryDecodeData(ref Vp8LBitReader reader, int width, int height, int cacheBits,
                                      bool allowMeta, out uint[]? pixels)
    {
        pixels = null;

        long total = (long)width * height;
        if (width <= 0 || height <= 0 || total > (1 << 28))
            return false;

        uint[]? groupImage = null;
        int groupBits = 0;
        int groups = 1;

        if (allowMeta && reader.Read(1) != 0)
        {
            groupBits = 2 + (int)reader.Read(3);
            int across = SubSampleSize(width, groupBits);
            int down = SubSampleSize(height, groupBits);

            if (!TryDecodeStream(ref reader, across, down, level0: false, out uint[]? image))
                return false;

            groupImage = image;

            // The tile's code group is in the red and green bytes of its pixel.
            for (int i = 0; i < groupImage!.Length; i++)
            {
                uint group = (groupImage[i] >> 8) & 0xFFFF;
                groupImage[i] = group;
                if (group >= groups)
                    groups = (int)group + 1;
            }

            if (groups > (1 << 16))
                return false;
        }

        Vp8LPrefixCode[][] codes = new Vp8LPrefixCode[groups][];
        int[] lengths = new int[LiteralCodes + LengthCodes + (1 << MaxCacheBits)];

        for (int group = 0; group < groups; group++)
        {
            codes[group] = new Vp8LPrefixCode[CodesPerGroup];
            for (int i = 0; i < CodesPerGroup; i++)
            {
                int alphabet = i switch
                {
                    0 => LiteralCodes + LengthCodes + (cacheBits > 0 ? 1 << cacheBits : 0),
                    4 => DistanceCodes,
                    _ => LiteralCodes,
                };

                if (!TryReadCode(ref reader, alphabet, lengths, out Vp8LPrefixCode? code))
                    return false;

                codes[group][i] = code!;
            }
        }

        uint[] output = new uint[total];
        uint[]? cache = cacheBits > 0 ? new uint[1 << cacheBits] : null;
        int cacheShift = 32 - cacheBits;

        int mask = groupBits > 0 ? (1 << groupBits) - 1 : 0;
        int tilesAcross = groupBits > 0 ? SubSampleSize(width, groupBits) : 1;

        int at = 0;
        int x = 0;
        int y = 0;
        int cached = 0;
        Vp8LPrefixCode[] active = codes[0];

        while (at < total)
        {
            if (groupImage is not null && (x & mask) == 0)
            {
                if (!TrySelect(groupImage, codes, groups, groupBits, tilesAcross, x, y, ref active))
                    return false;
            }

            int code = active[0].Read(ref reader);

            if (code < 0 || reader.Ended)
                return false;

            if (code < LiteralCodes)
            {
                int red = active[1].Read(ref reader);
                int blue = active[2].Read(ref reader);
                int alpha = active[3].Read(ref reader);

                if (red < 0 || blue < 0 || alpha < 0)
                    return false;

                output[at++] = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)code << 8) | (uint)blue;

                if (++x >= width)
                {
                    x = 0;
                    y++;
                }

                if (cache is not null)
                {
                    while (cached < at)
                        Insert(cache, cacheShift, output[cached++]);
                }

                continue;
            }

            if (code < LiteralCodes + LengthCodes)
            {
                int length = ReadExtra(ref reader, code - LiteralCodes);
                int distanceSymbol = active[4].Read(ref reader);
                if (distanceSymbol < 0)
                    return false;

                int distance = ToDistance(width, ReadExtra(ref reader, distanceSymbol));

                if (distance <= 0 || at < distance || at + length > total)
                    return false;

                for (int i = 0; i < length; i++, at++)
                    output[at] = output[at - distance];

                x += length;
                while (x >= width)
                {
                    x -= width;
                    y++;
                }

                // A run ending inside a tile leaves the group stale.
                if (groupImage is not null && (x & mask) != 0 &&
                    !TrySelect(groupImage, codes, groups, groupBits, tilesAcross, x, y, ref active))
                    return false;

                if (cache is not null)
                {
                    while (cached < at)
                        Insert(cache, cacheShift, output[cached++]);
                }

                continue;
            }

            if (cache is null)
                return false;

            int key = code - LiteralCodes - LengthCodes;
            if ((uint)key >= (uint)cache.Length)
                return false;

            while (cached < at)
                Insert(cache, cacheShift, output[cached++]);

            output[at++] = cache[key];

            if (++x >= width)
            {
                x = 0;
                y++;
            }
        }

        pixels = output;
        return true;
    }

    /// <summary>Picks the code group covering a position, which is named by a picture of its own.</summary>
    private static bool TrySelect(uint[] groupImage, Vp8LPrefixCode[][] codes, int groups, int bits,
                                  int tilesAcross, int x, int y, ref Vp8LPrefixCode[] active)
    {
        int tile = ((y >> bits) * tilesAcross) + (x >> bits);
        if ((uint)tile >= (uint)groupImage.Length)
            return false;

        uint index = groupImage[tile];
        if (index >= (uint)groups)
            return false;

        active = codes[index];
        return true;
    }

    private static void Insert(uint[] cache, int shift, uint colour) =>
        cache[(colour * 0x1E35A7BDu) >> shift] = colour;

    /// <summary>
    /// Reads one prefix code. The short form names one or two symbols outright, which is what a
    /// picture of one colour needs and what the general form cannot express in fewer bits.
    /// </summary>
    private static bool TryReadCode(ref Vp8LBitReader reader, int alphabet, int[] lengths,
                                    out Vp8LPrefixCode? code)
    {
        code = null;
        if (alphabet <= 0 || alphabet > lengths.Length)
            return false;

        Array.Clear(lengths, 0, alphabet);

        if (reader.Read(1) != 0)
        {
            int symbols = (int)reader.Read(1) + 1;
            int first = (int)reader.Read(reader.Read(1) == 0 ? 1 : 8);

            if (first >= lengths.Length)
                return false;

            lengths[first] = 1;

            if (symbols == 2)
            {
                int second = (int)reader.Read(8);
                if (second >= lengths.Length)
                    return false;

                lengths[second] = 1;
            }
        }
        else if (!TryReadCodeLengths(ref reader, alphabet, lengths))
        {
            return false;
        }

        if (reader.Ended)
            return false;

        code = Vp8LPrefixCode.Build(lengths.AsSpan(0, alphabet));
        return code is not null;
    }

    /// <summary>
    /// Reads the lengths themselves, which are coded with a prefix code of their own over a
    /// nineteen symbol alphabet: sixteen lengths and three ways of saying "and again".
    /// </summary>
    private static bool TryReadCodeLengths(ref Vp8LBitReader reader, int alphabet, int[] lengths)
    {
        Span<int> meta = stackalloc int[CodeLengthCodes];
        meta.Clear();

        int count = (int)reader.Read(4) + 4;
        if (count > CodeLengthCodes)
            return false;

        for (int i = 0; i < count; i++)
            meta[CodeLengthOrder[i]] = (int)reader.Read(3);

        Vp8LPrefixCode? outer = Vp8LPrefixCode.Build(meta);
        if (outer is null)
            return false;

        int limit = alphabet;
        if (reader.Read(1) != 0)
        {
            int bits = 2 + (2 * (int)reader.Read(3));
            limit = 2 + (int)reader.Read(bits);
            if (limit > alphabet)
                return false;
        }

        int previous = 8;
        int symbol = 0;

        while (symbol < alphabet)
        {
            if (limit-- == 0)
                break;

            int length = outer.Read(ref reader);
            if (length < 0 || reader.Ended)
                return false;

            if (length < 16)
            {
                lengths[symbol++] = length;
                if (length != 0)
                    previous = length;

                continue;
            }

            int slot = length - 16;
            if (slot >= CodeLengthExtraBits.Length)
                return false;

            int repeat = (int)reader.Read(CodeLengthExtraBits[slot]) + CodeLengthRepeatOffsets[slot];
            if (symbol + repeat > alphabet)
                return false;

            int value = length == 16 ? previous : 0;
            while (repeat-- > 0)
                lengths[symbol++] = value;
        }

        return true;
    }

    /// <summary>Lengths and distances share one coding: a symbol, then the bits it does not fix.</summary>
    private static int ReadExtra(ref Vp8LBitReader reader, int symbol)
    {
        if (symbol < 4)
            return symbol + 1;

        int extra = (symbol - 2) >> 1;
        int offset = (2 + (symbol & 1)) << extra;
        return offset + (int)reader.Read(extra) + 1;
    }

    private static int ToDistance(int width, int code)
    {
        if (code > DistanceMap.Length)
            return code - DistanceMap.Length;

        int mapped = DistanceMap[code - 1];
        int distance = ((mapped >> 4) * width) + 8 - (mapped & 0xF);
        return distance >= 1 ? distance : 1;
    }

    private static bool TryUndo(Transform transform, ref uint[]? pixels, ref int width, int height)
    {
        if (pixels is null)
            return false;

        switch (transform.Type)
        {
            case SubtractGreenTransform:
                AddGreen(pixels);
                return true;

            case PredictorTransform:
                return TryPredict(transform, pixels, width, height);

            case CrossColorTransform:
                return TryCrossColour(transform, pixels, width, height);

            default:
                return TryPalette(transform, ref pixels, ref width, height);
        }
    }

    /// <summary>Green was subtracted from the other two channels, so it goes back on.</summary>
    private static void AddGreen(uint[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            uint argb = pixels[i];
            uint green = (argb >> 8) & 0xFF;
            uint red = ((argb >> 16) + green) & 0xFF;
            uint blue = (argb + green) & 0xFF;
            pixels[i] = (argb & 0xFF00FF00u) | (red << 16) | blue;
        }
    }

    private static bool TryPredict(Transform transform, uint[] pixels, int width, int height)
    {
        int bits = transform.Bits;
        int tilesAcross = SubSampleSize(width, bits);

        // Nothing above the first row, so it is guessed from the left and the first from nothing.
        pixels[0] = AddPixels(pixels[0], 0xFF000000u);
        for (int x = 1; x < width; x++)
            pixels[x] = AddPixels(pixels[x], pixels[x - 1]);

        for (int y = 1; y < height; y++)
        {
            int row = y * width;

            // The first column has nothing to its left, so it is guessed from the pixel above.
            pixels[row] = AddPixels(pixels[row], pixels[row - width]);

            // Fixed across a tile, so it is settled once a tile rather than once a pixel.
            int x = 1;
            while (x < width)
            {
                int tileIndex = ((y >> bits) * tilesAcross) + (x >> bits);
                if ((uint)tileIndex >= (uint)transform.Data.Length)
                    return false;

                int mode = (int)((transform.Data[tileIndex] >> 8) & 0xF);
                int end = Math.Min(width, ((x >> bits) + 1) << bits);

                for (; x < end; x++)
                    pixels[row + x] = AddPixels(pixels[row + x], Predict(mode, pixels, row, x, width));
            }
        }

        return true;
    }

    /// <summary>
    /// The fourteen ways a pixel may be guessed from the ones left of and above it. Every one is
    /// an average or a gradient, so a smooth picture leaves almost nothing to code.
    /// </summary>
    private static uint Predict(int mode, uint[] pixels, int row, int x, int width)
    {
        uint left = pixels[row + x - 1];
        uint top = pixels[row - width + x];
        uint topLeft = pixels[row - width + x - 1];
        // At the last column this reaches the first pixel of the row being written, which is
        // where the format's own definition points once the rows lie end to end.
        uint topRight = pixels[row - width + x + 1];

        return mode switch
        {
            0 => 0xFF000000u,
            1 => left,
            2 => top,
            3 => topRight,
            4 => topLeft,
            5 => Average2(Average2(left, topRight), top),
            6 => Average2(left, topLeft),
            7 => Average2(left, top),
            8 => Average2(topLeft, top),
            9 => Average2(top, topRight),
            10 => Average2(Average2(left, topLeft), Average2(top, topRight)),
            11 => Select(top, left, topLeft),
            12 => ClampedAddSubtractFull(left, top, topLeft),
            13 => ClampedAddSubtractHalf(left, top, topLeft),
            _ => 0xFF000000u,
        };
    }

    private static bool TryCrossColour(Transform transform, uint[] pixels, int width, int height)
    {
        int bits = transform.Bits;
        int tilesAcross = SubSampleSize(width, bits);

        for (int y = 0; y < height; y++)
        {
            int row = y * width;

            // Fixed across a tile, so they are unpacked once a tile rather than once a pixel.
            int x = 0;
            while (x < width)
            {
                int tileIndex = ((y >> bits) * tilesAcross) + (x >> bits);
                if ((uint)tileIndex >= (uint)transform.Data.Length)
                    return false;

                uint multipliers = transform.Data[tileIndex];
                sbyte greenToRed = (sbyte)multipliers;
                sbyte greenToBlue = (sbyte)(multipliers >> 8);
                sbyte redToBlue = (sbyte)(multipliers >> 16);

                int end = Math.Min(width, ((x >> bits) + 1) << bits);

                for (; x < end; x++)
                {
                    uint argb = pixels[row + x];
                    sbyte green = (sbyte)(argb >> 8);

                    int red = (int)((argb >> 16) & 0xFF);
                    int blue = (int)(argb & 0xFF);

                    red = (red + Delta(greenToRed, green)) & 0xFF;
                    blue = (blue + Delta(greenToBlue, green) + Delta(redToBlue, (sbyte)red)) & 0xFF;

                    pixels[row + x] = (argb & 0xFF00FF00u) | ((uint)red << 16) | (uint)blue;
                }
            }
        }

        return true;
    }

    private static int Delta(sbyte multiplier, sbyte value) => (multiplier * value) >> 5;

    /// <summary>
    /// The colour index form, where the green byte is an index rather than a colour. A picture of
    /// few enough colours packs several indices into one byte, so undoing it widens each row.
    /// </summary>
    private static bool TryPalette(Transform transform, ref uint[]? pixels, ref int width, int height)
    {
        int full = transform.Width;
        int perPixel = 8 >> transform.Bits;
        int perByte = 1 << transform.Bits;
        int mask = (1 << perPixel) - 1;

        uint[] output = new uint[(long)full * height <= int.MaxValue ? full * height : 0];
        if (output.Length == 0)
            return false;

        for (int y = 0; y < height; y++)
        {
            int from = y * width;
            int to = y * full;

            for (int x = 0; x < full; x++)
            {
                int packed = x >> transform.Bits;
                if (from + packed >= pixels!.Length)
                    return false;

                int shift = (x & (perByte - 1)) * perPixel;
                int index = (int)((pixels[from + packed] >> (8 + shift)) & (uint)mask);

                // The format does not forbid an index past the table, so it resolves to nothing
                // rather than refusing the file.
                output[to + x] = (uint)index < (uint)transform.Data.Length ? transform.Data[index] : 0;
            }
        }

        pixels = output;
        width = full;
        return true;
    }

    private static int SubSampleSize(int size, int bits) => (size + (1 << bits) - 1) >> bits;

    private static uint AddPixels(uint a, uint b)
    {
        uint alphaAndGreen = (a & 0xFF00FF00u) + (b & 0xFF00FF00u);
        uint redAndBlue = (a & 0x00FF00FFu) + (b & 0x00FF00FFu);
        return (alphaAndGreen & 0xFF00FF00u) | (redAndBlue & 0x00FF00FFu);
    }

    private static uint Average2(uint a, uint b) => (((a ^ b) & 0xFEFEFEFEu) >> 1) + (a & b);

    private static uint Clip255(int value) => value < 0 ? 0u : value > 255 ? 255u : (uint)value;

    private static uint ClampedAddSubtractFull(uint c0, uint c1, uint c2)
    {
        uint result = 0;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            int a = (int)((c0 >> shift) & 0xFF);
            int b = (int)((c1 >> shift) & 0xFF);
            int c = (int)((c2 >> shift) & 0xFF);
            result |= Clip255(a + b - c) << shift;
        }

        return result;
    }

    private static uint ClampedAddSubtractHalf(uint c0, uint c1, uint c2)
    {
        uint average = Average2(c0, c1);
        uint result = 0;

        for (int shift = 0; shift <= 24; shift += 8)
        {
            int a = (int)((average >> shift) & 0xFF);
            int c = (int)((c2 >> shift) & 0xFF);
            result |= Clip255(a + ((a - c) / 2)) << shift;
        }

        return result;
    }

    /// <summary>Picks whichever of the two neighbours the gradient says is closer.</summary>
    private static uint Select(uint a, uint b, uint c)
    {
        int difference = 0;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            int first = (int)((a >> shift) & 0xFF);
            int second = (int)((b >> shift) & 0xFF);
            int third = (int)((c >> shift) & 0xFF);
            difference += Math.Abs(second - third) - Math.Abs(first - third);
        }

        return difference <= 0 ? a : b;
    }
}
