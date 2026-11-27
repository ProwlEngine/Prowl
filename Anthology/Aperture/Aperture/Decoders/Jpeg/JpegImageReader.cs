// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers;

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>
/// Walks the marker stream, gathers the tables each scan needs, decodes every scan into
/// coefficients, and turns the result into pixels. A sequential file has one scan and is finished
/// in a single pass; a progressive one revisits the same coefficients across many.
/// </summary>
internal static class JpegImageReader
{
    private const int MaxMarkers = 65536;
    private const int MaxComponents = 4;

    public static bool TryDecode(ReadOnlySpan<byte> data, DecodeOptions options, ImageInfo info,
                                 int channels, Span<byte> destination, int stride, bool flip,
                                 out ApertureError error)
    {
        JpegFrame? frame = null;
        JpegColorTransform transform = JpegColorTransform.YCbCr;
        int adobeTransform = -1;
        int restartInterval = 0;
        bool sawScan = false;
        bool sawHuffman = false;

        ushort[] quantization = BufferPool.UShorts.Rent(4 * JpegBlock.Coefficients);
        bool[] quantizationDefined = new bool[4];
        JpegHuffmanTable[] dcTables = [new(), new(), new(), new()];
        JpegHuffmanTable[] acTables = [new(), new(), new(), new()];
        JpegComponent[] scanComponents = new JpegComponent[MaxComponents];
        JpegConditioning[] conditioning = [new(), new(), new(), new()];

        try
        {
            quantization.AsSpan(0, 4 * JpegBlock.Coefficients).Clear();
            int offset = 2;

            for (int scanned = 0; scanned < MaxMarkers; scanned++)
            {
                if (!TryNextMarker(data, ref offset, out byte marker))
                    break;

                if (marker == 0xD9)
                    break;

                if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                    continue;

                if (offset + 2 > data.Length)
                    break;

                int length = (data[offset] << 8) | data[offset + 1];
                if (length < 2 || offset + length > data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                ReadOnlySpan<byte> payload = data.Slice(offset + 2, length - 2);
                offset += length;

                switch (marker)
                {
                    case 0xDB:
                        if (!ReadQuantizationTables(payload, quantization, quantizationDefined))
                        {
                            error = ApertureError.InvalidData;
                            return false;
                        }
                        break;

                    case 0xC4:
                        if (!ReadHuffmanTables(payload, dcTables, acTables))
                        {
                            error = ApertureError.InvalidData;
                            return false;
                        }
                        sawHuffman = true;
                        break;

                    case 0xDD:
                        if (payload.Length < 2)
                        {
                            error = ApertureError.InvalidData;
                            return false;
                        }
                        restartInterval = (payload[0] << 8) | payload[1];
                        break;

                    case 0xEE when payload.Length >= 12 && payload[..5].SequenceEqual("Adobe"u8):
                        adobeTransform = payload[11];
                        break;

                    case 0xC0:
                    case 0xC1:
                    case 0xC2:
                    case 0xC9:
                        if (frame is not null)
                        {
                            error = ApertureError.InvalidData;
                            return false;
                        }

                        if (!ReadFrame(payload, marker == 0xC2, marker == 0xC9, data, offset,
                                       out frame, out error) ||
                            !Allocate(frame, options, data.Length - offset, out error))
                            return false;
                        break;

                    case 0xCC:
                        if (!ReadConditioning(payload, conditioning))
                        {
                            error = ApertureError.InvalidData;
                            return false;
                        }
                        break;

                    // The lossless and hierarchical modes, and the progressive form of the
                    // arithmetic coder, all decode by rules this reader does not implement.
                    case 0xC3:
                    case 0xC5:
                    case 0xC6:
                    case 0xC7:
                    case 0xCA:
                    case 0xCB:
                    case >= 0xCD and <= 0xCF:
                        error = ApertureError.UnsupportedFeature;
                        return false;

                    case 0xDA:
                        if (frame is null)
                        {
                            error = ApertureError.InvalidData;
                            return false;
                        }

                        if (!ReadScan(payload, frame, scanComponents, out int count, out JpegScan scan))
                        {
                            error = ApertureError.InvalidData;
                            return false;
                        }

                        if (!sawHuffman)
                        {
                            JpegStandardTables.Install(dcTables, acTables);
                            sawHuffman = true;
                        }

                        JpegComponent[] active = scanComponents[..count];

                        if (frame.Arithmetic)
                        {
                            JpegArithmeticDecoder arithmetic =
                                new(data, offset, frame, conditioning, active.Length);

                            if (!arithmetic.Decode(active, restartInterval, out error))
                                return false;

                            sawScan = true;
                            offset = Math.Max(offset, arithmetic.Position);
                            break;
                        }

                        JpegScanDecoder decoder = new(data, offset, frame, dcTables, acTables, scan);
                        if (!decoder.Decode(active, restartInterval, out error))
                            return false;

                        sawScan = true;
                        offset = Math.Max(offset, decoder.Position);
                        break;
                }
            }

            if (frame is null || !sawScan)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            foreach (JpegComponent component in frame.Components)
            {
                if (!quantizationDefined[component.QuantizationTableId])
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
            }

            transform = ChooseTransform(frame, adobeTransform);
            Reconstruct(frame, quantization);
            JpegOutput.Write(frame, transform, channels, destination, stride, flip);

            error = ApertureError.None;
            return true;
        }
        finally
        {
            BufferPool.UShorts.Return(quantization);
            if (frame is not null)
            {
                foreach (JpegComponent component in frame.Components)
                {
                    if (component.Coefficients is { } coefficients)
                        BufferPool.Shorts.Return(coefficients);
                    if (component.Plane is { } plane)
                        BufferPool.Bytes.Return(plane);
                }
            }
        }
    }

    /// <summary>
    /// Three components are luminance and two chroma differences unless the file says otherwise,
    /// either through the marker that records the encoder's choice or by naming its components
    /// after the primaries. Four components add an ink channel on top of whichever it is.
    /// </summary>
    private static JpegColorTransform ChooseTransform(JpegFrame frame, int adobeTransform)
    {
        JpegComponent[] components = frame.Components;

        if (components.Length == 3)
        {
            if (adobeTransform == 0)
                return JpegColorTransform.None;
            if (adobeTransform > 0)
                return JpegColorTransform.YCbCr;
            return components[0].Id == 'R' && components[1].Id == 'G' && components[2].Id == 'B'
                ? JpegColorTransform.None
                : JpegColorTransform.YCbCr;
        }

        if (components.Length == 4)
        {
            return adobeTransform switch
            {
                2 => JpegColorTransform.YCck,
                >= 0 => JpegColorTransform.CmykInverted,
                _ => JpegColorTransform.Cmyk,
            };
        }

        return JpegColorTransform.None;
    }

    /// <summary>
    /// The bounds the arithmetic coder is conditioned with, one pair for each table a component
    /// may name. A file that states none gets the ones the format says to assume.
    /// </summary>
    private static bool ReadConditioning(ReadOnlySpan<byte> payload, JpegConditioning[] conditioning)
    {
        for (int at = 0; at + 1 < payload.Length; at += 2)
        {
            int table = payload[at] & 15;
            if (table > 3)
                return false;

            if ((payload[at] >> 4) != 0)
            {
                conditioning[table].Kx = payload[at + 1];
                continue;
            }

            conditioning[table].Lower = payload[at + 1] & 15;
            conditioning[table].Upper = payload[at + 1] >> 4;
        }

        return true;
    }

    private static bool ReadFrame(ReadOnlySpan<byte> payload, bool progressive, bool arithmetic,
                                  ReadOnlySpan<byte> data,
                                  int after, out JpegFrame frame, out ApertureError error)
    {
        frame = null!;

        if (payload.Length < 6)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        int precision = payload[0];
        int height = (payload[1] << 8) | payload[2];
        int width = (payload[3] << 8) | payload[4];
        int count = payload[5];

        // Everything below this point works in bytes, so a frame declaring wider samples is a
        // feature this reader does not have rather than a reader that has no pixels at all.
        if (precision != 8)
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        if (height == 0)
            height = FindNumberOfLines(data, after);

        if (width <= 0 || height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (count is < 1 or > MaxComponents || payload.Length < 6 + (count * 3))
        {
            error = ApertureError.InvalidColorType;
            return false;
        }

        JpegComponent[] components = new JpegComponent[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = payload.Slice(6 + (i * 3), 3);
            int horizontal = entry[1] >> 4;
            int vertical = entry[1] & 15;

            if (horizontal is < 1 or > 4 || vertical is < 1 or > 4 || entry[2] > 3)
            {
                error = ApertureError.InvalidData;
                return false;
            }

            components[i] = new JpegComponent
            {
                Id = entry[0],
                HorizontalFactor = horizontal,
                VerticalFactor = vertical,
                QuantizationTableId = entry[2],
            };
        }

        frame = new JpegFrame
        {
            Width = width,
            Height = height,
            Precision = precision,
            Progressive = progressive,
            Arithmetic = arithmetic,
            Components = components,
        };
        frame.Prepare();

        error = ApertureError.None;
        return true;
    }

    /// <summary>
    /// A frame may leave its height at zero and supply it after the first scan instead. Nothing
    /// can be sized until it is known, so the stream is walked ahead for it.
    /// </summary>
    private static int FindNumberOfLines(ReadOnlySpan<byte> data, int offset)
    {
        for (int scanned = 0; scanned < MaxMarkers; scanned++)
        {
            if (!TryNextMarker(data, ref offset, out byte marker) || marker == 0xD9)
                return 0;

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                continue;

            if (offset + 2 > data.Length)
                return 0;

            int length = (data[offset] << 8) | data[offset + 1];
            if (length < 2 || offset + length > data.Length)
                return 0;

            if (marker == 0xDC && length >= 4)
                return (data[offset + 2] << 8) | data[offset + 3];

            offset += length;
        }

        return 0;
    }

    private static bool Allocate(JpegFrame frame, DecodeOptions options, int available,
                                 out ApertureError error)
    {
        long budget = 0;
        long blocks = 0;
        foreach (JpegComponent component in frame.Components)
        {
            budget += component.CoefficientLength * sizeof(short);
            budget += (long)component.PlaneStride * component.BlocksPerColumn * 8;
            blocks += (long)component.BlocksPerLine * component.BlocksPerColumn;
        }

        // Every block costs at least one bit, so a file whose remaining bytes cannot cover that
        // has not described the frame it declares.
        if (blocks > (long)available * 8)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (budget > options.MaxAllocationBytes || budget > int.MaxValue)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        foreach (JpegComponent component in frame.Components)
        {
            int coefficients = (int)component.CoefficientLength;
            int plane = component.PlaneStride * component.BlocksPerColumn * 8;

            component.Coefficients = BufferPool.Shorts.Rent(coefficients);
            component.Coefficients.AsSpan(0, coefficients).Clear();
            component.Plane = BufferPool.Bytes.Rent(plane);
        }

        error = ApertureError.None;
        return true;
    }

    private static void Reconstruct(JpegFrame frame, ReadOnlySpan<ushort> quantization)
    {
        foreach (JpegComponent component in frame.Components)
        {
            ReadOnlySpan<ushort> table = quantization.Slice(
                component.QuantizationTableId * JpegBlock.Coefficients, JpegBlock.Coefficients);

            Span<short> coefficients = component.Coefficients.AsSpan();
            Span<byte> plane = component.Plane.AsSpan();
            int stride = component.PlaneStride;

            for (int blockY = 0; blockY < component.UsedBlocksPerColumn; blockY++)
            {
                int rowStart = blockY * component.BlocksPerLine * JpegBlock.Coefficients;
                int planeStart = blockY * 8 * stride;

                for (int blockX = 0; blockX < component.UsedBlocksPerLine; blockX++)
                {
                    JpegIdct.Transform(
                        coefficients.Slice(rowStart + (blockX * JpegBlock.Coefficients), JpegBlock.Coefficients),
                        table,
                        plane[(planeStart + (blockX * 8))..],
                        stride);
                }
            }
        }
    }

    private static bool ReadQuantizationTables(ReadOnlySpan<byte> payload, Span<ushort> tables, Span<bool> defined)
    {
        ReadOnlySpan<byte> zigzag = JpegBlock.ZigZag;
        int at = 0;

        while (at < payload.Length)
        {
            int precision = payload[at] >> 4;
            int id = payload[at] & 15;
            at++;

            if (id > 3 || precision > 1)
                return false;

            int width = precision == 0 ? 1 : 2;
            if (at + (JpegBlock.Coefficients * width) > payload.Length)
                return false;

            Span<ushort> table = tables.Slice(id * JpegBlock.Coefficients, JpegBlock.Coefficients);
            for (int i = 0; i < JpegBlock.Coefficients; i++, at += width)
                table[zigzag[i]] = precision == 0 ? payload[at] : (ushort)((payload[at] << 8) | payload[at + 1]);

            defined[id] = true;
        }

        return true;
    }

    private static bool ReadHuffmanTables(ReadOnlySpan<byte> payload, JpegHuffmanTable[] dc, JpegHuffmanTable[] ac)
    {
        int at = 0;

        while (at < payload.Length)
        {
            int kind = payload[at] >> 4;
            int id = payload[at] & 15;
            at++;

            if (id > 3 || kind > 1 || at + 16 > payload.Length)
                return false;

            ReadOnlySpan<byte> counts = payload.Slice(at, 16);
            at += 16;

            int total = 0;
            foreach (byte count in counts)
                total += count;

            if (total > 256 || at + total > payload.Length)
                return false;

            JpegHuffmanTable table = kind == 0 ? dc[id] : ac[id];
            if (!table.TryReset(counts, payload.Slice(at, total)))
                return false;

            at += total;
        }

        return true;
    }

    private static bool ReadScan(ReadOnlySpan<byte> payload, JpegFrame frame, JpegComponent[] scanComponents,
                                 out int count, out JpegScan scan)
    {
        count = 0;
        scan = default;

        if (payload.Length < 1)
            return false;

        count = payload[0];
        if (count is < 1 or > MaxComponents || payload.Length < 1 + (count * 2) + 3)
        {
            count = 0;
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            byte id = payload[1 + (i * 2)];
            byte tables = payload[2 + (i * 2)];

            JpegComponent? component = null;
            foreach (JpegComponent candidate in frame.Components)
            {
                if (candidate.Id == id)
                {
                    component = candidate;
                    break;
                }
            }

            if (component is null || (tables >> 4) > 3 || (tables & 15) > 3)
            {
                count = 0;
                return false;
            }

            component.DcTableId = tables >> 4;
            component.AcTableId = tables & 15;
            scanComponents[i] = component;
        }

        int tail = 1 + (count * 2);
        scan = new JpegScan
        {
            ComponentCount = count,
            SpectralStart = payload[tail],
            SpectralEnd = payload[tail + 1],
            ApproximationHigh = payload[tail + 2] >> 4,
            ApproximationLow = payload[tail + 2] & 15,
        };

        if (!frame.Progressive)
        {
            // A sequential scan always covers the whole block, whatever the header claims.
            scan.SpectralStart = 0;
            scan.SpectralEnd = 63;
            scan.ApproximationHigh = 0;
            scan.ApproximationLow = 0;
            return true;
        }

        if (scan.SpectralEnd > 63 || scan.SpectralStart > scan.SpectralEnd ||
            (scan.SpectralStart == 0 && scan.SpectralEnd != 0) ||
            (scan.SpectralStart != 0 && count != 1) ||
            scan.ApproximationLow > 13 || scan.ApproximationHigh > 13)
        {
            count = 0;
            return false;
        }

        return true;
    }

    /// <summary>Advances to the next marker, stepping over the fill bytes that may precede it.</summary>
    private static bool TryNextMarker(ReadOnlySpan<byte> data, ref int offset, out byte marker)
    {
        marker = 0;
        while (offset < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            int probe = offset;
            while (probe < data.Length && data[probe] == 0xFF)
                probe++;

            if (probe >= data.Length)
                return false;

            byte candidate = data[probe];
            offset = probe + 1;
            if (candidate == 0x00)
                continue;

            marker = candidate;
            return true;
        }

        return false;
    }
}
