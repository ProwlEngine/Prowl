// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Photonic.Rasterization;
using Prowl.Photonic.Raytracing;
using Prowl.Vector;

namespace Prowl.Photonic.Integration;

/// <summary>
/// Sparse texel sampling. Only a fraction of the atlas traces its own lighting; every other covered
/// texel is reconstructed from the traced points around it. Indirect lighting varies slowly over a
/// surface, so most of the atlas can be filled in far more cheaply than it can be traced.
/// </summary>
/// <remarks>
/// Points are elected in two populations. Over open surface, one per <c>Stride</c> x <c>Stride</c>
/// cell. Along contacts, a thin line of texels a couple wide is traced at the same spacing, hugging
/// wherever one surface meets another: the ring of floor where an object rests, the base of a wall,
/// the inside of a corner. Corners of that line are always elected, because a contact's shape is
/// carried by its corners and a regular grid walking along it can miss them entirely.
/// <para>The two populations never mix during reconstruction. A contact point answers only for
/// contact texels and an open point only for open ones, which is what stops a floor point inside a
/// room from being a perfectly reasonable interpolation source for the floor outside it: same plane,
/// same normal, a few centimetres apart, and a wall in between.</para>
/// </remarks>
internal sealed class SparseTexelSet
{
    /// <summary>True for texels that trace their own lighting.</summary>
    public required bool[] IsPoint { get; init; }

    /// <summary>True for texels on the thin contact line.</summary>
    public required bool[] IsContact { get; init; }

    /// <summary>Flat list of tracing texels, for an evenly balanced parallel loop.</summary>
    public int[] Points { get; private set; } = System.Array.Empty<int>();

    /// <summary>Points grouped by cell: cell <c>c</c> owns <c>CellPoints[CellStart[c] .. CellStart[c+1]]</c>.</summary>
    public required int[] CellPoints { get; init; }
    public required int[] CellStart { get; init; }

    public required int CellsX { get; init; }
    public required int CellsY { get; init; }
    public required int Stride { get; init; }

    /// <summary>Texels on the contact line, and how many of them trace.</summary>
    public required int ContactTexels { get; init; }
    public required int ContactPoints { get; init; }
    public required int CornerPoints { get; init; }

    /// <summary>Taps considered per texel. Capped at 32 so the visibility result fits one word per texel.</summary>
    private const int MaxTaps = 32;

    /// <summary>Bit per tap: set when geometry stands between this texel and that point.</summary>
    public uint[] BlockedTaps = System.Array.Empty<uint>();

    public static SparseTexelSet Build(TargetWorkspace ws, int stride, int contactStride)
    {
        stride = System.Math.Max(2, stride);
        int lineStride = contactStride > 0 ? System.Math.Max(1, contactStride) : stride;
        int w = ws.Width, h = ws.Height;
        int cellsX = (w + stride - 1) / stride;
        int cellsY = (h + stride - 1) / stride;
        int cellCount = cellsX * cellsY;

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

        var counts = new int[cellCount];
        foreach (int p in pointList)
            counts[(p / w / stride) * cellsX + (p % w / stride)]++;

        var cellStart = new int[cellCount + 1];
        for (int c = 0; c < cellCount; c++) cellStart[c + 1] = cellStart[c] + counts[c];
        var cellPoints = new int[pointList.Count];
        var cursor = (int[])cellStart.Clone();
        foreach (int p in pointList)
        {
            int cell = (p / w / stride) * cellsX + (p % w / stride);
            cellPoints[cursor[cell]++] = p;
        }

        return new SparseTexelSet
        {
            IsPoint = isPoint,
            IsContact = isContact,
            Points = pointList.ToArray(),
            CellPoints = cellPoints,
            CellStart = cellStart,
            CellsX = cellsX,
            CellsY = cellsY,
            Stride = stride,
            ContactTexels = contactTexels,
            ContactPoints = contactPoints,
            CornerPoints = cornerPoints,
        };
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

            candidates.Sort((a, b) => ws.Samples[a].Proximity.CompareTo(ws.Samples[b].Proximity));
            foreach (int idx in candidates)
            {
                bool represented = false;
                for (int e = 0; e < elected.Count && !represented; e++)
                    represented = Accept(ws.Samples[idx], ws.Samples[elected[e]], lineStride, out _);
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
                    represented = Accept(ws.Samples[idx], ws.Samples[elected[e]], stride, out _);
                if (represented) continue;

                elected.Add(idx);
                isPoint[idx] = true;
            }
        }
    }

    /// <summary>
    /// Collect the points allowed to speak for one texel, in a fixed order. Both the visibility pass
    /// and the reconstruction walk this, so a tap's index means the same thing in both and the
    /// blocked-bit for it lines up.
    /// </summary>
    private int GatherTaps(TargetWorkspace ws, int x, int y, in TexelSample texel, bool contact,
                           System.Span<int> taps, System.Span<float> distances, System.Span<float> facings)
    {
        int w = ws.Width;
        int count = 0;
        int cx = x / Stride, cy = y / Stride;
        for (int ny = cy - 1; ny <= cy + 1 && count < MaxTaps; ny++)
        for (int nx = cx - 1; nx <= cx + 1 && count < MaxTaps; nx++)
        {
            if (nx < 0 || ny < 0 || nx >= CellsX || ny >= CellsY) continue;
            int cell = ny * CellsX + nx;
            for (int p = CellStart[cell]; p < CellStart[cell + 1] && count < MaxTaps; p++)
            {
                int point = CellPoints[p];
                if (IsContact[point] != contact) continue;
                if (!Accept(texel, ws.Samples[point], Stride, out float facing)) continue;

                float dx = x - point % w, dy = y - point / w;
                taps[count] = point;
                distances[count] = System.MathF.Sqrt(dx * dx + dy * dy);
                facings[count] = facing;
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Mark the points each texel cannot actually see. Sharing a plane, a normal and a few
    /// centimetres is not enough to be the same piece of surface: a floor texel outside a room and one
    /// inside it satisfy all three, with a wall in between, and interpolating between them carries the
    /// room's light straight through the wall.
    /// </summary>
    public void ComputeVisibility(TargetWorkspace ws, Blas blas, BakeOptions options,
                                  System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        int w = ws.Width, h = ws.Height;
        BlockedTaps = new uint[w * h];
        var isolated = new bool[w * h];

        System.Threading.Tasks.Parallel.For(0, h, parallelOpts, y =>
        {
            System.Span<int> taps = stackalloc int[MaxTaps];
            System.Span<float> distances = stackalloc float[MaxTaps];
            System.Span<float> facings = stackalloc float[MaxTaps];

            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (!ws.Covered[idx]) continue;

                var texel = ws.Samples[idx];
                int count = GatherTaps(ws, x, y, texel, IsContact[idx], taps, distances, facings);

                uint blocked = 0;
                for (int t = 0; t < count; t++)
                {
                    var point = ws.Samples[taps[t]];
                    float straight = Float3.Distance(point.Position, texel.Position);

                    // Neighbours a texel or two away have no room to hide anything between them, and
                    // testing them would double the ray count for no result.
                    if (straight <= texel.WorldRadius * 3f) continue;

                    // Both ends lift off their own surface before the ray is cast. A straight line
                    // between two points on anything convex passes through the solid, so testing at
                    // surface level reports every sphere, tube and blob as blocking itself. The lift
                    // scales with the span so it always clears that sag, and stays far below the
                    // height of the walls this is meant to catch.
                    float lift = System.MathF.Max(texel.WorldRadius * 2f, straight * 0.15f);
                    var from = texel.Position + texel.Normal * lift;
                    var to = point.Position + point.Normal * lift;

                    var delta = to - from;
                    float distance = Float3.Length(delta);
                    if (distance <= options.RayBias * 8f) continue;

                    if (blas.AnyHit(from, delta / distance, options.RayBias, distance - options.RayBias * 4f))
                        blocked |= 1u << t;
                }

                // A texel that cannot see any of its points has nothing to interpolate from, so it
                // traces instead. Falling back to copying one point is what turns a walled-off region
                // into flat squares.
                if (count > 0 && System.Numerics.BitOperations.PopCount(blocked) == count && !IsPoint[idx])
                {
                    IsPoint[idx] = true;
                    isolated[idx] = true;
                }
                BlockedTaps[idx] = blocked;
            }
        });

        int extra = 0;
        for (int i = 0; i < isolated.Length; i++) if (isolated[i]) extra++;
        if (extra == 0) return;

        // Newly traced texels join the tracing list but not the cell lists: they answer for
        // themselves, and they are isolated precisely because they cannot answer for anyone else.
        var grown = new int[Points.Length + extra];
        System.Array.Copy(Points, grown, Points.Length);
        int at = Points.Length;
        for (int i = 0; i < isolated.Length; i++) if (isolated[i]) grown[at++] = i;
        System.Array.Sort(grown);
        Points = grown;
        IsolatedPoints = extra;
    }

    /// <summary>Texels promoted to tracing because nothing they could interpolate from was visible.</summary>
    public int IsolatedPoints { get; private set; }

    /// <summary>
    /// Reconstruct every covered texel from the points around it. Results go through a scratch buffer
    /// because traced texels are filtered too, and reading half-filtered values back would make the
    /// output depend on iteration order.
    /// </summary>
    public void Reconstruct(TargetWorkspace ws, System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        int w = ws.Width, h = ws.Height;
        var pixels = ws.Working();
        var scratch = ws.ScratchRGB();

        System.Threading.Tasks.Parallel.For(0, h, parallelOpts, y =>
        {
            System.Span<int> taps = stackalloc int[MaxTaps];
            System.Span<float> distances = stackalloc float[MaxTaps];
            System.Span<float> facings = stackalloc float[MaxTaps];

            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (!ws.Covered[idx]) continue;

                var texel = ws.Samples[idx];
                int tapCount = GatherTaps(ws, x, y, texel, IsContact[idx], taps, distances, facings);
                uint blocked = BlockedTaps.Length == 0 ? 0u : BlockedTaps[idx];

                Float3 value = new Float3(pixels[idx * 3], pixels[idx * 3 + 1], pixels[idx * 3 + 2]);
                if (tapCount > 0)
                {
                    float reach = Stride * 1.5f;
                    Float3 sum = Float3.Zero;
                    float total = 0f;
                    int nearest = -1;
                    for (int t = 0; t < tapCount; t++)
                    {
                        if (nearest < 0 || distances[t] < distances[nearest]) nearest = t;
                        if ((blocked & (1u << t)) != 0) continue;

                        float tent = 1f - distances[t] / reach;
                        if (tent <= 0f) continue;
                        float f2 = facings[t] * facings[t];
                        float weight = tent * f2 * f2;
                        int point = taps[t];
                        sum += new Float3(pixels[point * 3], pixels[point * 3 + 1], pixels[point * 3 + 2]) * weight;
                        total += weight;
                    }

                    // Everything blocked means the texel is walled off from its own neighbourhood.
                    // Those texels trace, so their own value is already the right answer; only a
                    // texel that somehow slipped through falls back to its closest point.
                    if (total > 1e-6f) value = sum * (1f / total);
                    else if (!IsPoint[idx] && nearest >= 0)
                    {
                        int point = taps[nearest];
                        value = new Float3(pixels[point * 3], pixels[point * 3 + 1], pixels[point * 3 + 2]);
                    }
                }

                scratch[idx * 3] = value.X;
                scratch[idx * 3 + 1] = value.Y;
                scratch[idx * 3 + 2] = value.Z;
            }
        });

        System.Threading.Tasks.Parallel.For(0, h, parallelOpts, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (!ws.Covered[idx]) continue;
                pixels[idx * 3] = scratch[idx * 3];
                pixels[idx * 3 + 1] = scratch[idx * 3 + 1];
                pixels[idx * 3 + 2] = scratch[idx * 3 + 2];
            }
        });
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
    /// Whether a point may speak for a texel at all: they have to be the same piece of surface,
    /// facing the same way, on the same plane, and close in world space. Atlas neighbours can be
    /// metres apart in the world, or be opposite faces of a wall.
    /// </summary>
    private static bool Accept(in TexelSample texel, in TexelSample point, int stride, out float facing)
    {
        facing = Float3.Dot(texel.Normal, point.Normal);
        if (facing < 0.5f) return false;

        var delta = point.Position - texel.Position;
        float radius = System.MathF.Max(texel.WorldRadius, 1e-5f);
        if (System.MathF.Abs(Float3.Dot(delta, texel.Normal)) > radius * 2f) return false;
        return Float3.LengthSquared(delta) <= radius * radius * stride * stride * 16f;
    }
}
