// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Prowl.Aperture.Decoders.Png;

/// <summary>
/// Undoes the per-scanline prediction filters, which for a photograph costs more than the inflate.
/// Four of the five read the pixel to the left, so the vector work is the channels within one
/// pixel with the previous one carried in a register; Up has no such dependency and goes a vector
/// of bytes at a time. Reading and writing separately lets a row unfilter straight into the frame.
/// </summary>
internal static class PngFilter
{
    /// <summary>Filter type codes, one leading byte per scanline.</summary>
    public const byte None = 0;
    public const byte Sub = 1;
    public const byte Up = 2;
    public const byte Average = 3;
    public const byte Paeth = 4;

    /// <summary>Reverses the filter on one scanline in place.</summary>
    public static bool Apply(byte filter, Span<byte> current, ReadOnlySpan<byte> previous, int bytesPerPixel) =>
        filter == None || Apply(filter, current, current, previous, bytesPerPixel);

    /// <summary>
    /// Reverses the filter on one scanline, reading it from <paramref name="source"/> and writing
    /// it to <paramref name="current"/>.
    /// </summary>
    public static bool Apply(byte filter, ReadOnlySpan<byte> source, Span<byte> current,
                             ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        switch (filter)
        {
            case None:
                source.CopyTo(current);
                return true;
            case Sub:
                ApplySub(source, current, bytesPerPixel);
                return true;
            case Up:
                ApplyUp(source, current, previous);
                return true;
            case Average:
                ApplyAverage(source, current, previous, bytesPerPixel);
                return true;
            case Paeth:
                ApplyPaeth(source, current, previous, bytesPerPixel);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether a pixel stride is one the vector paths handle.</summary>
    private static bool IsVectorStride(int bytesPerPixel) =>
        Vector128.IsHardwareAccelerated && bytesPerPixel is 3 or 4;

    // ---- Sub -------------------------------------------------------------------------

    /// <summary>Each byte gains the byte one pixel to its left.</summary>
    private static void ApplySub(ReadOnlySpan<byte> source, Span<byte> current, int bytesPerPixel)
    {
        int length = current.Length;

        // The first pixel has nothing to its left, so it passes through as it stands.
        source[..Math.Min(bytesPerPixel, length)].CopyTo(current);

        if (IsVectorStride(bytesPerPixel))
        {
            int limit = length - 4;
            Vector128<short> left = Load(current, 0, bytesPerPixel, limit);
            int i = bytesPerPixel;

            for (; i <= limit; i += bytesPerPixel)
            {
                Vector128<short> value = Load(source, i, bytesPerPixel, limit) + left;
                left = value & Vector128.Create((short)0xFF);
                Store(current, i, bytesPerPixel, left);
            }

            ScalarSub(source, current, bytesPerPixel, i);
            return;
        }

        ScalarSub(source, current, bytesPerPixel, bytesPerPixel);
    }

    private static void ScalarSub(ReadOnlySpan<byte> source, Span<byte> current, int bytesPerPixel, int start)
    {
        ref byte from = ref MemoryMarshal.GetReference(source);
        ref byte row = ref MemoryMarshal.GetReference(current);

        for (int i = start; i < current.Length; i++)
            Unsafe.Add(ref row, i) = (byte)(Unsafe.Add(ref from, i) + Unsafe.Add(ref row, i - bytesPerPixel));
    }

    // ---- Up --------------------------------------------------------------------------

    /// <summary>
    /// Each byte gains the byte above it. No dependency along the row, so whole vectors are
    /// added at a time.
    /// </summary>
    private static void ApplyUp(ReadOnlySpan<byte> source, Span<byte> current, ReadOnlySpan<byte> previous)
    {
        if (previous.IsEmpty)
        {
            source.CopyTo(current);
            return;
        }

        int i = 0;
        int length = current.Length;

        if (Vector.IsHardwareAccelerated && length >= Vector<byte>.Count)
        {
            int limit = length - Vector<byte>.Count;
            for (; i <= limit; i += Vector<byte>.Count)
            {
                Vector<byte> sum = new Vector<byte>(source.Slice(i)) + new Vector<byte>(previous.Slice(i));
                sum.CopyTo(current.Slice(i));
            }
        }

        ref byte from = ref MemoryMarshal.GetReference(source);
        ref byte row = ref MemoryMarshal.GetReference(current);
        ref byte above = ref MemoryMarshal.GetReference(previous);
        for (; i < length; i++)
            Unsafe.Add(ref row, i) = (byte)(Unsafe.Add(ref from, i) + Unsafe.Add(ref above, i));
    }

    // ---- Average ---------------------------------------------------------------------

    /// <summary>Each byte gains the mean of the byte to its left and the byte above it.</summary>
    private static void ApplyAverage(ReadOnlySpan<byte> source, Span<byte> current,
                                     ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        int length = current.Length;

        if (previous.IsEmpty)
        {
            source[..Math.Min(bytesPerPixel, length)].CopyTo(current);

            ref byte start = ref MemoryMarshal.GetReference(source);
            ref byte only = ref MemoryMarshal.GetReference(current);
            for (int i = bytesPerPixel; i < length; i++)
            {
                Unsafe.Add(ref only, i) =
                    (byte)(Unsafe.Add(ref start, i) + (Unsafe.Add(ref only, i - bytesPerPixel) >> 1));
            }

            return;
        }

        if (IsVectorStride(bytesPerPixel))
        {
            Vector128<short> mask = Vector128.Create((short)0xFF);
            Vector128<short> left = Vector128<short>.Zero;
            int limit = length - 4;
            int i = 0;

            for (; i <= limit; i += bytesPerPixel)
            {
                Vector128<short> above = Load(previous, i, bytesPerPixel, limit);
                Vector128<short> value = Load(source, i, bytesPerPixel, limit) + ((left + above) >> 1);
                left = value & mask;
                Store(current, i, bytesPerPixel, left);
            }

            ScalarAverage(source, current, previous, bytesPerPixel, i);
            return;
        }

        ScalarAverage(source, current, previous, bytesPerPixel, 0);
    }

    private static void ScalarAverage(ReadOnlySpan<byte> source, Span<byte> current,
                                      ReadOnlySpan<byte> previous, int bytesPerPixel, int start)
    {
        ref byte from = ref MemoryMarshal.GetReference(source);
        ref byte row = ref MemoryMarshal.GetReference(current);
        ref byte above = ref MemoryMarshal.GetReference(previous);

        for (int i = start; i < current.Length; i++)
        {
            int left = i >= bytesPerPixel ? Unsafe.Add(ref row, i - bytesPerPixel) : 0;
            Unsafe.Add(ref row, i) =
                (byte)(Unsafe.Add(ref from, i) + ((left + Unsafe.Add(ref above, i)) >> 1));
        }
    }

    // ---- Paeth -----------------------------------------------------------------------

    /// <summary>
    /// Each byte gains whichever of the left, above and above-left neighbours the Paeth
    /// predictor picks.
    /// </summary>
    private static void ApplyPaeth(ReadOnlySpan<byte> source, Span<byte> current,
                                   ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        int length = current.Length;

        if (previous.IsEmpty)
        {
            // With no row above, the predictor always resolves to the left neighbour.
            source[..Math.Min(bytesPerPixel, length)].CopyTo(current);

            ref byte start = ref MemoryMarshal.GetReference(source);
            ref byte only = ref MemoryMarshal.GetReference(current);
            for (int i = bytesPerPixel; i < length; i++)
            {
                Unsafe.Add(ref only, i) =
                    (byte)(Unsafe.Add(ref start, i) + Unsafe.Add(ref only, i - bytesPerPixel));
            }

            return;
        }

        if (IsVectorStride(bytesPerPixel))
        {
            Vector128<short> mask = Vector128.Create((short)0xFF);
            Vector128<short> left = Vector128<short>.Zero;
            Vector128<short> upperLeft = Vector128<short>.Zero;
            int limit = length - 4;
            int i = 0;

            for (; i <= limit; i += bytesPerPixel)
            {
                Vector128<short> above = Load(previous, i, bytesPerPixel, limit);

                // The estimate is left + above - upperLeft, so the three distances reduce to
                // differences between the neighbours themselves and no estimate is formed.
                Vector128<short> distanceLeft = Vector128.Abs(above - upperLeft);
                Vector128<short> distanceAbove = Vector128.Abs(left - upperLeft);
                Vector128<short> distanceUpperLeft = Vector128.Abs(left + above - upperLeft - upperLeft);

                Vector128<short> notLeft = Vector128.GreaterThan(distanceLeft, distanceAbove)
                                         | Vector128.GreaterThan(distanceLeft, distanceUpperLeft);
                Vector128<short> notAbove = Vector128.GreaterThan(distanceAbove, distanceUpperLeft);

                Vector128<short> fallback = Vector128.ConditionalSelect(notAbove, upperLeft, above);
                Vector128<short> predicted = Vector128.ConditionalSelect(notLeft, fallback, left);

                Vector128<short> value = Load(source, i, bytesPerPixel, limit) + predicted;
                left = value & mask;
                upperLeft = above;
                Store(current, i, bytesPerPixel, left);
            }

            ScalarPaeth(source, current, previous, bytesPerPixel, i);
            return;
        }

        ScalarPaeth(source, current, previous, bytesPerPixel, 0);
    }

    private static void ScalarPaeth(ReadOnlySpan<byte> source, Span<byte> current,
                                    ReadOnlySpan<byte> previous, int bytesPerPixel, int start)
    {
        ref byte from = ref MemoryMarshal.GetReference(source);
        ref byte row = ref MemoryMarshal.GetReference(current);
        ref byte above = ref MemoryMarshal.GetReference(previous);

        for (int i = start; i < current.Length; i++)
        {
            if (i < bytesPerPixel)
            {
                Unsafe.Add(ref row, i) = (byte)(Unsafe.Add(ref from, i) + Unsafe.Add(ref above, i));
                continue;
            }

            byte left = Unsafe.Add(ref row, i - bytesPerPixel);
            byte up = Unsafe.Add(ref above, i);
            byte upperLeft = Unsafe.Add(ref above, i - bytesPerPixel);
            Unsafe.Add(ref row, i) = (byte)(Unsafe.Add(ref from, i) + Predict(left, up, upperLeft));
        }
    }

    /// <summary>
    /// Picks the neighbour closest to the linear estimate left + above - above-left. Written
    /// branch-light because it runs once per byte of a filtered image.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Predict(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;

        int distanceLeft = estimate - left;
        distanceLeft = (distanceLeft ^ (distanceLeft >> 31)) - (distanceLeft >> 31);

        int distanceAbove = estimate - above;
        distanceAbove = (distanceAbove ^ (distanceAbove >> 31)) - (distanceAbove >> 31);

        int distanceUpperLeft = estimate - upperLeft;
        distanceUpperLeft = (distanceUpperLeft ^ (distanceUpperLeft >> 31)) - (distanceUpperLeft >> 31);

        if (distanceLeft <= distanceAbove && distanceLeft <= distanceUpperLeft)
            return left;

        return distanceAbove <= distanceUpperLeft ? above : upperLeft;
    }

    // ---- pixel sized loads and stores ------------------------------------------------

    /// <summary>
    /// Loads one pixel's channels into the low lanes of a vector, zero extended to sixteen bits
    /// so the arithmetic above cannot overflow.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Load(ReadOnlySpan<byte> source, int offset, int bytesPerPixel, int limit)
    {
        // Reading four bytes is safe up to the limit even when a pixel is three, which is why
        // the loops stop there and leave the last pixel to the scalar tail.
        uint packed = offset <= limit
            ? Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetReference(source), offset))
            : Gather(source, offset, bytesPerPixel);

        return Vector128.WidenLower(Vector128.CreateScalar(packed).AsByte()).AsInt16();
    }

    private static uint Gather(ReadOnlySpan<byte> source, int offset, int bytesPerPixel)
    {
        uint packed = 0;
        for (int i = 0; i < bytesPerPixel && offset + i < source.Length; i++)
            packed |= (uint)source[offset + i] << (i * 8);
        return packed;
    }

    /// <summary>Writes the low lanes back as bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Span<byte> destination, int offset, int bytesPerPixel, Vector128<short> value)
    {
        Vector128<byte> narrowed = Vector128.Narrow(value.AsUInt16(), Vector128<ushort>.Zero);
        uint packed = narrowed.AsUInt32().GetElement(0);

        ref byte at = ref Unsafe.Add(ref MemoryMarshal.GetReference(destination), offset);
        if (bytesPerPixel == 4)
        {
            Unsafe.WriteUnaligned(ref at, packed);
            return;
        }

        at = (byte)packed;
        Unsafe.Add(ref at, 1) = (byte)(packed >> 8);
        Unsafe.Add(ref at, 2) = (byte)(packed >> 16);
    }
}
