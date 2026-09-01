// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Dds;

/// <summary>
/// The block compression that lets a file choose how much it spends a pixel: any block size from
/// four by four to twelve by twelve, and per block the grouping, the quantisation and whether the
/// weights are interpolated. All of it is described in the first few bits.
/// </summary>
internal static class Astc
{
    private const int MaxWeight = 64;

    /// <summary>The sixteen bytes of a block, with the two readings its fields need.</summary>
    private readonly struct Block
    {
        private readonly ulong _low;
        private readonly ulong _high;

        public Block(ReadOnlySpan<byte> data)
        {
            _low = BinaryPrimitives.ReadUInt64LittleEndian(data);
            _high = BinaryPrimitives.ReadUInt64LittleEndian(data[8..]);
        }

        private Block(ulong low, ulong high)
        {
            _low = low;
            _high = high;
        }

        /// <summary>The same block read from the other end, which is how the weights are stored.</summary>
        public Block Reversed()
        {
            ulong low = Reverse(_high);
            ulong high = Reverse(_low);
            return new Block(low, high);
        }

        private static ulong Reverse(ulong value)
        {
            ulong result = 0;
            for (int i = 0; i < 64; i++)
                result |= ((value >> i) & 1) << (63 - i);

            return result;
        }

        public uint Get(int at, int count)
        {
            if (count == 0 || at >= 128)
                return 0;

            ulong value = at < 64
                ? (_low >> at) | (at == 0 ? 0 : _high << (64 - at))
                : _high >> (at - 64);

            return (uint)(value & ((1UL << count) - 1));
        }

        public uint Next(ref int at, int count)
        {
            uint value = Get(at, count);
            at += count;
            return value;
        }
    }

    /// <summary>What a block turns out to be once its first few bits are read.</summary>
    private struct Config
    {
        public int GridWidth;
        public int GridHeight;
        public int WeightRange;
        public bool DualPlane;
        public bool Solid;
        public uint SolidRed;
        public uint SolidGreen;
        public uint SolidBlue;
        public uint SolidAlpha;
    }

    public static void Decode(ReadOnlySpan<byte> data, int width, int height, Span<uint> pixels)
    {
        Block block = new(data);

        if (!TryConfig(block, out Config config))
        {
            pixels.Fill(0xFFFF00FFu);
            return;
        }

        if (config.Solid)
        {
            uint colour = ((config.SolidAlpha >> 8) << 24)
                | ((config.SolidRed >> 8) << 16)
                | ((config.SolidGreen >> 8) << 8)
                | (config.SolidBlue >> 8);

            pixels[..(width * height)].Fill(colour);
            return;
        }

        if (config.GridWidth > width || config.GridHeight > height)
        {
            pixels.Fill(0xFFFF00FFu);
            return;
        }

        int gridValues = (config.DualPlane ? 2 : 1) * config.GridWidth * config.GridHeight;
        int weightBits = SequenceBits(gridValues, config.WeightRange);

        if (gridValues == 0 || gridValues > 64 || weightBits < 24 || weightBits > 96)
        {
            pixels.Fill(0xFFFF00FFu);
            return;
        }

        int weightEnd = 128 - weightBits;
        int extraBits = 0;

        int partitions = (int)block.Get(11, 2) + 1;
        Span<int> modes = stackalloc int[4];
        int partitionId = 0;

        if (partitions == 1)
        {
            modes[0] = (int)block.Get(13, 4);
        }
        else
        {
            if (config.DualPlane && partitions == 4)
            {
                pixels.Fill(0xFFFF00FFu);
                return;
            }

            partitionId = (int)block.Get(13, 10);
            uint cem = block.Get(23, 6);

            if ((cem & 3) == 0)
            {
                for (int i = 0; i < partitions; i++)
                    modes[i] = (int)(cem >> 2);
            }
            else
            {
                // They do not fit beside the partition index, so most of the bits move down.
                int first = (int)(((cem & 3) - 1) * 4);
                extraBits = (3 * partitions) - 4;

                if (weightBits + extraBits > 128)
                {
                    pixels.Fill(0xFFFF00FFu);
                    return;
                }

                int at = weightEnd - extraBits;
                Span<uint> classes = stackalloc uint[4];
                Span<uint> offsets = stackalloc uint[4];

                cem >>= 2;
                for (int i = 0; i < partitions; i++, cem >>= 1)
                    classes[i] = cem & 1;

                switch (partitions)
                {
                    case 2:
                        offsets[0] = cem & 3;
                        offsets[1] = block.Next(ref at, 2);
                        break;

                    case 3:
                        offsets[0] = (cem & 1) | (block.Next(ref at, 1) << 1);
                        offsets[1] = block.Next(ref at, 2);
                        offsets[2] = block.Next(ref at, 2);
                        break;

                    default:
                        for (int i = 0; i < 4; i++)
                            offsets[i] = block.Next(ref at, 2);

                        break;
                }

                for (int i = 0; i < partitions; i++)
                    modes[i] = (int)(first + (classes[i] * 4) + offsets[i]);
            }
        }

        int selector = 0;
        if (config.DualPlane)
        {
            extraBits += 2;
            if (extraBits > weightEnd)
            {
                pixels.Fill(0xFFFF00FFu);
                return;
            }

            selector = (int)block.Get(weightEnd - extraBits, 2);
        }

        int configBits = 13 + (partitions == 1 ? 4 : 16) + extraBits;
        int remaining = 128 - configBits - weightBits;

        int endpointValues = 0;
        for (int i = 0; i < partitions; i++)
            endpointValues += 2 + (2 * (modes[i] >> 2));

        if (remaining < 0 || endpointValues > 18)
        {
            pixels.Fill(0xFFFF00FFu);
            return;
        }

        // The endpoints take whatever precision is left once the weights have had their share.
        int endpointRange = -1;
        for (int k = 20; k > 0; k--)
        {
            if (SequenceBits(endpointValues, k) <= remaining)
            {
                endpointRange = k;
                break;
            }
        }

        if (endpointRange < 4)
        {
            pixels.Fill(0xFFFF00FFu);
            return;
        }

        Span<byte> rawEndpoints = stackalloc byte[18];
        Span<byte> rawWeights = stackalloc byte[64];

        ReadSequence(block, endpointRange, rawEndpoints[..endpointValues], configBits - extraBits);
        ReadSequence(block.Reversed(), config.WeightRange, rawWeights[..gridValues], 0);

        Span<byte> weights = stackalloc byte[2 * 144];
        Span<byte> grid = stackalloc byte[2 * 144];

        for (int i = 0; i < gridValues; i++)
        {
            int plane = config.DualPlane ? i & 1 : 0;
            int index = config.DualPlane ? i >> 1 : i;
            grid[(plane * 144) + index] = (byte)DequantiseWeight(rawWeights[i], config.WeightRange);
        }

        Upsample(width, height, config.GridWidth, config.GridHeight, grid, weights);
        if (config.DualPlane)
            Upsample(width, height, config.GridWidth, config.GridHeight, grid[144..], weights[144..]);

        Span<int> endpoints = stackalloc int[4 * 4 * 2];
        Span<byte> values = stackalloc byte[8];
        int taken = 0;

        for (int subset = 0; subset < partitions; subset++)
        {
            values.Clear();
            int count = 2 + (2 * (modes[subset] >> 2));

            for (int i = 0; i < count; i++)
                values[i] = (byte)DequantiseEndpoint(rawEndpoints[taken + i], endpointRange);

            if (!DecodeEndpoints(modes[subset], values, endpoints[(subset * 8)..]))
            {
                pixels.Fill(0xFFFF00FFu);
                return;
            }

            taken += count;
        }

        bool small = width * height < 31;
        int component = config.DualPlane ? selector : -1;
        Span<int> channels = stackalloc int[4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = (y * width) + x;
                int subset = partitions > 1 ? Partition(partitionId, x, y, partitions, small) : 0;

                for (int c = 0; c < 4; c++)
                {
                    int weight = weights[(c == component ? 144 : 0) + at];

                    // Widened to sixteen bits, mixed there and only then narrowed. Doing it at
                    // eight sends a value between two steps the wrong way, once in a hundred.
                    int low = endpoints[(subset * 8) + (c * 2)] * 0x101;
                    int high = endpoints[(subset * 8) + (c * 2) + 1] * 0x101;
                    int wide = ((low * (MaxWeight - weight)) + (high * weight) + 32) >> 6;

                    // The format normalises by a power of two rather than the largest sample,
                    // and a result between two steps takes the lower.
                    channels[c] = ((wide * 255) + 32767) >> 16;
                }

                pixels[at] = ((uint)channels[3] << 24) | ((uint)channels[0] << 16)
                    | ((uint)channels[1] << 8) | (uint)channels[2];
            }
        }
    }

    /// <summary>
    /// Reads the first eleven bits, which say how big the weight grid is, how finely the weights
    /// are quantised and whether one channel gets a grid of its own.
    /// </summary>
    private static bool TryConfig(Block block, out Config config)
    {
        config = default;

        if (block.Get(0, 4) == 0)
            return false;

        if (block.Get(0, 2) == 0 && block.Get(6, 3) == 0b111 && block.Get(2, 4) != 0b1111)
            return false;

        if (block.Get(0, 9) == 0b111111100)
            return TryVoidExtent(block, ref config);

        uint low2 = block.Get(0, 2);
        uint next2 = block.Get(2, 2);
        uint five4 = block.Get(5, 4);
        uint eight1 = block.Get(8, 1);
        uint seven2 = block.Get(7, 2);

        int row = -1;

        if (low2 == 0)
        {
            // Two shapes are told apart by a wider field, so that test comes first.
            if (seven2 == 0b00)
                row = 5;
            else if (seven2 == 0b01)
                row = 6;
            else if (five4 == 0b1100)
                row = 7;
            else if (five4 == 0b1101)
                row = 8;
            else if (seven2 == 0b10)
                row = 9;
        }
        else
        {
            row = next2 switch
            {
                0b00 => 0,
                0b01 => 1,
                0b10 => 2,
                _ => eight1 == 0 ? 3 : 4,
            };
        }

        if (row < 0)
            return false;

        ReadOnlySpan<sbyte> table = AstcTables.BlockModes;
        int from = row * 11;

        sbyte dualOffset = table[from];
        sbyte precisionOffset = table[from + 1];
        sbyte widthOffset = table[from + 2];
        sbyte widthSize = table[from + 3];
        sbyte heightOffset = table[from + 4];
        sbyte heightSize = table[from + 5];

        int gridWidth = table[from + 6];
        int gridHeight = table[from + 7];

        bool precision = precisionOffset >= 0 && block.Get(precisionOffset, 1) != 0;
        config.DualPlane = dualOffset >= 0 && block.Get(dualOffset, 1) != 0;

        if (widthSize != 0)
            gridWidth += (int)block.Get(widthOffset, widthSize);

        if (heightSize != 0)
            gridHeight += (int)block.Get(heightOffset, heightSize);

        int packed = (int)block.Get(table[from + 8], 1)
            | ((int)block.Get(table[from + 9], 1) << 1)
            | ((int)block.Get(table[from + 10], 1) << 2);

        if (packed < 2)
            return false;

        config.GridWidth = gridWidth;
        config.GridHeight = gridHeight;
        config.WeightRange = packed - 2 + (precision ? 6 : 0);
        return true;
    }

    /// <summary>A block naming one colour for the whole of itself, and how far that colour reaches.</summary>
    private static bool TryVoidExtent(Block block, ref Config config)
    {
        if (block.Get(10, 2) != 0b11)
            return false;

        // A different reading of the same sixteen bits, so it is refused rather than guessed.
        if (block.Get(9, 1) != 0)
            return false;

        config.Solid = true;
        config.SolidRed = block.Get(64, 16);
        config.SolidGreen = block.Get(80, 16);
        config.SolidBlue = block.Get(96, 16);
        config.SolidAlpha = block.Get(112, 16);
        return true;
    }

    /// <summary>Bits a run of values of one range occupies, which is not a whole number each.</summary>
    private static int SequenceBits(int count, int range)
    {
        ReadOnlySpan<sbyte> table = AstcTables.Ranges;
        int from = range * 3;

        int total = table[from] * count;
        total += ((table[from + 1] * 8 * count) + 4) / 5;
        total += ((table[from + 2] * 7 * count) + 2) / 3;
        return total;
    }

    /// <summary>
    /// Reads a run of values whose range is not a power of two. Five base three digits share
    /// eight bits and three base five digits share seven, which is what saves the format from
    /// rounding every range up to the next power of two.
    /// </summary>
    private static void ReadSequence(Block block, int range, Span<byte> values, int at)
    {
        ReadOnlySpan<sbyte> table = AstcTables.Ranges;
        int bits = table[range * 3];

        if (table[(range * 3) + 1] != 0)
        {
            ReadOnlySpan<byte> widths = [2, 2, 1, 2, 1];
            Span<uint> low = stackalloc uint[5];

            for (int start = 0; start < values.Length; start += 5)
            {
                int count = Math.Min(values.Length - start, 5);
                low.Clear();
                uint packed = 0;

                for (int i = 0, shift = 0; i < count; i++)
                {
                    if (bits != 0)
                        low[i] = block.Next(ref at, bits);

                    packed |= block.Next(ref at, widths[i]) << shift;
                    shift += widths[i];
                }

                for (int i = 0; i < count; i++)
                    values[start + i] = (byte)(((uint)AstcTables.Trits[(int)((packed * 5) + i)] << bits) | low[i]);
            }

            return;
        }

        if (table[(range * 3) + 2] != 0)
        {
            ReadOnlySpan<byte> widths = [3, 2, 2];
            Span<uint> low = stackalloc uint[3];

            for (int start = 0; start < values.Length; start += 3)
            {
                int count = Math.Min(values.Length - start, 3);
                low.Clear();
                uint packed = 0;

                for (int i = 0, shift = 0; i < count; i++)
                {
                    if (bits != 0)
                        low[i] = block.Next(ref at, bits);

                    packed |= block.Next(ref at, widths[i]) << shift;
                    shift += widths[i];
                }

                for (int i = 0; i < count; i++)
                    values[start + i] = (byte)(((uint)AstcTables.Quints[(int)((packed * 3) + i)] << bits) | low[i]);
            }

            return;
        }

        for (int i = 0; i < values.Length; i++)
            values[i] = (byte)block.Next(ref at, bits);
    }

    /// <summary>Repeats a value's own bits to fill a wider field, which is exact at both ends.</summary>
    private static uint Replicate(uint value, int from, int to)
    {
        uint result = 0;
        for (int shift = to - from; shift > -from; shift -= from)
            result |= shift >= 0 ? value << shift : value >> -shift;

        return result;
    }

    /// <summary>
    /// Expands a stored weight to the nought to sixty four scale the interpolation runs on. The
    /// unquantisation lands on nought to sixty three, and the step above the halfway point gains
    /// one so that the top of the range reaches all the way.
    /// </summary>
    private static uint DequantiseWeight(uint value, int range)
    {
        uint weight = UnquantiseWeight(value, range);
        return weight > 32 ? weight + 1 : weight;
    }

    private static uint UnquantiseWeight(uint value, int range)
    {
        switch (range)
        {
            case 0:
                return value != 0 ? 63u : 0u;

            case 1:
                return value switch { 0 => 0u, 1 => 32u, _ => 63u };

            case 2:
                return Replicate(value, 2, 6);

            case 3:
                return value switch { 0 => 0u, 1 => 16u, 2 => 32u, 3 => 47u, _ => 63u };

            case 5:
                return Replicate(value, 3, 6);

            case 8:
                return Replicate(value, 4, 6);

            case 11:
                return Replicate(value, 5, 6);
        }

        ReadOnlySpan<sbyte> table = AstcTables.Ranges;
        int bits = table[range * 3];
        bool quints = table[(range * 3) + 2] != 0;

        int index = (bits * 2) + (quints ? 1 : 0);
        uint low = value & ((1u << bits) - 1);
        uint digit = value >> bits;

        uint a = (low & 1) != 0 ? 0x7Fu : 0u;
        uint b = 0;
        uint second = (low >> 1) & 1;
        uint third = (low >> 2) & 1;

        if (index == 4)
            b = (second << 6) | (second << 2) | second;
        else if (index == 5)
            b = (second << 6) | (second << 1);
        else if (index == 6)
            b = (third << 6) | (second << 5) | (third << 1) | second;

        ReadOnlySpan<byte> constants = [50, 28, 23, 13, 11];
        uint result = (digit * constants[index - 2]) + b;
        result ^= a;
        return (a & 0x20) | (result >> 2);
    }

    private static uint DequantiseEndpoint(uint value, int range)
    {
        switch (range)
        {
            case 5:
                return Replicate(value, 3, 8);

            case 8:
                return Replicate(value, 4, 8);

            case 11:
                return Replicate(value, 5, 8);

            case 14:
                return Replicate(value, 6, 8);

            case 17:
                return Replicate(value, 7, 8);

            case 20:
                return value;
        }

        ReadOnlySpan<sbyte> table = AstcTables.Ranges;
        int bits = table[range * 3];
        bool quints = table[(range * 3) + 2] != 0;

        int index = ((bits * 2) + (quints ? 1 : 0)) - 2;
        uint low = value & ((1u << bits) - 1);
        uint digit = value >> bits;

        uint a = (low & 1) != 0 ? 511u : 0u;
        uint b0 = (low >> 1) & 1;
        uint c0 = (low >> 2) & 1;
        uint d0 = (low >> 3) & 1;
        uint e0 = (low >> 4) & 1;
        uint f0 = (low >> 5) & 1;

        uint b = index switch
        {
            2 => (b0 << 1) | (b0 << 2) | (b0 << 4) | (b0 << 8),
            3 => (b0 << 2) | (b0 << 3) | (b0 << 8),
            4 => b0 | (c0 << 1) | (b0 << 2) | (c0 << 3) | (b0 << 7) | (c0 << 8),
            5 => c0 | (b0 << 1) | (c0 << 2) | (b0 << 7) | (c0 << 8),
            6 => b0 | (c0 << 1) | (d0 << 2) | (b0 << 6) | (c0 << 7) | (d0 << 8),
            7 => c0 | (d0 << 1) | (b0 << 6) | (c0 << 7) | (d0 << 8),
            8 => d0 | (e0 << 1) | (b0 << 5) | (c0 << 6) | (d0 << 7) | (e0 << 8),
            9 => e0 | (b0 << 5) | (c0 << 6) | (d0 << 7) | (e0 << 8),
            10 => f0 | (b0 << 4) | (c0 << 5) | (d0 << 6) | (e0 << 7) | (f0 << 8),
            _ => 0,
        };

        ReadOnlySpan<byte> constants = [204, 113, 93, 54, 44, 26, 22, 13, 11, 6, 5];
        uint result = (digit * constants[index]) + b;
        result ^= a;
        return (a & 0x80) | (result >> 2);
    }

    /// <summary>
    /// Spreads a coarse weight grid across the block. Each pixel takes a weighted blend of the
    /// four grid points around it, which is what lets a block store far fewer weights than it has
    /// pixels without the result showing steps.
    /// </summary>
    private static void Upsample(int width, int height, int gridWidth, int gridHeight,
                                 ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (gridWidth == width && gridHeight == height)
        {
            source[..(width * height)].CopyTo(destination);
            return;
        }

        int scaleX = (1024 + (width / 2)) / (width - 1);
        int scaleY = (1024 + (height / 2)) / (height - 1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int gx = ((scaleX * x * (gridWidth - 1)) + 32) >> 6;
                int gy = ((scaleY * y * (gridHeight - 1)) + 32) >> 6;

                int jx = gx >> 4;
                int jy = gy >> 4;
                int fx = gx & 0xF;
                int fy = gy & 0xF;

                int w11 = ((fx * fy) + 8) >> 4;
                int w10 = fy - w11;
                int w01 = fx - w11;
                int w00 = 16 - fx - fy + w11;

                // Never read, which keeps the blend inside the grid at the far edges.
                int total = 8;
                if (w00 != 0)
                    total += source[jx + (jy * gridWidth)] * w00;

                if (w01 != 0)
                    total += source[Math.Min(jx + 1, gridWidth - 1) + (jy * gridWidth)] * w01;

                if (w10 != 0)
                    total += source[jx + (Math.Min(jy + 1, gridHeight - 1) * gridWidth)] * w10;

                if (w11 != 0)
                {
                    total += source[Math.Min(jx + 1, gridWidth - 1) +
                                    (Math.Min(jy + 1, gridHeight - 1) * gridWidth)] * w11;
                }

                destination[x + (y * width)] = (byte)(total >> 4);
            }
        }
    }

    /// <summary>
    /// Which group a pixel belongs to. The shapes are not written down anywhere: they come out of
    /// a hash of the pattern number, which is what lets the format offer a thousand of them for
    /// the cost of ten bits.
    /// </summary>
    private static int Partition(int pattern, int x, int y, int partitions, bool small)
    {
        if (small)
        {
            x <<= 1;
            y <<= 1;
        }

        uint seed = (uint)(pattern + (1024 * (partitions - 1)));
        uint random = Hash(seed);

        Span<uint> seeds = stackalloc uint[12];
        seeds[0] = random & 0xF;
        seeds[1] = (random >> 4) & 0xF;
        seeds[2] = (random >> 8) & 0xF;
        seeds[3] = (random >> 12) & 0xF;
        seeds[4] = (random >> 16) & 0xF;
        seeds[5] = (random >> 20) & 0xF;
        seeds[6] = (random >> 24) & 0xF;
        seeds[7] = (random >> 28) & 0xF;
        seeds[8] = (random >> 18) & 0xF;
        seeds[9] = (random >> 22) & 0xF;
        seeds[10] = (random >> 26) & 0xF;
        seeds[11] = ((random >> 30) | (random << 2)) & 0xF;

        for (int i = 0; i < 12; i++)
            seeds[i] = (byte)(seeds[i] * seeds[i]);

        int shiftA = (seed & 2) != 0 ? 4 : 5;
        int shiftB = partitions == 3 ? 6 : 5;
        int shift1 = (seed & 1) != 0 ? shiftA : shiftB;
        int shift2 = (seed & 1) != 0 ? shiftB : shiftA;
        int shift3 = (seed & 0x10) != 0 ? shift1 : shift2;

        seeds[0] >>= shift1;
        seeds[1] >>= shift2;
        seeds[2] >>= shift1;
        seeds[3] >>= shift2;
        seeds[4] >>= shift1;
        seeds[5] >>= shift2;
        seeds[6] >>= shift1;
        seeds[7] >>= shift2;
        seeds[8] >>= shift3;
        seeds[9] >>= shift3;
        seeds[10] >>= shift3;
        seeds[11] >>= shift3;

        int a = (int)(0x3F & ((seeds[0] * (uint)x) + (seeds[1] * (uint)y) + (random >> 14)));
        int b = (int)(0x3F & ((seeds[2] * (uint)x) + (seeds[3] * (uint)y) + (random >> 10)));
        int c = partitions >= 3 ? (int)(0x3F & ((seeds[4] * (uint)x) + (seeds[5] * (uint)y) + (random >> 6))) : 0;
        int d = partitions >= 4 ? (int)(0x3F & ((seeds[6] * (uint)x) + (seeds[7] * (uint)y) + (random >> 2))) : 0;

        return a >= b && a >= c && a >= d ? 0
            : b >= c && b >= d ? 1
            : c >= d ? 2
            : 3;
    }

    private static uint Hash(uint value)
    {
        uint p = value;
        p ^= p >> 15;
        p -= p << 17;
        p += p << 7;
        p += p << 4;
        p ^= p >> 5;
        p += p << 16;
        p ^= p >> 7;
        p ^= p >> 3;
        p ^= p << 6;
        p ^= p >> 17;
        return p;
    }

    private static int Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

    /// <summary>Halves the distance of red and green from blue, which undoes a coding trick.</summary>
    private static void Contract(int r, int g, int b, int a, Span<int> to, int at)
    {
        to[at] = (r + b) >> 1;
        to[at + 2] = (g + b) >> 1;
        to[at + 4] = b;
        to[at + 6] = a;
    }

    /// <summary>Moves a bit from one value into another, which is how a signed offset is stored.</summary>
    private static void Transfer(ref int a, ref int b)
    {
        b >>= 1;
        b |= a & 0x80;
        a >>= 1;
        a &= 0x3F;

        if ((a & 0x20) != 0)
            a -= 0x40;
    }

    /// <summary>
    /// Turns a group's stored values into its pair of endpoint colours. There are eight ways to
    /// do this for pictures in the ordinary range, trading how many values are stored against how
    /// much of the colour they describe.
    /// </summary>
    private static bool DecodeEndpoints(int mode, Span<byte> values, Span<int> to)
    {
        int v0 = values[0];
        int v1 = values[1];
        int v2 = values[2];
        int v3 = values[3];
        int v4 = values[4];
        int v5 = values[5];
        int v6 = values[6];
        int v7 = values[7];

        switch (mode)
        {
            case 0:
                Set(to, v0, v0, v0, 0xFF, v1, v1, v1, 0xFF);
                return true;

            case 1:
            {
                int low = (v0 >> 2) | (v1 & 0xC0);
                int high = Math.Min(low + (v1 & 0x3F), 0xFF);
                Set(to, low, low, low, 0xFF, high, high, high, 0xFF);
                return true;
            }

            case 4:
                Set(to, v0, v0, v0, v2, v1, v1, v1, v3);
                return true;

            case 5:
                Transfer(ref v1, ref v0);
                Transfer(ref v3, ref v2);
                Set(to, v0, v0, v0, v2, v0 + v1, v0 + v1, v0 + v1, v2 + v3);
                ClampAll(to);
                return true;

            case 6:
                Set(to, (v0 * v3) >> 8, (v1 * v3) >> 8, (v2 * v3) >> 8, 0xFF, v0, v1, v2, 0xFF);
                return true;

            case 8:
                if (v1 + v3 + v5 >= v0 + v2 + v4)
                    Set(to, v0, v2, v4, 0xFF, v1, v3, v5, 0xFF);
                else
                {
                    Contract(v1, v3, v5, 0xFF, to, 0);
                    Contract(v0, v2, v4, 0xFF, to, 1);
                }

                return true;

            case 9:
                Transfer(ref v1, ref v0);
                Transfer(ref v3, ref v2);
                Transfer(ref v5, ref v4);

                if (v1 + v3 + v5 >= 0)
                    Set(to, v0, v2, v4, 0xFF, v0 + v1, v2 + v3, v4 + v5, 0xFF);
                else
                {
                    Contract(v0 + v1, v2 + v3, v4 + v5, 0xFF, to, 0);
                    Contract(v0, v2, v4, 0xFF, to, 1);
                }

                ClampAll(to);
                return true;

            case 10:
                Set(to, (v0 * v3) >> 8, (v1 * v3) >> 8, (v2 * v3) >> 8, v4, v0, v1, v2, v5);
                return true;

            case 12:
                if (v1 + v3 + v5 >= v0 + v2 + v4)
                    Set(to, v0, v2, v4, v6, v1, v3, v5, v7);
                else
                {
                    Contract(v1, v3, v5, v7, to, 0);
                    Contract(v0, v2, v4, v6, to, 1);
                }

                return true;

            case 13:
                Transfer(ref v1, ref v0);
                Transfer(ref v3, ref v2);
                Transfer(ref v5, ref v4);
                Transfer(ref v7, ref v6);

                if (v1 + v3 + v5 >= 0)
                    Set(to, v0, v2, v4, v6, v0 + v1, v2 + v3, v4 + v5, v6 + v7);
                else
                {
                    Contract(v0 + v1, v2 + v3, v4 + v5, v6 + v7, to, 0);
                    Contract(v0, v2, v4, v6, to, 1);
                }

                ClampAll(to);
                return true;

            default:
                // The remaining modes describe values past one, which this reader does not show.
                return false;
        }
    }

    private static void Set(Span<int> to, int r0, int g0, int b0, int a0, int r1, int g1, int b1, int a1)
    {
        to[0] = r0;
        to[1] = r1;
        to[2] = g0;
        to[3] = g1;
        to[4] = b0;
        to[5] = b1;
        to[6] = a0;
        to[7] = a1;
    }

    private static void ClampAll(Span<int> to)
    {
        for (int i = 0; i < 8; i++)
            to[i] = Clamp(to[i]);
    }
}
