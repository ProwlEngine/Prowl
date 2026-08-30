// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders;

/// <summary>
/// Reads unaligned bit fields, most significant bit first, which is the order the ISO base
/// media formats pack their headers in. Reads fail rather than throw when the data runs out,
/// same as <see cref="SpanReader"/>.
/// </summary>
internal ref struct BitReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _bitPosition;

    /// <summary>Bits left between the cursor and the end.</summary>
    public readonly int BitsRemaining => (_data.Length * 8) - _bitPosition;

    /// <summary>Reads an unsigned field of <paramref name="count"/> bits, at most 32.</summary>
    public bool TryReadBits(int count, out uint value)
    {
        value = 0;
        if (count is < 0 or > 32 || BitsRemaining < count)
            return false;

        for (int i = 0; i < count; i++)
        {
            int bit = (_data[_bitPosition >> 3] >> (7 - (_bitPosition & 7))) & 1;
            value = (value << 1) | (uint)bit;
            _bitPosition++;
        }

        return true;
    }
}
