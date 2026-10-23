// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;

namespace Prowl.Echo;

public enum EchoType
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

public sealed partial class EchoObject : IEquatable<EchoObject>
{
    // String, byte[], decimal, List and Compound values. Other primitives live unboxed in _bits, with _ref caching their box.
    private object? _ref;
    private long _bits;
    private int _listIndex = -1;

    public object? Value
    {
        get => TagType == EchoType.Compound ? Tags : IsBits ? _ref ??= BoxBits() : _ref;
        set => SetValue(value!);
    }

    private bool IsBits => TagType is not (EchoType.Null or EchoType.Decimal or EchoType.String or EchoType.ByteArray or EchoType.List or EchoType.Compound);

    private object BoxBits() => TagType switch
    {
        EchoType.Byte => (byte)_bits,
        EchoType.sByte => (sbyte)_bits,
        EchoType.Short => (short)_bits,
        EchoType.Int => (int)_bits,
        EchoType.Long => _bits,
        EchoType.UShort => (ushort)_bits,
        EchoType.UInt => (uint)_bits,
        EchoType.ULong => unchecked((ulong)_bits),
        EchoType.Float => BitConverter.Int32BitsToSingle((int)_bits),
        EchoType.Double => BitConverter.Int64BitsToDouble(_bits),
        _ => (object)(_bits != 0)
    };

    public EchoType TagType { get; private set; }

    // The value of any integer tag except ULong, without boxing.
    internal long IntegerBits => _bits;

    public EchoObject? Parent { get; private set; }
    public string? CompoundKey { get; private set; }
    public int? ListIndex
    {
        get => _listIndex < 0 ? null : _listIndex;
        private set => _listIndex = value ?? -1;
    }

    public EchoObject() { }
    public EchoObject(byte i) { _bits = i; TagType = EchoType.Byte; }
    public EchoObject(sbyte i) { _bits = i; TagType = EchoType.sByte; }
    public EchoObject(short i) { _bits = i; TagType = EchoType.Short; }
    public EchoObject(int i) { _bits = i; TagType = EchoType.Int; }
    public EchoObject(long i) { _bits = i; TagType = EchoType.Long; }
    public EchoObject(ushort i) { _bits = i; TagType = EchoType.UShort; }
    public EchoObject(uint i) { _bits = i; TagType = EchoType.UInt; }
    public EchoObject(ulong i) { _bits = unchecked((long)i); TagType = EchoType.ULong; }
    public EchoObject(float i) { _bits = BitConverter.SingleToInt32Bits(i); TagType = EchoType.Float; }
    public EchoObject(double i) { _bits = BitConverter.DoubleToInt64Bits(i); TagType = EchoType.Double; }
    public EchoObject(decimal i) { _ref = i; TagType = EchoType.Decimal; }
    public EchoObject(string i) { _ref = i ?? ""; TagType = EchoType.String; }
    public EchoObject(byte[] i) { _ref = i; TagType = EchoType.ByteArray; }
    public EchoObject(bool i) { _bits = i ? 1 : 0; TagType = EchoType.Bool; }
    public EchoObject(EchoType type, object? value)
    {
        TagType = type;
        if (type == EchoType.List)
        {
            _ref = value ?? new List<EchoObject>();
            var list = (List<EchoObject>)_ref;
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Parent = this;
                list[i].ListIndex = i;
            }
        }
        else if (type == EchoType.Compound)
        {
            if (value is IEnumerable<KeyValuePair<string, EchoObject>> entries)
                SetCompoundEntries(entries, (value as ICollection<KeyValuePair<string, EchoObject>>)?.Count ?? 0);
            else
                _ref = new CompoundStorage(0);
        }
        else if (value is null || type is EchoType.String or EchoType.ByteArray or EchoType.Decimal)
            _ref = value;
        else
            StorePrimitive(value);
    }
    public EchoObject(List<EchoObject> tags)
    {
        TagType = EchoType.List;
        _ref = tags;

        // Set parent for all children
        for (int i = 0; i < tags.Count; i++)
        {
            tags[i].Parent = this;
            tags[i].ListIndex = i;
        }
    }
    public static EchoObject NewCompound() => NewCompound(0);
    public static EchoObject NewCompound(int capacity) => new() { TagType = EchoType.Compound, _ref = new CompoundStorage(capacity) };
    public static EchoObject NewList() => new(EchoType.List, new List<EchoObject>());

    public EchoObject Clone()
    {
        if (TagType == EchoType.Null) return new(EchoType.Null, null);
        else if (TagType == EchoType.List)
        {
            // Value is a List<Tag>
            var list = (List<EchoObject>)Value!;
            var newList = new List<EchoObject>(list.Count);
            foreach (var tag in list)
                newList.Add(tag.Clone());

            return new(EchoType.List, newList);
        }
        else if (TagType == EchoType.Compound)
        {
            var clone = NewCompound(Count);
            foreach (var (key, tag) in Tags)
                clone.AddUnique(key, tag.Clone());
            return clone;
        }

        return new EchoObject { TagType = TagType, _ref = _ref, _bits = _bits };
    }

    /// <summary>
    /// Write this tag to a binary file in the Echo format.
    /// </summary>
    /// <param name="file">The file to write to</param>
    /// <param name="options">Optional serialization options</param>
    public void WriteToBinary(FileInfo file, BinarySerializationOptions? options = null)
    {
        using var stream = file.Create();
        using var writer = new BinaryWriter(stream);
        EchoBinaryFormat.WriteTo(this, writer, options);
    }

    /// <summary>
    /// Write this tag to a binary file in the Echo format.
    /// </summary>
    /// <param name="writer">The writer to write to</param>
    /// <param name="options">Optional serialization options</param>
    public void WriteToBinary(BinaryWriter writer, BinarySerializationOptions? options = null)
    {
        EchoBinaryFormat.WriteTo(this, writer, options);
    }

    /// <summary>
    /// Read a tag from a binary file in the Echo format.
    /// </summary>
    /// <param name="file">The file to read from</param>
    /// <param name="options">Optional serialization options</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromBinary(FileInfo file, BinarySerializationOptions? options = null)
    {
        return EchoBinaryFormat.ReadFromFile(file, options);
    }

    /// <summary>
    /// Read a tag from a binary file in the Echo format.
    /// </summary>
    /// <param name="reader">The reader to read from</param>
    /// <param name="options">Optional serialization options</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromBinary(BinaryReader reader, BinarySerializationOptions? options = null)
    {
        return EchoBinaryFormat.ReadFrom(reader, options);
    }


    /// <summary>
    /// Write this tag to a string in the Echo format.
    /// </summary>
    /// <param name="file">The file to write to</param>
    public void WriteToString(FileInfo file)
    {
        EchoTextFormat.WriteToFile(this, file);
    }

    /// <summary>
    /// Write this tag to a string in the Echo format.
    /// </summary>
    public string WriteToString()
    {
        return EchoTextFormat.Write(this);
    }

    /// <summary>
    /// Read a tag from a file in the Echo format.
    /// </summary>
    /// <param name="file">The file to read from</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromString(FileInfo file)
    {
        return EchoTextFormat.ReadFromFile(file);
    }

    /// <summary>
    /// Read a tag from a string in the Echo format.
    /// </summary>
    /// <param name="input">The string to read from</param>
    /// <returns>The tag read from the string</returns>
    public static EchoObject ReadFromString(string input)
    {
        return EchoTextFormat.Read(input);
    }

    #region Equality

    public bool Equals(EchoObject? other)
    {
        if (other is null) return false;
        if (object.ReferenceEquals(this, other)) return true;
        if (TagType != other.TagType) return false;

        // Handle different tag types
        switch (TagType)
        {
            case EchoType.Compound:
                if (Count != other.Count) return false;

                // Key order does not affect equality.
                foreach (var kvp in Tags)
                {
                    if (!other.TryGet(kvp.Key, out var otherVal) || !kvp.Value.Equals(otherVal))
                        return false;
                }
                return true;

            case EchoType.List:
                var thisList = (List<EchoObject>)Value!;
                var otherList = (List<EchoObject>)other.Value!;

                if (thisList.Count != otherList.Count) return false;

                for (int i = 0; i < thisList.Count; i++)
                {
                    if (!thisList[i].Equals(otherList[i])) return false;
                }
                return true;

            case EchoType.Null:
                return true; // Both are null type

            case EchoType.String:
            case EchoType.ByteArray:
            case EchoType.Decimal:
                if (_ref == null) return other._ref == null;
                if (other._ref == null) return false;
                if (_ref is byte[] thisBytes && other._ref is byte[] otherBytes)
                    return thisBytes.SequenceEqual(otherBytes);
                return _ref.Equals(other._ref);

            case EchoType.Float:
                return FloatValue == other.FloatValue;

            case EchoType.Double:
                return DoubleValue == other.DoubleValue;

            default:
                return _bits == other._bits;
        }
    }

    public override bool Equals(object? obj) => Equals(obj as EchoObject);
    public override int GetHashCode()
    {
        int hash = TagType.GetHashCode();

        if (TagType == EchoType.Compound)
        {
            foreach (var echo in Tags)
                hash = HashCode.Combine(hash.GetHashCode(), echo.Key, echo.Value.GetHashCode());
        }
        else if (TagType == EchoType.List)
        {
            foreach (var echo in List)
                hash = HashCode.Combine(hash.GetHashCode(), echo.GetHashCode());
        }
        else hash = HashCode.Combine(TagType.GetHashCode(), Value?.GetHashCode() ?? 0); // Primitive tag type

        return hash;
    }
    public static bool operator ==(EchoObject left, EchoObject right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(EchoObject left, EchoObject right) => !(left == right);

    #endregion

    #region Shortcuts

    /// <summary>
    /// Gets the number of tags in this tag.
    /// Returns 0 for all tags except Compound and List.
    /// </summary>
    public int Count
    {
        get
        {
            if (TagType == EchoType.Compound) return Compound.Count;
            else if (TagType == EchoType.List) return ((List<EchoObject>)_ref!).Count;
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
                EchoType.Compound => false,
                EchoType.List => false,
                EchoType.Null => false,
                _ => true
            };
        }
    }

    /// <summary>
    /// Utility to set the value of this tag with safety checks.
    /// </summary>
    public void SetValue(object value)
    {
        if (value != null && ReferenceEquals(_ref, value)) return;

        try
        {
            // Handle null for nullable types
            if (value == null)
            {
                if (TagType == EchoType.String)
                {
                    _ref = string.Empty;
                    return;
                }
                throw new ArgumentNullException(nameof(value), $"Cannot set null value for type {TagType}");
            }

            // Handle Compound type
            if (TagType == EchoType.Compound)
            {
                if (value is not IEnumerable<KeyValuePair<string, EchoObject>> entries)
                {
                    throw new InvalidOperationException(
                        $"Cannot convert type {value.GetType().Name} to compound entries");
                }

                SetCompoundEntries(entries.ToList(), (value as ICollection<KeyValuePair<string, EchoObject>>)?.Count ?? 0);
                return;
            }

            // Handle List type
            if (TagType == EchoType.List)
            {
                if (value is not List<EchoObject> list)
                {
                    throw new InvalidOperationException(
                        $"Cannot convert type {value.GetType().Name} to List<EchoObject>");
                }

                _ref = list;
                // Set parent for all children
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Parent = this;
                    list[i].ListIndex = i;
                }
                return;
            }

            StorePrimitive(value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to set value of type {TagType} to {value} ({value.GetType().Name})", ex);
        }
    }

    private void StorePrimitive(object value)
    {
        if (IsBits) _ref = null;
        switch (TagType)
        {
            case EchoType.Byte: _bits = Convert.ToByte(value); break;
            case EchoType.sByte: _bits = Convert.ToSByte(value); break;
            case EchoType.Short: _bits = Convert.ToInt16(value); break;
            case EchoType.Int: _bits = Convert.ToInt32(value); break;
            case EchoType.Long: _bits = Convert.ToInt64(value); break;
            case EchoType.UShort: _bits = Convert.ToUInt16(value); break;
            case EchoType.UInt: _bits = Convert.ToUInt32(value); break;
            case EchoType.ULong: _bits = value is long l ? l : unchecked((long)Convert.ToUInt64(value)); break;
            case EchoType.Float: _bits = BitConverter.SingleToInt32Bits(Convert.ToSingle(value)); break;
            case EchoType.Double: _bits = BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)); break;
            case EchoType.Decimal: _ref = Convert.ToDecimal(value); break;
            case EchoType.Bool: _bits = Convert.ToBoolean(value) ? 1 : 0; break;
            case EchoType.String: _ref = value as string ?? value.ToString(); break;
            case EchoType.ByteArray: _ref = value as byte[] ?? throw new InvalidOperationException($"Cannot convert type {value.GetType().Name} to byte array"); break;
            default: throw new InvalidOperationException($"Unsupported tag type: {TagType}");
        }
    }

    /// <summary> Returns the value of this tag, cast as a bool. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than BoolTag. </exception>
    public bool BoolValue
    {
        get => TagType == EchoType.Bool ? _bits != 0 : (bool)Value!;
        set { if (TagType == EchoType.Bool) { _bits = value ? 1 : 0; _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a byte. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than ByteTag. </exception>
    public byte ByteValue
    {
        get => TagType == EchoType.Byte ? (byte)_bits : Convert.ToByte(Value);
        set { if (TagType == EchoType.Byte) { _bits = value; _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a sbyte. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than sByteTag. </exception>
    public sbyte sByteValue
    {
        get => TagType == EchoType.sByte ? (sbyte)_bits : Convert.ToSByte(Value);
        set { if (TagType == EchoType.sByte) { _bits = value; _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a short. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than ShortTag. </exception>
    public short ShortValue
    {
        get => TagType == EchoType.Short ? (short)_bits : Convert.ToInt16(Value);
        set { if (TagType == EchoType.Short) { _bits = value; _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a int. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than IntTag. </exception>
    public int IntValue
    {
        get => TagType == EchoType.Int ? (int)_bits : Convert.ToInt32(Value);
        set { if (TagType == EchoType.Int) { _bits = value; _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a long. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than LongTag. </exception>
    public long LongValue
    {
        get => TagType == EchoType.Long ? _bits : Convert.ToInt64(Value);
        set { if (TagType == EchoType.Long) { _bits = value; _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a ushort. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than UShortTag. </exception>
    public ushort UShortValue
    {
        get => TagType == EchoType.UShort ? (ushort)_bits : Convert.ToUInt16(Value);
        set { if (TagType == EchoType.UShort) { _bits = value; _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as an uint. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than UIntTag. </exception>
    public uint UIntValue
    {
        get => TagType == EchoType.UInt ? (uint)_bits : Convert.ToUInt32(Value);
        set { if (TagType == EchoType.UInt) { _bits = value; _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a ulong. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than ULongTag. </exception>
    public ulong ULongValue
    {
        get => TagType is EchoType.ULong or EchoType.Long ? unchecked((ulong)_bits) : Convert.ToUInt64(Value);
        set { if (TagType == EchoType.ULong) { _bits = unchecked((long)value); _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a float. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than FloatTag. </exception>
    public float FloatValue
    {
        get => TagType == EchoType.Float ? BitConverter.Int32BitsToSingle((int)_bits) : Convert.ToSingle(Value);
        set { if (TagType == EchoType.Float) { _bits = BitConverter.SingleToInt32Bits(value); _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a double. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than DoubleTag. </exception>
    public double DoubleValue
    {
        get => TagType == EchoType.Double ? BitConverter.Int64BitsToDouble(_bits) : Convert.ToDouble(Value);
        set { if (TagType == EchoType.Double) { _bits = BitConverter.DoubleToInt64Bits(value); _ref = null; } else SetValue(value); }
    }

    /// <summary> Returns the value of this tag, cast as a decimal.
    /// Only supported by DecimalTag. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on an a tag other than DecimalTag. </exception>
    public decimal DecimalValue { get => Convert.ToDecimal(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a byte array. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than ByteArrayTag. </exception>
    public byte[] ByteArrayValue { get => (byte[])Value; set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a string.
    /// Returns exact value for StringTag, and ToString(InvariantCulture) value for Byte, SByte, Double, Float, Int, UInt, Long, uLong, Short, UShort, Decimal, Bool.
    /// ByteArray returns a Base64 string.
    /// Null returns a string with contents "NULL".
    /// Not supported by CompoundTag, ListTag. </summary>
    /// <exception cref="InvalidCastException"> Will throw when used on an unsupported tag. </exception>
    public string StringValue
    {
        get => TagType switch
        {
            EchoType.Null => "NULL",
            EchoType.String => _ref as string ?? "",
            EchoType.Byte => ByteValue.ToString(CultureInfo.InvariantCulture),
            EchoType.sByte => sByteValue.ToString(CultureInfo.InvariantCulture),
            EchoType.Double => DoubleValue.ToString(CultureInfo.InvariantCulture),
            EchoType.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
            EchoType.Int => IntValue.ToString(CultureInfo.InvariantCulture),
            EchoType.UInt => UIntValue.ToString(CultureInfo.InvariantCulture),
            EchoType.Long => LongValue.ToString(CultureInfo.InvariantCulture),
            EchoType.ULong => ULongValue.ToString(CultureInfo.InvariantCulture),
            EchoType.Short => ShortValue.ToString(CultureInfo.InvariantCulture),
            EchoType.UShort => UShortValue.ToString(CultureInfo.InvariantCulture),
            EchoType.Decimal => DecimalValue.ToString(CultureInfo.InvariantCulture),
            EchoType.Bool => BoolValue.ToString(CultureInfo.InvariantCulture),
            EchoType.ByteArray => Convert.ToBase64String(ByteArrayValue),
            _ => throw new InvalidCastException("Cannot get StringValue from " + TagType.ToString())
        };
        set => SetValue(value);
    }

    #endregion

}
