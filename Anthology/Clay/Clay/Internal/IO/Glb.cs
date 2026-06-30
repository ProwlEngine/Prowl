using System.Buffers.Binary;

namespace Prowl.Clay.Internal.IO;

/// <summary>
/// Parser for the binary glTF (GLB) container.
/// </summary>
/// <remarks>
/// GLB layout (little-endian, all u32):
/// <code>
///   header   : magic(0x46546C67 "glTF") | version(2) | totalLength
///   chunk N  : length | type | data[length]
/// </code>
/// A valid GLB always starts with a JSON chunk (type 0x4E4F534A "JSON") and may be followed
/// by exactly one binary chunk (type 0x004E4942 "BIN\0"). Implementations are required to
/// ignore unknown chunk types.
/// </remarks>
internal static class Glb
{
    public const uint Magic = 0x46546C67;    // "glTF"
    public const uint JsonChunkType = 0x4E4F534A; // "JSON"
    public const uint BinChunkType = 0x004E4942; // "BIN\0"

    /// <summary>
    /// Reads a GLB file from the given seekable stream and returns the JSON chunk text plus the
    /// optional binary chunk bytes. The stream is fully read.
    /// </summary>
    public static GlbContent Read(Stream stream)
    {
        if (!stream.CanSeek)
        {
            // Copy non-seekable streams into memory so we can index into the binary chunk later.
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            stream = ms;
        }

        Span<byte> header = stackalloc byte[12];
        ReadExact(stream, header);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4));
        uint totalLength = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));

        if (magic != Magic)
            throw new ImportException($"Not a GLB file (expected magic 0x{Magic:X8}, got 0x{magic:X8}).");
        if (version != 2)
            throw new ImportException($"Unsupported GLB version {version}; only version 2 is supported.");

        long endOfFile = stream.Position - 12 + totalLength;

        // JSON chunk is required.
        Span<byte> chunkHeader = stackalloc byte[8];
        ReadExact(stream, chunkHeader);
        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[..4]);
        uint jsonType = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.Slice(4, 4));
        if (jsonType != JsonChunkType)
            throw new ImportException("GLB first chunk must be a JSON chunk.");

        byte[] jsonBytes = new byte[jsonLen];
        ReadExact(stream, jsonBytes);

        // Optional BIN chunk; further chunks (unknown types) are skipped.
        byte[]? binBytes = null;
        while (stream.Position < endOfFile)
        {
            ReadExact(stream, chunkHeader);
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[..4]);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.Slice(4, 4));

            if (type == BinChunkType)
            {
                if (binBytes is not null)
                    throw new ImportException("GLB contains more than one BIN chunk.");
                binBytes = new byte[len];
                ReadExact(stream, binBytes);
            }
            else
            {
                // Unknown chunk type: skip per spec.
                stream.Seek(len, SeekOrigin.Current);
            }
        }

        return new GlbContent(jsonBytes, binBytes);
    }

    private static void ReadExact(Stream stream, Span<byte> dst)
    {
        int totalRead = 0;
        while (totalRead < dst.Length)
        {
            int n = stream.Read(dst[totalRead..]);
            if (n == 0)
                throw new ImportException($"Unexpected end of GLB stream (needed {dst.Length}, got {totalRead}).");
            totalRead += n;
        }
    }
}

/// <summary>Result of <see cref="Glb.Read"/>: raw JSON bytes and optional binary chunk bytes.</summary>
internal readonly struct GlbContent
{
    /// <summary>Raw UTF-8 JSON document bytes from the JSON chunk.</summary>
    public readonly byte[] Json;
    /// <summary>Binary chunk bytes, or <c>null</c> if the GLB had no BIN chunk.</summary>
    public readonly byte[]? Bin;

    public GlbContent(byte[] json, byte[]? bin)
    {
        Json = json;
        Bin = bin;
    }
}
