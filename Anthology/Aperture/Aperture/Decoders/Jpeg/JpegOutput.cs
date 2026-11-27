// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>How the stored components relate to the colours they describe.</summary>
internal enum JpegColorTransform
{
    /// <summary>Components are already the output channels, in order.</summary>
    None,
    YCbCr,

    /// <summary>Ink coverage, stored the plain way round.</summary>
    Cmyk,

    /// <summary>Ink coverage stored inverted, which is what the marker that declares it implies.</summary>
    CmykInverted,

    /// <summary>Inverted ink with the three colourants carried as luminance and chroma.</summary>
    YCck,
}

/// <summary>
/// Brings the decoded component planes back up to the frame's own resolution and turns them into
/// pixels. Chroma is usually stored at half resolution in one or both directions, so the missing
/// samples are interpolated rather than repeated, since a repeat leaves visible stair stepping
/// along every coloured edge.
/// </summary>
internal static class JpegOutput
{
    private static readonly int[] CrToRed = new int[256];
    private static readonly int[] CbToBlue = new int[256];
    private static readonly int[] CrToGreen = new int[256];
    private static readonly int[] CbToGreen = new int[256];

    static JpegOutput()
    {
        // The colour transform's weights, scaled to sixteen bit fixed point and built once.
        const int Scale = 16;
        const int Half = 1 << (Scale - 1);

        for (int i = 0; i < 256; i++)
        {
            int x = i - 128;
            CrToRed[i] = ((91881 * x) + Half) >> Scale;
            CbToBlue[i] = ((116130 * x) + Half) >> Scale;
            CrToGreen[i] = -46802 * x;
            CbToGreen[i] = (-22554 * x) + Half;
        }
    }

    public static void Write(JpegFrame frame, JpegColorTransform transform, int channels,
                             Span<byte> destination, int stride, bool flip)
    {
        int width = frame.Width;
        int height = frame.Height;
        JpegComponent[] components = frame.Components;

        // One expanded row per component, plus slack so an odd width can round up.
        int rowLength = width + 16;
        byte[] scratch = BufferPool.Bytes.Rent(rowLength * components.Length);

        // The vertical blend needs somewhere to land, and it is the same size every row.
        int blendLength = 1;
        foreach (JpegComponent component in components)
            blendLength = Math.Max(blendLength, component.SampledWidth + 8);

        ushort[] blend = BufferPool.UShorts.Rent(blendLength);

        // A full resolution component is read where it lies; only a subsampled one goes
        // through the scratch buffer.
        byte[][] sources = new byte[components.Length][];
        Span<int> pitches = stackalloc int[components.Length];

        for (int i = 0; i < components.Length; i++)
        {
            JpegComponent component = components[i];
            bool full = component.HorizontalFactor == frame.MaxHorizontalFactor &&
                        component.VerticalFactor == frame.MaxVerticalFactor;

            sources[i] = full ? component.Plane! : scratch;
            pitches[i] = full ? component.PlaneStride : 0;
        }

        Span<int> offsets = stackalloc int[components.Length];

        try
        {
            for (int y = 0; y < height; y++)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (pitches[i] != 0)
                    {
                        offsets[i] = Math.Min(y, components[i].SampledHeight - 1) * pitches[i];
                        continue;
                    }

                    offsets[i] = i * rowLength;
                    UpsampleRow(frame, components[i], y, scratch.AsSpan(offsets[i], rowLength), blend);
                }

                int target = flip ? height - 1 - y : y;
                Span<byte> row = destination.Slice(target * stride, width * channels);

                switch (components.Length)
                {
                    case 1:
                        WriteLuma(sources[0].AsSpan(offsets[0], width), row, channels);
                        break;

                    case 3 when transform == JpegColorTransform.YCbCr:
                        YCbCrToRgb(sources[0].AsSpan(offsets[0], width),
                                   sources[1].AsSpan(offsets[1], width),
                                   sources[2].AsSpan(offsets[2], width), row, channels);
                        break;

                    case 4:
                        CmykToRgb(sources, offsets, width, row, channels, transform);
                        break;

                    default:
                        Interleave(sources, offsets, width, row, channels);
                        break;
                }
            }
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
            BufferPool.UShorts.Return(blend);
        }
    }

    /// <summary>
    /// A single component image has no colour to reconstruct, so it either goes out as it is or
    /// gets copied across the three colour channels.
    /// </summary>
    private static void WriteLuma(ReadOnlySpan<byte> luma, Span<byte> destination, int channels)
    {
        if (channels == 1)
        {
            luma.CopyTo(destination);
            return;
        }

        if (channels == 4 && BitConverter.IsLittleEndian)
        {
            PixelConverter.GrayToRgba(luma, destination, luma.Length);
            return;
        }

        int at = 0;
        for (int x = 0; x < luma.Length; x++)
        {
            byte grey = luma[x];
            destination[at] = grey;
            destination[at + 1] = grey;
            destination[at + 2] = grey;
            if (channels == 4)
                destination[at + 3] = 255;

            at += channels;
        }
    }

    /// <summary>
    /// Expands one component's row to the frame width. The two halving ratios get a triangle
    /// filter; anything else repeats samples, which is all the format's rarer ratios warrant.
    /// </summary>
    private static void UpsampleRow(JpegFrame frame, JpegComponent component, int y,
                                    Span<byte> destination, ushort[] blend)
    {
        byte[] plane = component.Plane!;
        int stride = component.PlaneStride;
        int sampledWidth = component.SampledWidth;
        int sampledHeight = component.SampledHeight;
        int width = frame.Width;

        int horizontal = frame.MaxHorizontalFactor / component.HorizontalFactor;
        int vertical = frame.MaxVerticalFactor / component.VerticalFactor;

        bool exact = component.HorizontalFactor * horizontal == frame.MaxHorizontalFactor &&
                     component.VerticalFactor * vertical == frame.MaxVerticalFactor;

        if (exact && horizontal == 1 && vertical == 1)
        {
            int row = Math.Min(y, sampledHeight - 1);
            ReadOnlySpan<byte> source = plane.AsSpan(row * stride, sampledWidth);
            source[..Math.Min(width, sampledWidth)].CopyTo(destination);

            // A frame whose width is not a multiple of the block size leaves a few columns over.
            for (int x = sampledWidth; x < width; x++)
                destination[x] = source[sampledWidth - 1];
            return;
        }

        if (exact && horizontal == 2 && vertical == 1)
        {
            int row = Math.Min(y, sampledHeight - 1);
            TriangleRow(plane.AsSpan(row * stride, sampledWidth), destination, sampledWidth, width);
            return;
        }

        if (exact && horizontal == 2 && vertical == 2)
        {
            // Blend three to one down, then filter across, which is symmetric in both axes.
            int row = Math.Min(y >> 1, sampledHeight - 1);
            int neighbour = (y & 1) == 0 ? row - 1 : row + 1;
            neighbour = Math.Clamp(neighbour, 0, sampledHeight - 1);

            Span<ushort> blended = blend.AsSpan(0, sampledWidth);

            ReadOnlySpan<byte> near = plane.AsSpan(row * stride, sampledWidth);
            ReadOnlySpan<byte> far = plane.AsSpan(neighbour * stride, sampledWidth);
            Blend(near, far, blended);

            TriangleRow(blended, destination, sampledWidth, width);
            return;
        }

        Replicate(frame, component, y, destination, width);
    }

    /// <summary>
    /// Doubles a row by weighting each output three parts nearer sample to one part further,
    /// holding the outermost samples fixed so the edges do not drift.
    /// </summary>
    private static void TriangleRow(ReadOnlySpan<byte> source, Span<byte> destination, int sampledWidth, int width)
    {
        if (sampledWidth == 1)
        {
            destination[..width].Fill(source[0]);
            return;
        }

        int last = Math.Min(width, sampledWidth * 2);
        Span<byte> output = destination[..last];

        output[0] = source[0];
        if (last > 1)
            output[1] = (byte)(((source[0] * 3) + source[1] + 2) >> 2);

        int i = 1;
        if (Vector128.IsHardwareAccelerated)
        {
            for (; i + 9 <= sampledWidth && (i * 2) + 16 <= last; i += 8)
            {
                Pair(Spread(source, i - 1), Spread(source, i), Spread(source, i + 1), 2, 1, 2)
                    .CopyTo(output[(i * 2)..]);
            }
        }

        for (; i < sampledWidth - 1; i++)
        {
            int centre = source[i] * 3;
            int at = i * 2;
            if (at >= last)
                break;

            output[at] = (byte)((centre + source[i - 1] + 1) >> 2);
            if (at + 1 < last)
                output[at + 1] = (byte)((centre + source[i + 1] + 2) >> 2);
        }

        int tail = (sampledWidth - 1) * 2;
        if (tail < last)
            output[tail] = (byte)(((source[sampledWidth - 1] * 3) + source[sampledWidth - 2] + 1) >> 2);
        if (tail + 1 < last)
            output[tail + 1] = source[sampledWidth - 1];

        for (int x = last; x < width; x++)
            destination[x] = destination[last - 1];
    }

    /// <summary>The vertically blended variant, where each sample already carries a weight of four.</summary>
    private static void TriangleRow(ReadOnlySpan<ushort> source, Span<byte> destination, int sampledWidth, int width)
    {
        if (sampledWidth == 1)
        {
            destination[..width].Fill((byte)(((source[0] * 4) + 8) >> 4));
            return;
        }

        int last = Math.Min(width, sampledWidth * 2);
        Span<byte> output = destination[..last];

        output[0] = (byte)(((source[0] * 4) + 8) >> 4);
        if (last > 1)
            output[1] = (byte)(((source[0] * 3) + source[1] + 7) >> 4);

        int i = 1;
        if (Vector128.IsHardwareAccelerated)
        {
            for (; i + 9 <= sampledWidth && (i * 2) + 16 <= last; i += 8)
            {
                Pair(Vector128.Create(source.Slice(i - 1, 8)), Vector128.Create(source.Slice(i, 8)),
                     Vector128.Create(source.Slice(i + 1, 8)), 4, 8, 7)
                    .CopyTo(output[(i * 2)..]);
            }
        }

        for (; i < sampledWidth - 1; i++)
        {
            int centre = source[i] * 3;
            int at = i * 2;
            if (at >= last)
                break;

            output[at] = (byte)((centre + source[i - 1] + 8) >> 4);
            if (at + 1 < last)
                output[at + 1] = (byte)((centre + source[i + 1] + 7) >> 4);
        }

        int tail = (sampledWidth - 1) * 2;
        if (tail < last)
            output[tail] = (byte)(((source[sampledWidth - 1] * 3) + source[sampledWidth - 2] + 8) >> 4);
        if (tail + 1 < last)
            output[tail + 1] = (byte)(((source[sampledWidth - 1] * 4) + 7) >> 4);

        for (int x = last; x < width; x++)
            destination[x] = destination[last - 1];
    }

    /// <summary>Where each of the sixteen filtered bytes goes once the two halves are packed.</summary>
    private static ReadOnlySpan<byte> PairOrder =>
        [0, 8, 1, 9, 2, 10, 3, 11, 4, 12, 5, 13, 6, 14, 7, 15];

    /// <summary>
    /// Eight output pairs at once. Each pair is the sample weighted three to one against the
    /// neighbour on the side that pair falls, which is the whole of the filter.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Pair(Vector128<ushort> left, Vector128<ushort> centre,
                                        Vector128<ushort> right, int shift, ushort low, ushort high)
    {
        Vector128<ushort> weighted = centre * Vector128.Create((ushort)3);
        Vector128<ushort> even = Vector128.ShiftRightLogical(weighted + left + Vector128.Create(low), shift);
        Vector128<ushort> odd = Vector128.ShiftRightLogical(weighted + right + Vector128.Create(high), shift);

        return Vector128.Shuffle(Vector128.Narrow(even, odd), Vector128.Create(PairOrder));
    }

    /// <summary>Reads eight samples and spreads them one per lane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> Spread(ReadOnlySpan<byte> source, int at) =>
        Vector128.WidenLower(Vector128.CreateScalar(
            BinaryPrimitives.ReadUInt64LittleEndian(source[at..])).AsByte());

    /// <summary>Mixes two rows three to one, which is the vertical half of the same filter.</summary>
    private static void Blend(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<ushort> blended)
    {
        int x = 0;

        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<ushort> three = Vector128.Create((ushort)3);
            for (; x + 8 <= blended.Length; x += 8)
                ((Spread(near, x) * three) + Spread(far, x)).CopyTo(blended[x..]);
        }

        for (; x < blended.Length; x++)
            blended[x] = (ushort)((near[x] * 3) + far[x]);
    }

    /// <summary>Nearest sample expansion, for the sampling ratios that are neither one nor a half.</summary>
    private static void Replicate(JpegFrame frame, JpegComponent component, int y, Span<byte> destination, int width)
    {
        int row = Math.Min(y * component.VerticalFactor / frame.MaxVerticalFactor, component.SampledHeight - 1);
        ReadOnlySpan<byte> source = component.Plane.AsSpan(row * component.PlaneStride, component.SampledWidth);

        for (int x = 0; x < width; x++)
        {
            int at = x * component.HorizontalFactor / frame.MaxHorizontalFactor;
            destination[x] = source[Math.Min(at, component.SampledWidth - 1)];
        }
    }

    private static void YCbCrToRgb(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red,
                                   Span<byte> destination, int channels)
    {
        int x = 0;

        // Four channels packs into a word, so a pixel is assembled in registers and stored once.
        if (channels == 4 && Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian)
            x = YCbCrToRgbaWide(luma, blue, red, destination);

        int at = x * channels;
        for (; x < luma.Length; x++)
        {
            int y = luma[x];
            int cb = blue[x];
            int cr = red[x];

            destination[at] = Clamp(y + CrToRed[cr]);
            destination[at + 1] = Clamp(y + ((CbToGreen[cb] + CrToGreen[cr]) >> 16));
            destination[at + 2] = Clamp(y + CbToBlue[cb]);
            if (channels == 4)
                destination[at + 3] = 255;

            at += channels;
        }
    }

    /// <summary>
    /// The same arithmetic as the scalar path, eight pixels at a time, with the weights inline
    /// rather than looked up. Returns how many pixels it consumed.
    /// </summary>
    private static int YCbCrToRgbaWide(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue,
                                       ReadOnlySpan<byte> red, Span<byte> destination)
    {
        Span<uint> output = MemoryMarshal.Cast<byte, uint>(destination);
        Vector256<int> offset = Vector256.Create(128);
        Vector256<int> rounding = Vector256.Create(1 << 15);
        Vector256<int> ceiling = Vector256.Create(255);
        Vector256<int> opaque = Vector256.Create(unchecked((int)0xFF000000));

        int x = 0;
        for (; x <= luma.Length - 8; x += 8)
        {
            Vector256<int> y = Widen(luma, x);
            Vector256<int> cb = Widen(blue, x) - offset;
            Vector256<int> cr = Widen(red, x) - offset;

            Vector256<int> r = y + Vector256.ShiftRightArithmetic((cr * 91881) + rounding, 16);
            Vector256<int> g = y + Vector256.ShiftRightArithmetic(
                (cb * -22554) + (cr * -46802) + rounding, 16);
            Vector256<int> b = y + Vector256.ShiftRightArithmetic((cb * 116130) + rounding, 16);

            r = Vector256.Min(Vector256.Max(r, Vector256<int>.Zero), ceiling);
            g = Vector256.Min(Vector256.Max(g, Vector256<int>.Zero), ceiling);
            b = Vector256.Min(Vector256.Max(b, Vector256<int>.Zero), ceiling);

            Vector256<int> packed = r | Vector256.ShiftLeft(g, 8) | Vector256.ShiftLeft(b, 16) | opaque;
            packed.AsUInt32().CopyTo(output[x..]);
        }

        return x;
    }

    /// <summary>Reads eight bytes and spreads them one per lane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> Widen(ReadOnlySpan<byte> source, int at)
    {
        ulong packed = BinaryPrimitives.ReadUInt64LittleEndian(source[at..]);
        Vector128<ushort> bytes = Vector128.WidenLower(Vector128.CreateScalar(packed).AsByte());
        (Vector128<uint> low, Vector128<uint> high) = Vector128.Widen(bytes);

        return Vector256.Create(low, high).AsInt32();
    }

    /// <summary>
    /// Four component files hold ink coverage rather than light. The marker naming the transform
    /// also implies the colourants are stored inverted, so a stored byte already reads as light
    /// and the conversion is one multiply; without it the values are coverage and are inverted
    /// first. Neither path consults a profile.
    /// </summary>
    private static void CmykToRgb(byte[][] sources, ReadOnlySpan<int> offsets, int width,
                                  Span<byte> destination, int channels, JpegColorTransform transform)
    {
        ReadOnlySpan<byte> first = sources[0].AsSpan(offsets[0], width);
        ReadOnlySpan<byte> second = sources[1].AsSpan(offsets[1], width);
        ReadOnlySpan<byte> third = sources[2].AsSpan(offsets[2], width);
        ReadOnlySpan<byte> black = sources[3].AsSpan(offsets[3], width);

        int at = 0;
        for (int x = 0; x < width; x++)
        {
            // How much light each colourant lets through, before the black channel takes its share.
            int red, green, blue, key;

            if (transform == JpegColorTransform.YCck)
            {
                // Only the three colourants went through the transform, so only they invert.
                int luma = first[x];
                int cb = second[x];
                int cr = third[x];
                red = 255 - Clamp(luma + CrToRed[cr]);
                green = 255 - Clamp(luma + ((CbToGreen[cb] + CrToGreen[cr]) >> 16));
                blue = 255 - Clamp(luma + CbToBlue[cb]);
                key = black[x];
            }
            else if (transform == JpegColorTransform.CmykInverted)
            {
                red = first[x];
                green = second[x];
                blue = third[x];
                key = black[x];
            }
            else
            {
                red = 255 - first[x];
                green = 255 - second[x];
                blue = 255 - third[x];
                key = 255 - black[x];
            }

            destination[at] = Scale(red, key);
            destination[at + 1] = Scale(green, key);
            destination[at + 2] = Scale(blue, key);
            if (channels == 4)
                destination[at + 3] = 255;

            at += channels;
        }
    }

    /// <summary>Multiplies two coverages, rounding rather than truncating.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Scale(int value, int key) => (byte)(((value * key) + 127) / 255);

    /// <summary>
    /// Writes the components straight through, for the layouts that carry their output channels
    /// already: three components a file declares as red, green and blue, or any other count.
    /// </summary>
    private static void Interleave(byte[][] sources, ReadOnlySpan<int> offsets, int width,
                                   Span<byte> destination, int channels)
    {
        int planes = Math.Min(sources.Length, channels);

        for (int plane = 0; plane < planes; plane++)
        {
            ReadOnlySpan<byte> source = sources[plane].AsSpan(offsets[plane], width);
            int at = plane;
            for (int x = 0; x < width; x++, at += channels)
                destination[at] = source[x];
        }

        for (int plane = planes; plane < channels; plane++)
        {
            for (int x = 0, at = plane; x < width; x++, at += channels)
                destination[at] = 255;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp(int value) => value switch
    {
        < 0 => 0,
        > 255 => 255,
        _ => (byte)value,
    };
}
