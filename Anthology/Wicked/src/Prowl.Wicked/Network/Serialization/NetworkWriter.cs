namespace Prowl.Wicked.Network.Serialization;

using System.Text;

/// <summary>
/// Writes primitive types and objects to a byte buffer for network transmission.
/// </summary>
public class NetworkWriter
{
    private byte[] _buffer;
    private int _position;

    /// <summary>
    /// The current write position in the buffer.
    /// </summary>
    public int Position => _position;

    /// <summary>
    /// The total capacity of the buffer.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Creates a new NetworkWriter with the specified initial capacity.
    /// </summary>
    public NetworkWriter(int initialCapacity = 1024)
    {
        _buffer = new byte[initialCapacity];
        _position = 0;
    }

    /// <summary>
    /// Resets the writer position to the beginning.
    /// </summary>
    public void Reset()
    {
        _position = 0;
    }

    /// <summary>
    /// Gets the written data as an ArraySegment.
    /// </summary>
    public ArraySegment<byte> ToArraySegment()
    {
        return new ArraySegment<byte>(_buffer, 0, _position);
    }

    /// <summary>
    /// Gets the written data as a byte array.
    /// </summary>
    public byte[] ToArray()
    {
        var result = new byte[_position];
        Buffer.BlockCopy(_buffer, 0, result, 0, _position);
        return result;
    }

    private void EnsureCapacity(int additionalBytes)
    {
        int required = _position + additionalBytes;
        if (required > _buffer.Length)
        {
            int newSize = Math.Max(_buffer.Length * 2, required);
            var newBuffer = new byte[newSize];
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _position);
            _buffer = newBuffer;
        }
    }

    // Primitive writes

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    public void WriteSByte(sbyte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = (byte)value;
    }

    public void WriteBool(bool value)
    {
        WriteByte(value ? (byte)1 : (byte)0);
    }

    public void WriteShort(short value)
    {
        EnsureCapacity(2);
        _buffer[_position++] = (byte)value;
        _buffer[_position++] = (byte)(value >> 8);
    }

    public void WriteUShort(ushort value)
    {
        EnsureCapacity(2);
        _buffer[_position++] = (byte)value;
        _buffer[_position++] = (byte)(value >> 8);
    }

    public void WriteInt(int value)
    {
        EnsureCapacity(4);
        _buffer[_position++] = (byte)value;
        _buffer[_position++] = (byte)(value >> 8);
        _buffer[_position++] = (byte)(value >> 16);
        _buffer[_position++] = (byte)(value >> 24);
    }

    public void WriteUInt(uint value)
    {
        EnsureCapacity(4);
        _buffer[_position++] = (byte)value;
        _buffer[_position++] = (byte)(value >> 8);
        _buffer[_position++] = (byte)(value >> 16);
        _buffer[_position++] = (byte)(value >> 24);
    }

    public void WriteLong(long value)
    {
        EnsureCapacity(8);
        _buffer[_position++] = (byte)value;
        _buffer[_position++] = (byte)(value >> 8);
        _buffer[_position++] = (byte)(value >> 16);
        _buffer[_position++] = (byte)(value >> 24);
        _buffer[_position++] = (byte)(value >> 32);
        _buffer[_position++] = (byte)(value >> 40);
        _buffer[_position++] = (byte)(value >> 48);
        _buffer[_position++] = (byte)(value >> 56);
    }

    public void WriteULong(ulong value)
    {
        EnsureCapacity(8);
        _buffer[_position++] = (byte)value;
        _buffer[_position++] = (byte)(value >> 8);
        _buffer[_position++] = (byte)(value >> 16);
        _buffer[_position++] = (byte)(value >> 24);
        _buffer[_position++] = (byte)(value >> 32);
        _buffer[_position++] = (byte)(value >> 40);
        _buffer[_position++] = (byte)(value >> 48);
        _buffer[_position++] = (byte)(value >> 56);
    }

    public void WriteFloat(float value)
    {
        EnsureCapacity(4);
        var bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, _buffer, _position, 4);
        _position += 4;
    }

    public void WriteDouble(double value)
    {
        EnsureCapacity(8);
        var bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, _buffer, _position, 8);
        _position += 8;
    }

    public void WriteString(string? value)
    {
        if (value == null)
        {
            WriteUShort(0);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > ushort.MaxValue)
            throw new ArgumentException($"String too long: {byteCount} bytes (max {ushort.MaxValue})");

        WriteUShort((ushort)(byteCount + 1)); // +1 to distinguish from null
        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _position);
        _position += byteCount;
    }

    public void WriteBytes(byte[] bytes)
    {
        WriteInt(bytes.Length);
        EnsureCapacity(bytes.Length);
        Buffer.BlockCopy(bytes, 0, _buffer, _position, bytes.Length);
        _position += bytes.Length;
    }

    public void WriteBytes(ArraySegment<byte> segment)
    {
        WriteInt(segment.Count);
        EnsureCapacity(segment.Count);
        Buffer.BlockCopy(segment.Array!, segment.Offset, _buffer, _position, segment.Count);
        _position += segment.Count;
    }

    public void WriteBytesRaw(byte[] bytes, int offset, int count)
    {
        EnsureCapacity(count);
        Buffer.BlockCopy(bytes, offset, _buffer, _position, count);
        _position += count;
    }

    public void WriteGuid(Guid value)
    {
        var bytes = value.ToByteArray();
        EnsureCapacity(16);
        Buffer.BlockCopy(bytes, 0, _buffer, _position, 16);
        _position += 16;
    }

    /// <summary>
    /// Writes a value of type T. Only primitive types are supported.
    /// Supported types: byte, sbyte, bool, short, ushort, int, uint, long, ulong, float, double, string, Guid, byte[]
    /// </summary>
    public void Write<T>(T value)
    {
        switch (value)
        {
            case byte v: WriteByte(v); break;
            case sbyte v: WriteSByte(v); break;
            case bool v: WriteBool(v); break;
            case short v: WriteShort(v); break;
            case ushort v: WriteUShort(v); break;
            case int v: WriteInt(v); break;
            case uint v: WriteUInt(v); break;
            case long v: WriteLong(v); break;
            case ulong v: WriteULong(v); break;
            case float v: WriteFloat(v); break;
            case double v: WriteDouble(v); break;
            case string v: WriteString(v); break;
            case Guid v: WriteGuid(v); break;
            case byte[] v: WriteBytes(v); break;
            default:
                throw new NotSupportedException($"Type {typeof(T).Name} is not supported for network serialization. Only primitive types are allowed.");
        }
    }

    /// <summary>
    /// Writes an object value with type tag. Used for sync data where type is not known at compile time.
    /// </summary>
    public void WriteTypedValue(object? value)
    {
        if (value == null)
        {
            WriteByte(0); // null type tag
            return;
        }

        switch (value)
        {
            case byte v: WriteByte(1); WriteByte(v); break;
            case sbyte v: WriteByte(2); WriteSByte(v); break;
            case bool v: WriteByte(3); WriteBool(v); break;
            case short v: WriteByte(4); WriteShort(v); break;
            case ushort v: WriteByte(5); WriteUShort(v); break;
            case int v: WriteByte(6); WriteInt(v); break;
            case uint v: WriteByte(7); WriteUInt(v); break;
            case long v: WriteByte(8); WriteLong(v); break;
            case ulong v: WriteByte(9); WriteULong(v); break;
            case float v: WriteByte(10); WriteFloat(v); break;
            case double v: WriteByte(11); WriteDouble(v); break;
            case string v: WriteByte(12); WriteString(v); break;
            case Guid v: WriteByte(13); WriteGuid(v); break;
            case byte[] v: WriteByte(14); WriteBytes(v); break;
            default:
                throw new NotSupportedException($"Type {value.GetType().Name} is not supported for network serialization. Only primitive types are allowed.");
        }
    }
}
