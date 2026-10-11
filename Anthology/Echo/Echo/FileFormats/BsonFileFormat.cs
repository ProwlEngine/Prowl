// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

namespace Prowl.Echo;

/// <summary>
/// BSON (Binary JSON) file format for EchoObject serialization.
/// Implements the BSON spec (bsonspec.org) with no external dependencies.
/// Note: Some type precision may be lost (e.g., byte/short → int32, float → double, decimal → string).
/// For exact type preservation, use EchoTextFormat or EchoBinaryFormat.
/// </summary>
public sealed class BsonFileFormat : IFileFormat
{
    public static readonly BsonFileFormat Instance = new();

    // BSON element type bytes
    private const byte TypeDouble = 0x01;
    private const byte TypeString = 0x02;
    private const byte TypeDocument = 0x03;
    private const byte TypeArray = 0x04;
    private const byte TypeBinary = 0x05;
    private const byte TypeBoolean = 0x08;
    private const byte TypeNull = 0x0A;
    private const byte TypeInt32 = 0x10;
    private const byte TypeInt64 = 0x12;

    // Documents are written into one buffer and their length prefixes filled in afterwards.
    [ThreadStatic] private static MemoryStream? _documentBuffer;
    [ThreadStatic] private static byte[]? _scratch;

    private static byte[] Scratch(int size)
    {
        if (_scratch == null || _scratch.Length < size)
            _scratch = new byte[Math.Max(size, 256)];
        return _scratch;
    }

    public void WriteTo(EchoObject tag, Stream stream)
    {
        var buffer = _documentBuffer ??= new MemoryStream();
        buffer.SetLength(0);

        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            if (tag.TagType == EchoType.Compound)
            {
                WriteDocument(writer, tag);
            }
            else
            {
                // BSON requires a document at the top level; wrap non-compound values
                var wrapper = EchoObject.NewCompound();
                wrapper.Add("__echo_wrapped_value__", tag.Clone());
                WriteDocument(writer, wrapper);
            }
        }

        buffer.WriteTo(stream);
        if (buffer.Capacity > 16 * 1024 * 1024)
            _documentBuffer = null;
    }

    public EchoObject ReadFrom(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var result = ReadDocument(reader);

        // Unwrap if it was a non-compound value we wrapped during writing
        if (result.TagType == EchoType.Compound && result.Count == 1 && result.TryGet("__echo_wrapped_value__", out var inner))
            return inner;

        return result;
    }

    #region Writing

    // document ::= int32 e_list \x00, where the int32 counts the whole document.
    private static void WriteDocument(BinaryWriter writer, EchoObject compound)
    {
        long start = BeginDocument(writer);
        foreach (var kvp in compound.Tags)
        {
            WriteElementType(writer, kvp.Value);
            WriteCString(writer, kvp.Key);
            WriteElementValue(writer, kvp.Value);
        }
        EndDocument(writer, start);
    }

    private static void WriteArray(BinaryWriter writer, EchoObject list)
    {
        long start = BeginDocument(writer);
        var items = list.List;
        for (int i = 0; i < items.Count; i++)
        {
            WriteElementType(writer, items[i]);
            WriteIndexName(writer, i);
            WriteElementValue(writer, items[i]);
        }
        EndDocument(writer, start);
    }

    private static long BeginDocument(BinaryWriter writer)
    {
        long start = writer.BaseStream.Position;
        writer.Write(0);
        return start;
    }

    private static void EndDocument(BinaryWriter writer, long start)
    {
        writer.Write((byte)0x00);
        var stream = writer.BaseStream;
        long end = stream.Position;
        stream.Position = start;
        writer.Write((int)(end - start));
        stream.Position = end;
    }

    private static void WriteElementType(BinaryWriter writer, EchoObject tag)
    {
        writer.Write(tag.TagType switch
        {
            EchoType.Null => TypeNull,
            EchoType.Bool => TypeBoolean,
            EchoType.Byte or EchoType.sByte or EchoType.Short or EchoType.UShort or EchoType.Int => TypeInt32,
            EchoType.UInt or EchoType.Long or EchoType.ULong => TypeInt64,
            EchoType.Float or EchoType.Double => TypeDouble,
            EchoType.Decimal or EchoType.String => TypeString,
            EchoType.ByteArray => TypeBinary,
            EchoType.List => TypeArray,
            _ => TypeDocument
        });
    }

    private static void WriteElementValue(BinaryWriter writer, EchoObject tag)
    {
        switch (tag.TagType)
        {
            case EchoType.Bool:
                writer.Write(tag.BoolValue ? (byte)1 : (byte)0);
                break;

            case EchoType.Byte:
            case EchoType.sByte:
            case EchoType.Short:
            case EchoType.UShort:
            case EchoType.Int:
                writer.Write((int)tag.IntegerBits);
                break;

            case EchoType.UInt:
            case EchoType.Long:
                writer.Write(tag.IntegerBits);
                break;

            case EchoType.ULong:
                writer.Write(unchecked((long)tag.ULongValue));
                break;

            case EchoType.Float:
                writer.Write((double)tag.FloatValue);
                break;

            case EchoType.Double:
                writer.Write(tag.DoubleValue);
                break;

            case EchoType.Decimal:
                // BSON has no native decimal; store as string to preserve precision
                WriteBsonString(writer, tag.DecimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;

            case EchoType.String:
                WriteBsonString(writer, tag.StringValue);
                break;

            case EchoType.ByteArray:
                var bytes = tag.ByteArrayValue;
                writer.Write(bytes.Length);
                writer.Write((byte)0x00); // Generic binary subtype
                writer.Write(bytes);
                break;

            case EchoType.List:
                WriteArray(writer, tag);
                break;

            case EchoType.Compound:
                WriteDocument(writer, tag);
                break;
        }
    }

    private static void WriteIndexName(BinaryWriter writer, int index)
    {
        Span<byte> digits = stackalloc byte[11];
        System.Buffers.Text.Utf8Formatter.TryFormat(index, digits, out int length);
        writer.Write(digits.Slice(0, length));
        writer.Write((byte)0x00);
    }

    private static int WriteUtf8(BinaryWriter writer, string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        var bytes = Scratch(count);
        Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
        writer.Write(bytes, 0, count);
        return count;
    }

    private static void WriteCString(BinaryWriter writer, string value)
    {
        WriteUtf8(writer, value);
        writer.Write((byte)0x00);
    }

    private static void WriteBsonString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetByteCount(value) + 1); // length includes the null terminator
        WriteCString(writer, value);
    }

    #endregion

    #region Reading

    private static EchoObject ReadDocument(BinaryReader reader)
    {
        int size = reader.ReadInt32();
        var compound = EchoObject.NewCompound();

        while (true)
        {
            byte type = reader.ReadByte();
            if (type == 0x00) break;

            string name = ReadCString(reader);
            var value = ReadValue(reader, type);
            compound.Add(name, value);
        }

        return compound;
    }

    private static EchoObject ReadArray(BinaryReader reader)
    {
        int size = reader.ReadInt32();
        var list = EchoObject.NewList();

        while (true)
        {
            byte type = reader.ReadByte();
            if (type == 0x00) break;

            SkipCString(reader); // discard index key
            var value = ReadValue(reader, type);
            list.ListAdd(value);
        }

        return list;
    }

    private static EchoObject ReadValue(BinaryReader reader, byte type)
    {
        switch (type)
        {
            case TypeDouble:
                return new EchoObject(reader.ReadDouble());

            case TypeString:
                return new EchoObject(ReadBsonString(reader));

            case TypeDocument:
                return ReadDocument(reader);

            case TypeArray:
                return ReadArray(reader);

            case TypeBinary:
                int binLength = reader.ReadInt32();
                byte subtype = reader.ReadByte();
                if (subtype == 0x02)
                {
                    // Old binary subtype has an additional int32 inner length
                    int innerLength = reader.ReadInt32();
                    return new EchoObject(reader.ReadBytes(innerLength));
                }
                return new EchoObject(reader.ReadBytes(binLength));

            case TypeBoolean:
                return new EchoObject(reader.ReadByte() != 0);

            case TypeNull:
                return new EchoObject(EchoType.Null, null);

            case TypeInt32:
                return new EchoObject(reader.ReadInt32());

            case TypeInt64:
                return new EchoObject(reader.ReadInt64());

            default:
                throw new InvalidDataException($"Unknown BSON type: 0x{type:X2}");
        }
    }

    private static string ReadCString(BinaryReader reader)
    {
        var bytes = Scratch(64);
        int count = 0;
        byte b;
        while ((b = reader.ReadByte()) != 0x00)
        {
            if (count == bytes.Length)
            {
                Array.Resize(ref _scratch, count * 2);
                bytes = _scratch;
            }
            bytes[count++] = b;
        }
        return Encoding.UTF8.GetString(bytes, 0, count);
    }

    private static void SkipCString(BinaryReader reader)
    {
        while (reader.ReadByte() != 0x00) { }
    }

    private static string ReadBsonString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        int count = length - 1; // exclude null terminator from content
        if (count < 0)
            reader.ReadBytes(count); // throws like it always has

        // Like ReadBytes, a short stream yields what it has and the terminator read below throws.
        var bytes = Scratch(count);
        int read = 0;
        while (read < count)
        {
            int n = reader.Read(bytes, read, count - read);
            if (n == 0) break;
            read += n;
        }
        reader.ReadByte(); // consume null terminator
        return Encoding.UTF8.GetString(bytes, 0, read);
    }

    #endregion
}
