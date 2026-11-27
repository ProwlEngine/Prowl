// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// Turns the chunks a file is stored as into pixels. Each chunk holds a band of rows and an offset
/// table in front says where it begins, so the middle of a picture can be read without walking
/// what comes before. Within a chunk the rows are stored a channel at a time.
/// </summary>
internal static class ExrImageReader
{
    /// <summary>Which of the named channels a reader will show, and in what order.</summary>
    private static readonly string[] ColourNames = ["R", "G", "B", "A"];

    public static bool IsSupported(ExrHeader header) =>
        !header.Deep && header.SamplingIsSane &&
        (!header.Tiled || (header.LevelMode is 0 or 1 or 2 &&
                           header.TileWidth > 0 && header.TileHeight > 0)) &&
        header.Compression is ExrHeader.CompressionNone or ExrHeader.CompressionRle
            or ExrHeader.CompressionZips or ExrHeader.CompressionZip
            or ExrHeader.CompressionPxr24 or ExrHeader.CompressionPiz
            or ExrHeader.CompressionB44 or ExrHeader.CompressionB44A
            or ExrHeader.CompressionDwaa or ExrHeader.CompressionDwab;

    /// <summary>Channels the output carries, which is one for a grey file and three or four otherwise.</summary>
    public static int OutputChannels(ExrHeader header)
    {
        Resolve(header, out int[] map, out bool grey);
        return grey ? 1 : map[3] >= 0 ? 4 : 3;
    }

    public static PixelFormat NaturalFormat(ExrHeader header) => OutputChannels(header) switch
    {
        1 => PixelFormat.LF32,
        4 => PixelFormat.RgbaF32,
        _ => PixelFormat.RgbF32,
    };

    /// <summary>
    /// Works out which stored channel feeds each output one. A file naming red, green and blue
    /// gives them straight across; one naming a single luminance shows it as grey; anything else
    /// falls back to the first channels in the order the file lists them, which is alphabetical.
    /// </summary>
    private static void Resolve(ExrHeader header, out int[] map, out bool grey)
    {
        map = [-1, -1, -1, -1];
        List<ExrChannel> channels = header.Channels;

        for (int i = 0; i < channels.Count; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                if (map[c] < 0 && Matches(channels[i].Name, ColourNames[c]))
                    map[c] = i;
            }
        }

        grey = false;

        // A brightness and two differences is a colour picture, resolved once the samples are in.
        if (ExrLuminanceChroma.Describes(header))
        {
            for (int i = 0; i < channels.Count; i++)
            {
                switch (channels[i].Name)
                {
                    case "Y": map[0] = i; break;
                    case "RY": map[1] = i; break;
                    case "BY": map[2] = i; break;
                }
            }

            return;
        }

        if (map[0] < 0 && map[1] < 0 && map[2] < 0)
        {
            int luminance = -1;
            for (int i = 0; i < channels.Count && luminance < 0; i++)
            {
                if (Matches(channels[i].Name, "Y"))
                    luminance = i;
            }

            if (luminance >= 0 && map[3] < 0)
            {
                map[0] = map[1] = map[2] = luminance;
                grey = true;
                return;
            }

            // Channels named nothing this reader knows still have pixels, so the first stand in.
            for (int c = 0; c < 3 && c < channels.Count; c++)
                map[c] = c;

            grey = channels.Count == 1;
            if (grey)
                map[1] = map[2] = map[0];

            return;
        }

        // A channel the file leaves out reads as nothing rather than repeating another one.
        for (int c = 0; c < 3; c++)
        {
            if (map[c] < 0)
                map[c] = -1;
        }
    }

    /// <summary>Whether a channel name is the given one, allowing for a layer prefix.</summary>
    private static bool Matches(string name, string wanted) =>
        name == wanted || (name.Length > wanted.Length &&
                           name.EndsWith(wanted, StringComparison.Ordinal) &&
                           name[name.Length - wanted.Length - 1] == '.');

    public static bool TryDecode(ReadOnlySpan<byte> data, ExrHeader header, Span<byte> destination,
                                 int stride, bool flip, out ApertureError error)
    {
        Resolve(header, out int[] map, out bool grey);
        int outputs = grey ? 1 : map[3] >= 0 ? 4 : 3;

        bool read = header.Tiled
            ? TryTiles(data, header, map, outputs, destination, stride, flip, 0, out error)
            : TryScanlines(data, header, map, outputs, destination, stride, flip, out error);

        if (read && ExrLuminanceChroma.Describes(header))
        {
            ExrLuminanceChroma.ToColour(header, destination, stride, header.Width, header.Height,
                                        outputs);
        }

        return read;
    }

    /// <summary>
    /// Decodes one resolution of a tiled file. Level zero is the picture; the rest are the
    /// smaller copies of it a file may carry.
    /// </summary>
    public static bool TryDecodeLevel(ReadOnlySpan<byte> data, ExrHeader header, int level,
                                      Span<byte> destination, int stride, bool flip,
                                      out ApertureError error)
    {
        Resolve(header, out int[] map, out bool grey);
        int outputs = grey ? 1 : map[3] >= 0 ? 4 : 3;

        if (!TryTiles(data, header, map, outputs, destination, stride, flip, level, out error))
            return false;

        if (ExrLuminanceChroma.Describes(header))
        {
            List<ExrLevel> levels = ExrLevels.Enumerate(header);
            ExrLuminanceChroma.ToColour(header, destination, stride, levels[level].Width,
                                        levels[level].Height, outputs);
        }

        return true;
    }

    private static bool TryScanlines(ReadOnlySpan<byte> data, ExrHeader header, int[] map, int outputs,
                                     Span<byte> destination, int stride, bool flip, out ApertureError error)
    {
        int width = header.Width;
        int height = header.Height;
        int lines = header.LinesPerChunk;
        int chunks = (height + lines - 1) / lines;

        if (!TryReadTable(data, header.TableOffset, chunks, out long[]? table))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        long blockBytes = header.BandBytes(0, lines, width);
        if (blockBytes > int.MaxValue || blockBytes <= 0)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] block = BufferPool.Bytes.Rent((int)blockBytes);
        try
        {
            int prefix = header.ChunkPrefix;

            for (int chunk = 0; chunk < chunks; chunk++)
            {
                int at = (int)table![chunk] + prefix;
                if (at < prefix || at + 8 > data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                int y = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
                int size = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);

                if (size < 0 || at + 8 + size > data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                int first = y - header.YMin;
                int rows = Math.Min(lines, height - first);
                if (first < 0 || rows <= 0)
                    continue;

                long band = header.BandBytes(first, rows, width);
                if (band > blockBytes)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                Span<byte> raw = block.AsSpan(0, (int)band);
                if (!Expand(data.Slice(at + 8, size), header, raw, first, rows, width))
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                Place(raw, header, map, outputs, destination, stride, flip, width, height, first, rows);
            }

            error = ApertureError.None;
            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(block);
        }
    }

    private static bool TryTiles(ReadOnlySpan<byte> data, ExrHeader header, int[] map, int outputs,
                                 Span<byte> destination, int stride, bool flip, int level,
                                 out ApertureError error)
    {
        List<ExrLevel> levels = ExrLevels.Enumerate(header);
        if (level < 0 || level >= levels.Count)
        {
            error = ApertureError.InvalidData;
            return false;
        }

        ExrLevel wanted = levels[level];
        int width = wanted.Width;
        int height = wanted.Height;
        int across = wanted.Across;
        int down = wanted.Down;

        // The table covers every tile of every level, so only this level's run of it is walked.
        int total = 0;
        foreach (ExrLevel one in levels)
            total += one.Chunks;

        if (!TryReadTable(data, header.TableOffset, total, out long[]? table))
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        long blockBytes = header.BandBytes(0, header.TileHeight, header.TileWidth);
        if (blockBytes > int.MaxValue || blockBytes <= 0)
        {
            error = ApertureError.LimitExceeded;
            return false;
        }

        byte[] block = BufferPool.Bytes.Rent((int)blockBytes);
        try
        {
            int prefix = header.ChunkPrefix;

            for (int chunk = 0; chunk < wanted.Chunks; chunk++)
            {
                int at = (int)table![wanted.FirstChunk + chunk] + prefix;
                if (at < prefix || at + 20 > data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                int tileX = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
                int tileY = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);
                int levelX = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 8)..]);
                int levelY = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 12)..]);
                int size = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 16)..]);

                if (size < 0 || at + 20 + size > data.Length ||
                    (uint)tileX >= (uint)across || (uint)tileY >= (uint)down)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                // A chunk names its own level, so one filed wrongly is stepped over.
                if (levelX != wanted.X || levelY != wanted.Y)
                    continue;

                int left = tileX * header.TileWidth;
                int top = tileY * header.TileHeight;
                int columns = Math.Min(header.TileWidth, width - left);
                int rows = Math.Min(header.TileHeight, height - top);

                // An edge tile is stored at its true size, so its rows are short.
                long band = header.BandBytes(top, rows, columns);
                if (band > blockBytes)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                Span<byte> raw = block.AsSpan(0, (int)band);
                if (!Expand(data.Slice(at + 20, size), header, raw, top, rows, columns))
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                Place(raw, header, map, outputs, destination, stride, flip, columns, height,
                      top, rows, left);
            }

            error = ApertureError.None;
            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(block);
        }
    }

    private static bool TryReadTable(ReadOnlySpan<byte> data, int offset, int chunks, out long[]? table)
    {
        table = null;
        if (chunks <= 0 || offset < 0 || (long)offset + ((long)chunks * 8) > data.Length)
            return false;

        table = new long[chunks];
        for (int i = 0; i < chunks; i++)
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(data[(offset + (i * 8))..]);
            if (value > (ulong)data.Length)
                return false;

            table[i] = (long)value;
        }

        return true;
    }

    /// <summary>
    /// Undoes whatever the chunk was compressed with. A writer that found the compressed form no
    /// smaller than the plain one stores the plain one, which is why a chunk the size of its own
    /// output is copied rather than decoded.
    /// </summary>
    private static bool Expand(ReadOnlySpan<byte> source, ExrHeader header, Span<byte> destination,
                               int firstRow, int rows, int width)
    {
        if (source.Length == destination.Length)
        {
            source.CopyTo(destination);
            return true;
        }

        return header.Compression switch
        {
            ExrHeader.CompressionNone => false,
            ExrHeader.CompressionRle => ExrCodecs.TryRle(source, destination),
            ExrHeader.CompressionPxr24 => ExrCodecs.TryPxr24(source, destination, header, firstRow, rows, width),
            ExrHeader.CompressionPiz => ExrPiz.TryDecode(source, destination, header, firstRow, rows, width),
            ExrHeader.CompressionB44 or ExrHeader.CompressionB44A =>
                ExrB44.TryDecode(source, destination, header, firstRow, rows, width),
            ExrHeader.CompressionDwaa or ExrHeader.CompressionDwab =>
                ExrDwa.TryDecode(source, header, destination, firstRow, rows, width),
            _ => ExrCodecs.TryZip(source, destination),
        };
    }

    /// <summary>
    /// Copies one band of decoded rows into the picture. A channel sampled more coarsely than one
    /// value a pixel stores only some rows and only some columns, so the nearest value it does
    /// store stands in for the ones it does not.
    /// </summary>
    private static void Place(ReadOnlySpan<byte> raw, ExrHeader header, int[] map, int outputs,
                              Span<byte> destination, int stride, bool flip, int width, int height,
                              int firstRow, int rows, int left = 0)
    {
        List<ExrChannel> channels = header.Channels;

        // Where each channel last stored a row, which for a sampled one is not always this row.
        Span<int> latest = stackalloc int[16];
        latest.Clear();

        int at = 0;

        for (int row = 0; row < rows; row++)
        {
            int y = firstRow + row;

            for (int c = 0; c < channels.Count && c < 16; c++)
            {
                if (!channels[c].PresentOn(y))
                    continue;

                latest[c] = at;
                at += channels[c].SampledWidth(width) * channels[c].Bytes;
            }

            if ((uint)y >= (uint)height)
                continue;

            int to = flip ? height - 1 - y : y;
            Span<byte> line = destination.Slice((to * stride) + (left * outputs * 4), width * outputs * 4);

            // Where every output channel is a stored one at a value a pixel, they go together:
            // one pass straight down rather than one pass over the row for each.
            if (Interleaved(channels, map, outputs, latest, raw, line, width))
                continue;

            for (int c = 0; c < outputs; c++)
            {
                int index = c < 4 ? map[c] : -1;
                if (index < 0 || index >= channels.Count || index >= 16)
                {
                    for (int x = 0; x < width; x++)
                        BinaryPrimitives.WriteSingleLittleEndian(line[(((x * outputs) + c) * 4)..], 0f);

                    continue;
                }

                ExrChannel channel = channels[index];
                int size = channel.Bytes;
                int from = latest[index];
                int sampled = channel.SampledWidth(width);

                // The usual case, where the column is the pixel, so the division and the width
                // are settled outside the loop.
                if (channel.XSampling == 1)
                {
                    Spread(raw.Slice(from, width * size), channel.PixelType, line, c, outputs, width);
                    continue;
                }

                for (int x = 0; x < width; x++)
                {
                    int column = Math.Min(x / channel.XSampling, sampled - 1);
                    ReadOnlySpan<byte> value = raw[(from + (column * size))..];

                    float result = channel.PixelType switch
                    {
                        0 => BinaryPrimitives.ReadUInt32LittleEndian(value),
                        1 => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(value)),
                        _ => BinaryPrimitives.ReadSingleLittleEndian(value),
                    };

                    BinaryPrimitives.WriteSingleLittleEndian(line[(((x * outputs) + c) * 4)..], result);
                }
            }
        }
    }

    /// <summary>
    /// Writes every channel of a row in one pass, if they are all stored the same way and all
    /// hold a value for every pixel. Returns false where any of that does not hold and the
    /// general path has to do it a channel at a time.
    /// </summary>
    private static bool Interleaved(List<ExrChannel> channels, int[] map, int outputs,
                                    ReadOnlySpan<int> latest, ReadOnlySpan<byte> raw,
                                    Span<byte> line, int width)
    {
        if (outputs is not (3 or 4))
            return false;

        int type = -1;
        Span<int> from = stackalloc int[4];

        for (int c = 0; c < outputs; c++)
        {
            int index = map[c];
            if (index < 0 || index >= channels.Count || index >= 16)
                return false;

            ExrChannel channel = channels[index];
            if (channel.XSampling != 1 || (type >= 0 && channel.PixelType != type))
                return false;

            type = channel.PixelType;
            from[c] = latest[index];
        }

        if (type == 1)
        {
            // Widening the halves is most of the work, so it goes a run at a time.
            const int Run = 32;
            Span<float> widened = stackalloc float[Run * 4];

            int x = 0;
            for (; x + Run <= width; x += Run)
            {
                for (int c = 0; c < outputs; c++)
                    HalfSamples.ToSingle(raw[(from[c] + (x * 2))..], widened[(c * Run)..], Run);

                for (int i = 0; i < Run; i++)
                {
                    int to = (x + i) * outputs * 4;
                    for (int c = 0; c < outputs; c++)
                        BinaryPrimitives.WriteSingleLittleEndian(line[(to + (c * 4))..], widened[(c * Run) + i]);
                }
            }

            for (; x < width; x++)
            {
                int to = x * outputs * 4;
                for (int c = 0; c < outputs; c++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(line[(to + (c * 4))..],
                        HalfSamples.ToSingle(BinaryPrimitives.ReadUInt16LittleEndian(raw[(from[c] + (x * 2))..])));
                }
            }

            return true;
        }

        if (type == 2)
        {
            for (int x = 0; x < width; x++)
            {
                int to = x * outputs * 4;
                for (int c = 0; c < outputs; c++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(line[(to + (c * 4))..],
                        BinaryPrimitives.ReadUInt32LittleEndian(raw[(from[c] + (x * 4))..]));
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// One channel of one row into the interleaved output, a value a pixel. Which of the three
    /// stored widths the channel uses is settled once rather than at every sample.
    /// </summary>
    private static void Spread(ReadOnlySpan<byte> source, int pixelType, Span<byte> line,
                               int channel, int outputs, int width)
    {
        int at = channel * 4;
        int step = outputs * 4;

        switch (pixelType)
        {
            case 0:
                for (int x = 0; x < width; x++, at += step)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(line[at..],
                        BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]));
                }

                break;

            case 1:
                for (int x = 0; x < width; x++, at += step)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(line[at..], (float)BitConverter.UInt16BitsToHalf(
                        BinaryPrimitives.ReadUInt16LittleEndian(source[(x * 2)..])));
                }

                break;

            default:
                for (int x = 0; x < width; x++, at += step)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(line[at..],
                        BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]));
                }

                break;
        }
    }
}
