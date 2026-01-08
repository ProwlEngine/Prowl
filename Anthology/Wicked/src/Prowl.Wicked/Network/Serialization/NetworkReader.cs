namespace Prowl.Wicked.Network.Serialization;

using System.Text;

/// <summary>
/// Reads primitive types and objects from a byte buffer received from the network.
/// </summary>
public class NetworkReader
{
    private readonly byte[] _buffer;
    private readonly int _offset;
    private readonly int _length;
    private int _position;

    /// <summary>
    /// The current read position in the buffer.
    /// </summary>
    public int Position => _position;

    /// <summary>
    /// The total length of readable data.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// The number of bytes remaining to read.
    /// </summary>
    public int Remaining => _length - _position;

    /// <summary>
    /// Creates a new NetworkReader from a byte array.
    /// </summary>
    public NetworkReader(byte[] data)
    {
        _buffer = data;
        _offset = 0;
        _length = data.Length;
        _position = 0;
    }

    /// <summary>
    /// Creates a new NetworkReader from an ArraySegment.
    /// </summary>
    public NetworkReader(ArraySegment<byte> segment)
    {
        _buffer = segment.Array!;
        _offset = segment.Offset;
        _length = segment.Count;
        _position = 0;
    }

    /// <summary>
    /// Resets the reader position to the beginning.
    /// </summary>
    public void Reset()
    {
        _position = 0;
    }

    private void EnsureReadable(int bytes)
    {
        if (_position + bytes > _length)
            throw new InvalidOperationException($"Read overflow: trying to read {bytes} bytes at position {_position}, but only {_length - _position} bytes remaining");
    }

    // Primitive reads

    public byte ReadByte()
    {
        EnsureReadable(1);
        return _buffer[_offset + _position++];
    }

    public sbyte ReadSByte()
    {
        return (sbyte)ReadByte();
    }

    public bool ReadBool()
    {
        return ReadByte() != 0;
    }

    public short ReadShort()
    {
        EnsureReadable(2);
        short value = (short)(_buffer[_offset + _position] | (_buffer[_offset + _position + 1] << 8));
        _position += 2;
        return value;
    }

    public ushort ReadUShort()
    {
        EnsureReadable(2);
        ushort value = (ushort)(_buffer[_offset + _position] | (_buffer[_offset + _position + 1] << 8));
        _position += 2;
        return value;
    }

    public int ReadInt()
    {
        EnsureReadable(4);
        int value = _buffer[_offset + _position] |
                   (_buffer[_offset + _position + 1] << 8) |
                   (_buffer[_offset + _position + 2] << 16) |
                   (_buffer[_offset + _position + 3] << 24);
        _position += 4;
        return value;
    }

    public uint ReadUInt()
    {
        return (uint)ReadInt();
    }

    public long ReadLong()
    {
        EnsureReadable(8);
        long value = (long)_buffer[_offset + _position] |
                    ((long)_buffer[_offset + _position + 1] << 8) |
                    ((long)_buffer[_offset + _position + 2] << 16) |
                    ((long)_buffer[_offset + _position + 3] << 24) |
                    ((long)_buffer[_offset + _position + 4] << 32) |
                    ((long)_buffer[_offset + _position + 5] << 40) |
                    ((long)_buffer[_offset + _position + 6] << 48) |
                    ((long)_buffer[_offset + _position + 7] << 56);
        _position += 8;
        return value;
    }

    public ulong ReadULong()
    {
        return (ulong)ReadLong();
    }

    public float ReadFloat()
    {
        EnsureReadable(4);
        float value = BitConverter.ToSingle(_buffer, _offset + _position);
        _position += 4;
        return value;
    }

    public double ReadDouble()
    {
        EnsureReadable(8);
        double value = BitConverter.ToDouble(_buffer, _offset + _position);
        _position += 8;
        return value;
    }

    public string? ReadString()
    {
        ushort length = ReadUShort();
        if (length == 0)
            return null;

        length--; // Remove the +1 that was added for non-null
        EnsureReadable(length);
        string value = Encoding.UTF8.GetString(_buffer, _offset + _position, length);
        _position += length;
        return value;
    }

    public byte[] ReadBytes()
    {
        int length = ReadInt();
        EnsureReadable(length);
        var result = new byte[length];
        Buffer.BlockCopy(_buffer, _offset + _position, result, 0, length);
        _position += length;
        return result;
    }

    public ArraySegment<byte> ReadBytesSegment()
    {
        int length = ReadInt();
        EnsureReadable(length);
        var segment = new ArraySegment<byte>(_buffer, _offset + _position, length);
        _position += length;
        return segment;
    }

    public void ReadBytesInto(byte[] destination, int destOffset, int count)
    {
        EnsureReadable(count);
        Buffer.BlockCopy(_buffer, _offset + _position, destination, destOffset, count);
        _position += count;
    }

    public Guid ReadGuid()
    {
        EnsureReadable(16);
        var bytes = new byte[16];
        Buffer.BlockCopy(_buffer, _offset + _position, bytes, 0, 16);
        _position += 16;
        return new Guid(bytes);
    }

    /// <summary>
    /// Reads a value of type T. Only primitive types are supported.
    /// Supported types: byte, sbyte, bool, short, ushort, int, uint, long, ulong, float, double, string, Guid, byte[]
    /// </summary>
    public T Read<T>()
    {
        var type = typeof(T);

        if (type == typeof(byte)) return (T)(object)ReadByte();
        if (type == typeof(sbyte)) return (T)(object)ReadSByte();
        if (type == typeof(bool)) return (T)(object)ReadBool();
        if (type == typeof(short)) return (T)(object)ReadShort();
        if (type == typeof(ushort)) return (T)(object)ReadUShort();
        if (type == typeof(int)) return (T)(object)ReadInt();
        if (type == typeof(uint)) return (T)(object)ReadUInt();
        if (type == typeof(long)) return (T)(object)ReadLong();
        if (type == typeof(ulong)) return (T)(object)ReadULong();
        if (type == typeof(float)) return (T)(object)ReadFloat();
        if (type == typeof(double)) return (T)(object)ReadDouble();
        if (type == typeof(string)) return (T)(object)ReadString()!;
        if (type == typeof(Guid)) return (T)(object)ReadGuid();
        if (type == typeof(byte[])) return (T)(object)ReadBytes();

        throw new NotSupportedException($"Type {typeof(T).Name} is not supported for network deserialization. Only primitive types are allowed.");
    }

    /// <summary>
    /// Reads a typed value that was written with WriteTypedValue.
    /// </summary>
    public object? ReadTypedValue()
    {
        byte typeTag = ReadByte();
        return typeTag switch
        {
            0 => null,
            1 => ReadByte(),
            2 => ReadSByte(),
            3 => ReadBool(),
            4 => ReadShort(),
            5 => ReadUShort(),
            6 => ReadInt(),
            7 => ReadUInt(),
            8 => ReadLong(),
            9 => ReadULong(),
            10 => ReadFloat(),
            11 => ReadDouble(),
            12 => ReadString(),
            13 => ReadGuid(),
            14 => ReadBytes(),
            _ => throw new InvalidOperationException($"Unknown type tag: {typeTag}")
        };
    }

    /// <summary>
    /// Skips the specified number of bytes.
    /// </summary>
    public void Skip(int bytes)
    {
        EnsureReadable(bytes);
        _position += bytes;
    }
}
