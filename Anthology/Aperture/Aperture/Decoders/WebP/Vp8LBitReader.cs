// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// The bit reader the lossless form uses, which fills from the least significant end. Running off
/// the end raises a flag rather than failing, since a stream may legitimately stop on a byte
/// boundary partway through the last symbol. Bits can be looked at before they are taken, which
/// lets a prefix code resolve in one table read, and only the taking can run off the end.
/// </summary>
internal ref struct Vp8LBitReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private ulong _window;
    private int _held;

    /// <summary>Bits of the window that came from the stream rather than from padding it.</summary>
    private int _real;
    private int _at;

    /// <summary>Whether a read has been asked for past the end of the stream.</summary>
    public bool Ended { get; private set; }

    public int Position => _at - (_held / 8);

    /// <summary>Brings the window up to at least the given number of bits, padding with zeros.</summary>
    private void Fill(int count)
    {
        while (_held < count)
        {
            if (_at >= _data.Length)
            {
                _held += 8;
                continue;
            }

            _window |= (ulong)_data[_at++] << _held;
            _held += 8;
            _real += 8;
        }
    }

    private void Consume(int count)
    {
        _window >>= count;
        _held -= count;

        if (count > _real)
        {
            Ended = true;
            _real = 0;
            return;
        }

        _real -= count;
    }

    public uint Read(int count)
    {
        if (count == 0)
            return 0;

        Fill(count);
        uint value = (uint)(_window & ((1UL << count) - 1));
        Consume(count);
        return value;
    }

    /// <summary>The next bits without taking them, least significant first.</summary>
    public uint Peek(int count)
    {
        Fill(count);
        return (uint)(_window & ((1UL << count) - 1));
    }

    /// <summary>Takes bits that have already been looked at.</summary>
    public void Skip(int count) => Consume(count);

    /// <summary>Reads one bit, which is what a prefix code too long for the table is walked with.</summary>
    public uint ReadBit()
    {
        Fill(1);
        uint value = (uint)(_window & 1);
        Consume(1);
        return value;
    }
}
