// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// The arithmetic coder the lossy form is written with. It narrows an interval a bit at a time
/// against a probability, doubling and taking another byte when precision runs out. A frame holds
/// several reading different parts of the file at once, hence an object rather than a borrowed span.
/// </summary>
internal sealed class Vp8BoolDecoder
{
    private readonly byte[] _data;
    private readonly int _end;
    private uint _range;
    private uint _value;
    private int _shifted;
    private int _at;

    public Vp8BoolDecoder(byte[] data, int offset, int length)
    {
        _data = data;
        _at = offset;
        _end = Math.Min(data.Length, offset + Math.Max(length, 0));
        _value = ((uint)Next() << 8) | Next();
        _range = 255;
        _shifted = 0;
    }

    /// <summary>Whether a read has been asked for past the end of the partition.</summary>
    public bool Ended { get; private set; }

    private byte Next()
    {
        if (_at < _end)
            return _data[_at++];

        Ended = true;
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(int probability)
    {
        uint split = 1 + (((_range - 1) * (uint)probability) >> 8);
        uint boundary = split << 8;
        int bit;

        if (_value >= boundary)
        {
            bit = 1;
            _range -= split;
            _value -= boundary;
        }
        else
        {
            bit = 0;
            _range = split;
        }

        // How many doublings the interval needs is its count of leading zeros, so it is one
        // step rather than a loop.
        int shift = BitOperations.LeadingZeroCount(_range) - 24;
        if (shift > 0)
        {
            _range <<= shift;
            _value <<= shift;
            _shifted += shift;

            while (_shifted >= 8)
            {
                _shifted -= 8;
                _value |= (uint)Next() << _shifted;
            }
        }

        return bit;
    }

    /// <summary>One bit with no expectation either way, which is how raw values are written.</summary>
    public int ReadFlag() => Read(128);

    /// <summary>An unsigned value, most significant bit first.</summary>
    public int ReadValue(int bits)
    {
        int value = 0;
        while (bits-- > 0)
            value = (value << 1) | Read(128);

        return value;
    }

    /// <summary>A magnitude followed by its sign, which is how the headers store deltas.</summary>
    public int ReadSignedValue(int bits)
    {
        int value = ReadValue(bits);
        return Read(128) != 0 ? -value : value;
    }

    /// <summary>Applies a sign bit to an already decoded magnitude.</summary>
    public int ApplySign(int value) => Read(128) != 0 ? -value : value;
}
