// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>One entry of the channel list: what it is called and how it is stored.</summary>
internal sealed class ExrChannel
{
    public string Name = string.Empty;

    /// <summary>0 is a thirty two bit unsigned integer, 1 is a half, 2 is a float.</summary>
    public int PixelType;

    public int XSampling = 1;
    public int YSampling = 1;

    /// <summary>Whether the channel holds light rather than a value already shaped for the eye.</summary>
    public bool Linear;

    public int Bytes => PixelType == 1 ? 2 : 4;

    /// <summary>Values this channel stores across a window that wide, which may be fewer than one a pixel.</summary>
    public int SampledWidth(int width) => (width + XSampling - 1) / XSampling;

    /// <summary>Whether this channel stores anything at all on the given row of the window.</summary>
    public bool PresentOn(int y) => YSampling <= 1 || y % YSampling == 0;

    /// <summary>Rows this channel stores out of a band starting at the given row.</summary>
    public int SampledRows(int firstRow, int rows)
    {
        if (YSampling <= 1)
            return rows;

        int count = 0;
        for (int i = 0; i < rows; i++)
        {
            if (PresentOn(firstRow + i))
                count++;
        }

        return count;
    }
}

/// <summary>
/// The header, which is a list of named attributes rather than a fixed record. Only the window,
/// the channels and the compression have to be present; an unrecognised attribute is stepped over
/// by the length it declares.
/// </summary>
internal sealed class ExrHeader
{
    private const int MaxAttributes = 4096;
    private const int MaxChannels = 1024;

    public const int CompressionNone = 0;
    public const int CompressionRle = 1;
    public const int CompressionZips = 2;
    public const int CompressionZip = 3;
    public const int CompressionPiz = 4;
    public const int CompressionPxr24 = 5;
    public const int CompressionB44 = 6;
    public const int CompressionB44A = 7;
    public const int CompressionDwaa = 8;
    public const int CompressionDwab = 9;

    public int XMin;
    public int YMin;
    public int XMax = -1;
    public int YMax = -1;

    public List<ExrChannel> Channels = [];

    public int Compression;
    public bool SawCompression;

    /// <summary>0 stores rows top to bottom, 1 bottom to top, 2 in whatever order suits.</summary>
    public int LineOrder;

    public bool Tiled;
    public bool MultiPart;
    public bool Deep;

    /// <summary>What the part calls itself, for a file that holds several.</summary>
    public string Name = string.Empty;

    /// <summary>Chunks this part holds, which a part of a multi part file states outright.</summary>
    public int ChunkCount;

    /// <summary>Which part of the file this is, and what a chunk names to say it belongs here.</summary>
    public int PartIndex;

    /// <summary>
    /// Bytes each chunk begins with before the ones it would have in a file of one part. Where a
    /// file holds several, every chunk names the part it belongs to first.
    /// </summary>
    public int ChunkPrefix => MultiPart ? 4 : 0;
    public bool LongNames;

    public int TileWidth;
    public int TileHeight;
    public int LevelMode;

    /// <summary>Whether a level that halves an odd size rounds it up rather than down.</summary>
    public int LevelRounding;

    /// <summary>
    /// The two coordinates of each of the three primaries and then of white, which say what the
    /// colours in the file mean. The format names the 709 primaries as the default, which is what
    /// stands in below when a file carries no attribute of its own.
    /// </summary>
    public readonly float[] Chromaticities =
        [0.6400f, 0.3300f, 0.3000f, 0.6000f, 0.1500f, 0.0600f, 0.3127f, 0.3290f];

    /// <summary>Where the chunk offset table begins, which is straight after the header.</summary>
    public int TableOffset;

    public int Width => XMax - XMin + 1;
    public int Height => YMax - YMin + 1;

    /// <summary>Rows one chunk carries, which each compression fixes for itself.</summary>
    public int LinesPerChunk => Compression switch
    {
        CompressionZip or CompressionPxr24 => 16,
        CompressionPiz or CompressionB44 or CompressionB44A or CompressionDwaa => 32,
        CompressionDwab => 256,
        _ => 1,
    };

    /// <summary>Whether every channel names a sampling rate that makes sense.</summary>
    public bool SamplingIsSane
    {
        get
        {
            foreach (ExrChannel channel in Channels)
            {
                if (channel.XSampling < 1 || channel.YSampling < 1)
                    return false;
            }

            return true;
        }
    }

    /// <summary>Bytes one row of the window occupies once unpacked, over a width of that many.</summary>
    public long RowBytes(int y, int width)
    {
        long total = 0;
        foreach (ExrChannel channel in Channels)
        {
            if (channel.PresentOn(y))
                total += (long)channel.SampledWidth(width) * channel.Bytes;
        }

        return total;
    }

    /// <summary>Bytes a band of rows occupies once unpacked.</summary>
    public long BandBytes(int firstRow, int rows, int width)
    {
        long total = 0;
        for (int i = 0; i < rows; i++)
            total += RowBytes(firstRow + i, width);

        return total;
    }

    public static bool TryRead(ReadOnlySpan<byte> data, out ExrHeader? header, out ApertureError error)
    {
        header = null;
        if (!TryReadAll(data, out List<ExrHeader>? parts, out error))
            return false;

        header = parts![0];
        return true;
    }

    /// <summary>
    /// Reads every part's header. A file of one part has one; a file of several holds them one
    /// after another, with an empty name where the next would start saying there are no more.
    /// </summary>
    public static bool TryReadAll(ReadOnlySpan<byte> data, out List<ExrHeader>? headers, out ApertureError error)
    {
        headers = null;

        if (data.Length < 8)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (data[0] != 0x76 || data[1] != 0x2F || data[2] != 0x31 || data[3] != 0x01)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        SpanReader reader = new(data);
        reader.Skip(4);
        reader.TryReadInt32(out int versionField);

        int version = versionField & 0xFF;
        if (version is not (1 or 2))
        {
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        bool multiPart = (versionField & (1 << 12)) != 0;
        List<ExrHeader> parts = [];

        for (int part = 0; part < MaxParts; part++)
        {
            if (!TryReadPart(ref reader, versionField, part, out ExrHeader? one, out error))
                return false;

            parts.Add(one!);

            // In a file of several parts an empty name where the next header would start is what
            // says there are no more. A file of one part has its table straight after its header.
            if (!multiPart)
                break;

            if (reader.Remaining < 1)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            if (data[reader.Position] == 0)
            {
                reader.Skip(1);
                break;
            }
        }

        // The tables of every part follow every header, one after another.
        int table = reader.Position;
        foreach (ExrHeader one in parts)
        {
            one.TableOffset = table;
            table += one.ChunkCount * 8;
        }

        headers = parts;
        error = ApertureError.None;
        return true;
    }

    /// <summary>Cap on parts, well past what any file holds.</summary>
    private const int MaxParts = 1024;

    private static bool TryReadPart(ref SpanReader reader, int versionField, int index,
                                    out ExrHeader? header, out ApertureError error)
    {
        header = null;

        ExrHeader result = new()
        {
            Tiled = (versionField & (1 << 9)) != 0,
            LongNames = (versionField & (1 << 10)) != 0,
            Deep = (versionField & (1 << 11)) != 0,
            MultiPart = (versionField & (1 << 12)) != 0,
            PartIndex = index,
        };

        bool sawDataWindow = false;
        int nameLimit = result.LongNames ? 255 : 31;

        for (int i = 0; i < MaxAttributes; i++)
        {
            if (!reader.TryReadNullTerminated(nameLimit, out string name))
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            if (name.Length == 0)
                break;

            if (!reader.TryReadNullTerminated(nameLimit, out string type) ||
                !reader.TryReadInt32(out int size) || size < 0 || size > reader.Remaining)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            int valueStart = reader.Position;

            switch (name)
            {
                case "dataWindow" when type == "box2i" && size >= 16:
                    reader.TryReadInt32(out result.XMin);
                    reader.TryReadInt32(out result.YMin);
                    reader.TryReadInt32(out result.XMax);
                    reader.TryReadInt32(out result.YMax);
                    sawDataWindow = true;
                    break;

                case "channels" when type == "chlist":
                    ReadChannels(ref reader, valueStart + size, result);
                    break;

                case "compression" when type == "compression" && size >= 1:
                    reader.TryReadByte(out byte compression);
                    result.Compression = compression;
                    result.SawCompression = true;
                    break;

                case "lineOrder" when type == "lineOrder" && size >= 1:
                    reader.TryReadByte(out byte order);
                    result.LineOrder = order;
                    break;

                case "tiles" when type == "tiledesc" && size >= 9:
                    reader.TryReadInt32(out result.TileWidth);
                    reader.TryReadInt32(out result.TileHeight);
                    reader.TryReadByte(out byte mode);
                    result.LevelMode = mode & 0xF;
                    result.LevelRounding = mode >> 4;
                    break;

                case "chromaticities" when type == "chromaticities" && size >= 32:
                {
                    for (int c = 0; c < 8; c++)
                    {
                        if (!reader.TryReadSingle(out float value))
                            break;

                        result.Chromaticities[c] = value;
                    }

                    break;
                }

                case "name" when type == "string":
                    result.Name = ReadString(ref reader, size);
                    break;

                case "chunkCount" when type == "int" && size >= 4:
                    reader.TryReadInt32(out result.ChunkCount);
                    break;

                // A part of a multi part file says outright whether it is tiled or deep, where a
                // file of one part says so in the version field instead.
                case "type" when type == "string":
                {
                    string kind = ReadString(ref reader, size);
                    result.Tiled = kind is "tiledimage" or "deeptile";
                    result.Deep = kind is "deepscanline" or "deeptile";
                    break;
                }
            }

            if (!reader.Seek(valueStart + size))
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }
        }

        if (!sawDataWindow || result.Channels.Count == 0)
        {
            error = !sawDataWindow ? ApertureError.InvalidHeader : ApertureError.NoImageData;
            return false;
        }

        header = result;
        error = ApertureError.None;
        return true;
    }

    /// <summary>Reads an attribute's value as text, which is not null terminated.</summary>
    private static string ReadString(ref SpanReader reader, int size)
    {
        if (size is < 0 or > 4096 || size > reader.Remaining)
            return string.Empty;

        return reader.TryReadBytes(size, out ReadOnlySpan<byte> value)
            ? System.Text.Encoding.UTF8.GetString(value)
            : string.Empty;
    }

    /// <summary>
    /// Walks the channel list. Each entry is a name, a pixel type, a linearity flag, three
    /// reserved bytes and the two sampling rates; an empty name ends the list.
    /// </summary>
    private static void ReadChannels(ref SpanReader reader, int limit, ExrHeader header)
    {
        for (int i = 0; i < MaxChannels && reader.Position < limit; i++)
        {
            if (!reader.TryReadNullTerminated(255, out string name) || name.Length == 0)
                return;

            if (!reader.TryReadInt32(out int pixelType) || !reader.TryReadByte(out byte linear) ||
                !reader.Skip(3) || !reader.TryReadInt32(out int xSampling) ||
                !reader.TryReadInt32(out int ySampling))
                return;

            header.Channels.Add(new ExrChannel
            {
                Name = name,
                PixelType = pixelType,
                Linear = linear != 0,
                XSampling = xSampling,
                YSampling = ySampling,
            });
        }
    }
}
