// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;

namespace Prowl.Aperture.Metadata;

/// <summary>Tag numbers this library reads out of a TIFF or Exif image file directory.</summary>
internal static class TiffTag
{
    public const int NewSubfileType = 0x00FE;
    public const int ImageWidth = 0x0100;
    public const int ImageLength = 0x0101;
    public const int BitsPerSample = 0x0102;
    public const int Compression = 0x0103;
    public const int PhotometricInterpretation = 0x0106;
    public const int FillOrder = 0x010A;
    public const int StripOffsets = 0x0111;
    public const int RowsPerStrip = 0x0116;
    public const int StripByteCounts = 0x0117;
    public const int Make = 0x010F;
    public const int Model = 0x0110;
    public const int Orientation = 0x0112;
    public const int SamplesPerPixel = 0x0115;
    public const int XResolution = 0x011A;
    public const int YResolution = 0x011B;
    public const int PlanarConfiguration = 0x011C;
    public const int ResolutionUnit = 0x0128;
    public const int Predictor = 0x013D;
    public const int ColorMap = 0x0140;
    public const int TileWidth = 0x0142;
    public const int TileLength = 0x0143;
    public const int TileOffsets = 0x0144;
    public const int TileByteCounts = 0x0145;
    public const int SubIfds = 0x014A;
    public const int ExtraSamples = 0x0152;
    public const int SampleFormat = 0x0153;
    public const int JpegProc = 0x0200;
    public const int JpegInterchangeFormat = 0x0201;
    public const int JpegInterchangeLength = 0x0202;
    public const int JpegRestartInterval = 0x0203;
    public const int JpegQTables = 0x0207;
    public const int JpegDcTables = 0x0208;
    public const int JpegAcTables = 0x0209;
    public const int T4Options = 0x0124;
    public const int T6Options = 0x0125;
    public const int JpegTables = 0x015B;
    public const int YCbCrCoefficients = 0x0211;
    public const int YCbCrSubSampling = 0x0212;
    public const int ReferenceBlackWhite = 0x0214;
    public const int Xmp = 0x02BC;
    public const int IccProfile = 0x8773;
    public const int CfaRepeatPatternDim = 0x828D;
    public const int CfaPattern = 0x828E;
    public const int BlackLevel = 0xC61A;
    public const int WhiteLevel = 0xC61D;
    public const int ExifIfd = 0x8769;
    public const int DngVersion = 0xC612;
}

/// <summary>
/// A bounded reader for the image file directory structure shared by TIFF, BigTIFF, Exif and
/// every TIFF-derived camera raw format. Offsets in these files are absolute and attacker
/// controlled, so every lookup is range checked against the buffer.
/// </summary>
internal readonly ref struct TiffDirectory
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly int _entriesStart;
    private readonly int _entryCount;

    /// <summary>Whether values in this file are stored least significant byte first.</summary>
    public bool LittleEndian { get; }

    /// <summary>Whether this is BigTIFF, with 8 byte counts and offsets and 20 byte entries.</summary>
    public bool Big { get; }

    /// <summary>Offset of the next directory in the chain, zero when this is the last.</summary>
    public ulong NextDirectoryOffset { get; }

    /// <summary>Number of entries in this directory.</summary>
    public int Count => _entryCount;

    private int EntrySize => Big ? 20 : 12;

    private int InlineLimit => Big ? 8 : 4;

    private TiffDirectory(ReadOnlySpan<byte> data, bool littleEndian, bool big,
                          int entriesStart, int entryCount, ulong next)
    {
        _data = data;
        LittleEndian = littleEndian;
        Big = big;
        _entriesStart = entriesStart;
        _entryCount = entryCount;
        NextDirectoryOffset = next;
    }

    /// <summary>Reads the TIFF or BigTIFF header, returning the byte order and the offset of IFD0.</summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> data, out bool littleEndian, out bool big, out ulong firstDirectory)
    {
        littleEndian = true;
        big = false;
        firstDirectory = 0;
        if (data.Length < 8)
            return false;

        if (data[0] == 'I' && data[1] == 'I')
            littleEndian = true;
        else if (data[0] == 'M' && data[1] == 'M')
            littleEndian = false;
        else
            return false;

        ushort magic = Read16(data, 2, littleEndian);
        if (magic == 42)
        {
            firstDirectory = Read32(data, 4, littleEndian);
            return firstDirectory >= 8;
        }

        if (magic != 43 || data.Length < 16)
            return false;

        // BigTIFF declares its own offset width, which the spec fixes at 8, then a zero pad.
        if (Read16(data, 4, littleEndian) != 8 || Read16(data, 6, littleEndian) != 0)
            return false;

        big = true;
        firstDirectory = Read64(data, 8, littleEndian);
        return firstDirectory >= 16;
    }

    /// <summary>Opens the directory at an absolute offset, failing if it does not fit in the buffer.</summary>
    public static bool TryOpen(ReadOnlySpan<byte> data, bool littleEndian, bool big, ulong offset, out TiffDirectory directory)
    {
        directory = default;

        // The entry count is two bytes classic and eight in BigTIFF, but the next directory
        // pointer is four and eight, and treating them as one reads off the end of a file.
        int countBytes = big ? 8 : 2;
        int pointerBytes = big ? 8 : 4;
        int entrySize = big ? 20 : 12;

        if (offset + (ulong)countBytes > (ulong)data.Length)
            return false;

        ulong rawCount = big ? Read64(data, (int)offset, littleEndian) : Read16(data, (int)offset, littleEndian);
        int entriesStart = (int)offset + countBytes;

        if (rawCount == 0)
            return false;

        int count = (int)Math.Min(rawCount, (ulong)int.MaxValue);
        long span = (long)count * entrySize;

        if (entriesStart + span + pointerBytes > data.Length)
        {
            // A truncated tail still yields whichever entries are wholly present.
            count = (int)Math.Max(0, (data.Length - entriesStart) / entrySize);
            if (count == 0)
                return false;
            directory = new TiffDirectory(data, littleEndian, big, entriesStart, count, 0);
            return true;
        }

        ulong next = big
            ? Read64(data, entriesStart + (int)span, littleEndian)
            : Read32(data, entriesStart + (int)span, littleEndian);
        directory = new TiffDirectory(data, littleEndian, big, entriesStart, count, next);
        return true;
    }

    /// <summary>Finds a tag whose value is a run of bytes and returns where in the file it lies.</summary>
    public bool TryGetByteRange(int tag, out int offset, out int length)
    {
        offset = 0;
        length = 0;

        if (!TryFind(tag, out int type, out ulong count, out int valueOffset) || count == 0)
            return false;

        // Byte, undefined and signed byte all store one byte a value, which is what a tag
        // carrying an embedded stream rather than a number is written as.
        if (type is not (1 or 6 or 7) || count > (ulong)int.MaxValue)
            return false;

        if (valueOffset + (long)count > _data.Length)
            return false;

        offset = valueOffset;
        length = (int)count;
        return true;
    }

    /// <summary>Fills as many of a tag's rational values as fit, returning how many were read.</summary>
    public int GetRationals(int tag, Span<double> destination)
    {
        if (!TryFind(tag, out int type, out ulong count, out int valueOffset) || type is not (5 or 10))
            return 0;

        int read = (int)Math.Min(count, (ulong)destination.Length);
        for (int i = 0; i < read; i++)
        {
            int at = valueOffset + (i * 8);
            if (at + 8 > _data.Length)
                return i;

            long numerator = type == 5
                ? Read32(_data, at, LittleEndian)
                : unchecked((int)Read32(_data, at, LittleEndian));
            long denominator = type == 5
                ? Read32(_data, at + 4, LittleEndian)
                : unchecked((int)Read32(_data, at + 4, LittleEndian));

            if (denominator == 0)
                return i;

            destination[i] = (double)numerator / denominator;
        }

        return read;
    }

    /// <summary>Finds a tag and returns its first value widened to a long.</summary>
    public bool TryGetInteger(int tag, out long value)
    {
        value = 0;
        if (!TryFind(tag, out int type, out ulong count, out int valueOffset) || count == 0)
            return false;

        return TryReadScalar(type, valueOffset, out value);
    }

    /// <summary>Finds a tag of rational type and returns it as a double.</summary>
    public bool TryGetRational(int tag, out double value)
    {
        value = 0;
        if (!TryFind(tag, out int type, out ulong count, out int valueOffset) || count == 0)
            return false;

        if (type is not (5 or 10) || valueOffset + 8 > _data.Length)
            return false;

        long numerator = type == 5
            ? Read32(_data, valueOffset, LittleEndian)
            : unchecked((int)Read32(_data, valueOffset, LittleEndian));
        long denominator = type == 5
            ? Read32(_data, valueOffset + 4, LittleEndian)
            : unchecked((int)Read32(_data, valueOffset + 4, LittleEndian));

        if (denominator == 0)
            return false;

        value = (double)numerator / denominator;
        return true;
    }

    /// <summary>Finds an ASCII tag and returns it with the trailing NUL removed.</summary>
    public bool TryGetString(int tag, int maxLength, out string value)
    {
        value = string.Empty;
        if (!TryFind(tag, out int type, out ulong count, out int valueOffset) || type != 2)
            return false;

        int length = (int)Math.Min(count, (ulong)maxLength);
        if (length <= 0 || valueOffset + length > _data.Length)
            return false;

        value = Encoding.ASCII.GetString(_data.Slice(valueOffset, length)).TrimEnd('\0');
        return true;
    }

    /// <summary>Whether the directory contains a tag at all, regardless of its value.</summary>
    public bool Contains(int tag) => TryFind(tag, out _, out _, out _);

    /// <summary>
    /// Reads every value of a tag into <paramref name="destination"/>, returning how many were
    /// written. Used for BitsPerSample, which has one entry per channel.
    /// </summary>
    public int GetIntegers(int tag, Span<long> destination)
    {
        if (!TryFind(tag, out int type, out ulong count, out int valueOffset))
            return 0;

        int size = SizeOf(type);
        if (size == 0)
            return 0;

        int written = 0;
        for (ulong i = 0; i < count && written < destination.Length; i++)
        {
            if (!TryReadScalar(type, valueOffset + (int)i * size, out long value))
                break;
            destination[written++] = value;
        }
        return written;
    }

    /// <summary>
    /// Whether an entry names this tag at all, regardless of whether its value can be read. A tag
    /// that is present but points outside the file is malformed, which is a different thing from
    /// a tag that was never written.
    /// </summary>
    public bool HasTag(int tag)
    {
        for (int i = 0; i < _entryCount; i++)
        {
            int entry = _entriesStart + (i * EntrySize);
            if (entry + EntrySize > _data.Length)
                return false;

            if (Read16(_data, entry, LittleEndian) == tag)
                return true;
        }

        return false;
    }

    /// <summary>How many values a tag holds, or zero when it is absent.</summary>
    public long CountOf(int tag) =>
        TryFind(tag, out _, out ulong count, out _) ? (long)Math.Min(count, long.MaxValue) : 0;

    private bool TryFind(int tag, out int type, out ulong count, out int valueOffset)
    {
        type = 0;
        count = 0;
        valueOffset = 0;

        for (int i = 0; i < _entryCount; i++)
        {
            int entry = _entriesStart + i * EntrySize;
            if (entry + EntrySize > _data.Length)
                return false;

            if (Read16(_data, entry, LittleEndian) != tag)
                continue;

            type = Read16(_data, entry + 2, LittleEndian);
            count = Big ? Read64(_data, entry + 4, LittleEndian) : Read32(_data, entry + 4, LittleEndian);
            int size = SizeOf(type);
            if (size == 0)
                return false;

            int valueField = entry + (Big ? 12 : 8);
            ulong total = count * (ulong)size;

            // Values that fit in the entry's own value field are stored there rather than
            // pointed at, which is four bytes in TIFF and eight in BigTIFF.
            if (total <= (ulong)InlineLimit)
            {
                valueOffset = valueField;
                return true;
            }

            ulong pointer = Big ? Read64(_data, valueField, LittleEndian) : Read32(_data, valueField, LittleEndian);
            if (pointer + total > (ulong)_data.Length)
                return false;

            valueOffset = (int)pointer;
            return true;
        }

        return false;
    }

    private bool TryReadScalar(int type, int offset, out long value)
    {
        value = 0;
        int size = SizeOf(type);
        if (size == 0 || offset < 0 || offset + size > _data.Length)
            return false;

        value = type switch
        {
            1 or 2 or 7 => _data[offset],
            3 => Read16(_data, offset, LittleEndian),
            4 or 13 => Read32(_data, offset, LittleEndian),
            6 => (sbyte)_data[offset],
            8 => (short)Read16(_data, offset, LittleEndian),
            9 => unchecked((int)Read32(_data, offset, LittleEndian)),
            16 or 18 => (long)Math.Min(Read64(_data, offset, LittleEndian), long.MaxValue),
            17 => unchecked((long)Read64(_data, offset, LittleEndian)),
            5 or 10 => ReadRationalAsLong(offset, type == 10),
            _ => 0,
        };
        return true;
    }

    private long ReadRationalAsLong(int offset, bool signed)
    {
        if (offset + 8 > _data.Length)
            return 0;
        long numerator = signed
            ? unchecked((int)Read32(_data, offset, LittleEndian))
            : Read32(_data, offset, LittleEndian);
        long denominator = signed
            ? unchecked((int)Read32(_data, offset + 4, LittleEndian))
            : Read32(_data, offset + 4, LittleEndian);
        return denominator == 0 ? 0 : numerator / denominator;
    }

    private static int SizeOf(int type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 or 16 or 17 or 18 => 8,
        _ => 0,
    };

    private static ushort Read16(ReadOnlySpan<byte> data, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..])
               : BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static uint Read32(ReadOnlySpan<byte> data, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..])
               : BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);

    private static ulong Read64(ReadOnlySpan<byte> data, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt64LittleEndian(data[offset..])
               : BinaryPrimitives.ReadUInt64BigEndian(data[offset..]);
}
