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

public class EchoChangeEventArgs : EventArgs
{
    public EchoObject Source { get; }      // The root object where the event originated
    public string Path { get; }            // Full path to changed property
    public string RelativePath { get; }    // Path relative to the listening object
    public EchoObject Property { get; }    // The actual changed property
    public object? OldValue { get; }
    public object? NewValue { get; }
    public ChangeType Type { get; }        // Type of change that occurred

    public EchoChangeEventArgs(
        EchoObject source,
        EchoObject property,
        object? oldValue,
        object? newValue,
        ChangeType type)
    {
        Source = source;
        Property = property;
        Path = property.GetPath();
        RelativePath = source == property ? "" : EchoObject.GetRelativePath(source, property);
        OldValue = oldValue;
        NewValue = newValue;
        Type = type;
    }
}

public enum ChangeType
{
    ValueChanged,     // Value of a property changed
    ListTagAdded,     // Item added to a list
    ListTagRemoved,   // Item removed from a list
    ListTagMoved,     // Item moved within a list
    TagAdded,         // Property added to compound
    TagRemoved,       // Property removed from compound
    TagRenamed        // Property renamed in compound
}

public sealed partial class EchoObject
{
    public event EventHandler<EchoChangeEventArgs>? PropertyChanged;

    private object? _value;
    public object? Value { get { return _value; } set { SetValue(value); } }

    public EchoType TagType { get; private set; }

    public EchoObject? Parent { get; private set; }
    public string? CompoundKey { get; private set; }
    public int? ListIndex { get; private set; }

    public EchoObject() { }
    public EchoObject(byte i) { _value = i; TagType = EchoType.Byte; }
    public EchoObject(sbyte i) { _value = i; TagType = EchoType.sByte; }
    public EchoObject(short i) { _value = i; TagType = EchoType.Short; }
    public EchoObject(int i) { _value = i; TagType = EchoType.Int; }
    public EchoObject(long i) { _value = i; TagType = EchoType.Long; }
    public EchoObject(ushort i) { _value = i; TagType = EchoType.UShort; }
    public EchoObject(uint i) { _value = i; TagType = EchoType.UInt; }
    public EchoObject(ulong i) { _value = i; TagType = EchoType.ULong; }
    public EchoObject(float i) { _value = i; TagType = EchoType.Float; }
    public EchoObject(double i) { _value = i; TagType = EchoType.Double; }
    public EchoObject(decimal i) { _value = i; TagType = EchoType.Decimal; }
    public EchoObject(string i) { _value = i; TagType = EchoType.String; }
    public EchoObject(byte[] i) { _value = i; TagType = EchoType.ByteArray; }
    public EchoObject(bool i) { _value = i; TagType = EchoType.Bool; }
    public EchoObject(EchoType type, object? value)
    {
        TagType = type;
        if (type == EchoType.List && value == null)
        {
            _value = new List<EchoObject>();
            // Set parent for all children
            for (int i = 0; i < ((List<EchoObject>)_value).Count; i++)
            {
                ((List<EchoObject>)_value)[i].Parent = this;
                ((List<EchoObject>)_value)[i].ListIndex = i;
            }
        }
        else if (type == EchoType.Compound && value == null)
        {
            _value = new Dictionary<string, EchoObject>();

            // Set parent for all children
            foreach (var (key, tag) in (Dictionary<string, EchoObject>)_value)
            {
                tag.Parent = this;
                tag.CompoundKey = key;
            }
        }
        else
            _value = value;
    }
    public EchoObject(List<EchoObject> tags)
    {
        TagType = EchoType.List;
        _value = tags;

        // Set parent for all children
        for (int i = 0; i < tags.Count; i++)
        {
            tags[i].Parent = this;
            tags[i].ListIndex = i;
        }
    }
    public static EchoObject NewCompound() => new(EchoType.Compound, new Dictionary<string, EchoObject>());
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
            // Value is a Dictionary<string, Tag>
            var dict = (Dictionary<string, EchoObject>)Value!;
            var newDict = new Dictionary<string, EchoObject>(dict.Count);
            foreach (var (key, tag) in dict)
                newDict.Add(key, tag.Clone());

            return new(EchoType.Compound, newDict);
        }

        return new(TagType, Value);
    }

    /// <summary>
    /// Write this tag to a binary file in the Echo format.
    /// </summary>
    /// <param name="file">The file to write to</param>
    /// <param name="options">Optional serialization options</param>
    public void WriteToBinary(FileInfo file, BinarySerializationOptions? options = null)
    {
        using var stream = file.OpenWrite();
        using var writer = new BinaryWriter(stream);
        BinaryTagConverter.WriteTo(this, writer, options);
    }

    /// <summary>
    /// Write this tag to a binary file in the Echo format.
    /// </summary>
    /// <param name="writer">The writer to write to</param>
    /// <param name="options">Optional serialization options</param>
    public void WriteToBinary(BinaryWriter writer, BinarySerializationOptions? options = null)
    {
        BinaryTagConverter.WriteTo(this, writer, options);
    }

    /// <summary>
    /// Read a tag from a binary file in the Echo format.
    /// </summary>
    /// <param name="file">The file to read from</param>
    /// <param name="options">Optional serialization options</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromBinary(FileInfo file, BinarySerializationOptions? options = null)
    {
        return BinaryTagConverter.ReadFromFile(file, options);
    }

    /// <summary>
    /// Read a tag from a binary file in the Echo format.
    /// </summary>
    /// <param name="reader">The reader to read from</param>
    /// <param name="options">Optional serialization options</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromBinary(BinaryReader reader, BinarySerializationOptions? options = null)
    {
        return BinaryTagConverter.ReadFrom(reader, options);
    }


    /// <summary>
    /// Write this tag to a string in the Echo format.
    /// </summary>
    /// <param name="file">The file to write to</param>
    public void WriteToString(FileInfo file)
    {
        StringTagConverter.WriteToFile(this, file);
    }

    /// <summary>
    /// Write this tag to a string in the Echo format.
    /// </summary>
    public string WriteToString()
    {
        return StringTagConverter.Write(this);
    }

    /// <summary>
    /// Read a tag from a file in the Echo format.
    /// </summary>
    /// <param name="file">The file to read from</param>
    /// <returns>The tag read from the file</returns>
    public static EchoObject ReadFromString(FileInfo file)
    {
        return StringTagConverter.ReadFromFile(file);
    }

    /// <summary>
    /// Read a tag from a string in the Echo format.
    /// </summary>
    /// <param name="input">The string to read from</param>
    /// <returns>The tag read from the string</returns>
    public static EchoObject ReadFromString(string input)
    {
        return StringTagConverter.Read(input);
    }

    private void OnPropertyChanged(EchoChangeEventArgs e)
    {
        // Create a new event with the path relative to this object
        var localEvent = new EchoChangeEventArgs(
            this,  // Source is this object
            e.Property,
            e.OldValue,
            e.NewValue,
            e.Type);

        // Fire local event
        PropertyChanged?.Invoke(this, localEvent);

        // If we have a parent, propagate upwards
        if (Parent != null)
        {
            // For parent events, we need to include our key in the path
            string parentPath = Parent.TagType == EchoType.List
                ? ListIndex.ToString()
                : CompoundKey ?? "";

            var parentEvent = new EchoChangeEventArgs(
                Parent,  // Source is the parent
                e.Property,
                e.OldValue,
                e.NewValue,
                e.Type);

            Parent.OnPropertyChanged(parentEvent);
        }
    }

    #region Shortcuts

    /// <summary>
    /// Gets the number of tags in this tag.
    /// Returns 0 for all tags except Compound and List.
    /// </summary>
    public int Count
    {
        get
        {
            if (TagType == EchoType.Compound) return ((Dictionary<string, EchoObject>)Value!).Count;
            else if (TagType == EchoType.List) return ((List<EchoObject>)Value!).Count;
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
        if (_value == value) return;
        var oldValue = _value;

        try
        {
            // Handle null for nullable types
            if (value == null)
            {
                if (TagType == EchoType.String)
                {
                    _value = string.Empty;
                    OnPropertyChanged(new EchoChangeEventArgs(this, this, oldValue, _value, ChangeType.ValueChanged));
                    return;
                }
                throw new ArgumentNullException(nameof(value), $"Cannot set null value for type {TagType}");
            }

            // Handle Compound type
            if (TagType == EchoType.Compound)
            {
                if (value is not Dictionary<string, EchoObject> dict)
                {
                    throw new InvalidOperationException(
                        $"Cannot convert type {value.GetType().Name} to Dictionary<string, EchoObject>");
                }

                _value = dict;
                // Set parent for all children
                foreach (var (key, tag) in dict)
                {
                    tag.Parent = this;
                    tag.CompoundKey = key;
                }
                OnPropertyChanged(new EchoChangeEventArgs(this, this, oldValue, _value, ChangeType.ValueChanged));
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

                _value = list;
                // Set parent for all children
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Parent = this;
                    list[i].ListIndex = i;
                }
                OnPropertyChanged(new EchoChangeEventArgs(this, this, oldValue, _value, ChangeType.ValueChanged));
                return;
            }

            // Try converting using Convert.ChangeType for numeric types
            if (IsNumericType(TagType))
            {
                try
                {
                    _value = TagType switch {
                        EchoType.Byte => Convert.ToByte(value),
                        EchoType.sByte => Convert.ToSByte(value),
                        EchoType.Short => Convert.ToInt16(value),
                        EchoType.Int => Convert.ToInt32(value),
                        EchoType.Long => Convert.ToInt64(value),
                        EchoType.UShort => Convert.ToUInt16(value),
                        EchoType.UInt => Convert.ToUInt32(value),
                        EchoType.ULong => Convert.ToUInt64(value),
                        EchoType.Float => Convert.ToSingle(value),
                        EchoType.Double => Convert.ToDouble(value),
                        EchoType.Decimal => Convert.ToDecimal(value),
                        _ => throw new InvalidOperationException($"Unexpected numeric type: {TagType}")
                    };
                    OnPropertyChanged(new EchoChangeEventArgs(this, this, oldValue, _value, ChangeType.ValueChanged));
                    return;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to convert value '{value}' of type {value.GetType().Name} to {TagType}", ex);
                }
            }

            // Handle special types
            _value = TagType switch {
                EchoType.String => value is string str ? str : value.ToString(),
                EchoType.Bool => Convert.ToBoolean(value),
                EchoType.ByteArray => value is byte[] arr ? arr : throw new InvalidOperationException(
                    $"Cannot convert type {value.GetType().Name} to byte array"),
                _ => throw new InvalidOperationException($"Unsupported tag type: {TagType}")
            };

            OnPropertyChanged(new EchoChangeEventArgs(this, this, oldValue, _value, ChangeType.ValueChanged));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to set value of type {TagType} to {value} ({value.GetType().Name})", ex);
        }
    }

    private static bool IsNumericType(EchoType type) => type switch {
        EchoType.Byte or EchoType.sByte or
        EchoType.Short or EchoType.Int or EchoType.Long or
        EchoType.UShort or EchoType.UInt or EchoType.ULong or
        EchoType.Float or EchoType.Double or EchoType.Decimal => true,
        _ => false
    };

    /// <summary> Returns the value of this tag, cast as a bool. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than BoolTag. </exception>
    public bool BoolValue { get => (bool)Value; set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a byte. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than ByteTag. </exception>
    public byte ByteValue { get => Convert.ToByte(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a sbyte. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than sByteTag. </exception>
    public sbyte sByteValue { get => Convert.ToSByte(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a short. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than ShortTag. </exception>
    public short ShortValue { get => Convert.ToInt16(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a int. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than IntTag. </exception>
    public int IntValue { get => Convert.ToInt32(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a long. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than LongTag. </exception>
    public long LongValue { get => Convert.ToInt64(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a ushort. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than UShortTag. </exception>
    public ushort UShortValue { get => Convert.ToUInt16(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as an uint. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than UIntTag. </exception>
    public uint UIntValue { get => Convert.ToUInt32(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a ulong. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than ULongTag. </exception>
    public ulong ULongValue { get => Convert.ToUInt64(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a float. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than FloatTag. </exception>
    public float FloatValue { get => Convert.ToSingle(Value); set => SetValue(value); }

    /// <summary> Returns the value of this tag, cast as a double. </summary>
    /// <exception cref="InvalidCastException"> Can throw when used on a tag other than DoubleTag. </exception>
    public double DoubleValue { get => Convert.ToDouble(Value); set => SetValue(value); }

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
    public string StringValue {
        get => TagType switch {
            EchoType.Null => "NULL",
            EchoType.String => Value as string ?? "",
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
