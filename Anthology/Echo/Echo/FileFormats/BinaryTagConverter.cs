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

internal static class BinaryTagConverter
{
    private static readonly ThreadLocal<Dictionary<string, int>> SharedEncodeDictionary = new(() => new Dictionary<string, int>(4096));
    private static readonly ThreadLocal<Dictionary<int, string>> SharedDecodeDictionary = new(() => new Dictionary<int, string>(4096));
    private static readonly ThreadLocal<StringBuilder> SharedStringBuilder = new(() => new StringBuilder(4096));
    private static readonly ThreadLocal<List<int>> SharedCodeList = new(() => new List<int>(1024));

    private static void ClearSharedCollections()
    {
        SharedEncodeDictionary.Value!.Clear();
        SharedDecodeDictionary.Value!.Clear();
        SharedStringBuilder.Value!.Clear();
        SharedCodeList.Value!.Clear();
    }


    #region LZW Compression Helpers
    private const int MaxDictionarySize = 4096;
    private const int InitialDictionarySize = 256;

    private static void InitializeEncodeDictionary()
    {
        var dict = SharedEncodeDictionary.Value!;
        dict.Clear();
        for (int i = 0; i < InitialDictionarySize; i++)
            dict[((char)i).ToString()] = i;
    }

    private static void InitializeDecodeDictionary()
    {
        var dict = SharedDecodeDictionary.Value!;
        dict.Clear();
        for (int i = 0; i < InitialDictionarySize; i++)
            dict[i] = ((char)i).ToString();
    }

    private static List<int> CompressString(ReadOnlySpan<char> input)
    {
        if (input.Length == 0)
            return new List<int>();

        InitializeEncodeDictionary();
        var codes = SharedCodeList.Value!;
        codes.Clear();

        int nextCode = InitialDictionarySize;
        string current = input[0].ToString();

        for (int i = 1; i < input.Length; i++)
        {
            string combined = current + input[i];
            if (SharedEncodeDictionary.Value!.TryGetValue(combined, out int code))
            {
                current = combined;
            }
            else
            {
                codes.Add(SharedEncodeDictionary.Value![current]);
                if (nextCode < MaxDictionarySize)
                {
                    SharedEncodeDictionary.Value![combined] = nextCode++;
                }
                current = input[i].ToString();
            }
        }

        if (current.Length > 0)
            codes.Add(SharedEncodeDictionary.Value![current]);

        return codes;
    }

    private static string DecompressString(List<int> codes)
    {
        if (codes.Count == 0)
            return string.Empty;

        InitializeDecodeDictionary();
        var sb = SharedStringBuilder.Value!;
        sb.Clear();

        string current = SharedDecodeDictionary.Value![codes[0]];
        sb.Append(current);
        int nextCode = InitialDictionarySize;

        for (int i = 1; i < codes.Count; i++)
        {
            int code = codes[i];
            string entry;

            if (SharedDecodeDictionary.Value!.TryGetValue(code, out string? value))
            {
                entry = value;
            }
            else if (code == nextCode)
            {
                entry = current + current[0];
            }
            else
            {
                throw new Exception("Invalid compressed data");
            }

            sb.Append(entry);
            if (nextCode < MaxDictionarySize)
            {
                SharedDecodeDictionary.Value![nextCode++] = current + entry[0];
            }
            current = entry;
        }

        return sb.ToString();
    }

    private static void WriteLZWCompressed(BinaryWriter writer, ReadOnlySpan<char> input)
    {
        var codes = CompressString(input);
        LEB128.WriteUnsigned(writer, (ulong)codes.Count);
        foreach (int code in codes)
            LEB128.WriteUnsigned(writer, (ulong)code);
    }

    private static string ReadLZWCompressed(BinaryReader reader)
    {
        int codesCount = (int)LEB128.ReadUnsigned(reader);
        if (codesCount == 0)
            return string.Empty;

        SharedCodeList.Value!.Clear();
        for (int i = 0; i < codesCount; i++)
            SharedCodeList.Value!.Add((int)LEB128.ReadUnsigned(reader));

        return DecompressString(SharedCodeList.Value!);
    }
    #endregion

    #region Writing
    public static void WriteToFile(EchoObject tag, FileInfo file, BinarySerializationOptions? options = null)
    {
        using var stream = file.OpenWrite();
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
            byte[] stringBytes = System.Text.Encoding.UTF8.GetBytes(subTag.Key);
            writer.Write(stringBytes.Length);
            writer.Write(stringBytes);
            WriteTag_Performance(subTag.Value, writer, options);
        }
    }

    private static void WriteCompound_Size(EchoObject tag, BinaryWriter writer, BinarySerializationOptions options)
    {
        LEB128.WriteUnsigned(writer, (ulong)tag.Count);

        foreach (var subTag in tag.Tags)
        {
            WriteLZWCompressed(writer, subTag.Key.AsSpan());
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
        else if (type == EchoType.String)
        {
            byte[] stringBytes = Encoding.UTF8.GetBytes(tag.StringValue);
            writer.Write(stringBytes.Length);
            writer.Write(stringBytes);
        }
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
            WriteLZWCompressed(writer, tag.StringValue.AsSpan());
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
        EchoObject tag = EchoObject.NewCompound();

        int tagCount = reader.ReadInt32();
        for (int i = 0; i < tagCount; i++)
        {
            int nameLength = reader.ReadInt32();
            byte[] nameBytes = reader.ReadBytes(nameLength);
            string name = Encoding.UTF8.GetString(nameBytes);
            tag.Add(name, ReadTag_Performance(reader, options));
        }

        return tag;
    }

    private static EchoObject ReadCompound_Size(BinaryReader reader, BinarySerializationOptions options)
    {
        EchoObject tag = EchoObject.NewCompound();
        int tagCount = (int)LEB128.ReadUnsigned(reader);

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
        else if (type == EchoType.Byte) return new(EchoType.Byte, reader.ReadByte());
        else if (type == EchoType.sByte) return new(EchoType.sByte, reader.ReadSByte());
        else if (type == EchoType.Short) return new(EchoType.Short, reader.ReadInt16());
        else if (type == EchoType.Int) return new(EchoType.Int, reader.ReadInt32());
        else if (type == EchoType.Long) return new(EchoType.Long, reader.ReadInt64());
        else if (type == EchoType.UShort) return new(EchoType.UShort, reader.ReadUInt16());
        else if (type == EchoType.UInt) return new(EchoType.UInt, reader.ReadUInt32());
        else if (type == EchoType.ULong) return new(EchoType.ULong, reader.ReadUInt64());
        else if (type == EchoType.Float) return new(EchoType.Float, reader.ReadSingle());
        else if (type == EchoType.Double) return new(EchoType.Double, reader.ReadDouble());
        else if (type == EchoType.Decimal) return new(EchoType.Decimal, reader.ReadDecimal());
        else if (type == EchoType.String)
        {
            int length = reader.ReadInt32();
            byte[] stringBytes = reader.ReadBytes(length);
            return new(EchoType.String, Encoding.UTF8.GetString(stringBytes));
        }
        else if (type == EchoType.ByteArray)
        {
            int length = reader.ReadInt32();
            return new(EchoType.ByteArray, reader.ReadBytes(length));
        }
        else if (type == EchoType.Bool) return new(EchoType.Bool, reader.ReadBoolean());
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
        else if (type == EchoType.Byte) return new(EchoType.Byte, reader.ReadByte());
        else if (type == EchoType.sByte) return new(EchoType.sByte, reader.ReadSByte());
        else if (type == EchoType.Short) return new(EchoType.Short, (short)LEB128.ReadSigned(reader));
        else if (type == EchoType.Int) return new(EchoType.Int, (int)LEB128.ReadSigned(reader));
        else if (type == EchoType.Long) return new(EchoType.Long, LEB128.ReadSigned(reader));
        else if (type == EchoType.UShort) return new(EchoType.UShort, (ushort)LEB128.ReadUnsigned(reader));
        else if (type == EchoType.UInt) return new(EchoType.UInt, (uint)LEB128.ReadUnsigned(reader));
        else if (type == EchoType.ULong) return new(EchoType.ULong, LEB128.ReadUnsigned(reader));
        else if (type == EchoType.Float) return new(EchoType.Float, reader.ReadSingle());
        else if (type == EchoType.Double) return new(EchoType.Double, reader.ReadDouble());
        else if (type == EchoType.Decimal) return new(EchoType.Decimal, reader.ReadDecimal());
        else if (type == EchoType.String)
        {
            return new(EchoType.String, ReadLZWCompressed(reader));
        }
        else if (type == EchoType.ByteArray)
        {
            int length = (int)LEB128.ReadUnsigned(reader);
            return new(EchoType.ByteArray, reader.ReadBytes(length));
        }
        else if (type == EchoType.Bool) return new(EchoType.Bool, reader.ReadBoolean());
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