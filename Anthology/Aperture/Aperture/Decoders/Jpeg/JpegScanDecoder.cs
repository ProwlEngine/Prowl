// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>
/// Turns one entropy coded scan into coefficients. A sequential scan carries whole blocks; a
/// progressive one carries a spectral band at a chosen precision, and later scans refine what
/// earlier ones left, so a block is only complete once every scan that touches it has run.
/// </summary>
internal ref struct JpegScanDecoder
{
    private enum Mode
    {
        Sequential,
        DcFirst,
        DcRefine,
        AcFirst,
        AcRefine,
    }

    private JpegBitReader _reader;
    private readonly JpegFrame _frame;
    private readonly JpegHuffmanTable[] _dcTables;
    private readonly JpegHuffmanTable[] _acTables;
    private readonly JpegScan _scan;
    private readonly Mode _mode;
    private int _eobRun;

    public JpegScanDecoder(ReadOnlySpan<byte> data, int start, JpegFrame frame,
                           JpegHuffmanTable[] dcTables, JpegHuffmanTable[] acTables, in JpegScan scan)
    {
        _reader = new JpegBitReader(data, start);
        _frame = frame;
        _dcTables = dcTables;
        _acTables = acTables;
        _scan = scan;
        _mode = !frame.Progressive ? Mode.Sequential
            : scan.SpectralStart == 0
                ? scan.ApproximationHigh == 0 ? Mode.DcFirst : Mode.DcRefine
                : scan.ApproximationHigh == 0 ? Mode.AcFirst : Mode.AcRefine;
    }

    /// <summary>Offset of the marker that ended the scan, for the caller to carry on from.</summary>
    public readonly int Position => _reader.Position;

    public bool Decode(JpegComponent[] components, int restartInterval, out ApertureError error)
    {
        error = ApertureError.None;

        // A scan over one component walks that component's own blocks; anything more is
        // interleaved into minimum coded units, which is what the sampling factors are for.
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
                    if (!_reader.TryRestart())
                        return true;

                    Restart(components);
                    sinceRestart = 0;
                }

                bool ok = single
                    ? DecodeBlock(components[0], components[0].BlockOffset(column, row))
                    : DecodeUnit(components, row, column);

                if (!ok)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                sinceRestart++;
            }
        }

        return true;
    }

    /// <summary>Steps over a code that stands for nothing but itself.</summary>
    private int Skip(int length)
    {
        _reader.Skip(length);
        return 0;
    }

    private void Restart(JpegComponent[] components)
    {
        foreach (JpegComponent component in components)
            component.DcPredictor = 0;
        _eobRun = 0;
    }

    private bool DecodeUnit(JpegComponent[] components, int mcuRow, int mcuColumn)
    {
        foreach (JpegComponent component in components)
        {
            int baseRow = mcuRow * component.VerticalFactor;
            int baseColumn = mcuColumn * component.HorizontalFactor;

            for (int v = 0; v < component.VerticalFactor; v++)
            {
                for (int h = 0; h < component.HorizontalFactor; h++)
                {
                    if (!DecodeBlock(component, component.BlockOffset(baseColumn + h, baseRow + v)))
                        return false;
                }
            }
        }

        return true;
    }

    private bool DecodeBlock(JpegComponent component, int offset)
    {
        Span<short> block = component.Coefficients.AsSpan(offset, JpegBlock.Coefficients);
        return _mode switch
        {
            Mode.Sequential => DecodeSequential(component, block),
            Mode.DcFirst => DecodeDcFirst(component, block),
            Mode.DcRefine => DecodeDcRefine(block),
            Mode.AcFirst => DecodeAcFirst(component, block),
            _ => DecodeAcRefine(component, block),
        };
    }

    private bool DecodeSequential(JpegComponent component, Span<short> block)
    {
        JpegHuffmanTable dc = _dcTables[component.DcTableId];
        JpegHuffmanTable ac = _acTables[component.AcTableId];
        if (!dc.IsDefined || !ac.IsDefined)
            return false;

        _reader.Fill();
        int entry = dc.Decode(_reader.PeekWindow());
        if (entry == 0)
            return false;

        int size = entry & 0xFF;
        if (size > 16)
            return false;

        component.DcPredictor += size == 0
            ? Skip(entry >> 8)
            : _reader.SkipAndExtend(entry >> 8, size);

        block[0] = (short)component.DcPredictor;

        ReadOnlySpan<byte> zigzag = JpegBlock.ZigZag;
        for (int k = 1; k < JpegBlock.Coefficients;)
        {
            _reader.Fill();
            entry = ac.Decode(_reader.PeekWindow());
            if (entry == 0)
                return false;

            int symbol = entry & 0xFF;
            int magnitude = symbol & 15;
            int run = symbol >> 4;

            if (magnitude == 0)
            {
                _reader.Skip(entry >> 8);
                if (run != 15)
                    break;

                k += 16;
                continue;
            }

            k += run;
            if (k >= JpegBlock.Coefficients)
            {
                _reader.Skip(entry >> 8);
                break;
            }

            block[zigzag[k]] = (short)_reader.SkipAndExtend(entry >> 8, magnitude);
            k++;
        }

        return true;
    }

    private bool DecodeDcFirst(JpegComponent component, Span<short> block)
    {
        JpegHuffmanTable dc = _dcTables[component.DcTableId];
        if (!dc.IsDefined)
            return false;

        _reader.Fill();
        int entry = dc.Decode(_reader.PeekWindow());
        if (entry == 0)
            return false;

        int size = entry & 0xFF;
        if (size > 16)
            return false;

        component.DcPredictor += size == 0
            ? Skip(entry >> 8)
            : _reader.SkipAndExtend(entry >> 8, size);

        block[0] = (short)(component.DcPredictor << _scan.ApproximationLow);
        return true;
    }

    private bool DecodeDcRefine(Span<short> block)
    {
        if (_reader.ReadBit() != 0)
            block[0] |= (short)(1 << _scan.ApproximationLow);
        return true;
    }

    private bool DecodeAcFirst(JpegComponent component, Span<short> block)
    {
        if (_eobRun > 0)
        {
            _eobRun--;
            return true;
        }

        JpegHuffmanTable ac = _acTables[component.AcTableId];
        if (!ac.IsDefined)
            return false;

        ReadOnlySpan<byte> zigzag = JpegBlock.ZigZag;
        int low = _scan.ApproximationLow;

        for (int k = _scan.SpectralStart; k <= _scan.SpectralEnd;)
        {
            _reader.Fill();
            int entry = ac.Decode(_reader.PeekWindow());
            if (entry == 0)
                return false;

            int symbol = entry & 0xFF;
            _reader.Skip(entry >> 8);
            int magnitude = symbol & 15;
            int run = symbol >> 4;

            if (magnitude == 0)
            {
                if (run != 15)
                {
                    // A run of blocks that hold nothing further in this band.
                    _eobRun = (1 << run) - 1;
                    if (run > 0)
                        _eobRun += _reader.Receive(run);
                    break;
                }

                k += 16;
                continue;
            }

            k += run;
            if (k > _scan.SpectralEnd)
                break;

            block[zigzag[k]] = (short)(_reader.ReceiveExtend(magnitude) << low);
            k++;
        }

        return true;
    }

    /// <summary>
    /// Adds one bit of precision to a band that earlier scans already sketched. Every coefficient
    /// already known to be non zero takes a correction bit as the walk passes over it, which is
    /// why the run length only counts the ones that are still zero.
    /// </summary>
    private bool DecodeAcRefine(JpegComponent component, Span<short> block)
    {
        ReadOnlySpan<byte> zigzag = JpegBlock.ZigZag;
        int positive = 1 << _scan.ApproximationLow;
        int negative = -1 << _scan.ApproximationLow;
        int k = _scan.SpectralStart;

        if (_eobRun == 0)
        {
            JpegHuffmanTable ac = _acTables[component.AcTableId];
            if (!ac.IsDefined)
                return false;

            while (k <= _scan.SpectralEnd)
            {
                _reader.Fill();
                int entry = ac.Decode(_reader.PeekWindow());
                if (entry == 0)
                    return false;

                int symbol = entry & 0xFF;
                _reader.Skip(entry >> 8);
                int magnitude = symbol & 15;
                int run = symbol >> 4;
                int arrival = 0;

                if (magnitude == 0)
                {
                    if (run != 15)
                    {
                        _eobRun = 1 << run;
                        if (run > 0)
                            _eobRun += _reader.Receive(run);
                        break;
                    }
                }
                else
                {
                    if (magnitude != 1)
                        return false;

                    arrival = _reader.ReadBit() != 0 ? positive : negative;
                }

                while (k <= _scan.SpectralEnd)
                {
                    ref short coefficient = ref block[zigzag[k]];
                    if (coefficient != 0)
                    {
                        if (_reader.ReadBit() != 0 && (coefficient & positive) == 0)
                            coefficient += (short)(coefficient >= 0 ? positive : negative);
                    }
                    else
                    {
                        if (run == 0)
                            break;

                        run--;
                    }

                    k++;
                }

                if (arrival != 0 && k <= _scan.SpectralEnd)
                    block[zigzag[k]] = (short)arrival;

                k++;
            }
        }

        if (_eobRun <= 0)
            return true;

        while (k <= _scan.SpectralEnd)
        {
            ref short coefficient = ref block[zigzag[k]];
            if (coefficient != 0 && _reader.ReadBit() != 0 && (coefficient & positive) == 0)
                coefficient += (short)(coefficient >= 0 ? positive : negative);
            k++;
        }

        _eobRun--;
        return true;
    }
}
