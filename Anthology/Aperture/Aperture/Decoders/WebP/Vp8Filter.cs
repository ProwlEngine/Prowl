// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// The filter that softens the seams block coding leaves, where the step across an edge is small
/// enough to be an artefact rather than a real edge. Part of the decoding rather than a finishing
/// touch, since the encoder assumed it would run.
/// </summary>
internal static class Vp8Filter
{
    private static byte Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

    private static int ClipSigned(int value) => value < -128 ? -128 : value > 127 ? 127 : value;

    private static int ClipDelta(int value) => value < -16 ? -16 : value > 15 ? 15 : value;

    /// <summary>Whether the step across the edge is small enough to be worth softening.</summary>
    private static bool Needs(Span<byte> data, int at, int step, int threshold)
    {
        int p1 = data[at - (2 * step)];
        int p0 = data[at - step];
        int q0 = data[at];
        int q1 = data[at + step];
        return (4 * Math.Abs(p0 - q0)) + Math.Abs(p1 - q1) <= threshold;
    }

    /// <summary>The stricter test, which also asks that the pixels either side are smooth.</summary>
    private static bool Needs2(Span<byte> data, int at, int step, int threshold, int inner)
    {
        int p3 = data[at - (4 * step)];
        int p2 = data[at - (3 * step)];
        int p1 = data[at - (2 * step)];
        int p0 = data[at - step];
        int q0 = data[at];
        int q1 = data[at + step];
        int q2 = data[at + (2 * step)];
        int q3 = data[at + (3 * step)];

        if ((4 * Math.Abs(p0 - q0)) + Math.Abs(p1 - q1) > threshold)
            return false;

        return Math.Abs(p3 - p2) <= inner && Math.Abs(p2 - p1) <= inner &&
               Math.Abs(p1 - p0) <= inner && Math.Abs(q3 - q2) <= inner &&
               Math.Abs(q2 - q1) <= inner && Math.Abs(q1 - q0) <= inner;
    }

    /// <summary>Whether the edge is sharp enough that only the two nearest pixels should move.</summary>
    private static bool HighEdge(Span<byte> data, int at, int step, int threshold)
    {
        int p1 = data[at - (2 * step)];
        int p0 = data[at - step];
        int q0 = data[at];
        int q1 = data[at + step];
        return Math.Abs(p1 - p0) > threshold || Math.Abs(q1 - q0) > threshold;
    }

    private static void Filter2(Span<byte> data, int at, int step)
    {
        int p1 = data[at - (2 * step)];
        int p0 = data[at - step];
        int q0 = data[at];
        int q1 = data[at + step];

        int a = (3 * (q0 - p0)) + ClipSigned(p1 - q1);
        int a1 = ClipDelta((a + 4) >> 3);
        int a2 = ClipDelta((a + 3) >> 3);

        data[at - step] = Clip(p0 + a2);
        data[at] = Clip(q0 - a1);
    }

    private static void Filter4(Span<byte> data, int at, int step)
    {
        int p1 = data[at - (2 * step)];
        int p0 = data[at - step];
        int q0 = data[at];
        int q1 = data[at + step];

        int a = 3 * (q0 - p0);
        int a1 = ClipDelta((a + 4) >> 3);
        int a2 = ClipDelta((a + 3) >> 3);
        int a3 = (a1 + 1) >> 1;

        data[at - (2 * step)] = Clip(p1 + a3);
        data[at - step] = Clip(p0 + a2);
        data[at] = Clip(q0 - a1);
        data[at + step] = Clip(q1 - a3);
    }

    private static void Filter6(Span<byte> data, int at, int step)
    {
        int p2 = data[at - (3 * step)];
        int p1 = data[at - (2 * step)];
        int p0 = data[at - step];
        int q0 = data[at];
        int q1 = data[at + step];
        int q2 = data[at + (2 * step)];

        int a = ClipSigned((3 * (q0 - p0)) + ClipSigned(p1 - q1));
        int a1 = ((27 * a) + 63) >> 7;
        int a2 = ((18 * a) + 63) >> 7;
        int a3 = ((9 * a) + 63) >> 7;

        data[at - (3 * step)] = Clip(p2 + a3);
        data[at - (2 * step)] = Clip(p1 + a2);
        data[at - step] = Clip(p0 + a1);
        data[at] = Clip(q0 - a1);
        data[at + step] = Clip(q1 - a2);
        data[at + (2 * step)] = Clip(q2 - a3);
    }

    public static void SimpleVertical(Span<byte> data, int at, int stride, int threshold)
    {
        int limit = (2 * threshold) + 1;
        for (int i = 0; i < 16; i++)
        {
            if (Needs(data, at + i, stride, limit))
                Filter2(data, at + i, stride);
        }
    }

    public static void SimpleHorizontal(Span<byte> data, int at, int stride, int threshold)
    {
        int limit = (2 * threshold) + 1;
        for (int i = 0; i < 16; i++)
        {
            if (Needs(data, at + (i * stride), 1, limit))
                Filter2(data, at + (i * stride), 1);
        }
    }

    public static void SimpleVerticalInner(Span<byte> data, int at, int stride, int threshold)
    {
        for (int k = 3; k > 0; k--)
        {
            at += 4 * stride;
            SimpleVertical(data, at, stride, threshold);
        }
    }

    public static void SimpleHorizontalInner(Span<byte> data, int at, int stride, int threshold)
    {
        for (int k = 3; k > 0; k--)
        {
            at += 4;
            SimpleHorizontal(data, at, stride, threshold);
        }
    }

    /// <summary>The edge between two whole blocks, where six pixels may move.</summary>
    private static void Loop26(Span<byte> data, int at, int step, int rowStep, int size,
                               int threshold, int inner, int highEdge)
    {
        int limit = (2 * threshold) + 1;
        while (size-- > 0)
        {
            if (Needs2(data, at, step, limit, inner))
            {
                if (HighEdge(data, at, step, highEdge))
                    Filter2(data, at, step);
                else
                    Filter6(data, at, step);
            }

            at += rowStep;
        }
    }

    /// <summary>An edge inside a block, where at most four pixels may move.</summary>
    private static void Loop24(Span<byte> data, int at, int step, int rowStep, int size,
                               int threshold, int inner, int highEdge)
    {
        int limit = (2 * threshold) + 1;
        while (size-- > 0)
        {
            if (Needs2(data, at, step, limit, inner))
            {
                if (HighEdge(data, at, step, highEdge))
                    Filter2(data, at, step);
                else
                    Filter4(data, at, step);
            }

            at += rowStep;
        }
    }

    public static void Vertical16(Span<byte> data, int at, int stride, int threshold, int inner, int edge) =>
        Loop26(data, at, stride, 1, 16, threshold, inner, edge);

    public static void Horizontal16(Span<byte> data, int at, int stride, int threshold, int inner, int edge) =>
        Loop26(data, at, 1, stride, 16, threshold, inner, edge);

    public static void Vertical16Inner(Span<byte> data, int at, int stride, int threshold, int inner, int edge)
    {
        for (int k = 3; k > 0; k--)
        {
            at += 4 * stride;
            Loop24(data, at, stride, 1, 16, threshold, inner, edge);
        }
    }

    public static void Horizontal16Inner(Span<byte> data, int at, int stride, int threshold, int inner, int edge)
    {
        for (int k = 3; k > 0; k--)
        {
            at += 4;
            Loop24(data, at, 1, stride, 16, threshold, inner, edge);
        }
    }

    public static void Vertical8(Span<byte> u, int uAt, Span<byte> v, int vAt, int stride,
                                 int threshold, int inner, int edge)
    {
        Loop26(u, uAt, stride, 1, 8, threshold, inner, edge);
        Loop26(v, vAt, stride, 1, 8, threshold, inner, edge);
    }

    public static void Horizontal8(Span<byte> u, int uAt, Span<byte> v, int vAt, int stride,
                                   int threshold, int inner, int edge)
    {
        Loop26(u, uAt, 1, stride, 8, threshold, inner, edge);
        Loop26(v, vAt, 1, stride, 8, threshold, inner, edge);
    }

    public static void Vertical8Inner(Span<byte> u, int uAt, Span<byte> v, int vAt, int stride,
                                      int threshold, int inner, int edge)
    {
        Loop24(u, uAt + (4 * stride), stride, 1, 8, threshold, inner, edge);
        Loop24(v, vAt + (4 * stride), stride, 1, 8, threshold, inner, edge);
    }

    public static void Horizontal8Inner(Span<byte> u, int uAt, Span<byte> v, int vAt, int stride,
                                        int threshold, int inner, int edge)
    {
        Loop24(u, uAt + 4, 1, stride, 8, threshold, inner, edge);
        Loop24(v, vAt + 4, 1, stride, 8, threshold, inner, edge);
    }
}
