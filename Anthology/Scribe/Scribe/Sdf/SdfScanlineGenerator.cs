using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Prowl.Scribe.Internal;
using Prowl.Vector;

namespace Prowl.Scribe.Sdf
{
    // Result of rasterizing a single glyph into a signed distance field.
    internal struct SdfGlyphResult
    {
        public byte[] Rgba;     // width*height*4, R=G=B = the SDF value, A = 255
        public int Width;
        public int Height;

        // The rasterized region in font units (Y up), including distance-field padding.
        // Multiply by a pixel scale to obtain the on-screen quad relative to the pen origin.
        public double Rx0, Ry0, Rx1, Ry1;
    }

    // Scanline SDF generator. Builds inside spans per row (and per column) directly from the
    // nonzero-winding crossings, merges them, and writes each texel's signed distance as the distance
    // to the nearest span edge - taking the smaller of the horizontal and vertical results so every
    // edge orientation gets anti-aliasing.
    //
    // Nonzero winding makes overlapping strokes a single union span (the interior overlap edge never
    // becomes a span boundary), so self-overlap seams cannot form. Spans separated by less than a texel
    // are merged, since such a gap cannot be rendered and would otherwise read as a false edge. The
    // vertical pass only runs on columns the glyph occupies (tracked during the horizontal pass).
    internal static class SdfScanlineGenerator
    {
        private const double MergeGap = 1.0; // texels; spans closer than this are joined

        public static bool TryGenerate(FontFile font, int glyphIndex, float baseSize, float pxRange, out SdfGlyphResult result)
        {
            result = default;
            if (glyphIndex <= 0)
                return false;

            Shape shape = BuildShape(font, glyphIndex);
            if (shape == null)
                return false;

            double s = font.ScaleForPixelHeight(baseSize); // pixels per font unit
            if (s <= 0)
                return false;

            double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = -double.MaxValue, by1 = -double.MaxValue;
            shape.Bound(ref bx0, ref by0, ref bx1, ref by1);
            if (bx1 < bx0 || by1 < by0)
                return false;

            double padPx = pxRange * 0.5 + 1.0;
            double padFU = padPx / s;
            double rx0 = bx0 - padFU, ry0 = by0 - padFU, rx1 = bx1 + padFU, ry1 = by1 + padFU;

            int w = (int)Math.Ceiling((rx1 - rx0) * s);
            int h = (int)Math.Ceiling((ry1 - ry0) * s);
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096)
                return false;

            // Flatten edges and their bounds once. A scanline skips any edge it can't reach, which
            // avoids the unconditional curve solve in ScanlineIntersections for non-spanning edges.
            int ec = 0;
            foreach (var c in shape.Contours) ec += c.Edges.Count;
            var edges = new EdgeSegment[ec];
            var tedges = new EdgeSegment[ec];
            var eMinX = new double[ec];
            var eMinY = new double[ec];
            var eMaxX = new double[ec];
            var eMaxY = new double[ec];
            int ei = 0;
            foreach (var c in shape.Contours)
                foreach (var ed in c.Edges)
                {
                    edges[ei] = ed;
                    tedges[ei] = ed.Transposed();
                    double a = double.MaxValue, b = double.MaxValue, cc = -double.MaxValue, d = -double.MaxValue;
                    ed.Bound(ref a, ref b, ref cc, ref d);
                    eMinX[ei] = a; eMinY[ei] = b; eMaxX[ei] = cc; eMaxY[ei] = d;
                    ++ei;
                }

            float[] mag = new float[w * h];   // distance magnitude (texels) to nearest span edge
            Array.Fill(mag, (float)pxRange);  // default = fully outside, so skipped empty rows are correct
            bool[] inside = new bool[w * h];
            bool[] colHasInside = new bool[w];

            double[] xs = new double[3];
            int[] dys = new int[3];
            var crossings = new List<(double pos, int dy)>(16);
            var spans = new List<(double start, double end)>(8);
            Comparison<(double pos, int dy)> byPos = (a, b) => a.pos.CompareTo(b.pos);

            // Horizontal pass: per row, build inside spans in pixel X, fill the row distance.
            for (int row = 0; row < h; ++row)
            {
                double yShape = ry1 - (row + 0.5) / s;

                crossings.Clear();
                for (int e = 0; e < ec; ++e)
                {
                    if (yShape < eMinY[e] || yShape > eMaxY[e]) continue;
                    int n = edges[e].ScanlineIntersections(xs, dys, yShape);
                    for (int k = 0; k < n; ++k)
                        crossings.Add(((xs[k] - rx0) * s, dys[k]));
                }
                if (crossings.Count == 0) continue; // empty row stays outside (mag 0, inside false)
                crossings.Sort(byPos);
                BuildSpans(crossings, spans);

                int rowBase = row * w;
                int si = 0;
                for (int col = 0; col < w; ++col)
                {
                    double px = col + 0.5;
                    bool isIn = Resolve(spans, ref si, px, out double dist);
                    int idx = rowBase + col;
                    inside[idx] = isIn;
                    mag[idx] = (float)dist;
                    if (isIn) colHasInside[col] = true;
                }
            }

            // Vertical pass: only on columns the glyph occupies. Combine via min distance.
            for (int col = 0; col < w; ++col)
            {
                if (!colHasInside[col])
                    continue;

                double xShape = rx0 + (col + 0.5) / s;
                crossings.Clear();
                for (int e = 0; e < ec; ++e)
                {
                    if (xShape < eMinX[e] || xShape > eMaxX[e]) continue;
                    int n = tedges[e].ScanlineIntersections(xs, dys, xShape); // xs[k] = original Y crossing
                    for (int k = 0; k < n; ++k)
                        crossings.Add(((ry1 - xs[k]) * s, dys[k]));
                }
                if (crossings.Count == 0) continue;
                crossings.Sort(byPos);
                BuildSpans(crossings, spans);

                int si = 0;
                for (int row = 0; row < h; ++row)
                {
                    Resolve(spans, ref si, row + 0.5, out double vdist);
                    int idx = row * w + col;
                    if (vdist < mag[idx])
                        mag[idx] = (float)vdist;
                }
            }

            byte[] rgba = new byte[w * h * 4];
            double inv = 1.0 / pxRange;
            for (int i = 0; i < w * h; ++i)
            {
                double signed = inside[i] ? mag[i] : -mag[i];
                byte b = ToByte(0.5 + signed * inv);
                int o = i * 4;
                rgba[o + 0] = b;
                rgba[o + 1] = b;
                rgba[o + 2] = b;
                rgba[o + 3] = 255;
            }

            result = new SdfGlyphResult
            {
                Rgba = rgba,
                Width = w,
                Height = h,
                Rx0 = rx0,
                Ry0 = ry0,
                Rx1 = rx1,
                Ry1 = ry1
            };
            return true;
        }

        // Builds merged inside spans from sorted crossings using the nonzero-winding rule. Overlaps are
        // unioned automatically; spans closer than a texel are joined.
        private static void BuildSpans(List<(double pos, int dy)> crossings, List<(double start, double end)> spans)
        {
            spans.Clear();
            int wnd = 0;
            double start = 0;
            for (int i = 0; i < crossings.Count; ++i)
            {
                var cr = crossings[i];
                int before = wnd;
                wnd += cr.dy;
                if (before == 0 && wnd != 0)
                    start = cr.pos;
                else if (before != 0 && wnd == 0)
                {
                    int last = spans.Count - 1;
                    if (last >= 0 && start - spans[last].end < MergeGap)
                        spans[last] = (spans[last].start, cr.pos);
                    else
                        spans.Add((start, cr.pos));
                }
            }
        }

        // Whether p is inside a span, plus distance to the nearest span edge. `si` is a forward cursor
        // reused across a monotonically increasing sweep.
        private static bool Resolve(List<(double start, double end)> spans, ref int si, double p, out double dist)
        {
            while (si < spans.Count && spans[si].end < p)
                ++si;

            if (si < spans.Count && p >= spans[si].start)
            {
                var sp = spans[si];
                dist = Math.Min(p - sp.start, sp.end - p);
                return true;
            }

            double right = si < spans.Count ? spans[si].start - p : double.MaxValue;
            double left = si > 0 ? p - spans[si - 1].end : double.MaxValue;
            dist = Math.Min(left, right);
            return false;
        }

        internal static Shape BuildShape(FontFile font, int glyphIndex)
        {
            int count = font.GetGlyphShape(glyphIndex, out GlyphVertex[] verts);
            if (count <= 0 || verts == null)
                return null;

            var shape = new Shape();
            Contour contour = null;
            Double2 cur = new Double2(0, 0);

            for (int i = 0; i < count; ++i)
            {
                var v = verts[i];
                Double2 to = new Double2(v.x, v.y);
                switch ((int)v.type)
                {
                    case Common.STBTT_vmove:
                        contour = shape.AddContour();
                        cur = to;
                        break;
                    case Common.STBTT_vline:
                        if (contour != null && to != cur)
                            contour.AddEdge(EdgeSegment.Create(cur, to));
                        cur = to;
                        break;
                    case Common.STBTT_vcurve:
                        if (contour != null)
                            contour.AddEdge(EdgeSegment.Create(cur, new Double2(v.cx, v.cy), to));
                        cur = to;
                        break;
                    case Common.STBTT_vcubic:
                        if (contour != null)
                            contour.AddEdge(EdgeSegment.Create(cur, new Double2(v.cx, v.cy), new Double2(v.cx1, v.cy1), to));
                        cur = to;
                        break;
                }
            }

            shape.Contours.RemoveAll(c => c.Edges.Count == 0);
            return shape.Contours.Count > 0 ? shape : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ToByte(double v)
        {
            int i = (int)(v * 256.0);
            if (i < 0) i = 0;
            if (i > 255) i = 255;
            return (byte)i;
        }
    }
}
