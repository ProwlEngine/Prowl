using System;
using System.Collections.Generic;
using System.Globalization;

namespace Prowl.Runtime
{
    public enum PropertyType
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

    public sealed partial class SerializedProperty
    {
        private object? _value;
        public object? Value { get { return _value; } private set { Set(value); } }

        public PropertyType TagType { get; private set; }

        public SerializedProperty? Parent { get; private set; }

        public SerializedProperty() { }
        public SerializedProperty(byte i) { Value = i; TagType = PropertyType.Byte; }
        public SerializedProperty(sbyte i) { Value = i; TagType = PropertyType.sByte; }
        public SerializedProperty(short i) { Value = i; TagType = PropertyType.Short; }
        public SerializedProperty(int i) { Value = i; TagType = PropertyType.Int; }
        public SerializedProperty(long i) { Value = i; TagType = PropertyType.Long; }
        public SerializedProperty(ushort i) { Value = i; TagType = PropertyType.UShort; }
        public SerializedProperty(uint i) { Value = i; TagType = PropertyType.UInt; }
        public SerializedProperty(ulong i) { Value = i; TagType = PropertyType.ULong; }
        public SerializedProperty(float i) { Value = i; TagType = PropertyType.Float; }
        public SerializedProperty(double i) { Value = i; TagType = PropertyType.Double; }
        public SerializedProperty(decimal i) { Value = i; TagType = PropertyType.Decimal; }
        public SerializedProperty(string i) { Value = i; TagType = PropertyType.String; }
        public SerializedProperty(byte[] i) { Value = i; TagType = PropertyType.ByteArray; }
        public SerializedProperty(bool i) { Value = i; TagType = PropertyType.Bool; }
        public SerializedProperty(PropertyType type, object? value) 
        { 
            TagType = type;
            if (type == PropertyType.List && value == null)
                Value = new List<SerializedProperty>();
            else if (type == PropertyType.Compound && value == null)
                Value = new Dictionary<string, SerializedProperty>();
            else
                Value = value;
        }
        public SerializedProperty(List<SerializedProperty> tags)
        {
            TagType = PropertyType.List;
            Value = tags;
        }

        public static SerializedProperty NewCompound() => new(PropertyType.Compound, new Dictionary<string, SerializedProperty>());
        public static SerializedProperty NewList() => new(PropertyType.List, new List<SerializedProperty>());

        public SerializedProperty Clone()
        {
            if (TagType == PropertyType.Null) return new(PropertyType.Null, null);
            else if (TagType == PropertyType.List)
            {
                // Value is a List<Tag>
                var list = (List<SerializedProperty>)Value!;
                var newList = new List<SerializedProperty>(list.Count);
                foreach (var tag in list)
                    newList.Add(tag.Clone());
            }
            else if (TagType == PropertyType.Compound)
            {
                // Value is a Dictionary<string, Tag>
                var dict = (Dictionary<string, SerializedProperty>)Value!;
                var newDict = new Dictionary<string, SerializedProperty>(dict.Count);
                foreach (var (key, tag) in dict)
                    newDict.Add(key, tag.Clone());
            }
            return new(TagType, Value);
        }

        #region Shortcuts

        /// <summary>
        /// Gets the number of tags in this tag.
        /// Returns 0 for all tags except Compound and List.
        /// </summary>
        public int Count {
            get {
                if (TagType == PropertyType.Compound) return ((Dictionary<string, SerializedProperty>)Value!).Count;
                else if (TagType == PropertyType.List) return ((List<SerializedProperty>)Value!).Count;
                else return 0;
            }
        }

        /// <summary> 
        /// Returns true if tags of this type have a primitive value attached.
        /// All tags except Compound and List have values. 
        /// </summary>
        public bool IsPrimitive
        {
            get
            {
                return TagType switch
                {
                    PropertyType.Compound => false,
                    PropertyType.List => false,
                    PropertyType.Null => false,
                    _ => true
                };
            }
        }

        /// <summary>
        /// Utility to set the value of this tag with safety checks.
        /// </summary>
        public void Set(object value)
        {
            var old = _value;
            Value = TagType switch {
                PropertyType.Byte => (byte)value,
                PropertyType.sByte => (sbyte)value,
                PropertyType.Short => (short)value,
                PropertyType.Int => (int)value,
                PropertyType.Long => (long)value,
                PropertyType.UShort => (ushort)value,
                PropertyType.UInt => (uint)value,
                PropertyType.ULong => (ulong)value,
                PropertyType.Float => (float)value,
                PropertyType.Double => (double)value,
                PropertyType.Decimal => (decimal)value,
                PropertyType.String => (string)value,
                PropertyType.ByteArray => (byte[])value,
                PropertyType.Bool => (bool)value,
                _ => throw new InvalidOperationException("Cannot set value of " + TagType.ToString())
            };
        }

        /// <summary> Returns the value of this tag, cast as a bool. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than BoolTag. </exception>
        public bool BoolValue { get => (bool)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a byte. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than ByteTag. </exception>
        public byte ByteValue { get => (byte)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a sbyte. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than sByteTag. </exception>
        public sbyte sByteValue { get => (sbyte)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a short. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than ShortTag. </exception>
        public short ShortValue { get => (short)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a int. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than IntTag. </exception>
        public int IntValue { get => (int)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a long. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than LongTag. </exception>
        public long LongValue { get => (long)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a ushort. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than UShortTag. </exception>
        public ushort UShortValue { get => (ushort)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as an uint. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than UIntTag. </exception>
        public uint UIntValue { get => (uint)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a ulong. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than ULongTag. </exception>
        public ulong ULongValue { get => (ulong)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a float. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than FloatTag. </exception>
        public float FloatValue { get => (float)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a double. </summary>
        /// <exception cref="InvalidCastException"> When used on a tag other than DoubleTag. </exception>
        public double DoubleValue { get => (double)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a decimal. </summary>
        /// Only supported by DecimalTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        public decimal DecimalValue { get => (decimal)Value; set => Set(value); }

        /// <summary> Returns the value of this tag, cast as a string.
        /// Returns exact value for StringTag, and stringified (using InvariantCulture) value for ByteTag, DoubleTag, FloatTag, IntTag, LongTag, and ShortTag.
        /// Not supported by CompoundTag, ListTag, ByteArrayTag, FloatArrayTag, or IntArrayTag. </summary>
        /// <exception cref="InvalidCastException"> When used on an unsupported tag. </exception>
        public string StringValue {
            get {
                return TagType switch {
                    PropertyType.String => (string)Value!,
                    PropertyType.Byte => ((byte)Value!).ToString(CultureInfo.InvariantCulture),
                    PropertyType.Double => ((double)Value!).ToString(CultureInfo.InvariantCulture),
                    PropertyType.Float => ((float)Value!).ToString(CultureInfo.InvariantCulture),
                    PropertyType.Int => ((int)Value!).ToString(CultureInfo.InvariantCulture),
                    PropertyType.Long => ((long)Value!).ToString(CultureInfo.InvariantCulture),
                    PropertyType.Short => ((short)Value!).ToString(CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException("Cannot get StringValue from " + TagType.ToString())
                };
            }
            set => Set(value);
        }

        /// <summary> Returns the value of this tag, cast as a byte array.
        /// <exception cref="InvalidCastException"> When used on a tag other than ByteArrayTag. </exception>
        public byte[] ByteArrayValue { get => (byte[])Value; set => Set(value); }

        #endregion

    }
}
