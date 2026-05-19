using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Reads a binary FBX file into an <see cref="FbxNode"/> tree. Follows the layout described in
/// the public FBX SDK documentation:
/// <code>
///   header  : 21 bytes "Kaydara FBX Binary  \x00", 2 unused, u32 version
///   node    : end_offset, prop_count, prop_list_len, u8 name_len, name, properties..., children..., sentinel
/// </code>
/// All numeric fields are little-endian. Version &gt;= 7500 uses 64-bit offsets/counts; older
/// versions use 32-bit. Property arrays may be deflate-compressed (encoding == 1).
/// </summary>
internal sealed class FbxBinaryReader
{
    private static ReadOnlySpan<byte> MagicHeader => "Kaydara FBX Binary  "u8;

    private readonly byte[] _data;
    private int _cursor;
    private readonly bool _is64Bit;

    public uint Version { get; }

    private FbxBinaryReader(byte[] data, int cursor, uint version)
    {
        _data = data;
        _cursor = cursor;
        Version = version;
        _is64Bit = version >= 7500;
    }

    /// <summary>Returns true and creates a reader if <paramref name="data"/> looks like binary FBX.</summary>
    public static bool TryCreate(byte[] data, out FbxBinaryReader? reader, out string error)
    {
        reader = null;
        error = string.Empty;
        if (data.Length < 27)
        {
            error = "File is too short to be FBX binary.";
            return false;
        }
        if (!data.AsSpan(0, MagicHeader.Length).SequenceEqual(MagicHeader))
        {
            error = "Missing 'Kaydara FBX Binary' magic; not a binary FBX file.";
            return false;
        }
        // Bytes 20, 21, 22 are 0x1A, 0x00 plus a padding byte.
        int cursor = 23;
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor, 4));
        cursor += 4;
        reader = new FbxBinaryReader(data, cursor, version);
        return true;
    }

    /// <summary>Reads every top-level node from the file into a synthetic root.</summary>
    public FbxNode ReadRoot()
    {
        var root = new FbxNode { Name = "<Root>" };
        while (_cursor < _data.Length)
        {
            var node = ReadNode();
            if (node is null)
                break; // null-sentinel block marking end of file body
            root.Children.Add(node);
        }
        return root;
    }

    private FbxNode? ReadNode()
    {
        long endOffset, propCount, propListLen;
        if (_is64Bit)
        {
            endOffset = ReadInt64();
            propCount = ReadInt64();
            propListLen = ReadInt64();
        }
        else
        {
            endOffset = ReadUInt32();
            propCount = ReadUInt32();
            propListLen = ReadUInt32();
        }

        byte nameLen = ReadByte();
        string name = ReadFixedString(nameLen);

        // A null-record marks the end of a scope (or the end of the file body).
        if (endOffset == 0 && propCount == 0 && propListLen == 0 && nameLen == 0)
            return null;

        var node = new FbxNode { Name = name };

        int propsStart = _cursor;
        for (int i = 0; i < propCount; i++)
            node.Properties.Add(ReadProperty());

        // Sanity check: bytes consumed by properties must match prop_list_len.
        int propsConsumed = _cursor - propsStart;
        if (propsConsumed != propListLen)
            throw new ImportException(
                $"FBX node '{name}': declared property length {propListLen} but consumed {propsConsumed}.");

        // Children, if any. The block ends at endOffset; the gap before then is filled with a
        // nested set of child nodes followed by a sentinel block of all-zero bytes.
        int sentinelSize = _is64Bit ? 25 : 13;
        if (_cursor < endOffset)
        {
            while (_cursor < endOffset - sentinelSize)
            {
                var child = ReadNode();
                if (child is null) break;
                node.Children.Add(child);
            }

            // Verify and skip the sentinel block.
            for (int i = 0; i < sentinelSize; i++)
            {
                if (_cursor >= _data.Length || _data[_cursor + i] != 0)
                    throw new ImportException($"FBX node '{name}': malformed sentinel block at position {_cursor + i}.");
            }
            _cursor += sentinelSize;
        }

        if (_cursor != endOffset)
            throw new ImportException(
                $"FBX node '{name}': finished at offset {_cursor} but block end declared at {endOffset}.");

        return node;
    }

    private FbxProperty ReadProperty()
    {
        byte typeByte = ReadByte();
        var type = (FbxPropertyType)typeByte;
        var p = new FbxProperty { Type = type };
        switch (type)
        {
            case FbxPropertyType.Bool:
                p.IntegerValue = ReadByte();
                return p;
            case FbxPropertyType.Int16:
                p.IntegerValue = ReadInt16();
                return p;
            case FbxPropertyType.Int32:
                p.IntegerValue = ReadInt32();
                return p;
            case FbxPropertyType.Int64:
                p.IntegerValue = ReadInt64();
                return p;
            case FbxPropertyType.Float:
                p.DoubleValue = ReadFloat();
                return p;
            case FbxPropertyType.Double:
                p.DoubleValue = ReadDouble();
                return p;
            case FbxPropertyType.String:
            {
                int length = ReadInt32();
                p.StringValue = ReadFixedString(length);
                return p;
            }
            case FbxPropertyType.Raw:
            {
                int length = ReadInt32();
                p.BlobValue = ReadFixedBytes(length);
                return p;
            }
            case FbxPropertyType.ArrayInt32:
            case FbxPropertyType.ArrayInt64:
            case FbxPropertyType.ArrayFloat:
            case FbxPropertyType.ArrayDouble:
            case FbxPropertyType.ArrayByte:
            case FbxPropertyType.ArrayBool:
                ReadArrayProperty(type, p);
                return p;
            default:
                throw new ImportException($"Unknown FBX property type byte 0x{typeByte:X2} ('{(char)typeByte}').");
        }
    }

    private void ReadArrayProperty(FbxPropertyType type, FbxProperty p)
    {
        int length = ReadInt32();
        int encoding = ReadInt32();
        int compLen = ReadInt32();
        int elementBytes = type switch
        {
            FbxPropertyType.ArrayInt32 or FbxPropertyType.ArrayFloat => 4,
            FbxPropertyType.ArrayInt64 or FbxPropertyType.ArrayDouble => 8,
            FbxPropertyType.ArrayByte or FbxPropertyType.ArrayBool => 1,
            _ => throw new ImportException($"Unsupported array property type {type}."),
        };
        int uncompressedBytes = length * elementBytes;

        byte[] raw = encoding == 0
            ? ReadFixedBytes(compLen)
            : DecompressDeflate(ReadFixedBytes(compLen), uncompressedBytes);

        if (raw.Length != uncompressedBytes)
            throw new ImportException(
                $"FBX array property {type} length mismatch: declared {length} elements ({uncompressedBytes} bytes) but got {raw.Length} bytes.");

        switch (type)
        {
            case FbxPropertyType.ArrayInt32:
            {
                var arr = new int[length];
                Buffer.BlockCopy(raw, 0, arr, 0, uncompressedBytes);
                p.IntArrayValue = arr;
                break;
            }
            case FbxPropertyType.ArrayInt64:
            {
                var arr = new long[length];
                Buffer.BlockCopy(raw, 0, arr, 0, uncompressedBytes);
                p.LongArrayValue = arr;
                break;
            }
            case FbxPropertyType.ArrayFloat:
            {
                var arr = new float[length];
                Buffer.BlockCopy(raw, 0, arr, 0, uncompressedBytes);
                p.FloatArrayValue = arr;
                break;
            }
            case FbxPropertyType.ArrayDouble:
            {
                var arr = new double[length];
                Buffer.BlockCopy(raw, 0, arr, 0, uncompressedBytes);
                p.DoubleArrayValue = arr;
                break;
            }
            case FbxPropertyType.ArrayByte:
            case FbxPropertyType.ArrayBool:
                p.BlobValue = raw;
                break;
        }
    }

    /// <summary>
    /// FBX array properties use raw zlib stream (RFC 1950) - 2-byte header then deflate, then
    /// adler32. <see cref="ZLibStream"/> handles this directly.
    /// </summary>
    private static byte[] DecompressDeflate(byte[] compressed, int expectedSize)
    {
        using var input = new MemoryStream(compressed);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        byte[] result = new byte[expectedSize];
        int read = 0;
        while (read < expectedSize)
        {
            int n = z.Read(result, read, expectedSize - read);
            if (n == 0)
                throw new ImportException($"FBX deflate ended early at {read} of {expectedSize} bytes.");
            read += n;
        }
        return result;
    }

    private byte ReadByte()
    {
        if (_cursor >= _data.Length)
            throw new ImportException("Unexpected end of FBX while reading byte.");
        return _data[_cursor++];
    }

    private short ReadInt16()
    {
        short v = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(_cursor, 2));
        _cursor += 2;
        return v;
    }

    private int ReadInt32()
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_cursor, 4));
        _cursor += 4;
        return v;
    }

    private uint ReadUInt32()
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_cursor, 4));
        _cursor += 4;
        return v;
    }

    private long ReadInt64()
    {
        long v = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(_cursor, 8));
        _cursor += 8;
        return v;
    }

    private float ReadFloat()
    {
        float v = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(_cursor, 4));
        _cursor += 4;
        return v;
    }

    private double ReadDouble()
    {
        double v = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(_cursor, 8));
        _cursor += 8;
        return v;
    }

    private string ReadFixedString(int length)
    {
        if (length == 0) return string.Empty;
        if (_cursor + length > _data.Length)
            throw new ImportException("Unexpected end of FBX while reading string.");
        // Binary FBX object names embed a 0x00 0x01 separator between class and namespace; we
        // keep them as-is. Higher layers translate them when building the document.
        string s = Encoding.UTF8.GetString(_data, _cursor, length);
        _cursor += length;
        return s;
    }

    private byte[] ReadFixedBytes(int length)
    {
        if (length == 0) return Array.Empty<byte>();
        if (_cursor + length > _data.Length)
            throw new ImportException("Unexpected end of FBX while reading raw bytes.");
        byte[] result = new byte[length];
        Buffer.BlockCopy(_data, _cursor, result, 0, length);
        _cursor += length;
        return result;
    }
}
