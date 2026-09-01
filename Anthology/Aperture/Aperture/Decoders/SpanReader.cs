// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders;

/// <summary>
/// A cursor over a byte span whose reads fail rather than throw. Header parsers walk attacker
/// controlled offsets constantly, so every accessor returns a success flag and leaves the
/// cursor untouched when there is not enough data left.
/// </summary>
internal ref struct SpanReader(ReadOnlySpan<byte> data, bool littleEndian = true)
{
    private readonly ReadOnlySpan<byte> _data = data;

    /// <summary>Byte offset of the cursor from the start of the span.</summary>
    public int Position { get; private set; }

    /// <summary>Whether multi-byte integers are read least significant byte first.</summary>
    public bool LittleEndian { get; set; } = littleEndian;

    /// <summary>Total length of the underlying span.</summary>
    public readonly int Length => _data.Length;

    /// <summary>Bytes between the cursor and the end.</summary>
    public readonly int Remaining => _data.Length - Position;

    /// <summary>Moves the cursor to an absolute offset, failing if it lands outside the span.</summary>
    public bool Seek(long offset)
    {
        if (offset < 0 || offset > _data.Length)
            return false;
        Position = (int)offset;
        return true;
    }

    /// <summary>Advances the cursor, failing if that would run past the end.</summary>
    public bool Skip(long count) => Seek(Position + count);

    /// <summary>Reads one byte.</summary>
    public bool TryReadByte(out byte value)
    {
        if (Remaining < 1)
        {
            value = 0;
            return false;
        }
        value = _data[Position++];
        return true;
    }

    /// <summary>Reads a 16 bit unsigned integer in the current endianness.</summary>
    public bool TryReadUInt16(out ushort value)
    {
        if (Remaining < 2)
        {
            value = 0;
            return false;
        }
        ReadOnlySpan<byte> slice = _data.Slice(Position, 2);
        value = LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(slice)
                             : BinaryPrimitives.ReadUInt16BigEndian(slice);
        Position += 2;
        return true;
    }

    /// <summary>Reads a 32 bit unsigned integer in the current endianness.</summary>
    public bool TryReadUInt32(out uint value)
    {
        if (Remaining < 4)
        {
            value = 0;
            return false;
        }
        ReadOnlySpan<byte> slice = _data.Slice(Position, 4);
        value = LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(slice)
                             : BinaryPrimitives.ReadUInt32BigEndian(slice);
        Position += 4;
        return true;
    }

    /// <summary>Reads a 32 bit IEEE 754 float in the current endianness.</summary>
    public bool TryReadSingle(out float value)
    {
        if (!TryReadUInt32(out uint bits))
        {
            value = 0;
            return false;
        }

        value = BitConverter.UInt32BitsToSingle(bits);
        return true;
    }

    /// <summary>Reads a 32 bit signed integer in the current endianness.</summary>
    public bool TryReadInt32(out int value)
    {
        bool ok = TryReadUInt32(out uint raw);
        value = unchecked((int)raw);
        return ok;
    }

    /// <summary>Takes a slice of <paramref name="count"/> bytes and advances past it.</summary>
    public bool TryReadBytes(int count, out ReadOnlySpan<byte> value)
    {
        if (count < 0 || Remaining < count)
        {
            value = default;
            return false;
        }
        value = _data.Slice(Position, count);
        Position += count;
        return true;
    }

    /// <summary>Peeks <paramref name="count"/> bytes at an absolute offset without moving the cursor.</summary>
    public readonly bool TryPeekAt(long offset, int count, out ReadOnlySpan<byte> value)
    {
        if (offset < 0 || count < 0 || offset + count > _data.Length)
        {
            value = default;
            return false;
        }
        value = _data.Slice((int)offset, count);
        return true;
    }

    /// <summary>Whether the bytes at the cursor equal <paramref name="expected"/>, without advancing.</summary>
    public readonly bool Matches(ReadOnlySpan<byte> expected) =>
        Remaining >= expected.Length && _data.Slice(Position, expected.Length).SequenceEqual(expected);

    /// <summary>Reads a NUL terminated string of at most <paramref name="maxLength"/> bytes.</summary>
    public bool TryReadNullTerminated(int maxLength, out string value)
    {
        value = string.Empty;
        int limit = Math.Min(Remaining, maxLength);
        for (int i = 0; i < limit; i++)
        {
            if (_data[Position + i] != 0)
                continue;
            value = System.Text.Encoding.ASCII.GetString(_data.Slice(Position, i));
            Position += i + 1;
            return true;
        }
        return false;
    }
}
