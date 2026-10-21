// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Prowl.Warp;

/// <summary>
/// Builds a message in a growable buffer rented from <see cref="ArrayPool{T}.Shared"/>. Multi-byte
/// values are little-endian; read them back with <see cref="NetReader"/>.
/// </summary>
/// <remarks>Reuse a writer with <see cref="Reset"/>, and <see cref="Dispose"/> it to return its buffer.</remarks>
public sealed class NetWriter : IDisposable
{
    private byte[] _buffer;
    private int _length;

    public NetWriter(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 16));
    }

    /// <summary>Bytes written so far.</summary>
    public int Length => _length;

    /// <summary>The bytes written so far. Invalidated by any further write.</summary>
    public ReadOnlySpan<byte> WrittenSpan => new(_buffer, 0, _length);

    /// <summary>The bytes written so far. Invalidated by any further write.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => new(_buffer, 0, _length);

    /// <summary>Clears the contents, keeping the buffer for reuse.</summary>
    public void Reset() => _length = 0;

    /// <summary>Allocates an owned copy. Use WrittenSpan or WrittenMemory to avoid the copy.</summary>
    public byte[] ToArray() => WrittenSpan.ToArray();

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteByte(byte value)
    {
        Reserve(1)[0] = value;
        _length += 1;
    }

    public void WriteSByte(sbyte value) => WriteByte((byte)value);

    public void WriteInt16(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(Reserve(2), value);
        _length += 2;
    }

    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
        _length += 2;
    }

    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
        _length += 4;
    }

    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
        _length += 4;
    }

    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);
        _length += 8;
    }

    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
        _length += 8;
    }

    public void WriteSingle(float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(Reserve(4), value);
        _length += 4;
    }

    public void WriteDouble(double value)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(Reserve(8), value);
        _length += 8;
    }

    /// <summary>Writes 1 to 5 bytes: 7 bits per byte, small values smallest.</summary>
    public void WriteVarUInt32(uint value) => WriteVarUInt64(value);

    /// <summary>Writes 1 to 10 bytes: 7 bits per byte, small values smallest.</summary>
    public void WriteVarUInt64(ulong value)
    {
        Span<byte> span = Reserve(10);
        int i = 0;
        while (value >= 0x80)
        {
            span[i++] = (byte)(value | 0x80);
            value >>= 7;
        }
        span[i++] = (byte)value;
        _length += i;
    }

    /// <summary>Zigzag encoded, so values close to zero (positive or negative) stay small.</summary>
    public void WriteVarInt32(int value) => WriteVarUInt32((uint)((value << 1) ^ (value >> 31)));

    /// <summary>Zigzag encoded, so values close to zero (positive or negative) stay small.</summary>
    public void WriteVarInt64(long value) => WriteVarUInt64((ulong)((value << 1) ^ (value >> 63)));

    public void WriteGuid(Guid value)
    {
        value.TryWriteBytes(Reserve(16));
        _length += 16;
    }

    /// <summary>UTF-8, prefixed with its byte length as a var-uint. Null is written as the empty string.</summary>
    public void WriteString(string? value)
    {
        value ??= string.Empty;
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarUInt32((uint)byteCount);
        Encoding.UTF8.GetBytes(value, Reserve(byteCount));
        _length += byteCount;
    }

    /// <summary>Raw bytes prefixed with their length as a var-uint.</summary>
    public void WriteBytesWithLength(ReadOnlySpan<byte> value)
    {
        WriteVarUInt32((uint)value.Length);
        WriteBytes(value);
    }

    /// <summary>Raw bytes, no length prefix.</summary>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(Reserve(value.Length));
        _length += value.Length;
    }

    public void Dispose()
    {
        if (_buffer.Length == 0)
            return;

        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = Array.Empty<byte>();
        _length = 0;
    }

    private Span<byte> Reserve(int count)
    {
        if (_length + count > _buffer.Length)
            Grow(_length + count);

        return _buffer.AsSpan(_length, count);
    }

    private void Grow(int required)
    {
        if (_buffer.Length == 0)
            throw new ObjectDisposedException(nameof(NetWriter));

        byte[] larger = ArrayPool<byte>.Shared.Rent(Math.Max(required, _buffer.Length * 2));
        _buffer.AsSpan(0, _length).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = larger;
    }
}
