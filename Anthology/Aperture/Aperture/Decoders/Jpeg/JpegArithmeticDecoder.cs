// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>
/// The other entropy coder the format allows, which codes decisions rather than symbols: an
/// interval split in proportion to how likely each answer is, with the proportions learnt as it
/// goes. All the file carries is the conditioning bounds saying when a difference counts as small.
/// </summary>
internal ref struct JpegArithmeticDecoder
{
    private const int DcBins = 64;
    private const int AcBins = 256;

    private readonly ReadOnlySpan<byte> _data;
    private readonly JpegFrame _frame;
    private readonly JpegConditioning[] _conditioning;
    private readonly byte[][] _dcStatistics;
    private readonly byte[][] _acStatistics;
    private readonly int[] _dcContext;
    private readonly byte[] _fixed;

    private int _position;
    private long _interval;
    private long _code;
    private int _shift;
    private bool _ended;
    private bool _broken;

    public JpegArithmeticDecoder(ReadOnlySpan<byte> data, int start, JpegFrame frame,
                                 JpegConditioning[] conditioning, int components)
    {
        _data = data;
        _position = start;
        _frame = frame;
        _conditioning = conditioning;

        _dcStatistics = new byte[4][];
        _acStatistics = new byte[4][];
        for (int i = 0; i < 4; i++)
        {
            _dcStatistics[i] = new byte[DcBins];
            _acStatistics[i] = new byte[AcBins];
        }

        _dcContext = new int[components];
        _fixed = [JpegArithmeticTable.Fixed];
    }

    /// <summary>Offset of the marker that ended the scan, for the caller to carry on from.</summary>
    public readonly int Position => _position;

    public bool Decode(JpegComponent[] components, int restartInterval, out ApertureError error)
    {
        error = ApertureError.None;

        bool single = components.Length == 1;
        int rows = single ? components[0].UsedBlocksPerColumn : _frame.McusPerColumn;
        int columns = single ? components[0].UsedBlocksPerLine : _frame.McusPerLine;
        if (rows <= 0 || columns <= 0)
            return true;

        Restart(components);
        int sinceRestart = 0;

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                if (restartInterval > 0 && sinceRestart == restartInterval)
                {
                    if (!TrySkipRestart())
                        return true;

                    Restart(components);
                    sinceRestart = 0;
                }

                if (single)
                    DecodeBlock(components[0], 0, components[0].BlockOffset(column, row));
                else
                    DecodeUnit(components, row, column);

                if (_broken)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                sinceRestart++;
            }
        }

        FindMarker();
        return true;
    }

    private void Restart(JpegComponent[] components)
    {
        for (int i = 0; i < components.Length; i++)
        {
            Array.Clear(_dcStatistics[components[i].DcTableId]);
            Array.Clear(_acStatistics[components[i].AcTableId]);
            components[i].DcPredictor = 0;
            _dcContext[i] = 0;
        }

        _interval = 0;
        _code = 0;
        _shift = -16;
        _ended = false;
    }

    private void DecodeUnit(JpegComponent[] components, int mcuRow, int mcuColumn)
    {
        for (int i = 0; i < components.Length; i++)
        {
            JpegComponent component = components[i];
            int baseRow = mcuRow * component.VerticalFactor;
            int baseColumn = mcuColumn * component.HorizontalFactor;

            for (int v = 0; v < component.VerticalFactor; v++)
            {
                for (int h = 0; h < component.HorizontalFactor; h++)
                    DecodeBlock(component, i, component.BlockOffset(baseColumn + h, baseRow + v));
            }
        }
    }

    private void DecodeBlock(JpegComponent component, int index, int offset)
    {
        Span<short> block = component.Coefficients.AsSpan(offset, JpegBlock.Coefficients);
        DecodeDc(component, index, block);
        DecodeAc(component, block);
    }

    /// <summary>
    /// The difference from the block before, coded against how large the last one turned out to
    /// be. A run of flat blocks and a run of busy ones therefore learn separate estimates.
    /// </summary>
    private void DecodeDc(JpegComponent component, int index, Span<short> block)
    {
        int table = component.DcTableId;
        byte[] statistics = _dcStatistics[table];
        ref JpegConditioning bounds = ref _conditioning[table];

        int at = _dcContext[index];
        if (Read(statistics, at) != 0)
        {
            int sign = Read(statistics, at + 1);
            at += 2 + sign;

            int magnitude = Read(statistics, at);
            if (magnitude != 0)
            {
                at = 20;
                while (Read(statistics, at) != 0)
                {
                    magnitude <<= 1;
                    if (magnitude == 0x8000)
                    {
                        _broken = true;
                        return;
                    }

                    at++;
                }
            }

            // The bounds say what counts as no difference at all and what counts as a large one,
            // which is the whole of what the file conditions this coder with.
            _dcContext[index] = magnitude < (1 << bounds.Lower) >> 1 ? 0
                : magnitude > (1 << bounds.Upper) >> 1 ? 12 + (sign * 4)
                : 4 + (sign * 4);

            int value = magnitude;
            at += 14;
            while ((magnitude >>= 1) != 0)
            {
                if (Read(statistics, at) != 0)
                    value |= magnitude;
            }

            value++;
            component.DcPredictor = (component.DcPredictor + (sign != 0 ? -value : value)) & 0xFFFF;
        }
        else
        {
            _dcContext[index] = 0;
        }

        block[0] = (short)component.DcPredictor;
    }

    private void DecodeAc(JpegComponent component, Span<short> block)
    {
        int table = component.AcTableId;
        byte[] statistics = _acStatistics[table];
        int kx = _conditioning[table].Kx;
        ReadOnlySpan<byte> zigzag = JpegBlock.ZigZag;

        for (int k = 1; k < JpegBlock.Coefficients; k++)
        {
            int at = 3 * (k - 1);
            if (Read(statistics, at) != 0)
                return;

            while (Read(statistics, at + 1) == 0)
            {
                at += 3;
                k++;
                if (k >= JpegBlock.Coefficients)
                {
                    _broken = true;
                    return;
                }
            }

            // The sign is genuinely even, so it is read against the state that learns nothing.
            int sign = Read(_fixed, 0);
            at += 2;

            int magnitude = Read(statistics, at);
            if (magnitude != 0 && Read(statistics, at) != 0)
            {
                magnitude <<= 1;
                at = k <= kx ? 189 : 217;
                while (Read(statistics, at) != 0)
                {
                    magnitude <<= 1;
                    if (magnitude == 0x8000)
                    {
                        _broken = true;
                        return;
                    }

                    at++;
                }
            }

            int value = magnitude;
            at += 14;
            while ((magnitude >>= 1) != 0)
            {
                if (Read(statistics, at) != 0)
                    value |= magnitude;
            }

            value++;
            block[zigzag[k]] = (short)(sign != 0 ? -value : value);
        }
    }

    /// <summary>
    /// One decision, against the estimate the given bin has learned so far. The interval is
    /// narrowed by the less probable symbol's share, and whichever side the code falls on decides
    /// the answer and how the estimate moves.
    /// </summary>
    private int Read(byte[] statistics, int at)
    {
        while (_interval < 0x8000)
        {
            if (--_shift < 0)
            {
                _code = (_code << 8) | (uint)NextByte();

                if ((_shift += 8) < 0 && ++_shift == 0)
                    _interval = 0x8000;
            }

            _interval <<= 1;
        }

        int state = statistics[at];
        int index = state & 0x7F;
        long estimate = JpegArithmeticTable.Estimate[index];
        int mps = state & 0x80;

        // A few states are so close to even that the less probable symbol turning up is taken as
        // evidence that the two have swapped, so moving to them also flips which one is expected.
        int afterMps = JpegArithmeticTable.NextMps[index];
        int afterLps = JpegArithmeticTable.NextLps[index] | (JpegArithmeticTable.Exchange[index] << 7);

        long boundary = _interval - estimate;
        _interval = boundary;
        boundary <<= _shift;

        if (_code >= boundary)
        {
            _code -= boundary;

            // The narrowed interval is smaller than the share just taken out of it, so the two
            // symbols swap places: what was less probable is what actually turned up.
            bool swapped = _interval >= estimate;
            _interval = estimate;

            if (swapped)
            {
                statistics[at] = (byte)(mps ^ afterLps);
                state ^= 0x80;
            }
            else
            {
                statistics[at] = (byte)(mps ^ afterMps);
            }
        }
        else if (_interval < 0x8000)
        {
            if (_interval < estimate)
            {
                statistics[at] = (byte)(mps ^ afterLps);
                state ^= 0x80;
            }
            else
            {
                statistics[at] = (byte)(mps ^ afterMps);
            }
        }

        return state >> 7;
    }

    /// <summary>
    /// The next byte of the coded data, with the stuffing undone. Reaching a marker is allowed
    /// here rather than an error, and the coder is fed nothing from then on.
    /// </summary>
    private int NextByte()
    {
        if (_ended || _position >= _data.Length)
            return 0;

        int value = _data[_position++];
        if (value != 0xFF)
            return value;

        while (_position < _data.Length && _data[_position] == 0xFF)
            _position++;

        if (_position >= _data.Length)
        {
            _ended = true;
            return 0;
        }

        if (_data[_position] == 0)
        {
            _position++;
            return 0xFF;
        }

        _position--;
        _ended = true;
        return 0;
    }

    /// <summary>Steps over a restart marker, which the coder resynchronises on.</summary>
    private bool TrySkipRestart()
    {
        FindMarker();
        if (_position + 1 >= _data.Length || _data[_position] != 0xFF)
            return false;

        byte marker = _data[_position + 1];
        if (marker is < 0xD0 or > 0xD7)
            return false;

        _position += 2;
        _ended = false;
        return true;
    }

    /// <summary>Walks to the marker that ends the coded data, wherever the coder stopped short.</summary>
    private void FindMarker()
    {
        while (_position + 1 < _data.Length)
        {
            if (_data[_position] != 0xFF)
            {
                _position++;
                continue;
            }

            int next = _position + 1;
            while (next < _data.Length && _data[next] == 0xFF)
                next++;

            if (next >= _data.Length)
                break;

            if (_data[next] != 0)
            {
                _position = next - 1;
                return;
            }

            _position = next + 1;
        }

        _position = _data.Length;
    }
}

/// <summary>What the file says about when a difference is small, large, or none at all.</summary>
internal struct JpegConditioning
{
    public int Lower;
    public int Upper = 5;
    public int Kx = 5;

    public JpegConditioning()
    {
    }
}
