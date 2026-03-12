using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Wicked;

/// <summary>
/// Binary writer for serializing data to send over the network.
/// All multi-byte values are written in little-endian byte order.
/// </summary>
public class NetworkWriter
{
    private byte[] _buffer = new byte[256];
    private int _position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int additional)
    {
        int required = _position + additional;
        if (required > _buffer.Length)
        {
            int newSize = _buffer.Length;
            while (newSize < required) newSize *= 2;
            Array.Resize(ref _buffer, newSize);
        }
    }

    /// <summary>Writes a single byte.</summary>
    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    /// <summary>Writes a signed byte.</summary>
    public void WriteSByte(sbyte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = (byte)value;
    }

    /// <summary>Writes a 16-bit signed integer (little-endian).</summary>
    public void WriteShort(short value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += 2;
    }

    /// <summary>Writes a 16-bit unsigned integer (little-endian).</summary>
    public void WriteUShort(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += 2;
    }

    /// <summary>Writes a 32-bit signed integer (little-endian).</summary>
    public void WriteInt(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    /// <summary>Writes a 32-bit unsigned integer (little-endian).</summary>
    public void WriteUInt(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    /// <summary>Writes a 64-bit signed integer (little-endian).</summary>
    public void WriteLong(long value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    /// <summary>Writes a 64-bit unsigned integer (little-endian).</summary>
    public void WriteULong(ulong value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    /// <summary>Writes a 32-bit float (little-endian).</summary>
    public void WriteFloat(float value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    /// <summary>Writes a 64-bit double (little-endian).</summary>
    public void WriteDouble(double value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteDoubleLittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    /// <summary>Writes a boolean (1 byte: 0 = false, 1 = true).</summary>
    public void WriteBool(bool value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Writes a string (1-byte null flag + 4-byte length prefix + UTF-8 bytes).
    /// Null strings are supported — a null flag byte is written and ReadString() returns null.
    /// </summary>
    public void WriteString(string? value)
    {
        if (value == null)
        {
            WriteByte(0); // null flag
            return;
        }
        WriteByte(1); // not-null flag
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt(byteCount);
        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _position);
        _position += byteCount;
    }

    /// <summary>Writes a GUID (16 bytes).</summary>
    public void WriteGuid(Guid value)
    {
        EnsureCapacity(16);
        value.TryWriteBytes(_buffer.AsSpan(_position));
        _position += 16;
    }

    /// <summary>Writes a Vector2 (2 floats, 8 bytes).</summary>
    public void WriteVector2(Vector2 value)
    {
        WriteFloat(value.X);
        WriteFloat(value.Y);
    }

    /// <summary>
    /// Writes a NetworkEntity reference (1-byte null flag + 4-byte NetworkId).
    /// Null entities are supported — a false flag is written and ReadEntityRef() returns null.
    /// </summary>
    public void WriteEntityRef(NetworkEntity? entity)
    {
        if (entity == null)
        {
            WriteBool(false);
            return;
        }
        WriteBool(true);
        WriteUInt(entity.NetworkId);
    }

    /// <summary>Writes a custom serializable type.</summary>
    public void Write<T>(T value) where T : INetworkSerializable
    {
        value.Serialize(this);
    }

    /// <summary>
    /// Writes an array of serializable values (4-byte length + each element).
    /// Null arrays are supported — writes -1 as length.
    /// </summary>
    public void WriteArray<T>(T[]? array) where T : INetworkSerializable
    {
        if (array == null)
        {
            WriteInt(-1);
            return;
        }
        WriteInt(array.Length);
        foreach (var item in array)
            item.Serialize(this);
    }

    // --- Primitive array serialization ---

    /// <summary>Writes an int array. Null arrays write -1 as length.</summary>
    public void WriteIntArray(int[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteInt(v);
    }

    /// <summary>Writes a uint array. Null arrays write -1 as length.</summary>
    public void WriteUIntArray(uint[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteUInt(v);
    }

    /// <summary>Writes a float array. Null arrays write -1 as length.</summary>
    public void WriteFloatArray(float[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteFloat(v);
    }

    /// <summary>Writes a double array. Null arrays write -1 as length.</summary>
    public void WriteDoubleArray(double[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteDouble(v);
    }

    /// <summary>Writes a string array. Null arrays write -1 as length. Individual elements may be null.</summary>
    public void WriteStringArray(string?[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteString(v);
    }

    /// <summary>Writes a byte array. Null arrays write -1 as length.</summary>
    public void WriteByteArray(byte[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        EnsureCapacity(array.Length);
        Array.Copy(array, 0, _buffer, _position, array.Length);
        _position += array.Length;
    }

    /// <summary>Writes a long array. Null arrays write -1 as length.</summary>
    public void WriteLongArray(long[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteLong(v);
    }

    /// <summary>Writes a ulong array. Null arrays write -1 as length.</summary>
    public void WriteULongArray(ulong[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteULong(v);
    }

    /// <summary>Writes a short array. Null arrays write -1 as length.</summary>
    public void WriteShortArray(short[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteShort(v);
    }

    /// <summary>Writes a ushort array. Null arrays write -1 as length.</summary>
    public void WriteUShortArray(ushort[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteUShort(v);
    }

    /// <summary>Writes a bool array. Null arrays write -1 as length.</summary>
    public void WriteBoolArray(bool[]? array)
    {
        if (array == null) { WriteInt(-1); return; }
        WriteInt(array.Length);
        foreach (var v in array) WriteBool(v);
    }

    // --- Enum serialization ---

    /// <summary>
    /// Writes an enum value as its underlying integer type.
    /// Handles byte, sbyte, short, ushort, int, uint, long, and ulong underlying types.
    /// Safer than manual casting — correctly handles non-int backed enums.
    /// </summary>
    public void WriteEnum<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var underlyingType = Enum.GetUnderlyingType(typeof(TEnum));

        if (underlyingType == typeof(int)) WriteInt(Unsafe.As<TEnum, int>(ref value));
        else if (underlyingType == typeof(byte)) WriteByte(Unsafe.As<TEnum, byte>(ref value));
        else if (underlyingType == typeof(sbyte)) WriteSByte(Unsafe.As<TEnum, sbyte>(ref value));
        else if (underlyingType == typeof(short)) WriteShort(Unsafe.As<TEnum, short>(ref value));
        else if (underlyingType == typeof(ushort)) WriteUShort(Unsafe.As<TEnum, ushort>(ref value));
        else if (underlyingType == typeof(uint)) WriteUInt(Unsafe.As<TEnum, uint>(ref value));
        else if (underlyingType == typeof(long)) WriteLong(Unsafe.As<TEnum, long>(ref value));
        else if (underlyingType == typeof(ulong)) WriteULong(Unsafe.As<TEnum, ulong>(ref value));
        else throw new NotSupportedException($"Enum underlying type {underlyingType} is not supported.");
    }

    /// <summary>Writes raw bytes without a length prefix. Pair with ReadBytes(int count).</summary>
    public void WriteBytes(ArraySegment<byte> data)
    {
        if (data.Count > 0)
        {
            EnsureCapacity(data.Count);
            Array.Copy(data.Array!, data.Offset, _buffer, _position, data.Count);
            _position += data.Count;
        }
    }

    /// <summary>
    /// Returns the written data as an ArraySegment referencing the internal buffer.
    /// WARNING: The returned segment shares the writer's mutable buffer. If the writer
    /// is Reset() or reused before the consumer finishes reading, the data is silently
    /// corrupted. Copy the data immediately if the writer will be reused.
    /// </summary>
    public ArraySegment<byte> ToArraySegment()
    {
        return new ArraySegment<byte>(_buffer, 0, _position);
    }

    /// <summary>Resets the writer for reuse. Internal buffer is retained.</summary>
    public void Reset()
    {
        _position = 0;
    }
}
