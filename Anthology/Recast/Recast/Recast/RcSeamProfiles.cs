using System;
using System.Collections.Generic;

namespace Prowl.Recast
{
    /// <summary>
    /// The canonical height polylines of a tile's four seam lines.
    ///
    /// Two tiles meeting at a seam read the same cells — a heightfield built with a seam
    /// border hands both the same data — but each simplifies its own approximation of the
    /// height profile along the line, and two independent approximations agree with each
    /// other no better than each agrees with the profile. Instead of tightening budgets,
    /// the profile is simplified once, deterministically, from the shared cells; every tile
    /// emits exactly these knots along its seam edges, so both sides of a seam render one
    /// polyline: watertight by construction, and no denser than the error budget demands.
    ///
    /// Heights are in the heightfield's local frame (span heights times cell height, no
    /// origin, no detail-builder offset). A stretch whose cells hold surfaces further apart
    /// than the walkable climb has no single profile; nothing is evaluated or emitted there,
    /// and both sides skip it by the same rule.
    /// </summary>
    public class RcSeamProfileSet
    {
        private const int LineWest = 0, LineEast = 1, LineSouth = 2, LineNorth = 3;

        private readonly float _cs;
        private readonly float[] _lineCoord = new float[4];
        // Simplified knots per line, ascending t (the coordinate along the line). Knots sit
        // at cell-corner positions, so they land on the same grid polygon corners use.
        private readonly float[][] _knotT = new float[4][];
        private readonly float[][] _knotY = new float[4][];
        // True when the stretch between knot k and knot k + 1 crosses corners with no single
        // surface.
        private readonly bool[][] _gapAfter = new bool[4][];

        private readonly float _ch;

        // Build scratch, reused across the set's four lines and across tiles.
        private float[] _raw;
        private bool[] _valid;
        private readonly List<float> _knotScratchT = new List<float>();
        private readonly List<float> _knotScratchY = new List<float>();
        private readonly List<bool> _gapScratch = new List<bool>();
        private readonly List<int> _keepScratch = new List<int>();
        private readonly List<int> _workScratch = new List<int>();

        private RcSeamProfileSet(float cs, float ch)
        {
            _cs = cs;
            _ch = ch;
        }


        /// Builds the four profiles from a heightfield carrying a seam border. The border is
        /// what makes the result identical on both sides of every seam: each line's corners
        /// read the same world cells no matter which tile is asking. Returns null without a
        /// border, since then the tiles have nothing shared to agree on.
        public static RcSeamProfileSet Build(RcCompactHeightfield chf, float maxError)
        {
            if (chf.borderSize <= 0)
                return null;

            int b = chf.borderSize;
            int coreW = chf.width - b * 2;
            int coreH = chf.height - b * 2;
            if (coreW <= 0 || coreH <= 0)
                return null;

            RcSeamProfileSet set = new RcSeamProfileSet(chf.cs, chf.ch);
            set._lineCoord[LineWest] = 0;
            set._lineCoord[LineEast] = coreW * chf.cs;
            set._lineCoord[LineSouth] = 0;
            set._lineCoord[LineNorth] = coreH * chf.cs;
            set.BuildLine(chf, LineWest, b, b, 0, 1, coreH, maxError);
            set.BuildLine(chf, LineEast, b + coreW, b, 0, 1, coreH, maxError);
            set.BuildLine(chf, LineSouth, b, b, 1, 0, coreW, maxError);
            set.BuildLine(chf, LineNorth, b, b + coreH, 1, 0, coreW, maxError);
            return set;
        }

        /// Whether the edge from a to b lies along one of the seam lines. Positions are the
        /// heightfield's local floats, so seam vertices sit exactly on the line up to float
        /// rounding.
        public bool TryGetSeam(float ax, float az, float bx, float bz, out int line)
        {
            float eps = _cs * 0.01f;
            for (line = LineWest; line <= LineNorth; ++line)
            {
                float c = _lineCoord[line];
                bool onLine = line <= LineEast
                    ? MathF.Abs(ax - c) < eps && MathF.Abs(bx - c) < eps
                    : MathF.Abs(az - c) < eps && MathF.Abs(bz - c) < eps;
                if (onLine)
                    return true;
            }

            line = -1;
            return false;
        }

        /// Whether a pin next to the corner at (cx, cz) is safe to emit, judged by the polygon's
        /// other hull edge there, towards (nx, nz). Under ~20° off the seam the polygon tapers to
        /// a near-degenerate tip, where a vertex one cell from the corner forces a steep sliver
        /// facet no triangulation avoids.
        public bool PinSafe(int line, float cx, float cz, float nx, float nz)
        {
            float along = line <= LineEast ? nz - cz : nx - cx;
            float off = line <= LineEast ? nx - cx : nz - cz;
            return MathF.Abs(off) > MathF.Abs(along) * 0.364f;
        }

        /// Writes the knots the stretch from a to b covers into @p outVerts, ordered from a
        /// towards b, and returns how many — or -1 when the edge is not this profile's to fill
        /// and the caller should tessellate it itself. @p yOffset pre-compensates an offset the
        /// caller applies to every vertex later. Knots coinciding with an endpoint are skipped;
        /// the polygon corner is already there.
        public int EmitKnots(int line, float ax, float ay, float az, float bx, float by, float bz,
            float climb, bool pinNearA, bool pinNearB, float yOffset, float[] outVerts, int startVert, int maxKnots)
        {
            float[] kt = _knotT[line];
            float[] ky = _knotY[line];
            if (kt == null || kt.Length == 0)
                return -1;

            bool alongZ = line <= LineEast;
            float ta = alongZ ? az : ax;
            float tb = alongZ ? bz : bx;
            bool aOnProfile = TryEvalLine(line, ta, out float pa) && MathF.Abs(pa - ay) <= climb;
            bool bOnProfile = TryEvalLine(line, tb, out float pb) && MathF.Abs(pb - by) <= climb;

            // One endpoint on the profile is enough to claim the edge. Requiring both hands
            // the whole edge back to ordinary sampling whenever one end reaches past the
            // profiled stretch or onto another layer — and the tile across the seam, whose
            // edges end elsewhere, keeps drawing the profile over the part they share, which
            // is a disagreement over the whole overlap rather than near the one bad end.
            if (!aOnProfile && !bOnProfile)
                return -1;

            if (maxKnots <= 0)
                return 0;

            float tMin = Math.Min(ta, tb), tMax = Math.Max(ta, tb);
            float eps = _cs * 0.01f;

            // The profile's knots inside the stretch...
            int lo = 0;
            while (lo < kt.Length && kt[lo] <= tMin + eps)
                lo++;
            int hi = kt.Length - 1;
            while (hi >= 0 && kt[hi] >= tMax - eps)
                hi--;

            // ...plus a pin one cell inside each endpoint, holding a corner's rounding bend
            // inside its own cell instead of letting it ramp to the nearest knot as a wedge the
            // other side does not draw. A corner already on the profile has no bend to hold, and
            // pins go in pairs: every hull vertex must be used, so a lone one on a thin wedge
            // polygon pushes its best triangulation into a steep needle at the far end.
            bool ascending = ta <= tb;
            float exact = _ch * 0.05f;
            bool pins = pinNearA && pinNearB && aOnProfile && bOnProfile
                && (MathF.Abs(pa - ay) > exact || MathF.Abs(pb - by) > exact);

            // Knots belong to the surface this edge is on. Where an end sits off the profile
            // the edge is leaving it, so only knots within a climb of what the edge itself
            // spans are its to draw.
            float loY = MathF.Min(ay, by) - climb, hiY = MathF.Max(ay, by) + climb;
            float pinLoT = tMin + _cs, pinHiT = tMax - _cs;
            float pinLoY = 0, pinHiY = 0;
            bool wantPinLo = pins
                && pinLoT < tMax - eps
                && (lo > hi || MathF.Abs(kt[lo] - pinLoT) > _cs * 0.5f)
                && TryEvalLine(line, pinLoT, out pinLoY);
            bool wantPinHi = pins
                && pinHiT > tMin + eps
                && MathF.Abs(pinHiT - pinLoT) > _cs * 0.5f
                && (lo > hi || MathF.Abs(kt[hi] - pinHiT) > _cs * 0.5f)
                && TryEvalLine(line, pinHiT, out pinHiY);

            int total = (hi - lo + 1) + (wantPinLo ? 1 : 0) + (wantPinHi ? 1 : 0);
            Span<float> candT = total <= 64 ? stackalloc float[Math.Max(total, 1)] : new float[total];
            Span<float> candY = total <= 64 ? stackalloc float[Math.Max(total, 1)] : new float[total];
            int n = 0;
            if (wantPinLo)
            {
                candT[n] = pinLoT;
                candY[n++] = pinLoY;
            }

            for (int k = lo; k <= hi; ++k)
            {
                if (ky[k] < loY || ky[k] > hiY)
                    continue;

                candT[n] = kt[k];
                candY[n++] = ky[k];
            }

            if (wantPinHi)
            {
                candT[n] = pinHiT;
                candY[n++] = pinHiY;
            }

            int emitted = 0;
            for (int k = 0; k < n && emitted < maxKnots; ++k)
            {
                int c = ascending ? k : n - 1 - k;
                int pos = (startVert + emitted) * 3;
                outVerts[pos + 0] = alongZ ? _lineCoord[line] : candT[c];
                outVerts[pos + 1] = candY[c] + yOffset;
                outVerts[pos + 2] = alongZ ? candT[c] : _lineCoord[line];
                emitted++;
            }

            return emitted;
        }

        /// Evaluates the polyline at a position on a seam line, or reports there is nothing
        /// to agree on there: off every line, outside the profiled stretch, across a gap, or
        /// further than @p climb from @p yNear, the height the caller already has — the same
        /// two-surfaces guard #EmitKnots applies.
        public bool TryEval(float x, float z, float climb, float yNear, out float y)
        {
            y = 0;
            float eps = _cs * 0.01f;
            for (int line = LineWest; line <= LineNorth; ++line)
            {
                float c = _lineCoord[line];
                bool onLine = line <= LineEast ? MathF.Abs(x - c) < eps : MathF.Abs(z - c) < eps;
                if (!onLine)
                    continue;

                float t = line <= LineEast ? z : x;
                if (TryEvalLine(line, t, out float ly) && MathF.Abs(ly - yNear) <= climb)
                {
                    y = ly;
                    return true;
                }
            }

            return false;
        }

        private bool TryEvalLine(int line, float t, out float y)
        {
            y = 0;
            float[] kt = _knotT[line];
            float[] ky = _knotY[line];
            if (kt == null || kt.Length == 0)
                return false;

            float eps = _cs * 0.01f;
            if (t < kt[0] - eps || t > kt[^1] + eps)
                return false;

            int lo = 0, hi = kt.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) / 2;
                if (kt[mid] <= t)
                    lo = mid;
                else
                    hi = mid;
            }

            if (MathF.Abs(t - kt[lo]) < eps)
            {
                y = ky[lo];
                return true;
            }

            if (MathF.Abs(t - kt[hi]) < eps)
            {
                y = ky[hi];
                return true;
            }

            if (_gapAfter[line][lo])
                return false;

            float u = (t - kt[lo]) / (kt[hi] - kt[lo]);
            y = ky[lo] + (ky[hi] - ky[lo]) * u;
            return true;
        }

        /// Builds one line's polyline: the raw profile at every cell corner along the line, then
        /// each contiguous valid run simplified on its own so nothing interpolates across a break.
        private void BuildLine(RcCompactHeightfield chf, int line, int sx, int sz, int dx, int dz, int len,
            float maxError)
        {
            // Scratch shared by all four lines of all tiles built through this set.
            if (_raw == null || _raw.Length < len + 1)
            {
                _raw = new float[len + 1];
                _valid = new bool[len + 1];
            }

            float[] raw = _raw;
            bool[] valid = _valid;
            for (int k = 0; k <= len; ++k)
            {
                int cx = sx + dx * k;
                int cz = sz + dz * k;
                int lowest = int.MaxValue, highest = int.MinValue, n = 0;
                for (int oz = -1; oz <= 0; ++oz)
                {
                    for (int ox = -1; ox <= 0; ++ox)
                    {
                        int x = cx + ox, z = cz + oz;
                        if (x < 0 || z < 0 || x >= chf.width || z >= chf.height)
                            continue;

                        ref RcCompactCell cell = ref chf.cells[x + z * chf.width];
                        for (int si = cell.index, end = cell.index + cell.count; si < end; ++si)
                        {
                            int sy = chf.spans[si].y;
                            lowest = Math.Min(lowest, sy);
                            highest = Math.Max(highest, sy);
                            n++;
                        }
                    }
                }

                // Steep walkable ground spreads nearly a step per cell, so one surface can span
                // twice the climb across a corner's cells; separate layers stand much further.
                valid[k] = n > 0 && highest - lowest <= chf.walkableClimb * 2;
                // The highest touching span: it is already on the integer height grid corners are
                // stored on, so a corner meeting a knot equals it rather than rounding off it,
                // and it biases the seam upward like the detail builder's one-cell lift biases
                // interiors. An average sits below both and drags seam triangles under the ground.
                raw[k] = n > 0 ? highest * chf.ch : 0;
            }

            List<float> knotT = _knotScratchT;
            List<float> knotY = _knotScratchY;
            List<bool> gapAfter = _gapScratch;
            List<int> keep = _keepScratch;
            knotT.Clear();
            knotY.Clear();
            gapAfter.Clear();
            float budget = MathF.Max(maxError, chf.ch * 0.5f);
            int runStart = -1;
            for (int k = 0; k <= len + 1; ++k)
            {
                bool v = k <= len && valid[k];
                if (v && runStart < 0)
                {
                    runStart = k;
                }
                else if (!v && runStart >= 0)
                {
                    if (gapAfter.Count > 0)
                        gapAfter[^1] = true;

                    keep.Clear();
                    SimplifyRun(raw, runStart, k - 1, budget, keep);
                    keep.Sort();
                    foreach (int idx in keep)
                    {
                        knotT.Add(idx * _cs);
                        knotY.Add(raw[idx]);
                        gapAfter.Add(false);
                    }

                    runStart = -1;
                }
            }

            _knotT[line] = knotT.ToArray();
            _knotY[line] = knotY.ToArray();
            _gapAfter[line] = gapAfter.ToArray();
        }

        /// Douglas-Peucker on the run's vertical deviation: a knot is kept where the chord
        /// misses the raw profile by more than the budget. Run endpoints are always kept.
        private void SimplifyRun(float[] raw, int a, int b, float maxError, List<int> keep)
        {
            keep.Add(a);
            if (b <= a)
                return;

            keep.Add(b);
            List<int> work = _workScratch;
            work.Clear();
            work.Add(a);
            work.Add(b);
            while (work.Count > 0)
            {
                int hi = work[^1];
                int lo = work[^2];
                work.RemoveRange(work.Count - 2, 2);
                if (hi - lo < 2)
                    continue;

                float worst = 0;
                int worstAt = -1;
                for (int m = lo + 1; m < hi; ++m)
                {
                    float chord = raw[lo] + (raw[hi] - raw[lo]) * (m - lo) / (float)(hi - lo);
                    float dev = MathF.Abs(raw[m] - chord);
                    if (dev > worst)
                    {
                        worst = dev;
                        worstAt = m;
                    }
                }

                if (worstAt >= 0 && worst > maxError)
                {
                    keep.Add(worstAt);
                    work.Add(lo);
                    work.Add(worstAt);
                    work.Add(worstAt);
                    work.Add(hi);
                }
            }
        }
    }
}
