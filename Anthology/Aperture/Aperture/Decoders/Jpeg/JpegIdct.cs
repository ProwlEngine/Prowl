// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>
/// The accurate integer inverse transform: a Loeffler, Ligtenberg and Moschytz factorisation in
/// fixed point at thirteen bit precision, over the columns and then the rows. The format
/// specifies this step to an accuracy bound rather than exactly, so two conforming decoders
/// differ by a step or so on individual samples.
/// </summary>
internal static class JpegIdct
{
    private const int ConstBits = 13;
    private const int PassBits = 2;

    private const int Fix0_298631336 = 2446;
    private const int Fix0_390180644 = 3196;
    private const int Fix0_541196100 = 4433;
    private const int Fix0_765366865 = 6270;
    private const int Fix0_899976223 = 7373;
    private const int Fix1_175875602 = 9633;
    private const int Fix1_501321110 = 12299;
    private const int Fix1_847759065 = 15137;
    private const int Fix1_961570560 = 16069;
    private const int Fix2_053119869 = 16819;
    private const int Fix2_562915447 = 20995;
    private const int Fix3_072711026 = 25172;

    /// <summary>
    /// Dequantises one block, transforms it and writes the eight by eight result of samples,
    /// level shifted back into the unsigned range.
    /// </summary>
    [SkipLocalsInit]
    public static void Transform(ReadOnlySpan<short> block, ReadOnlySpan<ushort> quantization,
                                 Span<byte> destination, int stride)
    {
        // A block carrying nothing but its average expands to a flat square, which is what most
        // of a photograph's sky or a flat background is made of.
        if (IsFlat(block))
        {
            ulong flat = Clamp(Descale(block[0] * quantization[0], 3)) * 0x0101010101010101UL;
            for (int row = 0; row < 8; row++)
                BinaryPrimitives.WriteUInt64LittleEndian(destination[(row * stride)..], flat);
            return;
        }

        if (Avx2.IsSupported)
        {
            TransformWide(block, quantization, destination, stride);
            return;
        }

        Span<int> workspace = stackalloc int[JpegBlock.Coefficients];
        Columns(block, quantization, workspace);
        Rows(workspace, destination, stride);
    }

    /// <summary>True when every coefficient but the average is zero.</summary>
    private static bool IsFlat(ReadOnlySpan<short> block)
    {
        if (!Vector256.IsHardwareAccelerated)
            return !block[1..].ContainsAnyExcept((short)0);

        ref short first = ref MemoryMarshal.GetReference(block);
        Vector256<short> average = Vector256.CreateScalar((short)-1);

        Vector256<short> any = Vector256.AndNot(Vector256.LoadUnsafe(ref first), average) |
                               Vector256.LoadUnsafe(ref first, 16) |
                               Vector256.LoadUnsafe(ref first, 32) |
                               Vector256.LoadUnsafe(ref first, 48);

        return any == Vector256<short>.Zero;
    }

    /// <summary>
    /// Both passes in vectors. A vector holds one row, so the column pass is the scalar arithmetic
    /// eight ways at once; the row pass wants the other axis in the lanes, hence the transposes.
    /// </summary>
    [SkipLocalsInit]
    private static void TransformWide(ReadOnlySpan<short> block, ReadOnlySpan<ushort> quantization,
                                      Span<byte> destination, int stride)
    {
        Lanes lanes;
        lanes.L0 = Dequantize(block, quantization, 0);
        lanes.L1 = Dequantize(block, quantization, 1);
        lanes.L2 = Dequantize(block, quantization, 2);
        lanes.L3 = Dequantize(block, quantization, 3);
        lanes.L4 = Dequantize(block, quantization, 4);
        lanes.L5 = Dequantize(block, quantization, 5);
        lanes.L6 = Dequantize(block, quantization, 6);
        lanes.L7 = Dequantize(block, quantization, 7);

        Butterfly(ref lanes, ConstBits - PassBits, 0);
        Transpose(ref lanes);

        const int Final = ConstBits + PassBits + 3;
        Butterfly(ref lanes, Final, 128 << Final);
        ClampAll(ref lanes);
        Transpose(ref lanes);

        Store(lanes.L0, destination);
        Store(lanes.L1, destination[stride..]);
        Store(lanes.L2, destination[(stride * 2)..]);
        Store(lanes.L3, destination[(stride * 3)..]);
        Store(lanes.L4, destination[(stride * 4)..]);
        Store(lanes.L5, destination[(stride * 5)..]);
        Store(lanes.L6, destination[(stride * 6)..]);
        Store(lanes.L7, destination[(stride * 7)..]);
    }

    /// <summary>The eight vectors a block is held in while it is being transformed.</summary>
    private struct Lanes
    {
        public Vector256<int> L0;
        public Vector256<int> L1;
        public Vector256<int> L2;
        public Vector256<int> L3;
        public Vector256<int> L4;
        public Vector256<int> L5;
        public Vector256<int> L6;
        public Vector256<int> L7;
    }

    /// <summary>Pins every sample to the range a byte can hold.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClampAll(ref Lanes lanes)
    {
        Vector256<int> ceiling = Vector256.Create(255);
        lanes.L0 = Vector256.Min(Vector256.Max(lanes.L0, Vector256<int>.Zero), ceiling);
        lanes.L1 = Vector256.Min(Vector256.Max(lanes.L1, Vector256<int>.Zero), ceiling);
        lanes.L2 = Vector256.Min(Vector256.Max(lanes.L2, Vector256<int>.Zero), ceiling);
        lanes.L3 = Vector256.Min(Vector256.Max(lanes.L3, Vector256<int>.Zero), ceiling);
        lanes.L4 = Vector256.Min(Vector256.Max(lanes.L4, Vector256<int>.Zero), ceiling);
        lanes.L5 = Vector256.Min(Vector256.Max(lanes.L5, Vector256<int>.Zero), ceiling);
        lanes.L6 = Vector256.Min(Vector256.Max(lanes.L6, Vector256<int>.Zero), ceiling);
        lanes.L7 = Vector256.Min(Vector256.Max(lanes.L7, Vector256<int>.Zero), ceiling);
    }

    /// <summary>Narrows one row of samples to bytes and writes it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector256<int> row, Span<byte> destination)
    {
        Vector128<ushort> words = Vector128.Narrow(row.GetLower(), row.GetUpper()).AsUInt16();
        BinaryPrimitives.WriteUInt64LittleEndian(destination,
            Vector128.Narrow(words, words).AsUInt64().ToScalar());
    }

    /// <summary>
    /// One pass of the transform over eight vectors, each holding one position along the axis
    /// being transformed. The bias is added before the result is scaled back down, which is where
    /// the level shift is folded in on the second pass rather than paid for per sample.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Butterfly(ref Lanes lanes, int shift, int bias)
    {
        Vector256<int> c0 = lanes.L0;
        Vector256<int> c1 = lanes.L1;
        Vector256<int> c2 = lanes.L2;
        Vector256<int> c3 = lanes.L3;
        Vector256<int> c4 = lanes.L4;
        Vector256<int> c5 = lanes.L5;
        Vector256<int> c6 = lanes.L6;
        Vector256<int> c7 = lanes.L7;

        Vector256<int> z1 = (c2 + c6) * Vector256.Create(Fix0_541196100);
        Vector256<int> tmp2 = z1 + (c6 * Vector256.Create(-Fix1_847759065));
        Vector256<int> tmp3 = z1 + (c2 * Vector256.Create(Fix0_765366865));

        Vector256<int> tmp0 = Vector256.ShiftLeft(c0 + c4, ConstBits);
        Vector256<int> tmp1 = Vector256.ShiftLeft(c0 - c4, ConstBits);

        Vector256<int> even0 = tmp0 + tmp3;
        Vector256<int> even3 = tmp0 - tmp3;
        Vector256<int> even1 = tmp1 + tmp2;
        Vector256<int> even2 = tmp1 - tmp2;

        Vector256<int> a1 = c7 + c1;
        Vector256<int> a2 = c5 + c3;
        Vector256<int> a3 = c7 + c3;
        Vector256<int> a4 = c5 + c1;
        Vector256<int> a5 = (a3 + a4) * Vector256.Create(Fix1_175875602);

        tmp0 = c7 * Vector256.Create(Fix0_298631336);
        tmp1 = c5 * Vector256.Create(Fix2_053119869);
        tmp2 = c3 * Vector256.Create(Fix3_072711026);
        tmp3 = c1 * Vector256.Create(Fix1_501321110);
        a1 *= Vector256.Create(-Fix0_899976223);
        a2 *= Vector256.Create(-Fix2_562915447);
        a3 = (a3 * Vector256.Create(-Fix1_961570560)) + a5;
        a4 = (a4 * Vector256.Create(-Fix0_390180644)) + a5;

        tmp0 += a1 + a3;
        tmp1 += a2 + a4;
        tmp2 += a2 + a3;
        tmp3 += a1 + a4;

        Vector256<int> rounding = Vector256.Create(bias + (1 << (shift - 1)));

        lanes.L0 = Vector256.ShiftRightArithmetic(even0 + tmp3 + rounding, shift);
        lanes.L7 = Vector256.ShiftRightArithmetic(even0 - tmp3 + rounding, shift);
        lanes.L1 = Vector256.ShiftRightArithmetic(even1 + tmp2 + rounding, shift);
        lanes.L6 = Vector256.ShiftRightArithmetic(even1 - tmp2 + rounding, shift);
        lanes.L2 = Vector256.ShiftRightArithmetic(even2 + tmp1 + rounding, shift);
        lanes.L5 = Vector256.ShiftRightArithmetic(even2 - tmp1 + rounding, shift);
        lanes.L3 = Vector256.ShiftRightArithmetic(even3 + tmp0 + rounding, shift);
        lanes.L4 = Vector256.ShiftRightArithmetic(even3 - tmp0 + rounding, shift);
    }

    /// <summary>Turns eight vectors of eight values on their side, so rows become columns.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(ref Lanes lanes)
    {
        Vector256<int> t0 = Avx2.UnpackLow(lanes.L0, lanes.L1);
        Vector256<int> t1 = Avx2.UnpackHigh(lanes.L0, lanes.L1);
        Vector256<int> t2 = Avx2.UnpackLow(lanes.L2, lanes.L3);
        Vector256<int> t3 = Avx2.UnpackHigh(lanes.L2, lanes.L3);
        Vector256<int> t4 = Avx2.UnpackLow(lanes.L4, lanes.L5);
        Vector256<int> t5 = Avx2.UnpackHigh(lanes.L4, lanes.L5);
        Vector256<int> t6 = Avx2.UnpackLow(lanes.L6, lanes.L7);
        Vector256<int> t7 = Avx2.UnpackHigh(lanes.L6, lanes.L7);

        Vector256<int> s0 = Avx2.UnpackLow(t0.AsInt64(), t2.AsInt64()).AsInt32();
        Vector256<int> s1 = Avx2.UnpackHigh(t0.AsInt64(), t2.AsInt64()).AsInt32();
        Vector256<int> s2 = Avx2.UnpackLow(t1.AsInt64(), t3.AsInt64()).AsInt32();
        Vector256<int> s3 = Avx2.UnpackHigh(t1.AsInt64(), t3.AsInt64()).AsInt32();
        Vector256<int> s4 = Avx2.UnpackLow(t4.AsInt64(), t6.AsInt64()).AsInt32();
        Vector256<int> s5 = Avx2.UnpackHigh(t4.AsInt64(), t6.AsInt64()).AsInt32();
        Vector256<int> s6 = Avx2.UnpackLow(t5.AsInt64(), t7.AsInt64()).AsInt32();
        Vector256<int> s7 = Avx2.UnpackHigh(t5.AsInt64(), t7.AsInt64()).AsInt32();

        lanes.L0 = Avx2.Permute2x128(s0, s4, 0x20);
        lanes.L1 = Avx2.Permute2x128(s1, s5, 0x20);
        lanes.L2 = Avx2.Permute2x128(s2, s6, 0x20);
        lanes.L3 = Avx2.Permute2x128(s3, s7, 0x20);
        lanes.L4 = Avx2.Permute2x128(s0, s4, 0x31);
        lanes.L5 = Avx2.Permute2x128(s1, s5, 0x31);
        lanes.L6 = Avx2.Permute2x128(s2, s6, 0x31);
        lanes.L7 = Avx2.Permute2x128(s3, s7, 0x31);
    }

    /// <summary>Reads one row of coefficients and multiplies it by the matching quantisation row.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> Dequantize(ReadOnlySpan<short> block, ReadOnlySpan<ushort> quantization, int row)
    {
        (Vector128<int> low, Vector128<int> high) = Vector128.Widen(Vector128.Create(block.Slice(row * 8, 8)));
        (Vector128<uint> scaleLow, Vector128<uint> scaleHigh) =
            Vector128.Widen(Vector128.Create(quantization.Slice(row * 8, 8)));

        return Vector256.Create(low * scaleLow.AsInt32(), high * scaleHigh.AsInt32());
    }

    /// <summary>The same first pass without vectors, for a machine that has none wide enough.</summary>
    private static void Columns(ReadOnlySpan<short> block, ReadOnlySpan<ushort> quantization, Span<int> workspace)
    {
        for (int column = 0; column < 8; column++)
        {
            // A column holding only its own leading term expands to a constant.
            // Cast away the sign only to test for zero, which the sign cannot change.
            if (((ushort)block[column + 8] | (ushort)block[column + 16] | (ushort)block[column + 24] |
                 (ushort)block[column + 32] | (ushort)block[column + 40] | (ushort)block[column + 48] |
                 (ushort)block[column + 56]) == 0)
            {
                int flat = (block[column] * quantization[column]) << PassBits;
                for (int row = 0; row < 8; row++)
                    workspace[column + (row * 8)] = flat;
                continue;
            }

            int z2 = block[column + 16] * quantization[column + 16];
            int z3 = block[column + 48] * quantization[column + 48];
            int z1 = (z2 + z3) * Fix0_541196100;
            int tmp2 = z1 + (z3 * -Fix1_847759065);
            int tmp3 = z1 + (z2 * Fix0_765366865);

            z2 = block[column] * quantization[column];
            z3 = block[column + 32] * quantization[column + 32];
            int tmp0 = (z2 + z3) << ConstBits;
            int tmp1 = (z2 - z3) << ConstBits;

            int even0 = tmp0 + tmp3;
            int even3 = tmp0 - tmp3;
            int even1 = tmp1 + tmp2;
            int even2 = tmp1 - tmp2;

            tmp0 = block[column + 56] * quantization[column + 56];
            tmp1 = block[column + 40] * quantization[column + 40];
            tmp2 = block[column + 24] * quantization[column + 24];
            tmp3 = block[column + 8] * quantization[column + 8];

            z1 = tmp0 + tmp3;
            z2 = tmp1 + tmp2;
            z3 = tmp0 + tmp2;
            int z4 = tmp1 + tmp3;
            int z5 = (z3 + z4) * Fix1_175875602;

            tmp0 *= Fix0_298631336;
            tmp1 *= Fix2_053119869;
            tmp2 *= Fix3_072711026;
            tmp3 *= Fix1_501321110;
            z1 *= -Fix0_899976223;
            z2 *= -Fix2_562915447;
            z3 = (z3 * -Fix1_961570560) + z5;
            z4 = (z4 * -Fix0_390180644) + z5;

            tmp0 += z1 + z3;
            tmp1 += z2 + z4;
            tmp2 += z2 + z3;
            tmp3 += z1 + z4;

            const int Shift = ConstBits - PassBits;
            workspace[column] = Descale(even0 + tmp3, Shift);
            workspace[column + 56] = Descale(even0 - tmp3, Shift);
            workspace[column + 8] = Descale(even1 + tmp2, Shift);
            workspace[column + 48] = Descale(even1 - tmp2, Shift);
            workspace[column + 16] = Descale(even2 + tmp1, Shift);
            workspace[column + 40] = Descale(even2 - tmp1, Shift);
            workspace[column + 24] = Descale(even3 + tmp0, Shift);
            workspace[column + 32] = Descale(even3 - tmp0, Shift);
        }
    }

    private static void Rows(ReadOnlySpan<int> workspace, Span<byte> destination, int stride)
    {
        for (int row = 0; row < 8; row++)
        {
            ReadOnlySpan<int> source = workspace.Slice(row * 8, 8);
            Span<byte> output = destination.Slice(row * stride, 8);

            if ((source[1] | source[2] | source[3] | source[4] |
                 source[5] | source[6] | source[7]) == 0)
            {
                byte flat = Clamp(Descale(source[0], PassBits + 3));
                output.Fill(flat);
                continue;
            }

            int z2 = source[2];
            int z3 = source[6];
            int z1 = (z2 + z3) * Fix0_541196100;
            int tmp2 = z1 + (z3 * -Fix1_847759065);
            int tmp3 = z1 + (z2 * Fix0_765366865);

            int tmp0 = (source[0] + source[4]) << ConstBits;
            int tmp1 = (source[0] - source[4]) << ConstBits;

            int even0 = tmp0 + tmp3;
            int even3 = tmp0 - tmp3;
            int even1 = tmp1 + tmp2;
            int even2 = tmp1 - tmp2;

            tmp0 = source[7];
            tmp1 = source[5];
            tmp2 = source[3];
            tmp3 = source[1];

            z1 = tmp0 + tmp3;
            z2 = tmp1 + tmp2;
            z3 = tmp0 + tmp2;
            int z4 = tmp1 + tmp3;
            int z5 = (z3 + z4) * Fix1_175875602;

            tmp0 *= Fix0_298631336;
            tmp1 *= Fix2_053119869;
            tmp2 *= Fix3_072711026;
            tmp3 *= Fix1_501321110;
            z1 *= -Fix0_899976223;
            z2 *= -Fix2_562915447;
            z3 = (z3 * -Fix1_961570560) + z5;
            z4 = (z4 * -Fix0_390180644) + z5;

            tmp0 += z1 + z3;
            tmp1 += z2 + z4;
            tmp2 += z2 + z3;
            tmp3 += z1 + z4;

            const int Final = ConstBits + PassBits + 3;
            output[0] = Clamp(Descale(even0 + tmp3, Final));
            output[7] = Clamp(Descale(even0 - tmp3, Final));
            output[1] = Clamp(Descale(even1 + tmp2, Final));
            output[6] = Clamp(Descale(even1 - tmp2, Final));
            output[2] = Clamp(Descale(even2 + tmp1, Final));
            output[5] = Clamp(Descale(even2 - tmp1, Final));
            output[3] = Clamp(Descale(even3 + tmp0, Final));
            output[4] = Clamp(Descale(even3 - tmp0, Final));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Descale(int value, int bits) => (value + (1 << (bits - 1))) >> bits;

    /// <summary>Undoes the level shift and pins the result to the sample range.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp(int value)
    {
        value += 128;
        return value switch
        {
            < 0 => 0,
            > 255 => 255,
            _ => (byte)value,
        };
    }
}
