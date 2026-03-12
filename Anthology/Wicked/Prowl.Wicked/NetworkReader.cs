using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Wicked;

/// <summary>
/// Binary reader for deserializing data received from the network.
/// All multi-byte values are read in little-endian byte order.
/// </summary>
public class NetworkReader
{
    private readonly byte[] _array;
    private readonly int _offset;
    private readonly int _count;
    private int _position;

    /// <summary>Creates a reader positioned at the start of the given data.</summary>
    public NetworkReader(ArraySegment<byte> data)
    {
        _array = data.Array!;
        _offset = data.Offset;
        _count = data.Count;
        _position = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> ReadSpan(int count)
    {
        if (_position + count > _count)
            throw new InvalidOperationException($"Read past end of buffer. Tried to read {count} bytes at position {_position}, but only {_count - _position} bytes remain.");
        var span = new ReadOnlySpan<byte>(_array, _offset + _position, count);
        _position += count;
        return span;
    }

    /// <summary>Reads a single byte.</summary>
    public byte ReadByte()
    {
        if (_position + 1 > _count)
            throw new InvalidOperationException("Read past end of buffer.");
        return _array[_offset + _position++];
    }

    /// <summary>Reads a signed byte.</summary>
    public sbyte ReadSByte()
    {
        return (sbyte)ReadByte();
    }

    /// <summary>Reads a 16-bit signed integer (little-endian).</summary>
    public short ReadShort()
    {
        return BinaryPrimitives.ReadInt16LittleEndian(ReadSpan(2));
    }

    /// <summary>Reads a 16-bit unsigned integer (little-endian).</summary>
    public ushort ReadUShort()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(ReadSpan(2));
    }

    /// <summary>Reads a 32-bit signed integer (little-endian).</summary>
    public int ReadInt()
    {
        return BinaryPrimitives.ReadInt32LittleEndian(ReadSpan(4));
    }

    /// <summary>Reads a 32-bit unsigned integer (little-endian).</summary>
    public uint ReadUInt()
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(4));
    }

    /// <summary>Reads a 64-bit signed integer (little-endian).</summary>
    public long ReadLong()
    {
        return BinaryPrimitives.ReadInt64LittleEndian(ReadSpan(8));
    }

    /// <summary>Reads a 64-bit unsigned integer (little-endian).</summary>
    public ulong ReadULong()
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(ReadSpan(8));
    }

    /// <summary>Reads a 32-bit float (little-endian).</summary>
    public float ReadFloat()
    {
        return BinaryPrimitives.ReadSingleLittleEndian(ReadSpan(4));
    }

    /// <summary>Reads a 64-bit double (little-endian).</summary>
    public double ReadDouble()
    {
        return BinaryPrimitives.ReadDoubleLittleEndian(ReadSpan(8));
    }

    /// <summary>Reads a boolean (1 byte: 0 = false, non-zero = true).</summary>
    public bool ReadBool()
    {
        return ReadByte() != 0;
    }

    /// <summary>
    /// Reads a string (1-byte null flag + 4-byte length prefix + UTF-8 bytes).
    /// Returns null if a null string was written.
    /// </summary>
    public string? ReadString()
    {
        byte nullFlag = ReadByte();
        if (nullFlag == 0) return null;
        int byteCount = ReadInt();
        if (byteCount < 0)
            throw new InvalidOperationException($"Invalid string byte count: {byteCount}");
        if (byteCount == 0) return string.Empty;
        var span = ReadSpan(byteCount);
        return Encoding.UTF8.GetString(span);
    }

    /// <summary>Reads a GUID (16 bytes).</summary>
    public Guid ReadGuid()
    {
        var span = ReadSpan(16);
        return new Guid(span);
    }

    /// <summary>Reads a Vector2 (2 floats, 8 bytes).</summary>
    public Vector2 ReadVector2()
    {
        float x = ReadFloat();
        float y = ReadFloat();
        return new Vector2(x, y);
    }

    /// <summary>
    /// Reads a NetworkEntity reference written by WriteEntityRef.
    /// Returns null if a null entity was written or if the entity is not found.
    /// Looks up by NetworkId on both server and client.
    /// </summary>
    public NetworkEntity? ReadEntityRef()
    {
        bool hasValue = ReadBool();
        if (!hasValue) return null;
        uint networkId = ReadUInt();
        return Server.FindEntity(networkId) ?? Client.FindEntity(networkId);
    }

    /// <summary>Reads a custom serializable type.</summary>
    public T Read<T>() where T : INetworkSerializable, new()
    {
        var value = new T();
        value.Deserialize(this);
        return value;
    }

    /// <summary>
    /// Reads an array of serializable values. Returns null if a null array was written.
    /// </summary>
    public T[]? ReadArray<T>() where T : INetworkSerializable, new()
    {
        var length = ReadInt();
        if (length < 0) return null;
        if (length > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new T[length];
        for (int i = 0; i < length; i++)
            array[i] = Read<T>();
        return array;
    }

    // --- Primitive array deserialization ---

    /// <summary>Reads an int array. Returns null if a null array was written.</summary>
    public int[]? ReadIntArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if ((long)length * 4 > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new int[length];
        for (int i = 0; i < length; i++) array[i] = ReadInt();
        return array;
    }

    /// <summary>Reads a uint array. Returns null if a null array was written.</summary>
    public uint[]? ReadUIntArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if ((long)length * 4 > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new uint[length];
        for (int i = 0; i < length; i++) array[i] = ReadUInt();
        return array;
    }

    /// <summary>Reads a float array. Returns null if a null array was written.</summary>
    public float[]? ReadFloatArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if ((long)length * 4 > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new float[length];
        for (int i = 0; i < length; i++) array[i] = ReadFloat();
        return array;
    }

    /// <summary>Reads a double array. Returns null if a null array was written.</summary>
    public double[]? ReadDoubleArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if ((long)length * 8 > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new double[length];
        for (int i = 0; i < length; i++) array[i] = ReadDouble();
        return array;
    }

    /// <summary>Reads a string array. Returns null if a null array was written. Individual elements may be null.</summary>
    public string?[]? ReadStringArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        // Each string is at least 1 byte (null flag)
        if (length > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new string?[length];
        for (int i = 0; i < length; i++) array[i] = ReadString();
        return array;
    }

    /// <summary>Reads a byte array. Returns null if a null array was written.</summary>
    public byte[]? ReadByteArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if (length > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new byte[length];
        ReadSpan(length).CopyTo(array);
        return array;
    }

    /// <summary>Reads a long array. Returns null if a null array was written.</summary>
    public long[]? ReadLongArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if ((long)length * 8 > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new long[length];
        for (int i = 0; i < length; i++) array[i] = ReadLong();
        return array;
    }

    /// <summary>Reads a ulong array. Returns null if a null array was written.</summary>
    public ulong[]? ReadULongArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if ((long)length * 8 > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new ulong[length];
        for (int i = 0; i < length; i++) array[i] = ReadULong();
        return array;
    }

    /// <summary>Reads a short array. Returns null if a null array was written.</summary>
    public short[]? ReadShortArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if ((long)length * 2 > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new short[length];
        for (int i = 0; i < length; i++) array[i] = ReadShort();
        return array;
    }

    /// <summary>Reads a ushort array. Returns null if a null array was written.</summary>
    public ushort[]? ReadUShortArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if ((long)length * 2 > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new ushort[length];
        for (int i = 0; i < length; i++) array[i] = ReadUShort();
        return array;
    }

    /// <summary>Reads a bool array. Returns null if a null array was written.</summary>
    public bool[]? ReadBoolArray()
    {
        int length = ReadInt();
        if (length < 0) return null;
        if (length > Remaining)
            throw new InvalidOperationException($"Array length {length} exceeds remaining buffer ({Remaining} bytes).");
        var array = new bool[length];
        for (int i = 0; i < length; i++) array[i] = ReadBool();
        return array;
    }

    // --- Enum deserialization ---

    /// <summary>
    /// Reads an enum value written by WriteEnum&lt;TEnum&gt;.
    /// Reads the correct number of bytes based on the enum's underlying type.
    /// </summary>
    public TEnum ReadEnum<TEnum>() where TEnum : struct, Enum
    {
        var underlyingType = Enum.GetUnderlyingType(typeof(TEnum));

        if (underlyingType == typeof(int)) { var v = ReadInt(); return Unsafe.As<int, TEnum>(ref v); }
        if (underlyingType == typeof(byte)) { var v = ReadByte(); return Unsafe.As<byte, TEnum>(ref v); }
        if (underlyingType == typeof(sbyte)) { var v = ReadSByte(); return Unsafe.As<sbyte, TEnum>(ref v); }
        if (underlyingType == typeof(short)) { var v = ReadShort(); return Unsafe.As<short, TEnum>(ref v); }
        if (underlyingType == typeof(ushort)) { var v = ReadUShort(); return Unsafe.As<ushort, TEnum>(ref v); }
        if (underlyingType == typeof(uint)) { var v = ReadUInt(); return Unsafe.As<uint, TEnum>(ref v); }
        if (underlyingType == typeof(long)) { var v = ReadLong(); return Unsafe.As<long, TEnum>(ref v); }
        if (underlyingType == typeof(ulong)) { var v = ReadULong(); return Unsafe.As<ulong, TEnum>(ref v); }

        throw new NotSupportedException($"Enum underlying type {underlyingType} is not supported.");
    }

    /// <summary>Reads the specified number of raw bytes.</summary>
    public ArraySegment<byte> ReadBytes(int count)
    {
        if (_position + count > _count)
            throw new InvalidOperationException($"Read past end of buffer. Tried to read {count} bytes at position {_position}, but only {_count - _position} bytes remain.");
        var segment = new ArraySegment<byte>(_array, _offset + _position, count);
        _position += count;
        return segment;
    }

    /// <summary>Number of unread bytes remaining.</summary>
    public int Remaining => _count - _position;
}
