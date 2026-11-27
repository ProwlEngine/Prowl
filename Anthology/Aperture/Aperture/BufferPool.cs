// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers;

namespace Prowl.Aperture;

/// <summary>
/// Where every working buffer in the library comes from. The runtime's shared pool stops pooling
/// above a megabyte, which is smaller than most buffers here, so these are sized for image work
/// instead. A buffer rented here must be returned here.
/// </summary>
internal static class BufferPool
{
    /// <summary>Pixel buffers, component planes and row scratch.</summary>
    public static readonly ArrayPool<byte> Bytes = ArrayPool<byte>.Create(1 << 26, 4);

    /// <summary>Transform coefficients, which run to two bytes a sample.</summary>
    public static readonly ArrayPool<short> Shorts = ArrayPool<short>.Create(1 << 25, 4);

    /// <summary>Row sized intermediates.</summary>
    public static readonly ArrayPool<int> Ints = ArrayPool<int>.Create(1 << 22, 4);

    /// <summary>Whole pictures held as packed colour, which is how the animated formats build one.</summary>
    public static readonly ArrayPool<uint> Uints = ArrayPool<uint>.Create(1 << 24, 4);

    /// <summary>Quantisation and palette tables, which are small and numerous.</summary>
    public static readonly ArrayPool<ushort> UShorts = ArrayPool<ushort>.Create(1 << 24, 4);

    /// <summary>Wide scratch, for the code tables an entropy coder builds a block at a time.</summary>
    public static readonly ArrayPool<long> Longs = ArrayPool<long>.Create(1 << 20, 4);
}
