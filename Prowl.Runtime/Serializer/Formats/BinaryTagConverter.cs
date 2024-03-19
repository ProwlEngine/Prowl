using System;
using System.IO;
using System.Linq;

namespace Prowl.Runtime
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

        private static void WriteTag(SerializedProperty tag, BinaryWriter writer)
        {
            var type = tag.GetTagType();
            writer.Write((byte)type);
            if (type == PropertyType.Null) { } // Nothing for Null
            else if (type == PropertyType.Byte) writer.Write(tag.ByteValue);
            else if (type == PropertyType.sByte) writer.Write(tag.sByteValue);
            else if (type == PropertyType.Short) writer.Write(tag.ShortValue);
            else if (type == PropertyType.Int) writer.Write(tag.IntValue);
            else if (type == PropertyType.Long) writer.Write(tag.LongValue);
            else if (type == PropertyType.UShort) writer.Write(tag.UShortValue);
            else if (type == PropertyType.UInt) writer.Write(tag.UIntValue);
            else if (type == PropertyType.ULong) writer.Write(tag.ULongValue);
            else if (type == PropertyType.Float) writer.Write(tag.FloatValue);
            else if (type == PropertyType.Double) writer.Write(tag.DoubleValue);
            else if (type == PropertyType.Decimal) writer.Write(tag.DecimalValue);
            else if (type == PropertyType.String) writer.Write(tag.StringValue);
            else if (type == PropertyType.ByteArray)
            {
                writer.Write(tag.ByteArrayValue.Length);
                writer.Write(tag.ByteArrayValue);
            }
            else if (type == PropertyType.Bool) writer.Write(tag.BoolValue);
            else if (type == PropertyType.List)
            {
                var listTag = (ListTag)tag;
                writer.Write(listTag.Count);
                foreach (var subTag in listTag.Tags)
                    WriteTag(subTag, writer); // Lists dont care about names, so dont need to write Tag Names inside a List
            }
            else if (type == PropertyType.Compound) WriteCompound((CompoundTag)tag, writer);
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

        private static SerializedProperty ReadTag(BinaryReader reader)
        {
            var type = (PropertyType)reader.ReadByte();
            if (type == PropertyType.Null) return new NullTag();
            else if (type == PropertyType.Byte) return new ByteTag(reader.ReadByte());
            else if (type == PropertyType.sByte) return new sByteTag(reader.ReadSByte());
            else if (type == PropertyType.Short) return new ShortTag(reader.ReadInt16());
            else if (type == PropertyType.Int) return new IntTag(reader.ReadInt32());
            else if (type == PropertyType.Long) return new LongTag(reader.ReadInt64());
            else if (type == PropertyType.UShort) return new UShortTag(reader.ReadUInt16());
            else if (type == PropertyType.UInt) return new UIntTag(reader.ReadUInt32());
            else if (type == PropertyType.ULong) return new ULongTag(reader.ReadUInt64());
            else if (type == PropertyType.Float) return new FloatTag(reader.ReadSingle());
            else if (type == PropertyType.Double) return new DoubleTag(reader.ReadDouble());
            else if (type == PropertyType.Decimal) return new DecimalTag(reader.ReadDecimal());
            else if (type == PropertyType.String) return new StringTag(reader.ReadString());
            else if (type == PropertyType.ByteArray) return new ByteArrayTag(reader.ReadBytes(reader.ReadInt32()));
            else if (type == PropertyType.Bool) return new BoolTag(reader.ReadBoolean());
            else if (type == PropertyType.List)
            {
                var listTag = new ListTag();
                var tagCount = reader.ReadInt32();
                for (int i = 0; i < tagCount; i++)
                    listTag.Add(ReadTag(reader));
                return listTag;
            }
            else if (type == PropertyType.Compound) return ReadCompound(reader);
            else throw new Exception($"Unknown tag type: {type}");
        }

        #endregion

    }
}
