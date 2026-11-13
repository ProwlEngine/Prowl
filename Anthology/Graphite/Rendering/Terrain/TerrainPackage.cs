using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Prowl.Graphite.Rendering;

/// <summary>What a terrain package says about its whole world.</summary>
public sealed record TerrainHeader
{
    /// <summary>Metres between finest level samples.</summary>
    public required double Spacing { get; init; }

    /// <summary>The height of a 0 sample and the metres from it to a 65535 sample.</summary>
    public required double Bottom { get; init; }
    public required double Range { get; init; }

    /// <summary>The world's quads along x and z at the finest level.</summary>
    public required int QuadsX { get; init; }
    public required int QuadsZ { get; init; }

    /// <summary>Levels from the finest up to the one a single page covers.</summary>
    public required int Levels { get; init; }

    /// <summary>The levels from the finest that keep control samples.</summary>
    public required int ControlLevels { get; init; }

    /// <summary>The seed placement rules hash with.</summary>
    public ulong Seed { get; init; }

    public int PagesX(int level) => Math.Max(1, (QuadsX + (TerrainPage.Quads << level) - 1) / (TerrainPage.Quads << level));
    public int PagesZ(int level) => Math.Max(1, (QuadsZ + (TerrainPage.Quads << level) - 1) / (TerrainPage.Quads << level));

    /// <summary>The level count that ends at one page for a world of these quads.</summary>
    public static int LevelsFor(int quadsX, int quadsZ)
    {
        int levels = 1;
        while ((TerrainPage.Quads << (levels - 1)) < Math.Max(quadsX, quadsZ))
            levels++;
        return levels;
    }
}

/// <summary>A page's place in a package: its height bounds per quarter and where each of its layers is stored.</summary>
internal struct TerrainPageEntry
{
    public const int Layers = 3;

    /// <summary>Lowest and highest sample of each quarter, -x -z, +x -z, -x +z, +x +z, over the finest samples it covers.</summary>
    public ushort[] Bounds;
    public long[] Offsets;
    public int[] Lengths;

    public readonly (ushort Min, ushort Max) PageBounds()
    {
        ushort min = ushort.MaxValue, max = 0;
        for (int q = 0; q < 4; q++)
        {
            min = Math.Min(min, Bounds[q * 2]);
            max = Math.Max(max, Bounds[q * 2 + 1]);
        }
        return (min, max);
    }
}

/// <summary>
/// The terrain package file: a header, then every page's layers compressed apart so a server can read heights and
/// control alone, then the page directory, see Graphite/Docs/TerrainResearch.md part 2.2.
/// </summary>
public static class TerrainPackage
{
    private const uint Magic = 0x4E525447; // GTRN
    private const int Version = 1;
    private const byte Deflate = 1;
    internal const int HeaderBytes = 88;

    /// <summary>Writes a package one page at a time: <see cref="Begin"/>, <see cref="Writer.Add"/> each page, then <see cref="Writer.Finish"/>.</summary>
    public static Writer Begin(Stream stream, TerrainHeader header) => new(stream, header);

    public sealed class Writer
    {
        private readonly Stream _stream;
        private readonly TerrainHeader _header;
        private readonly long _start;
        private readonly List<(TerrainPageKey Key, TerrainPageEntry Entry)> _pages = new();

        internal Writer(Stream stream, TerrainHeader header)
        {
            if (!stream.CanSeek || !stream.CanWrite)
                throw new ArgumentException("A terrain package needs a seekable, writable stream.", nameof(stream));
            _stream = stream;
            _header = header;
            _start = stream.Position;
            stream.Write(new byte[HeaderBytes]);
        }

        /// <summary>Adds a page with its quarter bounds, see <see cref="TerrainPageEntry.Bounds"/>.</summary>
        public void Add(TerrainPage page, ReadOnlySpan<ushort> quarterBounds)
        {
            TerrainPageEntry entry = new() { Bounds = quarterBounds.ToArray(), Offsets = new long[TerrainPageEntry.Layers], Lengths = new int[TerrainPageEntry.Layers] };
            WriteLayer(ref entry, 0, EncodeHeights(page.Heights));
            WriteLayer(ref entry, 1, Planar(page.NormalAo, 4));
            if (page.Control != null && Array.Exists(page.Control, c => c != 0))
            {
                byte[] control = new byte[TerrainPage.Samples * 4];
                Buffer.BlockCopy(page.Control, 0, control, 0, control.Length);
                WriteLayer(ref entry, 2, Planar(control, 4));
            }
            _pages.Add((page.Key, entry));
        }

        private void WriteLayer(ref TerrainPageEntry entry, int layer, byte[] raw)
        {
            entry.Offsets[layer] = _stream.Position - _start;
            using (DeflateStream deflate = new(_stream, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(raw);
            entry.Lengths[layer] = (int)(_stream.Position - _start - entry.Offsets[layer]);
        }

        public void Finish()
        {
            long directory = _stream.Position - _start;
            using (BinaryWriter writer = new(_stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                foreach ((TerrainPageKey key, TerrainPageEntry entry) in _pages)
                {
                    writer.Write((byte)key.Level);
                    writer.Write((ushort)key.X);
                    writer.Write((ushort)key.Z);
                    foreach (ushort bound in entry.Bounds)
                        writer.Write(bound);
                    for (int layer = 0; layer < TerrainPageEntry.Layers; layer++)
                    {
                        writer.Write(entry.Offsets[layer]);
                        writer.Write(entry.Lengths[layer]);
                    }
                }
                long end = _stream.Position;
                _stream.Position = _start;
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(Deflate);
                writer.Write(new byte[3]);
                writer.Write(_header.Spacing);
                writer.Write(_header.Bottom);
                writer.Write(_header.Range);
                writer.Write(_header.QuadsX);
                writer.Write(_header.QuadsZ);
                writer.Write(_header.Levels);
                writer.Write(_header.ControlLevels);
                writer.Write(_header.Seed);
                writer.Write(_pages.Count);
                writer.Write(new byte[4]);
                writer.Write(directory);
                writer.Write(new byte[HeaderBytes - 76]);
                _stream.Position = end;
            }
        }
    }

    internal static (TerrainHeader Header, Dictionary<TerrainPageKey, TerrainPageEntry> Pages) ReadDirectory(Stream stream)
    {
        using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        long start = stream.Position;
        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("Not a terrain package.");
        int version = reader.ReadInt32();
        if (version != Version)
            throw new InvalidDataException($"Terrain package version {version} is not supported.");
        byte codec = reader.ReadByte();
        if (codec != Deflate)
            throw new InvalidDataException($"Terrain package codec {codec} is not supported.");
        reader.ReadBytes(3);
        TerrainHeader header = new()
        {
            Spacing = reader.ReadDouble(),
            Bottom = reader.ReadDouble(),
            Range = reader.ReadDouble(),
            QuadsX = reader.ReadInt32(),
            QuadsZ = reader.ReadInt32(),
            Levels = reader.ReadInt32(),
            ControlLevels = reader.ReadInt32(),
            Seed = reader.ReadUInt64(),
        };
        int count = reader.ReadInt32();
        reader.ReadInt32();
        long directory = reader.ReadInt64();

        stream.Position = start + directory;
        Dictionary<TerrainPageKey, TerrainPageEntry> pages = new(count);
        for (int i = 0; i < count; i++)
        {
            TerrainPageKey key = new(reader.ReadByte(), reader.ReadUInt16(), reader.ReadUInt16());
            TerrainPageEntry entry = new() { Bounds = new ushort[8], Offsets = new long[TerrainPageEntry.Layers], Lengths = new int[TerrainPageEntry.Layers] };
            for (int b = 0; b < 8; b++)
                entry.Bounds[b] = reader.ReadUInt16();
            for (int layer = 0; layer < TerrainPageEntry.Layers; layer++)
            {
                entry.Offsets[layer] = start + reader.ReadInt64();
                entry.Lengths[layer] = reader.ReadInt32();
            }
            pages[key] = entry;
        }
        return (header, pages);
    }

    internal static TerrainPage DecodePage(TerrainPageKey key, Func<long, int, byte[]> read, in TerrainPageEntry entry)
    {
        ushort[] heights = DecodeHeights(Inflate(read(entry.Offsets[0], entry.Lengths[0]), TerrainPage.Samples * 2));
        byte[] normalAo = Interleave(Inflate(read(entry.Offsets[1], entry.Lengths[1]), TerrainPage.Samples * 4), 4);
        uint[]? control = null;
        if (entry.Lengths[2] > 0)
        {
            byte[] bytes = Interleave(Inflate(read(entry.Offsets[2], entry.Lengths[2]), TerrainPage.Samples * 4), 4);
            control = new uint[TerrainPage.Samples];
            Buffer.BlockCopy(bytes, 0, control, 0, bytes.Length);
        }
        return new TerrainPage(key, heights, normalAo, control);
    }

    private static byte[] Inflate(byte[] compressed, int length)
    {
        byte[] raw = new byte[length];
        using DeflateStream deflate = new(new MemoryStream(compressed), CompressionMode.Decompress);
        deflate.ReadExactly(raw);
        return raw;
    }

    // Each sample as its difference from the one before it in its row, the first of a row from the first of the row
    // before, then low bytes and high bytes as two planes
    private static byte[] EncodeHeights(ushort[] heights)
    {
        byte[] bytes = new byte[heights.Length * 2];
        for (int z = 0; z < TerrainPage.Size; z++)
            for (int x = 0; x < TerrainPage.Size; x++)
            {
                int i = z * TerrainPage.Size + x;
                int previous = x > 0 ? heights[i - 1] : z > 0 ? heights[i - TerrainPage.Size] : 0;
                ushort delta = (ushort)(heights[i] - previous);
                bytes[i] = (byte)delta;
                bytes[heights.Length + i] = (byte)(delta >> 8);
            }
        return bytes;
    }

    private static ushort[] DecodeHeights(byte[] bytes)
    {
        ushort[] heights = new ushort[TerrainPage.Samples];
        for (int z = 0; z < TerrainPage.Size; z++)
            for (int x = 0; x < TerrainPage.Size; x++)
            {
                int i = z * TerrainPage.Size + x;
                int previous = x > 0 ? heights[i - 1] : z > 0 ? heights[i - TerrainPage.Size] : 0;
                heights[i] = (ushort)(previous + (bytes[i] | bytes[TerrainPage.Samples + i] << 8));
            }
        return heights;
    }

    private static byte[] Planar(byte[] interleaved, int channels)
    {
        int count = interleaved.Length / channels;
        byte[] planar = new byte[interleaved.Length];
        for (int i = 0; i < count; i++)
            for (int c = 0; c < channels; c++)
                planar[c * count + i] = interleaved[i * channels + c];
        return planar;
    }

    private static byte[] Interleave(byte[] planar, int channels)
    {
        int count = planar.Length / channels;
        byte[] interleaved = new byte[planar.Length];
        for (int i = 0; i < count; i++)
            for (int c = 0; c < channels; c++)
                interleaved[i * channels + c] = planar[c * count + i];
        return interleaved;
    }
}
