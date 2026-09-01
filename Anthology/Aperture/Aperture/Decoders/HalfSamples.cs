// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Prowl.Aperture.Decoders;

/// <summary>
/// Widens sixteen bit floating point samples to the thirty two bit kind. The arithmetic moves the
/// sign, exponent and mantissa into the wider layout and then fixes the two exponents that are not
/// a straight move: the one meaning infinity or not a number, and the one meaning too small to
/// have a leading one.
/// </summary>
internal static class HalfSamples
{
    /// <summary>The value whose subtraction renormalises a sample stored without a leading one.</summary>
    private const uint Magic = 113u << 23;

    /// <summary>The exponent that means infinity or not a number, once shifted into place.</summary>
    private const uint Special = 0x7C00u << 13;

    /// <summary>Widens one sample, which is what the tail of a run and the general paths use.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToSingle(ushort half) => (float)BitConverter.UInt16BitsToHalf(half);

    /// <summary>Widens a run of samples from the little endian order these formats store.</summary>
    public static void ToSingle(ReadOnlySpan<byte> source, Span<float> destination, int count)
    {
        ref byte from = ref MemoryMarshal.GetReference(source);
        int x = 0;

        if (Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            Vector256<uint> mantissa = Vector256.Create(0x7FFFu);
            Vector256<uint> sign = Vector256.Create(0x8000u);
            Vector256<uint> special = Vector256.Create(Special);
            Vector256<uint> bias = Vector256.Create((127u - 15u) << 23);
            Vector256<uint> further = Vector256.Create((128u - 16u) << 23);
            Vector256<uint> leading = Vector256.Create(1u << 23);
            Vector256<float> magic = Vector256.Create(Magic).AsSingle();

            for (; x + 8 <= count; x += 8)
            {
                Vector128<ushort> packed = Vector128.LoadUnsafe(ref from, (nuint)(x * 2)).AsUInt16();
                (Vector128<uint> low, Vector128<uint> high) = Vector128.Widen(packed);
                Vector256<uint> half = Vector256.Create(low, high);

                // The exponent has to be read before the bias is added to it, or the test for
                // the two special ones is being made against the wrong number.
                Vector256<uint> shifted = (half & mantissa) << 13;
                Vector256<uint> exponent = shifted & special;
                Vector256<uint> moved = shifted + bias;

                Vector256<uint> small = ((moved + leading).AsSingle() - magic).AsUInt32();

                Vector256<uint> result = Vector256.ConditionalSelect(
                    Vector256.Equals(exponent, special),
                    moved + further,
                    Vector256.ConditionalSelect(Vector256.Equals(exponent, Vector256<uint>.Zero),
                                                small, moved));

                (result | ((half & sign) << 16)).AsSingle().CopyTo(destination[x..]);
            }
        }

        for (; x < count; x++)
            destination[x] = ToSingle(Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref from, x * 2)));
    }
}
