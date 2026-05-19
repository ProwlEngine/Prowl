using System.IO;
using System.IO.Compression;

namespace Prowl.Unwrapper.Visualizer;

/// <summary>
/// Tiny PNG encoder good enough for 8-bit RGB output. Writes a single IDAT chunk
/// with zlib-compressed scanlines (filter byte 0 per row).
/// </summary>
internal static class PngWriter
{
    public static void WriteRgb(string path, byte[] rgb, int width, int height)
    {
        if (rgb.Length != width * height * 3)
            throw new System.ArgumentException("Buffer must be width*height*3 bytes.");

        using var fs = File.Create(path);

        // PNG signature.
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // IHDR
        byte[] ihdr = new byte[13];
        WriteUInt32BE(ihdr, 0, (uint)width);
        WriteUInt32BE(ihdr, 4, (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // color type: truecolor
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(fs, "IHDR", ihdr);

        // IDAT: each scanline gets a leading filter byte (0 = none).
        using var ms = new MemoryStream();
        for (int y = 0; y < height; ++y)
        {
            ms.WriteByte(0);
            ms.Write(rgb, y * width * 3, width * 3);
        }
        byte[] raw = ms.ToArray();

        using var compressed = new MemoryStream();
        // ZLibStream wraps DEFLATE with the 2-byte zlib header + Adler-32 trailer that PNG mandates.
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        WriteChunk(fs, "IDAT", compressed.ToArray());

        // IEND
        WriteChunk(fs, "IEND", System.Array.Empty<byte>());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        byte[] lengthBE = new byte[4];
        WriteUInt32BE(lengthBE, 0, (uint)data.Length);
        s.Write(lengthBE);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        // CRC-32 over type + data (PNG-style, big-endian on the wire).
        uint crc = 0xFFFFFFFFu;
        crc = Crc32Update(crc, typeBytes);
        crc = Crc32Update(crc, data);
        crc ^= 0xFFFFFFFFu;
        byte[] crcBytes = new byte[4];
        WriteUInt32BE(crcBytes, 0, crc);
        s.Write(crcBytes);
    }

    // IEEE 802.3 CRC-32. Lazy-initialised table so the encoder stays self-contained.
    private static readonly uint[] _crcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; ++i)
        {
            uint c = i;
            for (int k = 0; k < 8; ++k)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : (c >> 1);
            t[i] = c;
        }
        return t;
    }

    private static uint Crc32Update(uint crc, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; ++i)
            crc = _crcTable[(crc ^ bytes[i]) & 0xFFu] ^ (crc >> 8);
        return crc;
    }

    private static void WriteUInt32BE(byte[] dst, int offset, uint value)
    {
        dst[offset + 0] = (byte)(value >> 24);
        dst[offset + 1] = (byte)(value >> 16);
        dst[offset + 2] = (byte)(value >> 8);
        dst[offset + 3] = (byte)value;
    }
}
