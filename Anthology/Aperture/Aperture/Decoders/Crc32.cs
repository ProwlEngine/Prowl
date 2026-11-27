// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders;

/// <summary>
/// The CRC-32 used by PNG chunk checksums, polynomial 0xEDB88320, reflected. Sliced by eight, so
/// eight bytes are looked up at once rather than carrying a dependency from each byte to the next.
/// </summary>
internal static class Crc32
{
    private const int Slices = 8;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        uint[] table = new uint[Slices * 256];

        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

            table[i] = c;
        }

        // Each further table is the one before it advanced by another byte of zeros, which is
        // what lets a byte be looked up before the bytes ahead of it have been folded in.
        for (int slice = 1; slice < Slices; slice++)
        {
            for (int i = 0; i < 256; i++)
            {
                uint previous = table[((slice - 1) << 8) + i];
                table[(slice << 8) + i] = (previous >> 8) ^ table[previous & 0xFF];
            }
        }

        return table;
    }

    /// <summary>Computes the checksum of a span.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint[] table = Table;
        uint crc = 0xFFFFFFFFu;
        int at = 0;

        for (; at + 8 <= data.Length; at += 8)
        {
            uint low = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) ^ crc;
            uint high = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);

            crc = table[0x700 + (low & 0xFF)] ^
                  table[0x600 + ((low >> 8) & 0xFF)] ^
                  table[0x500 + ((low >> 16) & 0xFF)] ^
                  table[0x400 + (low >> 24)] ^
                  table[0x300 + (high & 0xFF)] ^
                  table[0x200 + ((high >> 8) & 0xFF)] ^
                  table[0x100 + ((high >> 16) & 0xFF)] ^
                  table[high >> 24];
        }

        for (; at < data.Length; at++)
            crc = table[(crc ^ data[at]) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFFu;
    }
}
