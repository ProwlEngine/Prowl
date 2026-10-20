// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Prowl.Warp;

/// <summary>
/// Reads values written by <see cref="NetWriter"/> from a span, without allocating (except for strings).
/// </summary>
/// <remarks>
/// Network data is untrusted. Every read is bounds checked: reading past the end throws
/// <see cref="EndOfStreamException"/> and malformed data throws <see cref="InvalidDataException"/>.
/// </remarks>
public ref struct NetReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public NetReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public readonly int Position => _position;
    public readonly int Length => _data.Length;
    public readonly int Remaining => _data.Length - _position;
    public readonly bool IsAtEnd => _position >= _data.Length;

    public bool ReadBool() => ReadByte() != 0;

    public byte ReadByte() => Take(1)[0];

    public sbyte ReadSByte() => (sbyte)ReadByte();

    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public uint ReadVarUInt32()
    {
        ulong value = ReadVarUInt64();
        if (value > uint.MaxValue)
            throw new InvalidDataException("Var-uint is too large for 32 bits.");
        return (uint)value;
    }

    public ulong ReadVarUInt64()
    {
        ulong result = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            byte b = ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
        }
        throw new InvalidDataException("Var-uint is longer than 10 bytes.");
    }

    public int ReadVarInt32()
    {
        uint value = ReadVarUInt32();
        return (int)(value >> 1) ^ -(int)(value & 1);
    }

    public long ReadVarInt64()
    {
        ulong value = ReadVarUInt64();
        return (long)(value >> 1) ^ -(long)(value & 1);
    }

    public Guid ReadGuid() => new(Take(16));

    public string ReadString()
    {
        int length = ReadLength();
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(Take(length));
    }

    /// <summary>Reads bytes written by <see cref="NetWriter.WriteBytesWithLength"/>. The span points into the source data.</summary>
    public ReadOnlySpan<byte> ReadBytesWithLength() => Take(ReadLength());

    /// <summary>Reads <paramref name="count"/> raw bytes. The span points into the source data.</summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        return Take(count);
    }

    private int ReadLength()
    {
        uint length = ReadVarUInt32();
        if (length > (uint)Remaining)
            throw new EndOfStreamException($"Length prefix of {length} bytes runs past the end of the data ({Remaining} left).");
        return (int)length;
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > _data.Length - _position)
            throw new EndOfStreamException($"Tried to read {count} bytes with {Remaining} left.");

        ReadOnlySpan<byte> span = _data.Slice(_position, count);
        _position += count;
        return span;
    }
}
