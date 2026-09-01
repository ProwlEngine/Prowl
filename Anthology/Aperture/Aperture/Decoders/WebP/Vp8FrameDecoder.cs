// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>The three planes a decoded lossy frame comes back as.</summary>
internal sealed class Vp8Frame
{
    public int Width;
    public int Height;
    public int LumaStride;
    public int ChromaStride;
    public byte[] Luma = [];
    public byte[] ChromaU = [];
    public byte[] ChromaV = [];
}

/// <summary>
/// The lossy form, which is a still frame of a video codec. Each sixteen by sixteen block names
/// how to guess itself from its neighbours and carries the difference as quantised coefficients.
/// A still picture has nothing to move from, so what remains is prediction, a transform and the
/// filter that hides the seams.
/// </summary>
internal sealed class Vp8FrameDecoder
{
    private const int Stride = Vp8Predict.Stride;

    private const int LumaOffset = Stride + 8;
    private const int ChromaUOffset = LumaOffset + (Stride * 16) + Stride;
    private const int ChromaVOffset = ChromaUOffset + 16;
    private const int ScratchSize = (Stride * 17) + (Stride * 9);

    private const int Segments = 4;

    /// <summary>Where each of the sixteen luma blocks sits inside the scratch block.</summary>
    private static ReadOnlySpan<int> Scan =>
    [
        0, 4, 8, 12,
        4 * Stride, 4 + (4 * Stride), 8 + (4 * Stride), 12 + (4 * Stride),
        8 * Stride, 4 + (8 * Stride), 8 + (8 * Stride), 12 + (8 * Stride),
        12 * Stride, 4 + (12 * Stride), 8 + (12 * Stride), 12 + (12 * Stride),
    ];

    private sealed class Quantiser
    {
        public readonly int[] Luma = new int[2];
        public readonly int[] Luma2 = new int[2];
        public readonly int[] Chroma = new int[2];
    }

    private sealed class Strength
    {
        public int Limit;
        public int Inner;
        public int HighEdge;
        public bool FilterInner;
    }

    /// <summary>What one block carries: how it is guessed and what correction it holds.</summary>
    private sealed class Block
    {
        public int Segment;
        public bool Skip;
        public bool Is4x4;
        public int ChromaMode;
        public int LumaMode;
        public readonly byte[] Modes = new byte[16];
        public readonly short[] Coefficients = new short[384];
        public uint NonZeroLuma;
        public uint NonZeroChroma;
    }

    private int _across;
    private int _down;
    private byte[] _probabilities = [];
    private Quantiser[] _quantisers = [];
    private Strength[,] _strengths = new Strength[0, 0];
    private int _filterType;
    private bool _useSkip;
    private int _skipProbability;
    private bool _updateMap;
    private byte[] _segmentProbabilities = [255, 255, 255];

    /// <summary>Non zero flags for the row above and the column left, one nibble a plane.</summary>
    private byte[] _topNonZero = [];
    private byte _leftNonZero;
    private byte[] _topNonZeroDc = [];
    private byte _leftNonZeroDc;

    /// <summary>The four by four modes along the top edge and the left edge.</summary>
    private byte[] _topModes = [];
    private readonly byte[] _leftModes = new byte[4];

    /// <summary>The bottom row of samples of each block above, which the next row predicts from.</summary>
    private byte[] _topLuma = [];
    private byte[] _topChromaU = [];
    private byte[] _topChromaV = [];

    private byte[] _scratch = [];
    private Vp8Frame _frame = new();

    public static bool TryDecode(ReadOnlySpan<byte> data, out Vp8Frame? frame) =>
        new Vp8FrameDecoder().TryRun(data, out frame);

    private bool TryRun(ReadOnlySpan<byte> data, out Vp8Frame? frame)
    {
        frame = null;

        if (data.Length < 10)
            return false;

        uint tag = (uint)(data[0] | (data[1] << 8) | (data[2] << 16));
        if ((tag & 1) != 0 || ((tag >> 4) & 1) == 0 || ((tag >> 1) & 7) > 3)
            return false;

        int firstPartition = (int)(tag >> 5);

        if (data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
            return false;

        int width = (data[6] | (data[7] << 8)) & 0x3FFF;
        int height = (data[8] | (data[9] << 8)) & 0x3FFF;

        if (width <= 0 || height <= 0)
            return false;

        byte[] rest = data[10..].ToArray();
        if (firstPartition > rest.Length)
            return false;

        Vp8BoolDecoder header = new(rest, 0, firstPartition);

        // Colour space and clamping, neither of which changes how the pixels are read.
        header.ReadFlag();
        header.ReadFlag();

        bool useSegment = header.ReadFlag() != 0;
        bool absoluteDelta = false;
        int[] segmentQuantiser = new int[Segments];
        int[] segmentFilter = new int[Segments];

        if (useSegment)
        {
            _updateMap = header.ReadFlag() != 0;
            if (header.ReadFlag() != 0)
            {
                absoluteDelta = header.ReadFlag() != 0;
                for (int s = 0; s < Segments; s++)
                    segmentQuantiser[s] = header.ReadFlag() != 0 ? header.ReadSignedValue(7) : 0;

                for (int s = 0; s < Segments; s++)
                    segmentFilter[s] = header.ReadFlag() != 0 ? header.ReadSignedValue(6) : 0;
            }

            if (_updateMap)
            {
                for (int s = 0; s < 3; s++)
                    _segmentProbabilities[s] = header.ReadFlag() != 0 ? (byte)header.ReadValue(8) : (byte)255;
            }
        }

        bool simpleFilter = header.ReadFlag() != 0;
        int filterLevel = header.ReadValue(6);
        int sharpness = header.ReadValue(3);

        int[] referenceDelta = new int[4];
        int[] modeDelta = new int[4];
        bool useDelta = header.ReadFlag() != 0;

        if (useDelta && header.ReadFlag() != 0)
        {
            for (int i = 0; i < 4; i++)
            {
                if (header.ReadFlag() != 0)
                    referenceDelta[i] = header.ReadSignedValue(6);
            }

            for (int i = 0; i < 4; i++)
            {
                if (header.ReadFlag() != 0)
                    modeDelta[i] = header.ReadSignedValue(6);
            }
        }

        _filterType = filterLevel == 0 ? 0 : simpleFilter ? 1 : 2;

        int partitions = 1 << header.ReadValue(2);
        Vp8BoolDecoder[]? tokens = SplitPartitions(rest, firstPartition, partitions);
        if (tokens is null)
            return false;

        _quantisers = ReadQuantisers(header, useSegment, absoluteDelta, segmentQuantiser);


        // The refresh flag belongs to the inter frame machinery a still picture does not use.
        header.ReadFlag();

        _probabilities = ReadProbabilities(header);

        _across = (width + 15) >> 4;
        _down = (height + 15) >> 4;
        _strengths = ComputeStrengths(useSegment, absoluteDelta, segmentFilter, filterLevel,
                                      sharpness, useDelta, referenceDelta, modeDelta);

        _frame = new Vp8Frame
        {
            Width = width,
            Height = height,
            LumaStride = _across * 16,
            ChromaStride = _across * 8,
            Luma = new byte[_across * 16 * _down * 16],
            ChromaU = new byte[_across * 8 * _down * 8],
            ChromaV = new byte[_across * 8 * _down * 8],
        };

        _topNonZero = new byte[_across + 1];
        _topNonZeroDc = new byte[_across + 1];
        _topModes = new byte[4 * _across];
        _topLuma = new byte[16 * _across];
        _topChromaU = new byte[8 * _across];
        _topChromaV = new byte[8 * _across];
        _scratch = new byte[ScratchSize];

        Block block = new();
        Strength[] rowStrengths = new Strength[_across];
        bool[] rowInner = new bool[_across];

        for (int y = 0; y < _down; y++)
        {
            _leftNonZero = 0;
            _leftNonZeroDc = 0;
            Array.Clear(_leftModes);

            Vp8BoolDecoder token = tokens[y & (partitions - 1)];

            PrepareRow(y);

            for (int x = 0; x < _across; x++)
            {
                ReadModes(header, block, x);

                bool empty = block.Skip;

                if (block.Skip)
                {
                    Array.Clear(block.Coefficients);
                    block.NonZeroLuma = 0;
                    block.NonZeroChroma = 0;

                    if (!block.Is4x4)
                    {
                        _leftNonZeroDc = 0;
                        _topNonZeroDc[x] = 0;
                    }

                    _leftNonZero = 0;
                    _topNonZero[x] = 0;
                }
                else
                {
                    if (!ReadResiduals(token, block, x))
                        return false;

                    empty = (block.NonZeroLuma | block.NonZeroChroma) == 0;
                }

                Reconstruct(block, x, y);

                // A block carrying any correction at all has its inner edges softened too, not
                // only one split into four by four pieces.
                rowStrengths[x] = _strengths[block.Segment, block.Is4x4 ? 1 : 0];
                rowInner[x] = block.Is4x4 || !empty;
            }

            if (_filterType > 0)
                FilterRow(y, rowStrengths, rowInner);
        }

        frame = _frame;
        return true;
    }

    private Vp8BoolDecoder[]? SplitPartitions(byte[] data, int from, int count)
    {
        int at = from;
        int available = data.Length - from;
        int last = count - 1;

        if (available < 3 * last)
            return null;

        Vp8BoolDecoder[] result = new Vp8BoolDecoder[count];
        int start = at + (last * 3);
        int left = available - (last * 3);

        for (int p = 0; p < last; p++)
        {
            int size = data[at + (p * 3)] | (data[at + (p * 3) + 1] << 8) | (data[at + (p * 3) + 2] << 16);
            if (size > left)
                size = left;

            result[p] = new Vp8BoolDecoder(data, start, size);
            start += size;
            left -= size;
        }

        result[last] = new Vp8BoolDecoder(data, start, left);
        return result;
    }

    private static Quantiser[] ReadQuantisers(Vp8BoolDecoder header, bool useSegment,
                                              bool absoluteDelta, int[] segmentQuantiser)
    {
        int baseIndex = header.ReadValue(7);
        int lumaDc = header.ReadFlag() != 0 ? header.ReadSignedValue(4) : 0;
        int luma2Dc = header.ReadFlag() != 0 ? header.ReadSignedValue(4) : 0;
        int luma2Ac = header.ReadFlag() != 0 ? header.ReadSignedValue(4) : 0;
        int chromaDc = header.ReadFlag() != 0 ? header.ReadSignedValue(4) : 0;
        int chromaAc = header.ReadFlag() != 0 ? header.ReadSignedValue(4) : 0;

        Quantiser[] result = new Quantiser[Segments];

        for (int s = 0; s < Segments; s++)
        {
            int q;
            if (useSegment)
            {
                q = segmentQuantiser[s];
                if (!absoluteDelta)
                    q += baseIndex;
            }
            else if (s > 0)
            {
                result[s] = result[0];
                continue;
            }
            else
            {
                q = baseIndex;
            }

            Quantiser quantiser = new();
            result[s] = quantiser;

            quantiser.Luma[0] = Vp8Tables.DcQuantiser[Clamp(q + lumaDc, 127)];
            quantiser.Luma[1] = Vp8Tables.AcQuantiser[Clamp(q, 127)];

            quantiser.Luma2[0] = Vp8Tables.DcQuantiser[Clamp(q + luma2Dc, 127)] * 2;

            // The second transform's steps are one and a half times the first's, written as a
            // multiplication and a shift so that every decoder rounds it the same way.
            quantiser.Luma2[1] = (Vp8Tables.AcQuantiser[Clamp(q + luma2Ac, 127)] * 101581) >> 16;
            if (quantiser.Luma2[1] < 8)
                quantiser.Luma2[1] = 8;

            quantiser.Chroma[0] = Vp8Tables.DcQuantiser[Clamp(q + chromaDc, 117)];
            quantiser.Chroma[1] = Vp8Tables.AcQuantiser[Clamp(q + chromaAc, 127)];
        }

        return result;
    }

    private static int Clamp(int value, int top) => value < 0 ? 0 : value > top ? top : value;

    /// <summary>
    /// Reads which of the coefficient probabilities this frame replaces. Most frames replace very
    /// few, so each is guarded by a flag whose own probability the format fixes.
    /// </summary>
    private byte[] ReadProbabilities(Vp8BoolDecoder header)
    {
        byte[] result = new byte[4 * 8 * 3 * 11];
        ReadOnlySpan<byte> initial = Vp8Tables.CoefficientProbabilities;
        ReadOnlySpan<byte> update = Vp8Tables.CoefficientUpdateProbabilities;

        for (int i = 0; i < result.Length; i++)
            result[i] = header.Read(update[i]) != 0 ? (byte)header.ReadValue(8) : initial[i];

        _useSkip = header.ReadFlag() != 0;
        _skipProbability = _useSkip ? header.ReadValue(8) : 0;
        return result;
    }

    private Strength[,] ComputeStrengths(bool useSegment, bool absoluteDelta, int[] segmentFilter,
                                         int level, int sharpness, bool useDelta,
                                         int[] referenceDelta, int[] modeDelta)
    {
        Strength[,] result = new Strength[Segments, 2];

        for (int s = 0; s < Segments; s++)
        {
            int baseLevel = useSegment
                ? segmentFilter[s] + (absoluteDelta ? 0 : level)
                : level;

            for (int inner = 0; inner <= 1; inner++)
            {
                Strength strength = new() { FilterInner = inner != 0 };
                result[s, inner] = strength;

                if (_filterType == 0)
                    continue;

                int current = baseLevel;
                if (useDelta)
                {
                    current += referenceDelta[0];
                    if (inner != 0)
                        current += modeDelta[0];
                }

                current = current < 0 ? 0 : current > 63 ? 63 : current;
                if (current == 0)
                    continue;

                int innerLevel = current;
                if (sharpness > 0)
                {
                    innerLevel >>= sharpness > 4 ? 2 : 1;
                    if (innerLevel > 9 - sharpness)
                        innerLevel = 9 - sharpness;
                }

                if (innerLevel < 1)
                    innerLevel = 1;

                strength.Inner = innerLevel;
                strength.Limit = (2 * current) + innerLevel;
                strength.HighEdge = current >= 40 ? 2 : current >= 15 ? 1 : 0;
            }
        }

        return result;
    }

    /// <summary>
    /// Reads how one block is to be guessed: which segment it belongs to, whether it carries any
    /// correction at all, and the prediction mode for its luma and chroma.
    /// </summary>
    private void ReadModes(Vp8BoolDecoder header, Block block, int x)
    {
        block.Segment = _updateMap
            ? header.Read(_segmentProbabilities[0]) == 0
                ? header.Read(_segmentProbabilities[1])
                : header.Read(_segmentProbabilities[2]) + 2
            : 0;

        block.Skip = _useSkip && header.Read(_skipProbability) != 0;
        block.Is4x4 = header.Read(145) == 0;

        if (!block.Is4x4)
        {
            int mode = header.Read(156) != 0
                ? header.Read(128) != 0 ? 1 : 3
                : header.Read(163) != 0 ? 2 : 0;

            block.LumaMode = mode;
            block.Modes.AsSpan().Fill((byte)mode);
            _topModes.AsSpan(4 * x, 4).Fill((byte)mode);
            _leftModes.AsSpan().Fill((byte)mode);
        }
        else
        {
            ReadOnlySpan<byte> table = Vp8Tables.BlockModeProbabilities;

            for (int y = 0; y < 4; y++)
            {
                int mode = _leftModes[y];
                for (int i = 0; i < 4; i++)
                {
                    int from = ((_topModes[(4 * x) + i] * 10) + mode) * 9;
                    mode = ReadBlockMode(header, table, from);
                    _topModes[(4 * x) + i] = (byte)mode;
                    block.Modes[(y * 4) + i] = (byte)mode;
                }

                _leftModes[y] = (byte)mode;
            }
        }

        block.ChromaMode = header.Read(142) == 0 ? 0
            : header.Read(114) == 0 ? 2
            : header.Read(183) != 0 ? 1 : 3;
    }

    /// <summary>The ten way choice of how to guess a four by four block, as a tree of bits.</summary>
    private static int ReadBlockMode(Vp8BoolDecoder header, ReadOnlySpan<byte> table, int from)
    {
        if (header.Read(table[from]) == 0)
            return 0;

        if (header.Read(table[from + 1]) == 0)
            return 1;

        if (header.Read(table[from + 2]) == 0)
            return 2;

        if (header.Read(table[from + 3]) == 0)
        {
            if (header.Read(table[from + 4]) == 0)
                return 3;

            return header.Read(table[from + 5]) == 0 ? 4 : 5;
        }

        if (header.Read(table[from + 6]) == 0)
            return 6;

        if (header.Read(table[from + 7]) == 0)
            return 7;

        return header.Read(table[from + 8]) == 0 ? 8 : 9;
    }

    /// <summary>
    /// Reads the coefficients of every block inside one macroblock. Which probabilities apply
    /// depends on whether the blocks above and to the left had anything in them, which is what
    /// makes an empty area cost so little.
    /// </summary>
    private bool ReadResiduals(Vp8BoolDecoder token, Block block, int x)
    {
        Array.Clear(block.Coefficients);

        Quantiser quantiser = _quantisers[block.Segment];
        short[] coefficients = block.Coefficients;

        int first;
        int lumaType;
        uint nonZeroLuma = 0;
        uint nonZeroChroma = 0;

        if (!block.Is4x4)
        {
            Span<short> dc = stackalloc short[16];
            int context = _topNonZeroDc[x] + _leftNonZeroDc;
            int count = ReadCoefficients(token, 1, context, quantiser.Luma2, 0, dc);

            _topNonZeroDc[x] = _leftNonZeroDc = (byte)(count > 0 ? 1 : 0);

            if (count > 1)
            {
                Vp8Transform.Walsh(dc, coefficients, 0);
            }
            else
            {
                short value = (short)((dc[0] + 3) >> 3);
                for (int i = 0; i < 256; i += 16)
                    coefficients[i] = value;
            }

            first = 1;
            lumaType = 0;
        }
        else
        {
            first = 0;
            lumaType = 3;
        }

        int top = _topNonZero[x];
        int left = _leftNonZero;

        int topLuma = top & 0x0F;
        int leftLuma = left & 0x0F;

        for (int y = 0; y < 4; y++)
        {
            int l = leftLuma & 1;
            uint bits = 0;

            for (int i = 0; i < 4; i++)
            {
                int context = l + (topLuma & 1);
                int at = ((y * 4) + i) * 16;
                int count = ReadCoefficients(token, lumaType, context, quantiser.Luma, first,
                                             coefficients.AsSpan(at, 16));

                l = count > first ? 1 : 0;
                topLuma = (topLuma >> 1) | (l << 7);
                bits = Code(bits, count, coefficients[at] != 0);
            }

            topLuma >>= 4;
            leftLuma = (leftLuma >> 1) | (l << 7);
            nonZeroLuma = (nonZeroLuma << 8) | bits;
        }

        int outTop = topLuma;
        int outLeft = leftLuma >> 4;

        for (int plane = 0; plane < 4; plane += 2)
        {
            uint bits = 0;
            int topChroma = top >> (4 + plane);
            int leftChroma = left >> (4 + plane);

            for (int y = 0; y < 2; y++)
            {
                int l = leftChroma & 1;
                for (int i = 0; i < 2; i++)
                {
                    int context = l + (topChroma & 1);
                    int at = (16 + (plane * 2) + (y * 2) + i) * 16;
                    int count = ReadCoefficients(token, 2, context, quantiser.Chroma, 0,
                                                 coefficients.AsSpan(at, 16));

                    l = count > 0 ? 1 : 0;
                    topChroma = (topChroma >> 1) | (l << 3);
                    bits = Code(bits, count, coefficients[at] != 0);
                }

                topChroma >>= 2;
                leftChroma = (leftChroma >> 1) | (l << 5);
            }

            nonZeroChroma |= bits << (4 * plane);
            outTop |= (topChroma << 4) << plane;
            outLeft |= (leftChroma & 0xF0) << plane;
        }

        _topNonZero[x] = (byte)outTop;
        _leftNonZero = (byte)outLeft;

        block.NonZeroLuma = nonZeroLuma;
        block.NonZeroChroma = nonZeroChroma;
        return !token.Ended;
    }

    /// <summary>Two bits a block saying whether it has nothing, a flat value, or more.</summary>
    private static uint Code(uint bits, int count, bool flat)
    {
        bits <<= 2;
        bits |= count > 3 ? 3u : count > 1 ? 2u : flat ? 1u : 0u;
        return bits;
    }

    /// <summary>
    /// Reads one four by four block's coefficients, in the order that puts the coarse detail
    /// first, and stops as soon as the rest are known to be zero.
    /// </summary>
    private int ReadCoefficients(Vp8BoolDecoder token, int type, int context, int[] quantiser,
                                 int n, Span<short> output)
    {
        ReadOnlySpan<byte> bands = Vp8Tables.Bands;
        ReadOnlySpan<byte> zigzag = Vp8Tables.Zigzag;

        int at = Probabilities(type, bands[n], context);

        for (; n < 16; n++)
        {
            if (token.Read(_probabilities[at]) == 0)
                return n;

            while (token.Read(_probabilities[at + 1]) == 0)
            {
                if (++n == 16)
                    return 16;

                at = Probabilities(type, bands[n], 0);
            }

            int value;
            if (token.Read(_probabilities[at + 2]) == 0)
            {
                value = 1;
                at = Probabilities(type, bands[n + 1], 1);
            }
            else
            {
                value = ReadLargeValue(token, at);
                at = Probabilities(type, bands[n + 1], 2);
            }

            output[zigzag[n]] = (short)(token.ApplySign(value) * quantiser[n > 0 ? 1 : 0]);
        }

        return 16;
    }

    private static int Probabilities(int type, int band, int context) =>
        (((type * 8) + band) * 3 * 11) + (context * 11);

    /// <summary>
    /// Anything above one, which is coded as a range and then the bits inside it. The largest
    /// range needs eleven extra bits, each with its own probability.
    /// </summary>
    private int ReadLargeValue(Vp8BoolDecoder token, int at)
    {
        if (token.Read(_probabilities[at + 3]) == 0)
        {
            if (token.Read(_probabilities[at + 4]) == 0)
                return 2;

            return 3 + token.Read(_probabilities[at + 5]);
        }

        if (token.Read(_probabilities[at + 6]) == 0)
        {
            if (token.Read(_probabilities[at + 7]) == 0)
                return 5 + token.Read(159);

            int value = 7 + (2 * token.Read(165));
            return value + token.Read(145);
        }

        int high = token.Read(_probabilities[at + 8]);
        int low = token.Read(_probabilities[at + 9 + high]);
        int category = (2 * high) + low;

        ReadOnlySpan<byte> extra = category switch
        {
            0 => Vp8Tables.Category3,
            1 => Vp8Tables.Category4,
            2 => Vp8Tables.Category5,
            _ => Vp8Tables.Category6,
        };

        int result = 0;
        foreach (byte probability in extra)
            result += result + token.Read(probability);

        return result + 3 + (8 << category);
    }

    /// <summary>Sets up the samples above and to the left before a row of blocks is rebuilt.</summary>
    private void PrepareRow(int y)
    {
        Span<byte> scratch = _scratch;

        for (int j = 0; j < 16; j++)
            scratch[LumaOffset + (j * Stride) - 1] = 129;

        for (int j = 0; j < 8; j++)
        {
            scratch[ChromaUOffset + (j * Stride) - 1] = 129;
            scratch[ChromaVOffset + (j * Stride) - 1] = 129;
        }

        if (y > 0)
        {
            scratch[LumaOffset - Stride - 1] = 129;
            scratch[ChromaUOffset - Stride - 1] = 129;
            scratch[ChromaVOffset - Stride - 1] = 129;
            return;
        }

        // The topmost row has nothing above it, and the format says to read that as a mid grey
        // one shade below the left hand default so the two are told apart.
        scratch.Slice(LumaOffset - Stride - 1, 16 + 4 + 1).Fill(127);
        scratch.Slice(ChromaUOffset - Stride - 1, 8 + 1).Fill(127);
        scratch.Slice(ChromaVOffset - Stride - 1, 8 + 1).Fill(127);
    }

    /// <summary>Guesses one block, adds its correction, and copies the result into the picture.</summary>
    private void Reconstruct(Block block, int x, int y)
    {
        Span<byte> scratch = _scratch;

        // Carry the right hand column of the block just finished across to be this one's left.
        if (x > 0)
        {
            for (int j = -1; j < 16; j++)
            {
                scratch.Slice(LumaOffset + (j * Stride) + 12, 4)
                       .CopyTo(scratch[(LumaOffset + (j * Stride) - 4)..]);
            }

            for (int j = -1; j < 8; j++)
            {
                scratch.Slice(ChromaUOffset + (j * Stride) + 4, 4)
                       .CopyTo(scratch[(ChromaUOffset + (j * Stride) - 4)..]);
                scratch.Slice(ChromaVOffset + (j * Stride) + 4, 4)
                       .CopyTo(scratch[(ChromaVOffset + (j * Stride) - 4)..]);
            }
        }

        if (y > 0)
        {
            _topLuma.AsSpan(16 * x, 16).CopyTo(scratch[(LumaOffset - Stride)..]);
            _topChromaU.AsSpan(8 * x, 8).CopyTo(scratch[(ChromaUOffset - Stride)..]);
            _topChromaV.AsSpan(8 * x, 8).CopyTo(scratch[(ChromaVOffset - Stride)..]);
        }

        uint bits = block.NonZeroLuma;

        if (block.Is4x4)
        {
            // A four by four block may predict from the four samples past its top right corner,
            // which belong to the block after this one and are repeated where there is none.
            int topRight = LumaOffset - Stride + 16;

            if (y > 0)
            {
                if (x >= _across - 1)
                    scratch.Slice(topRight, 4).Fill(_topLuma[(16 * x) + 15]);
                else
                    _topLuma.AsSpan(16 * (x + 1), 4).CopyTo(scratch[topRight..]);
            }

            // The three blocks below it in the same column have no true top right either, so
            // the same four samples stand in for all of them.
            for (int j = 1; j <= 3; j++)
                scratch.Slice(topRight, 4).CopyTo(scratch[(topRight + (j * 4 * Stride))..]);

            for (int n = 0; n < 16; n++, bits <<= 2)
            {
                int at = LumaOffset + Scan[n];
                Vp8Predict.Luma4(scratch, at, block.Modes[n]);
                Transform(bits, block.Coefficients, n * 16, scratch, at);
            }
        }
        else
        {
            Vp8Predict.Luma16(scratch, LumaOffset, CheckMode(x, y, block.LumaMode));

            if (bits != 0)
            {
                for (int n = 0; n < 16; n++, bits <<= 2)
                    Transform(bits, block.Coefficients, n * 16, scratch, LumaOffset + Scan[n]);
            }
        }

        int chromaMode = CheckMode(x, y, block.ChromaMode);
        Vp8Predict.Chroma8(scratch, ChromaUOffset, chromaMode);
        Vp8Predict.Chroma8(scratch, ChromaVOffset, chromaMode);

        ChromaTransform(block.NonZeroChroma, block.Coefficients, 16 * 16, scratch, ChromaUOffset);
        ChromaTransform(block.NonZeroChroma >> 8, block.Coefficients, 20 * 16, scratch, ChromaVOffset);

        if (y < _down - 1)
        {
            scratch.Slice(LumaOffset + (15 * Stride), 16).CopyTo(_topLuma.AsSpan(16 * x));
            scratch.Slice(ChromaUOffset + (7 * Stride), 8).CopyTo(_topChromaU.AsSpan(8 * x));
            scratch.Slice(ChromaVOffset + (7 * Stride), 8).CopyTo(_topChromaV.AsSpan(8 * x));
        }

        for (int j = 0; j < 16; j++)
        {
            scratch.Slice(LumaOffset + (j * Stride), 16)
                   .CopyTo(_frame.Luma.AsSpan((((y * 16) + j) * _frame.LumaStride) + (x * 16)));
        }

        for (int j = 0; j < 8; j++)
        {
            int to = (((y * 8) + j) * _frame.ChromaStride) + (x * 8);
            scratch.Slice(ChromaUOffset + (j * Stride), 8).CopyTo(_frame.ChromaU.AsSpan(to));
            scratch.Slice(ChromaVOffset + (j * Stride), 8).CopyTo(_frame.ChromaV.AsSpan(to));
        }
    }

    /// <summary>A block on the top or left edge has fewer neighbours, so its guess is a weaker one.</summary>
    private static int CheckMode(int x, int y, int mode)
    {
        if (mode != 0)
            return mode;

        if (x == 0)
            return y == 0 ? 6 : 5;

        return y == 0 ? 4 : 0;
    }

    /// <summary>Picks the cheapest transform that can produce what the block actually holds.</summary>
    private static void Transform(uint bits, short[] coefficients, int from, Span<byte> block, int at)
    {
        switch (bits >> 30)
        {
            case 3:
                Vp8Transform.One(coefficients, from, block, at);
                break;

            case 2:
                Vp8Transform.Ac3(coefficients, from, block, at);
                break;

            case 1:
                Vp8Transform.Dc(coefficients, from, block, at);
                break;
        }
    }

    private static void ChromaTransform(uint bits, short[] coefficients, int from, Span<byte> block, int at)
    {
        if ((bits & 0xFF) == 0)
            return;

        if ((bits & 0xAA) != 0)
        {
            Vp8Transform.One(coefficients, from, block, at);
            Vp8Transform.One(coefficients, from + 16, block, at + 4);
            Vp8Transform.One(coefficients, from + 32, block, at + (4 * Stride));
            Vp8Transform.One(coefficients, from + 48, block, at + (4 * Stride) + 4);
            return;
        }

        if (coefficients[from] != 0)
            Vp8Transform.Dc(coefficients, from, block, at);

        if (coefficients[from + 16] != 0)
            Vp8Transform.Dc(coefficients, from + 16, block, at + 4);

        if (coefficients[from + 32] != 0)
            Vp8Transform.Dc(coefficients, from + 32, block, at + (4 * Stride));

        if (coefficients[from + 48] != 0)
            Vp8Transform.Dc(coefficients, from + 48, block, at + (4 * Stride) + 4);
    }

    /// <summary>Softens the seams along one row of blocks, once every block in it is rebuilt.</summary>
    private void FilterRow(int y, Strength[] strengths, bool[] inner)
    {
        Span<byte> luma = _frame.Luma;
        Span<byte> chromaU = _frame.ChromaU;
        Span<byte> chromaV = _frame.ChromaV;

        int lumaStride = _frame.LumaStride;
        int chromaStride = _frame.ChromaStride;

        for (int x = 0; x < _across; x++)
        {
            Strength strength = strengths[x];
            if (strength.Limit == 0)
                continue;

            bool filterInner = inner[x];

            int lumaAt = (y * 16 * lumaStride) + (x * 16);
            int chromaAt = (y * 8 * chromaStride) + (x * 8);

            if (_filterType == 1)
            {
                if (x > 0)
                    Vp8Filter.SimpleHorizontal(luma, lumaAt, lumaStride, strength.Limit + 4);

                if (filterInner)
                    Vp8Filter.SimpleHorizontalInner(luma, lumaAt, lumaStride, strength.Limit);

                if (y > 0)
                    Vp8Filter.SimpleVertical(luma, lumaAt, lumaStride, strength.Limit + 4);

                if (filterInner)
                    Vp8Filter.SimpleVerticalInner(luma, lumaAt, lumaStride, strength.Limit);

                continue;
            }

            if (x > 0)
            {
                Vp8Filter.Horizontal16(luma, lumaAt, lumaStride, strength.Limit + 4,
                                       strength.Inner, strength.HighEdge);
                Vp8Filter.Horizontal8(chromaU, chromaAt, chromaV, chromaAt, chromaStride,
                                      strength.Limit + 4, strength.Inner, strength.HighEdge);
            }

            if (filterInner)
            {
                Vp8Filter.Horizontal16Inner(luma, lumaAt, lumaStride, strength.Limit,
                                            strength.Inner, strength.HighEdge);
                Vp8Filter.Horizontal8Inner(chromaU, chromaAt, chromaV, chromaAt, chromaStride,
                                           strength.Limit, strength.Inner, strength.HighEdge);
            }

            if (y > 0)
            {
                Vp8Filter.Vertical16(luma, lumaAt, lumaStride, strength.Limit + 4,
                                     strength.Inner, strength.HighEdge);
                Vp8Filter.Vertical8(chromaU, chromaAt, chromaV, chromaAt, chromaStride,
                                    strength.Limit + 4, strength.Inner, strength.HighEdge);
            }

            if (filterInner)
            {
                Vp8Filter.Vertical16Inner(luma, lumaAt, lumaStride, strength.Limit,
                                          strength.Inner, strength.HighEdge);
                Vp8Filter.Vertical8Inner(chromaU, chromaAt, chromaV, chromaAt, chromaStride,
                                         strength.Limit, strength.Inner, strength.HighEdge);
            }
        }
    }
}
