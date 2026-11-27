// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Photonic.Rasterization;
using Prowl.Vector;

namespace Prowl.Photonic.Integration;

/// <summary>
/// Chooses which texels trace their own lighting. Everything else is filled in by
/// <see cref="SparseNeighbours"/> from the points around it, which is far cheaper than tracing it:
/// indirect lighting varies slowly over a surface, so most of an atlas can be interpolated.
/// </summary>
/// <remarks>
/// Points are elected in two populations. Over open surface, one per <c>Stride</c> x <c>Stride</c>
/// cell. Along contacts, a thin line of texels a couple wide is traced at the same spacing, hugging
/// wherever one surface meets another: the ring of floor where an object rests, the base of a wall,
/// the inside of a corner. Corners of that line are always elected, because a contact's shape is
/// carried by its corners and a grid marching along it at a fixed spacing can step over them.
/// <para>A cell elects as many points as it needs. Two texels only share a point when they are the
/// same piece of surface, facing the same way, on the same plane, and close in the world, so a cell
/// holding several surfaces elects one for each rather than averaging them together.</para>
/// </remarks>
internal sealed class SparseTexelSet
{
    /// <summary>True for texels that trace their own lighting.</summary>
    public required bool[] IsPoint { get; init; }

    /// <summary>True for texels on the thin contact line.</summary>
    public required bool[] IsContact { get; init; }

    /// <summary>Flat list of tracing texels, for an evenly balanced parallel loop.</summary>
    public int[] Points { get; private set; } = System.Array.Empty<int>();

    public required int Stride { get; init; }

    /// <summary>Texels on the contact line, and how many of them trace.</summary>
    public required int ContactTexels { get; init; }
    public required int ContactPoints { get; init; }
    public required int CornerPoints { get; init; }

    public static SparseTexelSet Build(TargetWorkspace ws, int stride)
    {
        stride = System.Math.Max(2, stride);

        // Contacts are sampled at the same rate as open surface. Their corners are elected on top of
        // that, which is what carries the shape of a junction, so a denser line buys little.
        int lineStride = stride;
        int w = ws.Width, h = ws.Height;

        var isPoint = new bool[w * h];
        var isContact = new bool[w * h];
        int contactTexels = 0;
        for (int i = 0; i < isContact.Length; i++)
        {
            if (!ws.Covered[i] || ws.Samples[i].Proximity >= 1f) continue;
            isContact[i] = true;
            contactTexels++;
        }

        int cornerPoints = ElectCorners(ws, isContact, isPoint);
        int contactPoints = cornerPoints + ElectContactLine(ws, isContact, isPoint, lineStride);
        ElectOpenSurface(ws, isContact, isPoint, stride);

        var pointList = new System.Collections.Generic.List<int>(1024);
        for (int i = 0; i < isPoint.Length; i++)
            if (isPoint[i]) pointList.Add(i);

        return new SparseTexelSet
        {
            IsPoint = isPoint,
            IsContact = isContact,
            Points = pointList.ToArray(),
            Stride = stride,
            ContactTexels = contactTexels,
            ContactPoints = contactPoints,
            CornerPoints = cornerPoints,
        };
    }

    /// <summary>
    /// Promote texels that turned out to have nothing to interpolate from. They answer for themselves
    /// from here on, which is the only honest thing to do with a texel nothing else can speak for.
    /// </summary>
    public void AddPoints(bool[] extra, int count)
    {
        if (count <= 0) return;
        var grown = new int[Points.Length + count];
        System.Array.Copy(Points, grown, Points.Length);

        int at = Points.Length;
        for (int i = 0; i < extra.Length && at < grown.Length; i++)
        {
            if (!extra[i] || IsPoint[i]) continue;
            IsPoint[i] = true;
            grown[at++] = i;
        }

        if (at < grown.Length) System.Array.Resize(ref grown, at);
        System.Array.Sort(grown);
        Points = grown;
    }

    /// <summary>
    /// Every corner of the contact line traces, unconditionally. A corner is where the line turns or
    /// ends, or where the surface dips closest to whatever it is near. Those carry the shape of the
    /// contact, and a grid marching along the line at a fixed spacing will happily step over them.
    /// </summary>
    private static int ElectCorners(TargetWorkspace ws, bool[] isContact, bool[] isPoint)
    {
        int w = ws.Width, h = ws.Height;
        int elected = 0;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = y * w + x;
            if (!isContact[idx]) continue;

            // Which of the eight neighbours are also on the line. Eight, not four, or every diagonal
            // run would read as a string of isolated texels.
            int neighbours = 0;
            int sumX = 0, sumY = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx | dy) == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                if (!isContact[ny * w + nx]) continue;
                neighbours++;
                sumX += dx;
                sumY += dy;
            }

            // An end of the line, or a turn: two neighbours that do not cancel each other out. A
            // straight run has its two neighbours on opposite sides and sums to zero.
            bool corner = neighbours < 2 || (neighbours == 2 && (sumX != 0 || sumY != 0));

            // Or a strict closest approach, which is where a concave corner bottoms out. Strict on
            // both sides, so a run of equally-close texels does not elect every one of itself.
            if (!corner)
            {
                float proximity = ws.Samples[idx].Proximity;
                corner = true;
                for (int ny = y - 1; ny <= y + 1 && corner; ny++)
                for (int nx = x - 1; nx <= x + 1 && corner; nx++)
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (n == idx || !isContact[n]) continue;
                    if (ws.Samples[n].Proximity <= proximity) corner = false;
                }
            }

            if (!corner || isPoint[idx]) continue;
            isPoint[idx] = true;
            elected++;
        }
        return elected;
    }

    /// <summary>
    /// The rest of the contact line, at the same spacing as the open grid. Within each cell the point
    /// goes to the texel closest to whatever the contact is with, so it lands on the line itself.
    /// </summary>
    private static int ElectContactLine(TargetWorkspace ws, bool[] isContact, bool[] isPoint, int lineStride)
    {
        int w = ws.Width, h = ws.Height;
        int cellsX = (w + lineStride - 1) / lineStride;
        int cellsY = (h + lineStride - 1) / lineStride;
        var elected = new System.Collections.Generic.List<int>(4);
        var candidates = new System.Collections.Generic.List<int>(lineStride * lineStride);
        int total = 0;

        for (int cy = 0; cy < cellsY; cy++)
        for (int cx = 0; cx < cellsX; cx++)
        {
            candidates.Clear();
            elected.Clear();

            int x0 = cx * lineStride, y0 = cy * lineStride;
            int x1 = System.Math.Min(w, x0 + lineStride), y1 = System.Math.Min(h, y0 + lineStride);
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int idx = y * w + x;
                if (!isContact[idx]) continue;
                if (isPoint[idx]) elected.Add(idx);   // corners already elected represent their area
                else candidates.Add(idx);
            }
            if (candidates.Count == 0) continue;

            // Closest to the contact first: that texel is the one actually on the edge.
            candidates.Sort((a, b) => ws.Samples[a].Proximity.CompareTo(ws.Samples[b].Proximity));

            foreach (int idx in candidates)
            {
                bool represented = false;
                for (int e = 0; e < elected.Count && !represented; e++)
                    represented = Accept(ws.Samples[idx], ws.Samples[elected[e]], lineStride);
                if (represented) continue;
                elected.Add(idx);
                isPoint[idx] = true;
                total++;
            }
        }
        return total;
    }

    /// <summary>Coarse grid over everything off the contact line, elected from the cell centre outwards.</summary>
    private static void ElectOpenSurface(TargetWorkspace ws, bool[] isContact, bool[] isPoint, int stride)
    {
        int w = ws.Width, h = ws.Height;
        int cellsX = (w + stride - 1) / stride;
        int cellsY = (h + stride - 1) / stride;
        var scanOrder = BuildScanOrder(stride);
        var elected = new System.Collections.Generic.List<int>(8);

        for (int cy = 0; cy < cellsY; cy++)
        for (int cx = 0; cx < cellsX; cx++)
        {
            int x0 = cx * stride, y0 = cy * stride;
            int x1 = System.Math.Min(w, x0 + stride), y1 = System.Math.Min(h, y0 + stride);

            elected.Clear();
            for (int order = 0; order < scanOrder.Length; order++)
            {
                int x = x0 + scanOrder[order].X, y = y0 + scanOrder[order].Y;
                if (x >= x1 || y >= y1) continue;

                int idx = y * w + x;
                if (!ws.Covered[idx] || isContact[idx]) continue;

                bool represented = false;
                for (int e = 0; e < elected.Count && !represented; e++)
                    represented = Accept(ws.Samples[idx], ws.Samples[elected[e]], stride);
                if (represented) continue;

                elected.Add(idx);
                isPoint[idx] = true;
            }
        }
    }

    /// <summary>Add the per-texel direct term back over a reconstructed indirect field.</summary>
    public static void AddDirect(TargetWorkspace ws, Float3[] direct, System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        int w = ws.Width, h = ws.Height;
        var pixels = ws.Working();
        System.Threading.Tasks.Parallel.For(0, h, parallelOpts, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (!ws.Covered[idx]) continue;
                pixels[idx * 3] += direct[idx].X;
                pixels[idx * 3 + 1] += direct[idx].Y;
                pixels[idx * 3 + 2] += direct[idx].Z;
            }
        });
    }

    /// <summary>Cell-local visit order, nearest the centre first, shared by every cell.</summary>
    private static (int X, int Y)[] BuildScanOrder(int stride)
    {
        var order = new (int X, int Y)[stride * stride];
        int i = 0;
        for (int y = 0; y < stride; y++)
        for (int x = 0; x < stride; x++)
            order[i++] = (x, y);

        float centre = (stride - 1) * 0.5f;
        System.Array.Sort(order, (a, b) =>
        {
            float da = (a.X - centre) * (a.X - centre) + (a.Y - centre) * (a.Y - centre);
            float db = (b.X - centre) * (b.X - centre) + (b.Y - centre) * (b.Y - centre);
            int c = da.CompareTo(db);
            return c != 0 ? c : (a.Y * stride + a.X).CompareTo(b.Y * stride + b.X);
        });
        return order;
    }

    /// <summary>
    /// Whether one point may stand in for a texel during election. They have to be the same piece of
    /// surface: facing the same way, on the same plane, and close in the world. Atlas neighbours can
    /// be metres apart, or opposite faces of a wall.
    /// </summary>
    private static bool Accept(in TexelSample texel, in TexelSample point, int stride)
    {
        if (Float3.Dot(texel.Normal, point.Normal) < 0.5f) return false;

        var delta = point.Position - texel.Position;
        float radius = System.MathF.Max(texel.WorldRadius, 1e-5f);
        if (System.MathF.Abs(Float3.Dot(delta, texel.Normal)) > radius * 2f) return false;
        return Float3.LengthSquared(delta) <= radius * radius * stride * stride * 16f;
    }
}
