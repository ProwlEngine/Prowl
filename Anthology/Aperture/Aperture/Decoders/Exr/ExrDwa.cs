// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// The two lossy compressions, the only ones the format offers. A channel is treated by what it is
/// called: colour becomes a brightness and two differences, transformed and quantised; alpha is
/// run length coded; anything else is deflated as it lies. Each block states its own rules, so a
/// file says how to read itself rather than relying on a decoder agreeing about names.
/// </summary>
internal static class ExrDwa
{
    /// <summary>The eleven counts each block begins with.</summary>
    private const int HeaderFields = 11;

    private const int Version = 0;
    private const int UnknownUncompressed = 1;
    private const int UnknownCompressed = 2;
    private const int AcCompressed = 3;
    private const int DcCompressed = 4;
    private const int RleCompressed = 5;
    private const int RleUncompressed = 6;
    private const int RleRaw = 7;
    private const int AcCount = 8;
    private const int DcCount = 9;
    private const int AcCompression = 10;

    private const int SchemeUnknown = 0;
    private const int SchemeLossyDct = 1;
    private const int SchemeRle = 2;

    /// <summary>How a channel is to be read, and which of the three colours it stands for.</summary>
    private readonly record struct Rule(string Suffix, int Scheme, int PixelType, int ColourIndex,
                                        bool IgnoreCase);

    /// <summary>What a channel turned out to be, once the rules have been applied to its name.</summary>
    private sealed class Plane
    {
        public ExrChannel Channel = null!;
        public int Scheme = SchemeUnknown;
        public int Width;
        public int Height;
        public bool Done;

        /// <summary>Where each of this channel's rows begins in the output band.</summary>
        public int[] Rows = [];
    }

    public static bool TryDecode(ReadOnlySpan<byte> source, ExrHeader header, Span<byte> destination,
                                 int firstRow, int rows, int width)
    {
        if (source.Length < HeaderFields * 8)
            return false;

        Span<long> counts = stackalloc long[HeaderFields];
        for (int i = 0; i < HeaderFields; i++)
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(source[(i * 8)..]);
            if (value > long.MaxValue)
                return false;

            counts[i] = (long)value;
        }

        if (counts[Version] > 2)
            return false;

        int at = HeaderFields * 8;
        List<Rule> rules = counts[Version] < 2 ? LegacyRules : [];

        if (counts[Version] >= 2 && !TryReadRules(source, ref at, rules))
            return false;

        long compressed = counts[UnknownCompressed] + counts[AcCompressed] +
                          counts[DcCompressed] + counts[RleCompressed];

        if (compressed < 0 || at + compressed > source.Length)
            return false;

        int unknownAt = at;
        int acAt = unknownAt + (int)counts[UnknownCompressed];
        int dcAt = acAt + (int)counts[AcCompressed];
        int rleAt = dcAt + (int)counts[DcCompressed];

        List<Plane> planes = Classify(header, rules, firstRow, rows, width, out List<int[]> colourSets);

        // Everything that is neither colour nor alpha is deflated whole.
        byte[]? unknown = null;
        if (counts[UnknownCompressed] > 0)
        {
            unknown = ExrCodecs.Inflate(source.Slice(unknownAt, (int)counts[UnknownCompressed]),
                                        (int)counts[UnknownUncompressed]);
            if (unknown is null)
                return false;
        }

        ushort[]? ac = null;
        if (counts[AcCompressed] > 0)
        {
            ac = new ushort[counts[AcCount]];
            if (!TryReadAc(source.Slice(acAt, (int)counts[AcCompressed]), counts[AcCompression], ac))
                return false;
        }

        ushort[]? dc = null;
        if (counts[DcCompressed] > 0)
        {
            byte[]? packed = ExrCodecs.Inflate(source.Slice(dcAt, (int)counts[DcCompressed]),
                                               (int)counts[DcCount] * 2);
            if (packed is null)
                return false;

            // The direct current values are stored the way the zip codec stores everything, with
            // each byte a difference from the one before and the halves split into two runs.
            byte[] joined = new byte[packed.Length];
            ExrCodecs.UndoPrediction(packed);
            ExrCodecs.UndoInterleave(packed, joined);

            dc = new ushort[counts[DcCount]];
            for (int i = 0; i < dc.Length; i++)
                dc[i] = BinaryPrimitives.ReadUInt16LittleEndian(joined.AsSpan(i * 2));
        }

        byte[]? rle = null;
        if (counts[RleRaw] > 0)
        {
            byte[]? packed = ExrCodecs.Inflate(source.Slice(rleAt, (int)counts[RleCompressed]),
                                               (int)counts[RleUncompressed]);
            if (packed is null)
                return false;

            rle = new byte[counts[RleRaw]];
            if (!TryUnRle(packed, rle))
                return false;
        }

        return TryPlace(header, planes, colourSets, destination, firstRow, rows, width,
                        ac, dc, unknown, rle);
    }

    /// <summary>
    /// Works out how each channel is stored, and which threes of them were turned into a
    /// brightness and two colour differences together.
    /// </summary>
    private static List<Plane> Classify(ExrHeader header, List<Rule> rules, int firstRow, int rows,
                                        int width, out List<int[]> colourSets)
    {
        List<Plane> planes = [];
        Dictionary<string, int[]> prefixes = [];

        for (int c = 0; c < header.Channels.Count; c++)
        {
            ExrChannel channel = header.Channels[c];
            Plane plane = new()
            {
                Channel = channel,
                Width = channel.SampledWidth(width),
                Height = channel.SampledRows(firstRow, rows),
            };

            string name = channel.Name;
            int dot = name.LastIndexOf('.');
            string suffix = dot < 0 ? name : name[(dot + 1)..];
            string prefix = dot < 0 ? string.Empty : name[..(dot + 1)];

            foreach (Rule rule in rules)
            {
                if (rule.PixelType != channel.PixelType)
                    continue;

                bool matches = rule.IgnoreCase
                    ? string.Equals(suffix, rule.Suffix, StringComparison.OrdinalIgnoreCase)
                    : suffix == rule.Suffix;

                if (!matches)
                    continue;

                plane.Scheme = rule.Scheme;

                if (rule.ColourIndex >= 0)
                {
                    if (!prefixes.TryGetValue(prefix, out int[]? set))
                    {
                        set = [-1, -1, -1];
                        prefixes[prefix] = set;
                    }

                    set[rule.ColourIndex] = c;
                }
            }

            planes.Add(plane);
        }

        colourSets = [];
        foreach (int[] set in prefixes.Values)
        {
            if (set[0] < 0 || set[1] < 0 || set[2] < 0)
                continue;

            ExrChannel red = header.Channels[set[0]];
            ExrChannel green = header.Channels[set[1]];
            ExrChannel blue = header.Channels[set[2]];

            if (red.XSampling != green.XSampling || red.XSampling != blue.XSampling ||
                red.YSampling != green.YSampling || red.YSampling != blue.YSampling)
                continue;

            colourSets.Add(set);
        }

        return planes;
    }

    /// <summary>Reads the table of rules a block carries, which says how to read its channels.</summary>
    private static bool TryReadRules(ReadOnlySpan<byte> source, ref int at, List<Rule> rules)
    {
        if (at + 2 > source.Length)
            return false;

        int size = BinaryPrimitives.ReadUInt16LittleEndian(source[at..]);
        if (size < 2 || at + size > source.Length)
            return false;

        int end = at + size;
        int read = at + 2;

        while (read < end)
        {
            int stop = source[read..end].IndexOf((byte)0);
            if (stop < 0 || read + stop + 3 > end)
                return false;

            string suffix = System.Text.Encoding.UTF8.GetString(source.Slice(read, stop));
            read += stop + 1;

            byte value = source[read];
            byte type = source[read + 1];
            read += 2;

            int colour = (value >> 4) - 1;
            int scheme = (value >> 2) & 3;

            if (colour is < -1 or >= 3 || scheme >= 3 || type > 2)
                return false;

            rules.Add(new Rule(suffix, scheme, type, colour, (value & 1) != 0));
        }

        at = end;
        return true;
    }

    /// <summary>The rules a file written before they were stored assumed both ends knew.</summary>
    private static List<Rule> LegacyRules
    {
        get
        {
            List<Rule> rules = [];
            foreach (string name in new[] { "r", "red" })
            {
                rules.Add(new Rule(name, SchemeLossyDct, 1, 0, true));
                rules.Add(new Rule(name, SchemeLossyDct, 2, 0, true));
            }

            foreach (string name in new[] { "g", "grn", "green" })
            {
                rules.Add(new Rule(name, SchemeLossyDct, 1, 1, true));
                rules.Add(new Rule(name, SchemeLossyDct, 2, 1, true));
            }

            foreach (string name in new[] { "b", "blu", "blue" })
            {
                rules.Add(new Rule(name, SchemeLossyDct, 1, 2, true));
                rules.Add(new Rule(name, SchemeLossyDct, 2, 2, true));
            }

            foreach (string name in new[] { "y", "by", "ry" })
            {
                rules.Add(new Rule(name, SchemeLossyDct, 1, -1, true));
                rules.Add(new Rule(name, SchemeLossyDct, 2, -1, true));
            }

            rules.Add(new Rule("a", SchemeRle, 0, -1, true));
            rules.Add(new Rule("a", SchemeRle, 1, -1, true));
            rules.Add(new Rule("a", SchemeRle, 2, -1, true));
            return rules;
        }
    }

    /// <summary>
    /// The coefficients above the first, which are coded either by the same Huffman coder the
    /// wavelet compression uses or by deflate, whichever the writer found smaller.
    /// </summary>
    private static bool TryReadAc(ReadOnlySpan<byte> source, long kind, ushort[] destination)
    {
        if (kind == 0)
            return ExrHuffman.TryDecompress(source, destination);

        byte[]? plain = ExrCodecs.Inflate(source, destination.Length * 2);
        if (plain is null || plain.Length != destination.Length * 2)
            return false;

        for (int i = 0; i < destination.Length; i++)
            destination[i] = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(i * 2));

        return true;
    }

    /// <summary>Undoes the run length coding the alpha style channels are stored with.</summary>
    private static bool TryUnRle(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int read = 0;
        int written = 0;

        while (read < source.Length)
        {
            sbyte count = (sbyte)source[read++];

            if (count < 0)
            {
                int literal = -count;
                if (read + literal > source.Length || written + literal > destination.Length)
                    return false;

                source.Slice(read, literal).CopyTo(destination[written..]);
                read += literal;
                written += literal;
                continue;
            }

            int run = count + 1;
            if (read >= source.Length || written + run > destination.Length)
                return false;

            destination.Slice(written, run).Fill(source[read++]);
            written += run;
        }

        return written == destination.Length;
    }

    /// <summary>Writes every channel into the band the rest of the reader expects.</summary>
    private static bool TryPlace(ExrHeader header, List<Plane> planes, List<int[]> colourSets,
                                 Span<byte> destination, int firstRow, int rows, int width,
                                 ushort[]? ac, ushort[]? dc, byte[]? unknown, byte[]? rle)
    {
        // Where each channel's rows begin, which is what the interleaved band layout decides.
        int at = 0;
        foreach (Plane plane in planes)
            plane.Rows = new int[Math.Max(plane.Height, 0)];

        Span<int> next = stackalloc int[planes.Count];
        next.Clear();

        for (int row = 0; row < rows; row++)
        {
            int y = firstRow + row;
            for (int c = 0; c < planes.Count; c++)
            {
                if (!planes[c].Channel.PresentOn(y))
                    continue;

                if (next[c] < planes[c].Rows.Length)
                    planes[c].Rows[next[c]++] = at;

                at += planes[c].Width * planes[c].Channel.Bytes;
            }
        }

        int acAt = 0;
        int dcAt = 0;

        foreach (int[] set in colourSets)
        {
            Plane red = planes[set[0]];
            Plane green = planes[set[1]];
            Plane blue = planes[set[2]];

            if (red.Scheme != SchemeLossyDct || green.Scheme != SchemeLossyDct ||
                blue.Scheme != SchemeLossyDct)
                return false;

            if (!TryLossy(destination, [red, green, blue], ac, dc, ref acAt, ref dcAt))
                return false;

            red.Done = green.Done = blue.Done = true;
        }

        int unknownAt = 0;
        Span<int> rleAt = stackalloc int[4];

        // The run length coded channels share one buffer, split by which byte of the sample the
        // run belongs to, so where each of them starts depends on every channel before it.
        int rleStart = 0;

        foreach (Plane plane in planes)
        {
            if (plane.Done || plane.Width <= 0 || plane.Height <= 0)
                continue;

            switch (plane.Scheme)
            {
                case SchemeLossyDct:
                    if (!TryLossy(destination, [plane], ac, dc, ref acAt, ref dcAt))
                        return false;

                    break;

                case SchemeRle:
                {
                    if (rle is null)
                        return false;

                    int bytes = plane.Channel.Bytes;
                    int samples = plane.Width * plane.Height;

                    for (int b = 0; b < bytes; b++)
                        rleAt[b] = rleStart + (b * samples);

                    for (int row = 0; row < plane.Height; row++)
                    {
                        int to = plane.Rows[row];
                        for (int x = 0; x < plane.Width; x++)
                        {
                            for (int b = 0; b < bytes; b++)
                            {
                                if (rleAt[b] >= rle.Length)
                                    return false;

                                destination[to + (x * bytes) + b] = rle[rleAt[b]++];
                            }
                        }
                    }

                    rleStart += samples * bytes;
                    break;
                }

                default:
                {
                    if (unknown is null)
                        return false;

                    int lineBytes = plane.Width * plane.Channel.Bytes;
                    for (int row = 0; row < plane.Height; row++)
                    {
                        if (unknownAt + lineBytes > unknown.Length)
                            return false;

                        unknown.AsSpan(unknownAt, lineBytes).CopyTo(destination[plane.Rows[row]..]);
                        unknownAt += lineBytes;
                    }

                    break;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Reads one channel, or a three that were coded together, out of the transformed
    /// coefficients and writes the samples back.
    /// </summary>
    private static bool TryLossy(Span<byte> destination, Plane[] planes, ushort[]? ac, ushort[]? dc,
                                 ref int acAt, ref int dcAt)
    {
        int width = planes[0].Width;
        int height = planes[0].Height;
        if (width <= 0 || height <= 0)
            return true;

        int across = (width + 7) / 8;
        int down = (height + 7) / 8;
        int leftoverX = width - ((across - 1) * 8);
        int leftoverY = height - ((down - 1) * 8);
        int count = planes.Length;

        if (dc is null || dcAt + (count * across * down) > dc.Length)
            return false;

        // The flat values of every block of one channel are stored together, which is why each
        // channel needs its own place in that run rather than taking turns.
        Span<int> dcNext = stackalloc int[3];
        for (int c = 0; c < count; c++)
            dcNext[c] = dcAt + (c * across * down);

        ushort[] blocks = new ushort[count * across * 64];
        Span<ushort> zigzag = stackalloc ushort[64];
        float[] transformed = new float[count * 64];

        ushort[] toLinear = ExrDwaTables.ToLinear;

        for (int blockY = 0; blockY < down; blockY++)
        {
            int maxY = blockY == down - 1 ? leftoverY : 8;

            for (int blockX = 0; blockX < across; blockX++)
            {
                int maxX = blockX == across - 1 ? leftoverX : 8;
                bool flat = true;

                for (int c = 0; c < count; c++)
                {
                    zigzag.Clear();
                    zigzag[0] = dc[dcNext[c]++];

                    if (!TryUnRleAc(ac, ref acAt, zigzag, out int lastNonZero))
                        return false;

                    Span<float> block = transformed.AsSpan(c * 64, 64);

                    if (lastNonZero == 0)
                    {
                        block[0] = (float)BitConverter.UInt16BitsToHalf(zigzag[0]);
                        ExrDct.InverseFlat(block);
                        continue;
                    }

                    flat = false;
                    ExrDct.FromZigZag(zigzag, block);

                    // A block is stored only as far as its last coefficient that is not zero, so
                    // the rows past that one are known to be nothing and are not transformed.
                    int zeroed = lastNonZero < 2 ? 7
                        : lastNonZero < 3 ? 6
                        : lastNonZero < 9 ? 5
                        : lastNonZero < 10 ? 4
                        : lastNonZero < 20 ? 3
                        : lastNonZero < 21 ? 2
                        : lastNonZero < 35 ? 1
                        : 0;

                    ExrDct.Inverse(block, zeroed);
                }

                if (count == 3)
                {
                    ExrDct.ColourInverse(transformed.AsSpan(0, 64), transformed.AsSpan(64, 64),
                                         transformed.AsSpan(128, 64), flat ? 1 : 64);
                }

                for (int c = 0; c < count; c++)
                {
                    Span<ushort> to = blocks.AsSpan((c * across * 64) + (blockX * 64), 64);
                    if (flat)
                    {
                        to.Fill(BitConverter.HalfToUInt16Bits((Half)transformed[c * 64]));
                        continue;
                    }

                    for (int i = 0; i < 64; i++)
                        to[i] = BitConverter.HalfToUInt16Bits((Half)transformed[(c * 64) + i]);
                }
            }

            // The blocks of this row are now a row of the picture, once the values are put back
            // through the curve they were made perceptually even by.
            for (int c = 0; c < count; c++)
            {
                Plane plane = planes[c];
                bool linear = plane.Channel.Linear;

                for (int y = 0; y < maxY; y++)
                {
                    int row = (blockY * 8) + y;
                    if (row >= plane.Rows.Length)
                        break;

                    int to = plane.Rows[row];

                    for (int blockX = 0; blockX < across; blockX++)
                    {
                        int columns = blockX == across - 1 ? leftoverX : 8;
                        Span<ushort> from = blocks.AsSpan((c * across * 64) + (blockX * 64) + (y * 8), 8);

                        for (int x = 0; x < columns; x++)
                        {
                            ushort value = linear ? from[x] : toLinear[from[x]];
                            int offset = to + (((blockX * 8) + x) * plane.Channel.Bytes);

                            if (plane.Channel.Bytes == 4)
                            {
                                BinaryPrimitives.WriteSingleLittleEndian(destination[offset..],
                                    (float)BitConverter.UInt16BitsToHalf(value));
                            }
                            else
                            {
                                BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], value);
                            }
                        }
                    }
                }
            }

        }

        dcAt = dcNext[count - 1];
        return true;
    }

    /// <summary>
    /// Undoes the run length coding of the coefficients above the first. A value whose high byte
    /// is all ones is a run of that many zeros; anything else is a coefficient as it stands.
    /// </summary>
    private static bool TryUnRleAc(ushort[]? ac, ref int at, Span<ushort> block, out int lastNonZero)
    {
        lastNonZero = 0;
        int i = 1;

        while (i < 64)
        {
            if (ac is null || at >= ac.Length)
                return false;

            ushort value = ac[at++];

            if ((value & 0xFF00) == 0xFF00)
            {
                int run = value & 0xFF;
                i += run == 0 ? 64 : run;
                continue;
            }

            lastNonZero = i;
            block[i] = value;
            i++;
        }

        return true;
    }
}
