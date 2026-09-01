// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Pnm;

/// <summary>
/// The Netpbm header. Six of the seven variants are the same three or four whitespace separated
/// numbers after a magic; the seventh spells its fields out by name and ends with a marker, which
/// is what lets it carry a channel count the others cannot express.
/// </summary>
internal struct PnmHeader
{
    /// <summary>Cap on header bytes walked, so a file of nothing but comments cannot spin.</summary>
    private const int MaxHeaderBytes = 64 * 1024;

    public int Variant;
    public int Width;
    public int Height;
    public int Channels;
    public int MaxValue;
    public bool IsAscii;
    public bool IsBitmap;
    public int DataOffset;
    public string TupleType;

    public readonly int BitsPerChannel => MaxValue > 255 ? 16 : 8;

    public static bool TryRead(ReadOnlySpan<byte> data, out PnmHeader header, out ApertureError error)
    {
        header = default;
        header.TupleType = string.Empty;

        if (data.Length < 3)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (data[0] != (byte)'P' || data[1] is < (byte)'1' or > (byte)'7')
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        header.Variant = data[1] - '0';
        return header.Variant == 7 ? TryReadPam(data, ref header, out error)
                                   : TryReadClassic(data, ref header, out error);
    }

    private static bool TryReadClassic(ReadOnlySpan<byte> data, ref PnmHeader header, out ApertureError error)
    {
        int variant = header.Variant;
        header.IsAscii = variant is 1 or 2 or 3;
        header.IsBitmap = variant is 1 or 4;
        header.Channels = variant is 3 or 6 ? 3 : 1;

        int offset = 2;
        if (!TryReadInteger(data, ref offset, out long width) ||
            !TryReadInteger(data, ref offset, out long height))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        long maxValue = 1;
        if (!header.IsBitmap && !TryReadInteger(data, ref offset, out maxValue))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        // The format caps the largest sample at 65535, and zero would leave every sample undefined.
        if (maxValue is < 1 or > 65535)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        header.Width = (int)width;
        header.Height = (int)height;
        header.MaxValue = (int)maxValue;

        // Exactly one whitespace byte separates the header from binary data, and anything inside
        // that data could look like more of it.
        header.DataOffset = header.IsAscii ? offset : offset + 1;

        error = ApertureError.None;
        return true;
    }

    private static bool TryReadPam(ReadOnlySpan<byte> data, ref PnmHeader header, out ApertureError error)
    {
        long width = 0, height = 0, depth = 0, maxValue = 0;
        bool sawEnd = false;
        int offset = 2;
        int limit = Math.Min(data.Length, MaxHeaderBytes);

        while (offset < limit)
        {
            if (!TryReadLine(data, limit, ref offset, out string line))
                break;

            line = line.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line.Equals("ENDHDR", StringComparison.Ordinal))
            {
                sawEnd = true;
                break;
            }

            int space = line.IndexOf(' ');
            if (space <= 0)
                continue;

            string value = line[(space + 1)..].Trim();
            switch (line[..space])
            {
                case "WIDTH": long.TryParse(value, out width); break;
                case "HEIGHT": long.TryParse(value, out height); break;
                case "DEPTH": long.TryParse(value, out depth); break;
                case "MAXVAL": long.TryParse(value, out maxValue); break;
                case "TUPLTYPE": header.TupleType = value; break;
            }
        }

        if (!sawEnd)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        if (depth is < 1 or > 4 || maxValue is < 1 or > 65535)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        header.Width = (int)width;
        header.Height = (int)height;
        header.Channels = (int)depth;
        header.MaxValue = (int)maxValue;
        header.DataOffset = offset;

        error = ApertureError.None;
        return true;
    }

    /// <summary>Reads one decimal token, skipping whitespace and any comment that runs to a line end.</summary>
    private static bool TryReadInteger(ReadOnlySpan<byte> data, ref int offset, out long value)
    {
        value = 0;
        int limit = Math.Min(data.Length, MaxHeaderBytes);
        bool any = false;

        while (offset < limit)
        {
            byte c = data[offset];
            if (c == (byte)'#')
            {
                while (offset < limit && data[offset] is not ((byte)'\n' or (byte)'\r'))
                    offset++;
                continue;
            }

            if (c is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 11 or 12)
            {
                offset++;
                continue;
            }

            break;
        }

        while (offset < limit && data[offset] is >= (byte)'0' and <= (byte)'9')
        {
            value = (value * 10) + (data[offset] - '0');
            offset++;
            any = true;

            if (value > int.MaxValue)
                return false;
        }

        return any;
    }

    private static bool TryReadLine(ReadOnlySpan<byte> data, int limit, ref int offset, out string line)
    {
        line = string.Empty;
        if (offset >= limit)
            return false;

        int start = offset;
        while (offset < limit && data[offset] != (byte)'\n')
            offset++;

        int end = offset;
        if (end > start && data[end - 1] == (byte)'\r')
            end--;

        line = System.Text.Encoding.ASCII.GetString(data[start..end]);
        if (offset < limit)
            offset++;

        return true;
    }
}
