using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Prowl.Runtime
{
    public enum TagType
    {
        Null = 0,
        Byte,
        sByte,
        Short,
        Int,
        Long,
        UShort,
        UInt,
        ULong,
        Float,
        Double,
        Decimal,
        String,
        ByteArray,
        Bool,
        List,
        Compound,
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(NullTag), "NULL")]
    [JsonDerivedType(typeof(ByteTag), "B")]
    [JsonDerivedType(typeof(sByteTag), "sB")]
    [JsonDerivedType(typeof(ShortTag), "S")]
    [JsonDerivedType(typeof(IntTag), "I")]
    [JsonDerivedType(typeof(LongTag), "L")]
    [JsonDerivedType(typeof(UShortTag), "uS")]
    [JsonDerivedType(typeof(UIntTag), "uI")]
    [JsonDerivedType(typeof(ULongTag), "uL")]
    [JsonDerivedType(typeof(FloatTag), "F")]
    [JsonDerivedType(typeof(DoubleTag), "D")]
    [JsonDerivedType(typeof(DecimalTag), "DEC")]
    [JsonDerivedType(typeof(StringTag), "STR")]
    [JsonDerivedType(typeof(ByteArrayTag), "BARR")]
    [JsonDerivedType(typeof(BoolTag), "BOOL")]
    [JsonDerivedType(typeof(ListTag), "LIST")]
    [JsonDerivedType(typeof(CompoundTag), "COMPOUND")]
    public abstract class Tag
    {
        public Tag() { }

        public abstract object GetValue();
        public abstract TagType GetTagType();
        public abstract Tag Clone();

        #region Shortcuts

        /// <summary> Returns true if tags of this type have a value attached.
        /// All tags except Compound, List, and End have values. </summary>
        [JsonIgnore]
        public bool HasValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Compound => false,
                    TagType.Null => false,
                    _ => true
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as a bool.
        /// Only supported by ByteTag, ShortTag, IntTag, LongTag, StringTag tags. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than ByteTag. </exception>
        [JsonIgnore]
        public bool BoolValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Bool => Convert.ToBoolean(((BoolTag)this).Value),
                    TagType.Byte => Convert.ToBoolean(((ByteTag)this).Value),
                    TagType.Short => Convert.ToBoolean(((ShortTag)this).Value),
                    TagType.Int => Convert.ToBoolean(((IntTag)this).Value),
                    TagType.Long => Convert.ToBoolean(((LongTag)this).Value),
                    TagType.String => Convert.ToBoolean(((StringTag)this).Value),
                    _ => throw new InvalidCastException("Cannot get BoolValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as a byte.
        /// Only supported by ByteTag tags. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than ByteTag. </exception>
        [JsonIgnore]
        public byte ByteValue
        {
            get
            {
                if (GetTagType() == TagType.Byte) return ((ByteTag)this).Value;
                else throw new InvalidCastException("Cannot get ByteValue from " + ToString());
            }
        }

        /// <summary> Returns the value of this tag, cast as a sbyte.
        /// Only supported by sByteTag tags. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than sByteTag. </exception>
        [JsonIgnore]
        public sbyte sByteValue
        {
            get
            {
                if (GetTagType() == TagType.sByte) return ((sByteTag)this).Value;
                else throw new InvalidCastException("Cannot get sByteValue from " + ToString());
            }
        }


        /// <summary> Returns the value of this tag, cast as a short (16-bit signed integer).
        /// Only supported by ByteTag and ShortTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public short ShortValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Byte => ((ByteTag)this).Value,
                    TagType.sByte => ((sByteTag)this).Value,
                    TagType.Short => ((ShortTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get ShortValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as an int (32-bit signed integer).
        /// Only supported by ByteTag, ShortTag, and IntTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public int IntValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Byte => ((ByteTag)this).Value,
                    TagType.sByte => ((sByteTag)this).Value,
                    TagType.Short => ((ShortTag)this).Value,
                    TagType.Int => ((IntTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get IntValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as a long (64-bit signed integer).
        /// Only supported by ByteTag, ShortTag, IntTag, and LongTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public long LongValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Byte => ((ByteTag)this).Value,
                    TagType.sByte => ((sByteTag)this).Value,
                    TagType.Short => ((ShortTag)this).Value,
                    TagType.Int => ((IntTag)this).Value,
                    TagType.Long => ((LongTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get LongValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as a ushort (16-bit unsigned integer).
        /// Only supported by ByteTag and UShortTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public ushort UShortValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Byte => ((ByteTag)this).Value,
                    TagType.UShort => ((UShortTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get UShortValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as an uint (32-bit unsigned integer).
        /// Only supported by ByteTag, UShortTag, and UIntTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public uint UIntValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Byte => ((ByteTag)this).Value,
                    TagType.UShort => ((UShortTag)this).Value,
                    TagType.UInt => ((UIntTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get UIntValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as a ulong (64-bit unsigned integer).
        /// Only supported by ByteTag, UShortTag, UIntTag, and ULongTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public ulong ULongValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Byte => ((ByteTag)this).Value,
                    TagType.UShort => ((UShortTag)this).Value,
                    TagType.UInt => ((UIntTag)this).Value,
                    TagType.ULong => ((ULongTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get ULongValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as a long (64-bit signed integer).
        /// Only supported by FloatTag and, with loss of precision, by DoubleTag, ByteTag, ShortTag, IntTag, and LongTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public float FloatValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Byte => ((ByteTag)this).Value,
                    TagType.Short => ((ShortTag)this).Value,
                    TagType.Int => ((IntTag)this).Value,
                    TagType.Long => ((LongTag)this).Value,
                    TagType.Float => ((FloatTag)this).Value,
                    TagType.Double => (float)((DoubleTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get FloatValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as a long (64-bit signed integer).
        /// Only supported by FloatTag, DoubleTag, and, with loss of precision, by ByteTag, ShortTag, IntTag, and LongTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public double DoubleValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Byte => ((ByteTag)this).Value,
                    TagType.Short => ((ShortTag)this).Value,
                    TagType.Int => ((IntTag)this).Value,
                    TagType.Long => ((LongTag)this).Value,
                    TagType.Float => ((FloatTag)this).Value,
                    TagType.Double => ((DoubleTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get DoubleValue from " + ToString())
                };
            }
        }


        /// <summary> Returns the value of this tag, cast as a decimal.
        /// Only supported by DecimalTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public decimal DecimalValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.Decimal => ((DecimalTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get DecimalTag from " + ToString())
                };
            }
        }

        /// <summary> Returns the value of this tag, cast as a string.
        /// Returns exact value for StringTag, and stringified (using InvariantCulture) value for ByteTag, DoubleTag, FloatTag, IntTag, LongTag, and ShortTag.
        /// Not supported by CompoundTag, ListTag, ByteArrayTag, FloatArrayTag, or IntArrayTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        [JsonIgnore]
        public string StringValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.String => ((StringTag)this).Value,
                    TagType.Byte => ((ByteTag)this).Value.ToString(CultureInfo.InvariantCulture),
                    TagType.Short => ((ShortTag)this).Value.ToString(CultureInfo.InvariantCulture),
                    TagType.Int => ((IntTag)this).Value.ToString(CultureInfo.InvariantCulture),
                    TagType.Long => ((LongTag)this).Value.ToString(CultureInfo.InvariantCulture),
                    TagType.Float => ((FloatTag)this).Value.ToString(CultureInfo.InvariantCulture),
                    TagType.Double => ((DoubleTag)this).Value.ToString(CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException("Cannot get StringValue from " + ToString())
                };
            }
        }

        [JsonIgnore]
        public byte[] ByteArrayValue
        {
            get
            {
                return GetTagType() switch
                {
                    TagType.ByteArray => ((ByteArrayTag)this).Value,
                    _ => throw new InvalidCastException("Cannot get ByteArrayValue from " + ToString())
                };
            }

        }

        #endregion

    }
}
