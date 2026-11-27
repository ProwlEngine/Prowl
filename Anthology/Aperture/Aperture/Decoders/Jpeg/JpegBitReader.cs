// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>
/// Reads the entropy coded segment a bit at a time, undoing the byte stuffing that keeps 0xFF
/// out of the data. Bits are held left aligned in a sixty four bit window, which lets a code and
/// its payload be peeked together and consumed in one step.
/// </summary>
internal ref struct JpegBitReader
{
    /// <summary>
    /// Bits at or above this count satisfy any single read, so the window is topped up only when
    /// it drops below. A code is at most sixteen bits and the magnitude after it at most fifteen,
    /// so thirty two always covers one coefficient, and the window holding sixty four means one
    /// top up serves several of them.
    /// </summary>
    private const int Refilled = 32;

    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private ulong _bits;
    private int _count;
    private byte _marker;
    private bool _sawEnd;

    public JpegBitReader(ReadOnlySpan<byte> data, int start)
    {
        _data = data;
        _position = start;
    }

    /// <summary>Offset of the 0xFF that ended the segment, or of the first unread byte.</summary>
    public readonly int Position => _position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Fill()
    {
        if (_count >= Refilled)
            return;

        // Eight bytes with no 0xFF among them need no unstuffing, so they load in one read.
        if (!_sawEnd && _position + 8 <= _data.Length)
        {
            ulong chunk = BinaryPrimitives.ReadUInt64BigEndian(_data[_position..]);
            if (!ContainsFF(chunk))
            {
                int bytes = (64 - _count) >> 3;
                _bits |= (chunk & (~0UL << (64 - (bytes << 3)))) >> _count;
                _count += bytes << 3;
                _position += bytes;
                return;
            }
        }

        FillSlowly();
    }

    private void FillSlowly()
    {
        while (_count < Refilled)
        {
            _bits |= (ulong)NextByte() << (56 - _count);
            _count += 8;
        }
    }

    /// <summary>Detects an 0xFF anywhere in the eight bytes, using the usual zero byte trick inverted.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsFF(ulong value)
    {
        ulong inverted = ~value;
        return ((inverted - 0x0101010101010101UL) & ~inverted & 0x8080808080808080UL) != 0;
    }

    /// <summary>
    /// One byte of entropy data. 0xFF is written as 0xFF 0x00 inside a scan, and any other
    /// value after it is a marker, which ends the segment. Past that point the reader hands out
    /// zeroes so a truncated file still produces the part of the image that did arrive.
    /// </summary>
    private byte NextByte()
    {
        if (_sawEnd)
            return 0;

        int at = _position;
        if (at >= _data.Length)
        {
            _sawEnd = true;
            return 0;
        }

        byte value = _data[at];
        if (value != 0xFF)
        {
            _position = at + 1;
            return value;
        }

        int probe = at;
        while (probe < _data.Length && _data[probe] == 0xFF)
            probe++;

        if (probe >= _data.Length)
        {
            _position = _data.Length;
            _sawEnd = true;
            return 0;
        }

        byte next = _data[probe];
        if (next == 0x00)
        {
            _position = probe + 1;
            return 0xFF;
        }

        _marker = next;
        _position = probe - 1;
        _sawEnd = true;
        return 0;
    }

    /// <summary>The next sixteen bits without consuming them, for a Huffman lookup.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int PeekWindow() => (int)(_bits >> 48);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int bits)
    {
        _bits <<= bits;
        _count -= bits;
    }

    /// <summary>
    /// Steps over a code and reads the magnitude that follows it, both in one move. A coefficient
    /// is always a code and then its bits, so consuming them together halves the work the window
    /// does per coefficient.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int SkipAndExtend(int length, int size)
    {
        int value = (int)((_bits << length) >> (64 - size));
        int used = length + size;
        _bits <<= used;
        _count -= used;

        int mask = (1 << size) - 1;
        return value - (mask & (((value >> (size - 1)) & 1) - 1));
    }

    /// <summary>Reads a magnitude of the given size and sign extends it the way the format defines.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReceiveExtend(int size)
    {
        int value = (int)(_bits >> (64 - size));
        _bits <<= size;
        _count -= size;

        // A clear top bit stands for a negative magnitude, folded into arithmetic because the
        // sign is a branch nothing can predict.
        int mask = (1 << size) - 1;
        return value - (mask & (((value >> (size - 1)) & 1) - 1));
    }

    /// <summary>Reads a magnitude without sign extension, which is what a refinement pass needs.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Receive(int size)
    {
        int value = (int)(_bits >> (64 - size));
        _bits <<= size;
        _count -= size;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadBit()
    {
        Fill();
        int bit = (int)(_bits >> 63);
        _bits <<= 1;
        _count--;
        return bit;
    }

    /// <summary>
    /// Steps over a restart marker and starts a fresh bit stream after it. Anything between the
    /// end of the coded data and the marker is discarded, because encoders pad there and a
    /// damaged file can leave worse. Returns false if what comes next is not a restart, which
    /// means the scan is genuinely over.
    /// </summary>
    public bool TryRestart()
    {
        _bits = 0;
        _count = 0;

        int at = _position;
        while (at < _data.Length)
        {
            if (_data[at] != 0xFF)
            {
                at++;
                continue;
            }

            int probe = at;
            while (probe < _data.Length && _data[probe] == 0xFF)
                probe++;

            if (probe >= _data.Length)
                break;

            byte next = _data[probe];
            if (next == 0x00)
            {
                at = probe + 1;
                continue;
            }

            if (next is >= 0xD0 and <= 0xD7)
            {
                _position = probe + 1;
                _marker = 0;
                _sawEnd = false;
                return true;
            }

            _marker = next;
            _position = probe - 1;
            _sawEnd = true;
            return false;
        }

        _position = _data.Length;
        _sawEnd = true;
        return false;
    }
}
