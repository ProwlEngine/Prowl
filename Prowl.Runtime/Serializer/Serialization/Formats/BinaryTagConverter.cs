using Prowl.Runtime.Serialization;
using System;
using System.IO;
using System.Linq;

namespace Prowl.Runtime.Serializer
{
    /// <summary>
    /// This class is responsible for converting CompoundTags to and from binary data.
    /// Binary data is not human-readable, so bad for git, and doesn't work well with versioning, but it is much more compact than text data.
    /// Works great for sending data over the network, or for standalone builds.
    /// </summary>
    public static class BinaryTagConverter
    {

        #region Writing
        public static void WriteToFile(CompoundTag tag, FileInfo file)
        {
            using var stream = file.OpenWrite();
            using var writer = new BinaryWriter(stream);
            WriteTo(tag, writer);
        }

        public static void WriteTo(CompoundTag tag, BinaryWriter writer) => WriteCompound(tag, writer);

        private static void WriteCompound(CompoundTag tag, BinaryWriter writer)
        {
            writer.Write(tag.SerializedType);
            writer.Write(tag.SerializedID);
            writer.Write(tag.AllTags.Count());
            foreach (var subTag in tag.Tags)
            {
                writer.Write(subTag.Key); // Compounds always need tag names
                WriteTag(subTag.Value, writer);
            }
        }

        private static void WriteTag(Tag tag, BinaryWriter writer)
        {
            var type = tag.GetTagType();
            writer.Write((byte)type);
            if (type == TagType.Null) { } // Nothing for Null
            else if(type == TagType.Byte) writer.Write(tag.ByteValue);
            else if(type == TagType.sByte) writer.Write(tag.sByteValue);
            else if (type == TagType.Short) writer.Write(tag.ShortValue);
            else if (type == TagType.Int) writer.Write(tag.IntValue);
            else if (type == TagType.Long) writer.Write(tag.LongValue);
            else if (type == TagType.UShort) writer.Write(tag.UShortValue);
            else if (type == TagType.UInt) writer.Write(tag.UIntValue);
            else if (type == TagType.ULong) writer.Write(tag.ULongValue);
            else if (type == TagType.Float) writer.Write(tag.FloatValue);
            else if (type == TagType.Double) writer.Write(tag.DoubleValue);
            else if (type == TagType.Decimal) writer.Write(tag.DecimalValue);
            else if (type == TagType.String) writer.Write(tag.StringValue);
            else if (type == TagType.ByteArray)
            {
                writer.Write(tag.ByteArrayValue.Length);
                writer.Write(tag.ByteArrayValue);
            }
            else if (type == TagType.Bool) writer.Write(tag.BoolValue);
            else if (type == TagType.List)
            {
                var listTag = (ListTag)tag;
                writer.Write(listTag.Count);
                foreach (var subTag in listTag.Tags)
                    WriteTag(subTag, writer); // Lists dont care about names, so dont need to write Tag Names inside a List
            }
            else if (type == TagType.Compound) WriteCompound((CompoundTag)tag, writer);
            else throw new Exception($"Unknown tag type: {type}");
        }

        #endregion


        #region Reading
        public static CompoundTag ReadFromFile(FileInfo file)
        {
            using var stream = file.OpenRead();
            using var reader = new BinaryReader(stream);
            return ReadFrom(reader);
        }

        public static CompoundTag ReadFrom(BinaryReader reader) => ReadCompound(reader);

        private static CompoundTag ReadCompound(BinaryReader reader)
        {
            CompoundTag tag = new();
            tag.SerializedType = reader.ReadString();
            tag.SerializedID = reader.ReadInt32();
            var tagCount = reader.ReadInt32();
            for (int i = 0; i < tagCount; i++)
            {
                tag.Add(reader.ReadString(), ReadTag(reader));
            }
            return tag;
        }

        private static Tag ReadTag(BinaryReader reader)
        {
            var type = (TagType)reader.ReadByte();
            if (type == TagType.Null) return new NullTag();
            else if(type == TagType.Byte) return new ByteTag(reader.ReadByte());
            else if (type == TagType.sByte) return new sByteTag(reader.ReadSByte());
            else if (type == TagType.Short) return new ShortTag(reader.ReadInt16());
            else if (type == TagType.Int) return new IntTag(reader.ReadInt32());
            else if (type == TagType.Long) return new LongTag(reader.ReadInt64());
            else if (type == TagType.UShort) return new UShortTag(reader.ReadUInt16());
            else if (type == TagType.UInt) return new UIntTag(reader.ReadUInt32());
            else if (type == TagType.ULong) return new ULongTag(reader.ReadUInt64());
            else if (type == TagType.Float) return new FloatTag(reader.ReadSingle());
            else if (type == TagType.Double) return new DoubleTag(reader.ReadDouble());
            else if (type == TagType.Decimal) return new DecimalTag(reader.ReadDecimal());
            else if (type == TagType.String) return new StringTag(reader.ReadString());
            else if (type == TagType.ByteArray) return new ByteArrayTag(reader.ReadBytes(reader.ReadInt32()));
            else if (type == TagType.Bool) return new BoolTag(reader.ReadBoolean());
            else if (type == TagType.List)
            {
                var listType = (TagType)reader.ReadByte();
                var listTag = new ListTag(listType);
                var tagCount = reader.ReadInt32();
                for (int i = 0; i < tagCount; i++)
                    listTag.Add(ReadTag(reader));
                return listTag;
            }
            else if (type == TagType.Compound) return ReadCompound(reader);
            else throw new Exception($"Unknown tag type: {type}");
        }

        #endregion

    }
}
