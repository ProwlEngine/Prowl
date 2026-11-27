// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// The compressions a scanned document is stored with, which code the lengths of a row's runs of
/// white and black rather than its pixels. The two dimensional forms go further and code each row
/// as differences from the one above.
/// </summary>
internal static class TiffFax
{
    private const int MaxCodeBits = 14;

    /// <summary>A bit reader that can put bits back, which the mode codes need.</summary>
    private ref struct Reader(ReadOnlySpan<byte> data, bool reversed)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private readonly bool _reversed = reversed;
        private int _at;

        public bool Ended { get; private set; }

        public int Position => _at;

        public int Read()
        {
            if (_at >= _data.Length * 8)
            {
                Ended = true;
                return 0;
            }

            byte value = _data[_at >> 3];
            int shift = _reversed ? _at & 7 : 7 - (_at & 7);
            _at++;
            return (value >> shift) & 1;
        }

        public void Rewind(int count) => _at = Math.Max(0, _at - count);
    }

    public static bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination, int width,
                                 int rows, int rowBytes, int compression, long options, bool reversed)
    {
        bool twoDimensional = compression == 4 || (compression == 3 && (options & 1) != 0);
        bool group4 = compression == 4;

        Reader reader = new(source, reversed);

        int[] reference = new int[width + 3];
        int[] current = new int[width + 3];

        // The row above the first is taken as all white, which is what the format says.
        int referenceCount = 0;
        reference[0] = width;
        reference[1] = width;

        for (int y = 0; y < rows; y++)
        {
            bool rowIs2d = twoDimensional;

            if (!group4)
            {
                SkipEndOfLine(ref reader);

                if (twoDimensional)
                    rowIs2d = reader.Read() == 0;
            }

            if (reader.Ended)
                return y > 0;

            int count = rowIs2d
                ? DecodeTwoDimensional(ref reader, reference, referenceCount, current, width)
                : DecodeOneDimensional(ref reader, current, width);

            if (count < 0)
                return y > 0;

            Fill(current, count, destination[(y * rowBytes)..], width, rowBytes);

            (reference, current) = (current, reference);
            referenceCount = count;
        }

        return true;
    }

    /// <summary>
    /// Steps over the marker that separates rows, and over the zero bits a writer may pad with so
    /// that each row starts on a byte boundary.
    /// </summary>
    private static void SkipEndOfLine(ref Reader reader)
    {
        int start = reader.Position;
        int zeroes = 0;

        while (!reader.Ended)
        {
            int bit = reader.Read();
            if (bit == 0)
            {
                zeroes++;
                continue;
            }

            if (zeroes >= 11)
                return;

            break;
        }

        // What was read was not a marker after all, so the row starts where it did.
        reader.Rewind(reader.Position - start);
    }

    /// <summary>Runs of one colour then the other, all the way across.</summary>
    private static int DecodeOneDimensional(ref Reader reader, int[] changes, int width)
    {
        int at = 0;
        int count = 0;
        int colour = 0;

        while (at < width)
        {
            int run = ReadRun(ref reader, colour);
            if (run < 0)
                return count > 0 ? count : -1;

            at = Math.Min(at + run, width);
            changes[count++] = at;
            colour ^= 1;

            if (count >= changes.Length - 2)
                break;
        }

        changes[count] = width;
        changes[count + 1] = width;
        return count;
    }

    /// <summary>
    /// Each row as a set of differences from the row above. Most of the time the edge of a shape
    /// is within three pixels of where it was on the row before, and that case costs one to seven
    /// bits; only where the picture genuinely changes does a run length get coded.
    /// </summary>
    private static int DecodeTwoDimensional(ref Reader reader, int[] reference, int referenceCount,
                                            int[] changes, int width)
    {
        int a0 = -1;
        int colour = 0;
        int count = 0;

        while (a0 < width)
        {
            int b1 = FindB1(reference, referenceCount, a0, colour, width);
            int b2 = b1 < width ? Next(reference, referenceCount, b1, width) : width;

            int mode = ReadMode(ref reader);
            if (mode == ModeEnd)
                return count > 0 ? Close(changes, count, width) : -1;

            if (mode == ModePass)
            {
                a0 = b2;
                continue;
            }

            if (mode == ModeHorizontal)
            {
                int first = ReadRun(ref reader, colour);
                int second = ReadRun(ref reader, colour ^ 1);

                if (first < 0 || second < 0)
                    return count > 0 ? Close(changes, count, width) : -1;

                int start = a0 < 0 ? 0 : a0;
                int a1 = Math.Min(start + first, width);
                int a2 = Math.Min(a1 + second, width);

                if (count + 2 > changes.Length - 2)
                    return Close(changes, count, width);

                changes[count++] = a1;
                changes[count++] = a2;
                a0 = a2;
                continue;
            }

            // A vertical mode names where the edge is relative to the one above it.
            int position = Math.Clamp(b1 + (mode - ModeVertical), 0, width);

            if (count + 1 > changes.Length - 2)
                return Close(changes, count, width);

            changes[count++] = position;
            a0 = position;
            colour ^= 1;
        }

        return Close(changes, count, width);
    }

    private static int Close(int[] changes, int count, int width)
    {
        changes[count] = width;
        changes[count + 1] = width;
        return count;
    }

    /// <summary>
    /// The first place the row above changes to the colour opposite the one being written, past
    /// where this row has got to.
    /// </summary>
    private static int FindB1(int[] reference, int count, int a0, int colour, int width)
    {
        for (int i = 0; i < count; i++)
        {
            if (reference[i] > a0 && (i & 1) == colour)
                return reference[i];
        }

        return width;
    }

    private static int Next(int[] reference, int count, int b1, int width)
    {
        for (int i = 0; i < count; i++)
        {
            if (reference[i] == b1 && i + 1 < count)
                return reference[i + 1];
        }

        return width;
    }

    private const int ModePass = -100;
    private const int ModeHorizontal = -101;
    private const int ModeEnd = -102;
    private const int ModeVertical = 0;

    /// <summary>Reads which of the seven ways this row names its next edge.</summary>
    private static int ReadMode(ref Reader reader)
    {
        // 1 is the common case, where the edge sits exactly where it did on the row above.
        if (reader.Read() == 1)
            return ModeVertical;

        if (reader.Read() == 1)
            return reader.Read() == 1 ? ModeVertical + 1 : ModeVertical - 1;

        if (reader.Read() == 1)
            return ModeHorizontal;

        if (reader.Read() == 1)
            return ModePass;

        if (reader.Read() == 1)
            return reader.Read() == 1 ? ModeVertical + 2 : ModeVertical - 2;

        if (reader.Read() == 1)
            return reader.Read() == 1 ? ModeVertical + 3 : ModeVertical - 3;

        return ModeEnd;
    }

    /// <summary>
    /// One run length, which is a code for a multiple of sixty four followed by a code for the
    /// remainder whenever the run is longer than sixty three.
    /// </summary>
    private static int ReadRun(ref Reader reader, int colour)
    {
        int total = 0;

        while (true)
        {
            int run = ReadCode(ref reader, colour);
            if (run < 0)
                return -1;

            total += run;

            // Only a multiple of sixty four is a prefix; anything else ends the run.
            if (run < 64 || run % 64 != 0)
                return total;
        }
    }

    private static int ReadCode(ref Reader reader, int colour)
    {
        ReadOnlySpan<short> table = colour == 0 ? FaxTables.White : FaxTables.Black;

        int code = 0;
        for (int length = 1; length <= MaxCodeBits; length++)
        {
            code = (code << 1) | reader.Read();
            if (reader.Ended)
                return -1;

            for (int i = 0; i < table.Length; i += 3)
            {
                if (table[i] == length && table[i + 1] == code)
                    return table[i + 2];
            }
        }

        return -1;
    }

    /// <summary>Turns a row's changing positions into bits, white first.</summary>
    private static void Fill(int[] changes, int count, Span<byte> row, int width, int rowBytes)
    {
        row[..rowBytes].Clear();

        int at = 0;
        int colour = 0;

        for (int i = 0; i < count && at < width; i++)
        {
            int next = Math.Min(changes[i], width);

            if (colour == 1)
            {
                for (int x = at; x < next; x++)
                    row[x >> 3] |= (byte)(0x80 >> (x & 7));
            }

            at = next;
            colour ^= 1;
        }

        if (colour == 1)
        {
            for (int x = at; x < width; x++)
                row[x >> 3] |= (byte)(0x80 >> (x & 7));
        }
    }
}
