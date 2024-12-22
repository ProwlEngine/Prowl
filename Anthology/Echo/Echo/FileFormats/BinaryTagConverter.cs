// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

internal static class BinaryTagConverter
{

    #region Writing
    public static void WriteToFile(EchoObject tag, FileInfo file)
    {
        using var stream = file.OpenWrite();
        using var writer = new BinaryWriter(stream);
        WriteTo(tag, writer);
    }

    public static void WriteTo(EchoObject tag, BinaryWriter writer) => WriteCompound(tag, writer);

    private static void WriteCompound(EchoObject tag, BinaryWriter writer)
    {
        writer.Write(tag.GetAllTags().Count());
        foreach (var subTag in tag.Tags)
        {
            writer.Write(subTag.Key); // Compounds always need tag names
            WriteTag(subTag.Value, writer);
        }
    }

    private static void WriteTag(EchoObject tag, BinaryWriter writer)
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
        else if (type == EchoType.String) writer.Write(tag.StringValue);
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
                WriteTag(subTag, writer); // Lists dont care about names, so dont need to write Tag Names inside a List
        }
        else if (type == EchoType.Compound) WriteCompound(tag, writer);
        else throw new Exception($"Unknown tag type: {type}");
    }

    #endregion


    #region Reading
    public static EchoObject ReadFromFile(FileInfo file)
    {
        using var stream = file.OpenRead();
        using var reader = new BinaryReader(stream);
        return ReadFrom(reader);
    }

    public static EchoObject ReadFrom(BinaryReader reader) => ReadCompound(reader);

    private static EchoObject ReadCompound(BinaryReader reader)
    {
        EchoObject tag = EchoObject.NewCompound();
        var tagCount = reader.ReadInt32();
        for (int i = 0; i < tagCount; i++)
            tag.Add(reader.ReadString(), ReadTag(reader));
        return tag;
    }

    private static EchoObject ReadTag(BinaryReader reader)
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
        else if (type == EchoType.String) return new(EchoType.String, reader.ReadString());
        else if (type == EchoType.ByteArray) return new(EchoType.ByteArray, reader.ReadBytes(reader.ReadInt32()));
        else if (type == EchoType.Bool) return new(EchoType.Bool, reader.ReadBoolean());
        else if (type == EchoType.List)
        {
            var listTag = EchoObject.NewList();
            var tagCount = reader.ReadInt32();
            for (int i = 0; i < tagCount; i++)
                listTag.ListAdd(ReadTag(reader));
            return listTag;
        }
        else if (type == EchoType.Compound) return ReadCompound(reader);
        else throw new Exception($"Unknown tag type: {type}");
    }

    #endregion

}
