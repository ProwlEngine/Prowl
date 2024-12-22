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
    public EchoObject Property { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }

    public EchoChangeEventArgs(EchoObject property, object? oldValue, object? newValue)
    {
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

public sealed partial class EchoObject
{
    public event EventHandler<EchoChangeEventArgs>? PropertyChanged;

    private object? _value;
    public object? Value { get { return _value; } private set { SetValue(value); } }

    public EchoType TagType { get; private set; }

    public EchoObject? Parent { get; private set; }

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
            _value = new List<EchoObject>();
        else if (type == EchoType.Compound && value == null)
            _value = new Dictionary<string, EchoObject>();
        else
            _value = value;
    }
    public EchoObject(List<EchoObject> tags)
    {
        TagType = EchoType.List;
        _value = tags;
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
        }
        else if (TagType == EchoType.Compound)
        {
            // Value is a Dictionary<string, Tag>
            var dict = (Dictionary<string, EchoObject>)Value!;
            var newDict = new Dictionary<string, EchoObject>(dict.Count);
            foreach (var (key, tag) in dict)
                newDict.Add(key, tag.Clone());
        }
        return new(TagType, Value);
    }

    private void OnPropertyChanged(EchoChangeEventArgs e)
    {
        PropertyChanged?.Invoke(this, e);
        Parent?.OnPropertyChanged(e);
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
        var old = _value;
        try 
        { 
            _value = TagType switch
            {
                EchoType.Byte => (byte)value,
                EchoType.sByte => (sbyte)value,
                EchoType.Short => (short)value,
                EchoType.Int => (int)value,
                EchoType.Long => (long)value,
                EchoType.UShort => (ushort)value,
                EchoType.UInt => (uint)value,
                EchoType.ULong => (ulong)value,
                EchoType.Float => (float)value,
                EchoType.Double => (double)value,
                EchoType.Decimal => (decimal)value,
                EchoType.String => (string)value,
                EchoType.ByteArray => (byte[])value,
                EchoType.Bool => (bool)value,
                _ => throw new Exception()
            };
        }
        catch (Exception e) { throw new InvalidOperationException("Cannot set value of " + TagType.ToString() + " to " + value.ToString(), e); }

        OnPropertyChanged(new EchoChangeEventArgs(this, old, value));
    }

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
