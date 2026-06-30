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

    public void WriteTo(EchoObject tag, Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

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

    private static void WriteDocument(BinaryWriter writer, EchoObject compound)
    {
        using var buffer = new MemoryStream();
        using var bufWriter = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);

        foreach (var kvp in compound.Tags)
            WriteElement(bufWriter, kvp.Key, kvp.Value);

        bufWriter.Flush();
        var bytes = buffer.ToArray();

        // document ::= int32 e_list \x00
        writer.Write(4 + bytes.Length + 1);
        writer.Write(bytes);
        writer.Write((byte)0x00);
    }

    private static void WriteArray(BinaryWriter writer, EchoObject list)
    {
        using var buffer = new MemoryStream();
        using var bufWriter = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);

        var items = list.List;
        for (int i = 0; i < items.Count; i++)
            WriteElement(bufWriter, i.ToString(), items[i]);

        bufWriter.Flush();
        var bytes = buffer.ToArray();

        writer.Write(4 + bytes.Length + 1);
        writer.Write(bytes);
        writer.Write((byte)0x00);
    }

    private static void WriteElement(BinaryWriter writer, string name, EchoObject tag)
    {
        switch (tag.TagType)
        {
            case EchoType.Null:
                writer.Write(TypeNull);
                WriteCString(writer, name);
                break;

            case EchoType.Bool:
                writer.Write(TypeBoolean);
                WriteCString(writer, name);
                writer.Write(tag.BoolValue ? (byte)1 : (byte)0);
                break;

            case EchoType.Byte:
            case EchoType.sByte:
            case EchoType.Short:
            case EchoType.UShort:
            case EchoType.Int:
                writer.Write(TypeInt32);
                WriteCString(writer, name);
                writer.Write(Convert.ToInt32(tag.Value));
                break;

            case EchoType.UInt:
            case EchoType.Long:
                writer.Write(TypeInt64);
                WriteCString(writer, name);
                writer.Write(Convert.ToInt64(tag.Value));
                break;

            case EchoType.ULong:
                writer.Write(TypeInt64);
                WriteCString(writer, name);
                writer.Write(unchecked((long)tag.ULongValue));
                break;

            case EchoType.Float:
            case EchoType.Double:
                writer.Write(TypeDouble);
                WriteCString(writer, name);
                writer.Write(Convert.ToDouble(tag.Value));
                break;

            case EchoType.Decimal:
                // BSON has no native decimal; store as string to preserve precision
                writer.Write(TypeString);
                WriteCString(writer, name);
                WriteBsonString(writer, tag.DecimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;

            case EchoType.String:
                writer.Write(TypeString);
                WriteCString(writer, name);
                WriteBsonString(writer, tag.StringValue);
                break;

            case EchoType.ByteArray:
                writer.Write(TypeBinary);
                WriteCString(writer, name);
                var bytes = tag.ByteArrayValue;
                writer.Write(bytes.Length);
                writer.Write((byte)0x00); // Generic binary subtype
                writer.Write(bytes);
                break;

            case EchoType.List:
                writer.Write(TypeArray);
                WriteCString(writer, name);
                WriteArray(writer, tag);
                break;

            case EchoType.Compound:
                writer.Write(TypeDocument);
                WriteCString(writer, name);
                WriteDocument(writer, tag);
                break;
        }
    }

    private static void WriteCString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes);
        writer.Write((byte)0x00);
    }

    private static void WriteBsonString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length + 1); // length includes the null terminator
        writer.Write(bytes);
        writer.Write((byte)0x00);
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

            ReadCString(reader); // discard index key
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
        var bytes = new List<byte>();
        byte b;
        while ((b = reader.ReadByte()) != 0x00)
            bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string ReadBsonString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        var bytes = reader.ReadBytes(length - 1); // exclude null terminator from content
        reader.ReadByte(); // consume null terminator
        return Encoding.UTF8.GetString(bytes);
    }

    #endregion
}
