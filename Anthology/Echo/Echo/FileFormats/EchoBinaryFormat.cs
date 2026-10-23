// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

namespace Prowl.Echo;

/// <summary>
/// Specifies the binary encoding mode for the Echo format.
/// </summary>
public enum BinaryEncodingMode
{
    /// <summary>
    /// Optimized for performance with fixed-width integers.
    /// Results in larger file sizes but faster read/write operations.
    /// </summary>
    Performance,

    /// <summary>
    /// Optimized for size using LEB128 encoding for integers.
    /// Results in smaller file sizes but slightly slower read/write operations.
    /// </summary>
    Size
}

/// <summary>
/// Configuration options for binary serialization.
/// </summary>
public class BinarySerializationOptions
{
    /// <summary>
    /// Gets or sets the encoding mode for binary serialization.
    /// </summary>
    public BinaryEncodingMode EncodingMode { get; set; } = BinaryEncodingMode.Performance;

    /// <summary>
    /// Creates a new instance of BinarySerializationOptions with default settings.
    /// </summary>
    public static BinarySerializationOptions Default => new();
}

/// <summary>
/// IFileFormat wrapper around the Echo binary format.
/// Preserves all EchoObject types exactly during roundtrip.
/// </summary>
public sealed class EchoBinaryFormat : IFileFormat
{
    public static readonly EchoBinaryFormat Instance = new();

    /// <summary>
    /// Options controlling binary encoding mode (Performance vs Size).
    /// </summary>
    public BinarySerializationOptions Options { get; set; } = BinarySerializationOptions.Default;

    public void WriteTo(EchoObject tag, Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteTo(tag, writer, Options);
    }

    public EchoObject ReadFrom(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return ReadFrom(reader, Options);
    }

    #region LZW Compression
    // Codes below 256 are single bytes, every later code is an earlier code plus one byte.
    private const int MaxDictionarySize = 4096;
    private const int InitialDictionarySize = 256;
    private const int HashSize = 8192;

    [ThreadStatic] private static LzwBuffers? _lzw;

    private sealed class LzwBuffers
    {
        // Encoder table from (prefix code << 8 | byte) to code. A slot belongs to the current string only when its stamp matches.
        public readonly int[] HashKeys = new int[HashSize];
        public readonly int[] HashCodes = new int[HashSize];
        public readonly int[] HashStamps = new int[HashSize];
        public int Stamp;

        public readonly int[] Prefix = new int[MaxDictionarySize];
        public readonly byte[] Suffix = new byte[MaxDictionarySize];
        public readonly int[] Length = new int[MaxDictionarySize];

        public byte[] Bytes = new byte[256];
        public int[] Codes = new int[256];

        public int NextStamp()
        {
            if (Stamp == int.MaxValue)
            {
                Array.Clear(HashStamps, 0, HashSize);
                Stamp = 0;
            }
            return ++Stamp;
        }
    }

    private static LzwBuffers Lzw => _lzw ??= new LzwBuffers();

    // Compresses the UTF-8 bytes rather than the chars, so any Unicode text works with a 256 symbol dictionary.
    private static void WriteLZWCompressed(BinaryWriter writer, string input)
    {
        var lzw = Lzw;
        int byteCount = Encoding.UTF8.GetByteCount(input);
        if (byteCount == 0)
        {
            LEB128.WriteUnsigned(writer, 0);
            return;
        }

        if (lzw.Bytes.Length < byteCount)
            lzw.Bytes = new byte[Math.Max(byteCount, lzw.Bytes.Length * 2)];
        byte[] bytes = lzw.Bytes;
        Encoding.UTF8.GetBytes(input, 0, input.Length, bytes, 0);

        if (lzw.Codes.Length < byteCount)
            lzw.Codes = new int[Math.Max(byteCount, lzw.Codes.Length * 2)];
        int[] codes = lzw.Codes;

        int stamp = lzw.NextStamp();
        int count = 0;
        int nextCode = InitialDictionarySize;
        int current = bytes[0];

        for (int i = 1; i < byteCount; i++)
        {
            byte next = bytes[i];
            int key = (current << 8) | next;
            int slot = (int)(((uint)key * 0x9E3779B1u) >> 19);
            int found = -1;
            while (lzw.HashStamps[slot] == stamp)
            {
                if (lzw.HashKeys[slot] == key)
                {
                    found = lzw.HashCodes[slot];
                    break;
                }
                slot = (slot + 1) & (HashSize - 1);
            }

            if (found >= 0)
            {
                current = found;
                continue;
            }

            codes[count++] = current;
            if (nextCode < MaxDictionarySize)
            {
                lzw.HashStamps[slot] = stamp;
                lzw.HashKeys[slot] = key;
                lzw.HashCodes[slot] = nextCode++;
            }
            current = next;
        }
        codes[count++] = current;

        LEB128.WriteUnsigned(writer, (ulong)count);
        for (int i = 0; i < count; i++)
            LEB128.WriteUnsigned(writer, (ulong)codes[i]);
    }

    private static string ReadLZWCompressed(BinaryReader reader)
    {
        int codesCount = (int)LEB128.ReadUnsigned(reader);
        if (codesCount == 0)
            return string.Empty;

        var lzw = Lzw;
        byte[] output = lzw.Bytes;
        int outLength = 0;
        int nextCode = InitialDictionarySize;
        int previous = -1;

        for (int i = 0; i < codesCount; i++)
        {
            int code = (int)LEB128.ReadUnsigned(reader);
            int start = outLength;

            if (code < nextCode)
            {
                int length = code < InitialDictionarySize ? 1 : lzw.Length[code];
                EnsureCapacity(ref output, outLength + length);
                WriteEntry(lzw, output, code, start + length - 1);
                outLength += length;
            }
            else if (code == nextCode && previous >= 0)
            {
                int length = previous < InitialDictionarySize ? 1 : lzw.Length[previous];
                EnsureCapacity(ref output, outLength + length + 1);
                WriteEntry(lzw, output, previous, start + length - 1);
                output[start + length] = output[start];
                outLength += length + 1;
            }
            else
            {
                throw new Exception("Invalid compressed data");
            }

            if (previous >= 0 && nextCode < MaxDictionarySize)
            {
                lzw.Prefix[nextCode] = previous;
                lzw.Suffix[nextCode] = output[start];
                lzw.Length[nextCode] = (previous < InitialDictionarySize ? 1 : lzw.Length[previous]) + 1;
                nextCode++;
            }
            previous = code;
        }

        lzw.Bytes = output;
        return Encoding.UTF8.GetString(output, 0, outLength);
    }

    // Writes a code's bytes backwards, ending at index end.
    private static void WriteEntry(LzwBuffers lzw, byte[] output, int code, int end)
    {
        while (code >= InitialDictionarySize)
        {
            output[end--] = lzw.Suffix[code];
            code = lzw.Prefix[code];
        }
        output[end] = (byte)code;
    }

    private static void EnsureCapacity(ref byte[] buffer, int size)
    {
        if (buffer.Length < size)
            Array.Resize(ref buffer, Math.Max(size, buffer.Length * 2));
    }
    #endregion

    #region Writing

    // Length prefixed UTF-8, as an Int32 byte count followed by the bytes.
    private static void WriteUtf8(BinaryWriter writer, string value)
    {
        var lzw = Lzw;
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (lzw.Bytes.Length < byteCount)
            lzw.Bytes = new byte[Math.Max(byteCount, lzw.Bytes.Length * 2)];
        Encoding.UTF8.GetBytes(value, 0, value.Length, lzw.Bytes, 0);
        writer.Write(byteCount);
        writer.Write(lzw.Bytes, 0, byteCount);
    }

    public static void WriteToFile(EchoObject tag, FileInfo file, BinarySerializationOptions? options = null)
    {
        using var stream = file.Create();
        using var writer = new BinaryWriter(stream);
        WriteTo(tag, writer, options);
    }

    public static void WriteTo(EchoObject tag, BinaryWriter writer, BinarySerializationOptions? options = null)
    {
        options ??= BinarySerializationOptions.Default;
        if (options.EncodingMode == BinaryEncodingMode.Size)
            WriteTag_Size(tag, writer, options);
        else
            WriteTag_Performance(tag, writer, options);
    }

    private static void WriteCompound_Performance(EchoObject tag, BinaryWriter writer, BinarySerializationOptions options)
    {

        writer.Write(tag.Count);
        foreach (var subTag in tag.Tags)
        {
            WriteUtf8(writer, subTag.Key);
            WriteTag_Performance(subTag.Value, writer, options);
        }
    }

    private static void WriteCompound_Size(EchoObject tag, BinaryWriter writer, BinarySerializationOptions options)
    {
        LEB128.WriteUnsigned(writer, (ulong)tag.Count);

        foreach (var subTag in tag.Tags)
        {
            WriteLZWCompressed(writer, subTag.Key);
            WriteTag_Size(subTag.Value, writer, options);
        }
    }

    private static void WriteTag_Performance(EchoObject tag, BinaryWriter writer, BinarySerializationOptions options)
    {
        var type = tag.TagType;
        writer.Write((byte)type);

        if (type == EchoType.Null) { } // Nothing for Null
        else if (type == EchoType.Byte) writer.Write(tag.ByteValue);
        else if (type == EchoType.sByte) writer.Write(tag.sByteValue);
        else if (type == EchoType.Short) writer.Write(tag.ShortValue);
        else if (type == EchoType.Int) writer.Write(tag.IntValue);
        else if (type == EchoType.Long) writer.Write(tag.LongValue);
        else if (type == EchoType.UShort) writer.Write(tag.UShortValue);
        else if (type == EchoType.UInt) writer.Write(tag.UIntValue);
        else if (type == EchoType.ULong) writer.Write(tag.ULongValue);
        else if (type == EchoType.Float) writer.Write(tag.FloatValue);
        else if (type == EchoType.Double) writer.Write(tag.DoubleValue);
        else if (type == EchoType.Decimal) writer.Write(tag.DecimalValue);
        else if (type == EchoType.String) WriteUtf8(writer, tag.StringValue);
        else if (type == EchoType.ByteArray)
        {
            writer.Write(tag.ByteArrayValue.Length);
            writer.Write(tag.ByteArrayValue);
        }
        else if (type == EchoType.Bool) writer.Write(tag.BoolValue);
        else if (type == EchoType.List)
        {
            var listTag = tag;
            writer.Write(listTag.Count);
            foreach (var subTag in listTag.List)
                WriteTag_Performance(subTag, writer, options);
        }
        else if (type == EchoType.Compound) WriteCompound_Performance(tag, writer, options);
        else throw new Exception($"Unknown tag type: {type}");
    }

    private static void WriteTag_Size(EchoObject tag, BinaryWriter writer, BinarySerializationOptions options)
    {
        var type = tag.TagType;
        writer.Write((byte)type);

        if (type == EchoType.Null) { } // Nothing for Null
        else if (type == EchoType.Byte) writer.Write(tag.ByteValue);
        else if (type == EchoType.sByte) writer.Write(tag.sByteValue);
        else if (type == EchoType.Short) LEB128.WriteSigned(writer, tag.ShortValue);
        else if (type == EchoType.Int) LEB128.WriteSigned(writer, tag.IntValue);
        else if (type == EchoType.Long) LEB128.WriteSigned(writer, tag.LongValue);
        else if (type == EchoType.UShort) LEB128.WriteUnsigned(writer, tag.UShortValue);
        else if (type == EchoType.UInt) LEB128.WriteUnsigned(writer, tag.UIntValue);
        else if (type == EchoType.ULong) LEB128.WriteUnsigned(writer, tag.ULongValue);
        else if (type == EchoType.Float) writer.Write(tag.FloatValue);
        else if (type == EchoType.Double) writer.Write(tag.DoubleValue);
        else if (type == EchoType.Decimal) writer.Write(tag.DecimalValue);
        else if (type == EchoType.String)
        {
            WriteLZWCompressed(writer, tag.StringValue);
        }
        else if (type == EchoType.ByteArray)
        {
            LEB128.WriteUnsigned(writer, (ulong)tag.ByteArrayValue.Length);
            writer.Write(tag.ByteArrayValue);
        }
        else if (type == EchoType.Bool) writer.Write(tag.BoolValue);
        else if (type == EchoType.List)
        {
            var listTag = tag;
            LEB128.WriteUnsigned(writer, (ulong)listTag.Count);
            foreach (var subTag in listTag.List)
                WriteTag_Size(subTag, writer, options);
        }
        else if (type == EchoType.Compound) WriteCompound_Size(tag, writer, options);
        else throw new Exception($"Unknown tag type: {type}");
    }

    #endregion

    #region Reading

    private static string ReadUtf8(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length == 0)
            return string.Empty;

        var lzw = Lzw;
        if (lzw.Bytes.Length < length)
            lzw.Bytes = new byte[Math.Max(length, lzw.Bytes.Length * 2)];

        int read = 0;
        while (read < length)
        {
            int n = reader.Read(lzw.Bytes, read, length - read);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
        return Encoding.UTF8.GetString(lzw.Bytes, 0, length);
    }

    public static EchoObject ReadFromFile(FileInfo file, BinarySerializationOptions? options = null)
    {
        using var stream = file.OpenRead();
        using var reader = new BinaryReader(stream);
        return ReadFrom(reader, options);
    }

    public static EchoObject ReadFrom(BinaryReader reader, BinarySerializationOptions? options = null)
    {
        options ??= BinarySerializationOptions.Default;
        if (options.EncodingMode == BinaryEncodingMode.Size)
            return ReadTag_Size(reader, options);
        else
            return ReadTag_Performance(reader, options);
    }

    private static EchoObject ReadCompound_Performance(BinaryReader reader, BinarySerializationOptions options)
    {
        int tagCount = reader.ReadInt32();
        EchoObject tag = EchoObject.NewCompound(tagCount);
        for (int i = 0; i < tagCount; i++)
        {
            tag.Add(ReadUtf8(reader), ReadTag_Performance(reader, options));
        }

        return tag;
    }

    private static EchoObject ReadCompound_Size(BinaryReader reader, BinarySerializationOptions options)
    {
        int tagCount = (int)LEB128.ReadUnsigned(reader);
        EchoObject tag = EchoObject.NewCompound(tagCount);

        for (int i = 0; i < tagCount; i++)
        {
            string name = ReadLZWCompressed(reader);
            tag.Add(name, ReadTag_Size(reader, options));
        }

        return tag;
    }

    private static EchoObject ReadTag_Performance(BinaryReader reader, BinarySerializationOptions options)
    {
        var type = (EchoType)reader.ReadByte();

        if (type == EchoType.Null) return new(EchoType.Null, null);
        else if (type == EchoType.Byte) return new(reader.ReadByte());
        else if (type == EchoType.sByte) return new(reader.ReadSByte());
        else if (type == EchoType.Short) return new(reader.ReadInt16());
        else if (type == EchoType.Int) return new(reader.ReadInt32());
        else if (type == EchoType.Long) return new(reader.ReadInt64());
        else if (type == EchoType.UShort) return new(reader.ReadUInt16());
        else if (type == EchoType.UInt) return new(reader.ReadUInt32());
        else if (type == EchoType.ULong) return new(reader.ReadUInt64());
        else if (type == EchoType.Float) return new(reader.ReadSingle());
        else if (type == EchoType.Double) return new(reader.ReadDouble());
        else if (type == EchoType.Decimal) return new(reader.ReadDecimal());
        else if (type == EchoType.String) return new(ReadUtf8(reader));
        else if (type == EchoType.ByteArray)
        {
            int length = reader.ReadInt32();
            return new(EchoType.ByteArray, reader.ReadBytes(length));
        }
        else if (type == EchoType.Bool) return new(reader.ReadBoolean());
        else if (type == EchoType.List)
        {
            var listTag = EchoObject.NewList();
            int tagCount = reader.ReadInt32();
            for (int i = 0; i < tagCount; i++)
                listTag.ListAdd(ReadTag_Performance(reader, options));
            return listTag;
        }
        else if (type == EchoType.Compound) return ReadCompound_Performance(reader, options);
        else throw new Exception($"Unknown tag type: {type}");
    }

    private static EchoObject ReadTag_Size(BinaryReader reader, BinarySerializationOptions options)
    {
        var type = (EchoType)reader.ReadByte();

        if (type == EchoType.Null) return new(EchoType.Null, null);
        else if (type == EchoType.Byte) return new(reader.ReadByte());
        else if (type == EchoType.sByte) return new(reader.ReadSByte());
        else if (type == EchoType.Short) return new((short)LEB128.ReadSigned(reader));
        else if (type == EchoType.Int) return new((int)LEB128.ReadSigned(reader));
        else if (type == EchoType.Long) return new(LEB128.ReadSigned(reader));
        else if (type == EchoType.UShort) return new((ushort)LEB128.ReadUnsigned(reader));
        else if (type == EchoType.UInt) return new((uint)LEB128.ReadUnsigned(reader));
        else if (type == EchoType.ULong) return new(LEB128.ReadUnsigned(reader));
        else if (type == EchoType.Float) return new(reader.ReadSingle());
        else if (type == EchoType.Double) return new(reader.ReadDouble());
        else if (type == EchoType.Decimal) return new(reader.ReadDecimal());
        else if (type == EchoType.String)
        {
            return new(EchoType.String, ReadLZWCompressed(reader));
        }
        else if (type == EchoType.ByteArray)
        {
            int length = (int)LEB128.ReadUnsigned(reader);
            return new(EchoType.ByteArray, reader.ReadBytes(length));
        }
        else if (type == EchoType.Bool) return new(reader.ReadBoolean());
        else if (type == EchoType.List)
        {
            var listTag = EchoObject.NewList();
            int tagCount = (int)LEB128.ReadUnsigned(reader);
            for (int i = 0; i < tagCount; i++)
                listTag.ListAdd(ReadTag_Size(reader, options));
            return listTag;
        }
        else if (type == EchoType.Compound) return ReadCompound_Size(reader, options);
        else throw new Exception($"Unknown tag type: {type}");
    }

    #endregion
}
