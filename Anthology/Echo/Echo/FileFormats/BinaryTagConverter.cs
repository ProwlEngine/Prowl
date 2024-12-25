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
    private static readonly Dictionary<string, int> SharedEncodeDictionary = new(4096);
    private static readonly Dictionary<int, string> SharedDecodeDictionary = new(4096);
    private static readonly StringBuilder SharedStringBuilder = new(4096);
    private static readonly List<int> SharedCodeList = new(1024);

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

        // Reuse shared dictionary
        SharedEncodeDictionary.Clear();
        SharedCodeList.Clear();
        for (int i = 0; i < 256; i++)
            SharedEncodeDictionary[((char)i).ToString()] = i;

        int nextCode = 256;

        foreach (var subTag in tag.Tags)
        {
            SharedCodeList.Clear();
            ReadOnlySpan<char> keySpan = subTag.Key.AsSpan();
            if (keySpan.Length == 0)
            {
                LEB128.WriteUnsigned(writer, 0ul);
                WriteTag_Size(subTag.Value, writer, options);
                continue;
            }

            string current = keySpan[0].ToString();
            for (int i = 1; i < keySpan.Length; i++)
            {
                string combined = current + keySpan[i];
                if (SharedEncodeDictionary.TryGetValue(combined, out int code))
                {
                    current = combined;
                }
                else
                {
                    SharedCodeList.Add(SharedEncodeDictionary[current]);
                    // Only add new entries if we haven't exceeded dictionary limit
                    if (nextCode < 4096)
                    {
                        SharedEncodeDictionary[combined] = nextCode++;
                    }
                    current = keySpan[i].ToString();
                }
            }

            if (current.Length > 0)
                SharedCodeList.Add(SharedEncodeDictionary[current]);

            // Write compressed field name
            LEB128.WriteUnsigned(writer, (ulong)SharedCodeList.Count);
            foreach (int code in SharedCodeList)
                LEB128.WriteUnsigned(writer, (ulong)code);

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
            ReadOnlySpan<char> valueSpan = tag.StringValue.AsSpan();

            SharedEncodeDictionary.Clear();
            SharedCodeList.Clear();
            for (int i = 0; i < 256; i++)
                SharedEncodeDictionary[((char)i).ToString()] = i;

            int nextCode = 256;

            if (valueSpan.Length == 0)
            {
                LEB128.WriteUnsigned(writer, 0ul);
                return;
            }

            string current = valueSpan[0].ToString();
            for (int i = 1; i < valueSpan.Length; i++)
            {
                string combined = current + valueSpan[i];
                if (SharedEncodeDictionary.TryGetValue(combined, out int code))
                {
                    current = combined;
                }
                else
                {
                    SharedCodeList.Add(SharedEncodeDictionary[current]);
                    SharedEncodeDictionary[combined] = nextCode++;
                    current = valueSpan[i].ToString();
                }
            }

            if (current.Length > 0)
                SharedCodeList.Add(SharedEncodeDictionary[current]);

            LEB128.WriteUnsigned(writer, (ulong)SharedCodeList.Count);
            foreach (int code in SharedCodeList)
                LEB128.WriteUnsigned(writer, (ulong)code);
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

        SharedDecodeDictionary.Clear();
        SharedCodeList.Clear();
        SharedStringBuilder.Clear();

        for (int i = 0; i < 256; i++)
            SharedDecodeDictionary[i] = ((char)i).ToString();

        int nextCode = 256;  // Track dictionary size

        for (int i = 0; i < tagCount; i++)
        {
            int nameCodesCount = (int)LEB128.ReadUnsigned(reader);
            if (nameCodesCount == 0)
            {
                tag.Add(string.Empty, ReadTag_Size(reader, options));
                continue;
            }

            SharedCodeList.Clear();
            for (int j = 0; j < nameCodesCount; j++)
                SharedCodeList.Add((int)LEB128.ReadUnsigned(reader));

            SharedStringBuilder.Clear();
            string current = SharedDecodeDictionary[SharedCodeList[0]];
            SharedStringBuilder.Append(current);

            for (int j = 1; j < SharedCodeList.Count; j++)
            {
                int code = SharedCodeList[j];
                string entry;

                if (SharedDecodeDictionary.TryGetValue(code, out string? value))
                {
                    entry = value;
                }
                else if (code == nextCode)
                {
                    entry = current + current[0];
                }
                else
                {
                    throw new Exception($"Invalid compressed field name data: code {code} not found in dictionary");
                }

                SharedStringBuilder.Append(entry);

                // Only add new entries if we haven't exceeded dictionary limit
                if (nextCode < 4096)
                {
                    SharedDecodeDictionary[nextCode++] = current + entry[0];
                }
                current = entry;
            }

            tag.Add(SharedStringBuilder.ToString(), ReadTag_Size(reader, options));
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
            int codesCount = (int)LEB128.ReadUnsigned(reader);
            if (codesCount == 0)
                return new(EchoType.String, string.Empty);

            // Reuse shared collections
            SharedDecodeDictionary.Clear();
            SharedCodeList.Clear();
            SharedStringBuilder.Clear();

            for (int i = 0; i < 256; i++)
                SharedDecodeDictionary[i] = ((char)i).ToString();

            // Read all codes at once
            for (int i = 0; i < codesCount; i++)
                SharedCodeList.Add((int)LEB128.ReadUnsigned(reader));

            // Fast decompression
            string current = SharedDecodeDictionary[SharedCodeList[0]];
            SharedStringBuilder.Append(current);
            int nextCode = 256;

            // Process remaining codes
            for (int i = 1; i < codesCount; i++)
            {
                int code = SharedCodeList[i];
                string entry;

                if (SharedDecodeDictionary.TryGetValue(code, out string? value))
                    entry = value;
                else if (code == nextCode)
                    entry = current + current[0];
                else
                    throw new Exception("Invalid compressed string data");

                SharedStringBuilder.Append(entry);
                SharedDecodeDictionary[nextCode++] = current + entry[0];
                current = entry;
            }

            return new(EchoType.String, SharedStringBuilder.ToString());
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