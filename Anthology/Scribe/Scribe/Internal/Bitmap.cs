using System;
using System.Numerics;
using static Prowl.Scribe.Internal.Common;

namespace Prowl.Scribe.Internal
{
    internal class Bitmap
    {
        // Backing fields match the original port for drop-in use elsewhere.
        public int w;                 // Width in pixels
        public int h;                 // Height in pixels
        public int stride;            // Row stride in bytes
        public FakePtr<byte> pixels;  // Pixel buffer (8-bit coverage)

        /// <summary>Flatten curves → rasterize.</summary>
        public void Rasterize(
            float flatnessInPixels,
            GlyphVertex[] vertices,
            int numVerts,
            float scaleX,
            float scaleY,
            int offX,
            int offY,
            int invert)
        {
            float scale = scaleX > scaleY ? scaleY : scaleX;
            var windings = FlattenCurves(vertices, numVerts, flatnessInPixels / scale,
                                         out int[] windingLengths,
                                         out int windingCount);
            if (windings != null)
            {
                RasterizeContours(windings, windingLengths, windingCount,
                    scaleX, scaleY, offX, offY, invert);
            }
        }

        /// <summary>
        /// Clip a single edge segment to a 1-pixel wide vertical slab [x, x+1] and accumulate signed coverage.
        /// </summary>
        private static void HandleClippedEdge(float[] scanline, int offset, int x, ActiveEdge e, float x0, float y0, float x1, float y1)
        {
            if (y0 == y1) return;         // No vertical extent
            if (y0 > e.ey) return;         // Entirely below
            if (y1 < e.sy) return;         // Entirely above

            // Clamp vertically to [e.sy, e.ey]
            if (y0 < e.sy)
            {
                x0 += (x1 - x0) * (e.sy - y0) / (y1 - y0);
                y0 = e.sy;
            }
            if (y1 > e.ey)
            {
                x1 += (x1 - x0) * (e.ey - y1) / (y1 - y0);
                y1 = e.ey;
            }

            // Contribution to slab [x, x+1]
            if (x0 <= x && x1 <= x)
            {
                scanline[x + offset] += e.direction * (y1 - y0);
            }
            else if (x0 >= x + 1 && x1 >= x + 1)
            {
                // no-op, fully to right
            }
            else
            {
                scanline[x + offset] += e.direction * (y1 - y0) * (1 - (x0 - x + (x1 - x)) * 0.5f);
            }
        }

        /// <summary>Integrate all active edges over the scanline at yTop..yTop+1.</summary>
        private static void FillActiveEdges(float[] scanline, int scanlineFill, int len, ActiveEdge e, float yTop)
        {
            float yBottom = yTop + 1;

            while (e != null)
            {
                if (e.fdx == 0)
                {
                    // Vertical edge
                    float x0 = e.fx;
                    if (x0 < len)
                    {
                        if (x0 >= 0)
                        {
                            HandleClippedEdge(scanline, 0, (int)x0, e, x0, yTop, x0, yBottom);
                            HandleClippedEdge(scanline, scanlineFill - 1, (int)x0 + 1, e, x0, yTop, x0, yBottom);
                        }
                        else
                        {
                            HandleClippedEdge(scanline, scanlineFill - 1, 0, e, x0, yTop, x0, yBottom);
                        }
                    }
                }
                else
                {
                    // General case
                    float x0 = e.fx;
                    float dx = e.fdx;
                    float xb = x0 + dx; // endpoint at yBottom before clamping
                    float dy = e.fdy;

                    float xTop, sy0;
                    if (e.sy > yTop) { xTop = x0 + dx * (e.sy - yTop); sy0 = e.sy; }
                    else { xTop = x0; sy0 = yTop; }

                    float xBottom, sy1;
                    if (e.ey < yBottom) { xBottom = x0 + dx * (e.ey - yTop); sy1 = e.ey; }
                    else { xBottom = xb; sy1 = yBottom; }

                    if (xTop >= 0 && xBottom >= 0 && xTop < len && xBottom < len)
                    {
                        // Fully inside horizontally
                        if ((int)xTop == (int)xBottom)
                        {
                            int xi = (int)xTop;
                            float height = sy1 - sy0;
                            scanline[xi] += e.direction * (1 - (xTop - xi + (xBottom - xi)) * 0.5f) * height;
                            scanline[xi + scanlineFill] += e.direction * height;
                        }
                        else
                        {
                            // Crosses integer x's
                            if (xTop > xBottom)
                            {
                                // Normalize so xTop <= xBottom
                                sy0 = yBottom - (sy0 - yTop);
                                sy1 = yBottom - (sy1 - yTop);
                                (sy0, sy1) = (sy1, sy0);
                                (xBottom, xTop) = (xTop, xBottom);
                                dx = -dx; dy = -dy;
                                (x0, xb) = (xb, x0);
                            }

                            int x1 = (int)xTop;
                            int x2 = (int)xBottom;
                            float yCross = (x1 + 1 - x0) * dy + yTop;
                            float sign = e.direction;
                            float area = sign * (yCross - sy0);

                            // First partial column (x1)
                            scanline[x1] += area * (1 - (xTop - x1 + 1f) * 0.5f);

                            // Full columns between x1+1 .. x2-1
                            float step = sign * dy;
                            for (int xi = x1 + 1; xi < x2; ++xi)
                            {
                                scanline[xi] += area + step * 0.5f;
                                area += step;
                            }

                            // Last partial column (x2)
                            yCross += dy * (x2 - (x1 + 1));
                            scanline[x2] += area + sign * (1 - (xBottom - x2) * 0.5f) * (sy1 - yCross);
                            scanline[x2 + scanlineFill] += sign * (sy1 - sy0);
                        }
                    }
                    else
                    {
                        // Slow path: may straddle horizontal bounds; clip per column
                        for (int xi = 0; xi < len; ++xi)
                        {
                            float y0 = yTop;
                            float x1 = xi;
                            float x2 = xi + 1;
                            float x3 = xb;
                            float y3 = yBottom;
                            float y1 = (xi - x0) / dx + yTop;
                            float y2 = (xi + 1 - x0) / dx + yTop;

                            if (x0 < x1 && x3 > x2)
                            {
                                HandleClippedEdge(scanline, 0, xi, e, x0, y0, x1, y1);
                                HandleClippedEdge(scanline, 0, xi, e, x1, y1, x2, y2);
                                HandleClippedEdge(scanline, 0, xi, e, x2, y2, x3, y3);
                            }
                            else if (x3 < x1 && x0 > x2)
                            {
                                HandleClippedEdge(scanline, 0, xi, e, x0, y0, x2, y2);
                                HandleClippedEdge(scanline, 0, xi, e, x2, y2, x1, y1);
                                HandleClippedEdge(scanline, 0, xi, e, x1, y1, x3, y3);
                            }
                            else if (x0 < x1 && x3 > x1)
                            {
                                HandleClippedEdge(scanline, 0, xi, e, x0, y0, x1, y1);
                                HandleClippedEdge(scanline, 0, xi, e, x1, y1, x3, y3);
                            }
                            else if (x3 < x1 && x0 > x1)
                            {
                                HandleClippedEdge(scanline, 0, xi, e, x0, y0, x1, y1);
                                HandleClippedEdge(scanline, 0, xi, e, x1, y1, x3, y3);
                            }
                            else if (x0 < x2 && x3 > x2)
                            {
                                HandleClippedEdge(scanline, 0, xi, e, x0, y0, x2, y2);
                                HandleClippedEdge(scanline, 0, xi, e, x2, y2, x3, y3);
                            }
                            else if (x3 < x2 && x0 > x2)
                            {
                                HandleClippedEdge(scanline, 0, xi, e, x0, y0, x2, y2);
                                HandleClippedEdge(scanline, 0, xi, e, x2, y2, x3, y3);
                            }
                            else
                            {
                                HandleClippedEdge(scanline, 0, xi, e, x0, y0, x3, y3);
                            }
                        }
                    }
                }

                e = e.next;
            }
        }

        /// <summary>Main scanline rasterization over sorted edges. Uses a sentinel head for the active edge list.</summary>
        private void RasterizeSortedEdges(FakePtr<Edge> edges, int count, int vsubsample, int offX, int offY)
        {
            // Active list sentinel (dummy head)
            var head = new ActiveEdge { next = null };

            // Scratch scanlines: coverage + running sums
            float[] scanline = w > 64 ? new float[w * 2 + 1] : new float[129];
            int scanlineFill = w; // second half holds column sums

            int y = offY;   // absolute y in destination bitmap
            int row = 0;    // row index into pixels

            // Edge sentinel to stop insertion loop
            edges[count].y0 = offY + h + 1;

            while (row < h)
            {
                float yTop = y;
                float yBottom = y + 1;

                // 1) Remove finished edges from the active list
                var prev = head; // prev.next is current
                while (prev.next != null)
                {
                    var cur = prev.next;
                    if (cur.ey <= yTop)
                    {
                        prev.next = cur.next;  // unlink
                        cur.direction = 0;     // keep behavior identical
                    }
                    else
                    {
                        prev = cur;
                    }
                }

                // 2) Insert new edges whose y0 enters this scan band
                while (edges.Value.y0 <= yBottom)
                {
                    if (edges.Value.y0 != edges.Value.y1)
                    {
                        var z = NewActive(edges.Value, offX, yTop);
                        if (z != null)
                        {
                            if (row == 0 && offY != 0 && z.ey < yTop)
                                z.ey = yTop; // clamp first row if needed
                            // Push-front
                            z.next = head.next;
                            head.next = z;
                        }
                    }
                    ++edges; // advance edge pointer
                }

                // 3) Rasterize active edges into scanline coverage
                Array.Clear(scanline, 0, w);
                Array.Clear(scanline, scanlineFill, w + 1);

                if (head.next != null)
                {
                    FillActiveEdges(scanline, scanlineFill + 1, w, head.next, yTop);
                }

                // 4) Convert integrated coverage to 0..255 and store to pixels
                float sum = 0f;
                for (int x = 0; x < w; ++x)
                {
                    sum += scanline[scanlineFill + x];
                    float k = scanline[x] + sum;
                    k = MathF.Abs(k) * 255 + 0.5f;
                    int m = (int)k;
                    if (m > 255) m = 255;
                    pixels[row * stride + x] = (byte)m;
                }

                // 5) Advance active edges in x for next scanline
                for (var e = head.next; e != null; e = e.next)
                {
                    e.fx += e.fdx;
                }

                ++y;
                ++row;
            }
        }

        private ActiveEdge NewActive(Edge e, int offX, float startY)
        {
            var z = new ActiveEdge();
            float dxdy = (e.x1 - e.x0) / (e.y1 - e.y0); // safe: edges generated with y0 != y1
            z.fdx = dxdy;
            z.fdy = dxdy != 0.0f ? 1.0f / dxdy : 0.0f;
            z.fx = e.x0 + dxdy * (startY - e.y0);
            z.fx -= offX;
            z.direction = e.invert != 0 ? 1.0f : -1.0f;
            z.sy = e.y0;
            z.ey = e.y1;
            z.next = null;
            return z;
        }

        /// <summary>Build edges from contours (already flattened) and rasterize.</summary>
        private void RasterizeContours(Vector2[] pts, int[] wcount, int windings, float scaleX, float scaleY, int offX, int offY, int invert)
        {
            float yScale = invert != 0 ? -scaleY : scaleY;
            int vsubsample = 1; // retained from original code path

            // Count edges over all windings
            int totalEdges = 0;
            for (int i = 0; i < windings; ++i) totalEdges += wcount[i];

            // +1 sentinel edge
            var edges = new Edge[totalEdges + 1];
            for (int i = 0; i < edges.Length; ++i) edges[i] = new Edge();

            int n = 0; // number of produced edges
            int m = 0; // running index into pts per winding

            for (int wIndex = 0; wIndex < windings; ++wIndex)
            {
                var p = new FakePtr<Vector2>(pts, m);
                int contourVerts = wcount[wIndex];
                m += contourVerts;

                int j = contourVerts - 1;
                for (int k = 0; k < contourVerts; j = k++)
                {
                    if (p[j].Y == p[k].Y) continue; // horizontal edges don't contribute

                    int a = k, b = j;
                    edges[n].invert = 0;

                    bool upward = (invert != 0 && p[j].Y > p[k].Y) || (invert == 0 && p[j].Y < p[k].Y);
                    if (upward)
                    {
                        edges[n].invert = 1;
                        a = j; b = k;
                    }

                    edges[n].x0 = p[a].X * scaleX;
                    edges[n].y0 = (p[a].Y * yScale) * vsubsample;
                    edges[n].x1 = p[b].X * scaleX;
                    edges[n].y1 = (p[b].Y * yScale) * vsubsample;
                    ++n;
                }
            }

            var edgePtr = new FakePtr<Edge>(edges);

            SortEdgesQuicksort(edgePtr, n);
            SortEdgesInsSort(edgePtr, n);

            RasterizeSortedEdges(edgePtr, n, vsubsample, offX, offY);
        }

        private Vector2[] FlattenCurves(GlyphVertex[] vertices, int numVerts, float objspaceFlatness, out int[] contourLengths, out int numContours)
        {
            int vCount = Math.Min(numVerts, vertices?.Length ?? 0);
            contourLengths = null;
            numContours = 0;

            if (vCount <= 0) return null;

            // Count contours (vmove starts a new contour)
            for (int i = 0; i < vCount; ++i)
                if (vertices[i].type == STBTT_vmove)
                    ++numContours;

            if (numContours == 0) return null;

            contourLengths = new int[numContours];

            float flat2 = objspaceFlatness * objspaceFlatness;

            // Pass 0: count points & fill contour lengths (target == null)
            int totalPoints = FlattenCurvesPass(vertices, vCount, flat2, target: null, contourLengths, fillContourLens: true);

            // Pass 1: allocate and write points
            var points = new Vector2[totalPoints];
            FlattenCurvesPass(vertices, vCount, flat2, points, contourLengths, fillContourLens: false);

            return points;
        }

        private int FlattenCurvesPass(GlyphVertex[] vertices, int vCount, float flat2, Vector2[] target, int[] contourLengths, bool fillContourLens)
        {
            float x = 0f, y = 0f;
            int contourIndex = -1;
            int startIndex = 0;
            int numPoints = 0;

            for (int i = 0; i < vCount; ++i)
            {
                switch (vertices[i].type)
                {
                    case STBTT_vmove:
                        // finalize previous contour
                        if (contourIndex >= 0 && fillContourLens)
                            contourLengths[contourIndex] = numPoints - startIndex;

                        // start new contour
                        ++contourIndex;
                        startIndex = numPoints;

                        x = vertices[i].x;
                        y = vertices[i].y;
                        AddPoint(target, numPoints++, x, y);
                        break;

                    case STBTT_vline:
                        x = vertices[i].x;
                        y = vertices[i].y;
                        AddPoint(target, numPoints++, x, y);
                        break;

                    case STBTT_vcurve:
                        TesselateCurve(target, ref numPoints, x, y,
                                       vertices[i].cx, vertices[i].cy,
                                       vertices[i].x, vertices[i].y,
                                       flat2, 0);
                        x = vertices[i].x;
                        y = vertices[i].y;
                        break;

                    case STBTT_vcubic:
                        TesselateCubic(target, ref numPoints, x, y,
                                       vertices[i].cx, vertices[i].cy,
                                       vertices[i].cx1, vertices[i].cy1,
                                       vertices[i].x, vertices[i].y,
                                       flat2, 0);
                        x = vertices[i].x;
                        y = vertices[i].y;
                        break;
                }
            }

            // finalize last contour
            if (contourIndex >= 0 && fillContourLens)
                contourLengths[contourIndex] = numPoints - startIndex;

            return numPoints;
        }

        private void AddPoint(Vector2[] points, int index, float x, float y)
        {
            if (points == null) return;
            points[index].X = x;
            points[index].Y = y;
        }

        private int TesselateCurve(Vector2[] points, ref int numPoints, float x0, float y0, float x1, float y1, float x2, float y2, float flat2, int depth)
        {
            // Midpoint curvature metric
            float mx = (x0 + 2 * x1 + x2) * 0.25f;
            float my = (y0 + 2 * y1 + y2) * 0.25f;
            float dx = (x0 + x2) * 0.5f - mx;
            float dy = (y0 + y2) * 0.5f - my;

            if (depth > 16) return 1;
            if (dx * dx + dy * dy > flat2)
            {
                TesselateCurve(points, ref numPoints, x0, y0,
                    (x0 + x1) * 0.5f, (y0 + y1) * 0.5f, mx, my, flat2, depth + 1);
                TesselateCurve(points, ref numPoints, mx, my,
                    (x1 + x2) * 0.5f, (y1 + y2) * 0.5f, x2, y2, flat2, depth + 1);
            }
            else
            {
                AddPoint(points, numPoints, x2, y2);
                numPoints++;
            }
            return 1;
        }

        private void TesselateCubic(Vector2[] points, ref int numPoints, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float flat2, int depth)
        {
            float dx0 = x1 - x0, dy0 = y1 - y0;
            float dx1 = x2 - x1, dy1 = y2 - y1;
            float dx2 = x3 - x2, dy2 = y3 - y2;
            float dx = x3 - x0, dy = y3 - y0;

            float longlen = (float)(Math.Sqrt(dx0 * dx0 + dy0 * dy0) +
                                    Math.Sqrt(dx1 * dx1 + dy1 * dy1) +
                                    Math.Sqrt(dx2 * dx2 + dy2 * dy2));
            float shortlen = (float)Math.Sqrt(dx * dx + dy * dy);
            float flatness2 = longlen * longlen - shortlen * shortlen;

            if (depth > 16) return;
            if (flatness2 > flat2)
            {
                float x01 = (x0 + x1) * 0.5f;
                float y01 = (y0 + y1) * 0.5f;
                float x12 = (x1 + x2) * 0.5f;
                float y12 = (y1 + y2) * 0.5f;
                float x23 = (x2 + x3) * 0.5f;
                float y23 = (y2 + y3) * 0.5f;

                float xa = (x01 + x12) * 0.5f;
                float ya = (y01 + y12) * 0.5f;
                float xb = (x12 + x23) * 0.5f;
                float yb = (y12 + y23) * 0.5f;

                float mx = (xa + xb) * 0.5f;
                float my = (ya + yb) * 0.5f;

                TesselateCubic(points, ref numPoints, x0, y0, x01, y01, xa, ya, mx, my, flat2, depth + 1);
                TesselateCubic(points, ref numPoints, mx, my, xb, yb, x23, y23, x3, y3, flat2, depth + 1);
            }
            else
            {
                AddPoint(points, numPoints, x3, y3);
                numPoints++;
            }
        }

        // ── Sorting helpers ───────────────────────────────────────────────────────────

        private static int CompareEdges(Edge a, Edge b)
        {
            // Primary: y0
            if (a.y0 < b.y0) return -1;
            if (a.y0 > b.y0) return 1;
            // Tie-breaker: x0
            if (a.x0 < b.x0) return -1;
            if (a.x0 > b.x0) return 1;
            // Next: y1
            if (a.y1 < b.y1) return -1;
            if (a.y1 > b.y1) return 1;
            return 0;
        }

        private void SortEdgesInsSort(FakePtr<Edge> p, int n)
        {
            for (int i = 1; i < n; ++i)
            {
                Edge t = p[i];
                int j = i;
                while (j > 0 && CompareEdges(t, p[j - 1]) < 0)
                {
                    p[j] = p[j - 1];
                    --j;
                }
                if (i != j) p[j] = t;
            }
        }

        private void SortEdgesQuicksort(FakePtr<Edge> p, int n)
        {
            while (n > 12)
            {
                // Median-of-three pivot selection using CompareEdges
                int m = n >> 1;

                // Order first, middle, last
                if (CompareEdges(p[m], p[0]) < 0) { var tmp = p[0]; p[0] = p[m]; p[m] = tmp; }
                if (CompareEdges(p[n - 1], p[m]) < 0) { var tmp = p[m]; p[m] = p[n - 1]; p[n - 1] = tmp; }
                if (CompareEdges(p[m], p[0]) < 0) { var tmp = p[0]; p[0] = p[m]; p[m] = tmp; }

                // Pivot at p[0] (middle after swaps)
                Edge pivot = p[0];
                int i = 1, j = n - 1;

                for (; ; )
                {
                    while (i < n && CompareEdges(p[i], pivot) < 0) ++i;
                    while (j > 0 && CompareEdges(pivot, p[j]) < 0) --j;
                    if (i >= j) break;
                    var t = p[i]; p[i] = p[j]; p[j] = t;
                    ++i; --j;
                }

                // Recurse on smaller side first (tail recursion elimination)
                if (j < n - i)
                {
                    SortEdgesQuicksort(p, j);
                    p = p + i;
                    n = n - i;
                }
                else
                {
                    SortEdgesQuicksort(p + i, n - i);
                    n = j;
                }
            }
        }

        // ── Internal types ───────────────────────────────────────────────────────────

        private class ActiveEdge
        {
            public float direction;
            public float ey;
            public float fdx;
            public float fdy;
            public float fx;
            public ActiveEdge next;
            public float sy;
        }

        private class Edge
        {
            public int invert;
            public float x0, x1;
            public float y0, y1;
        }
    }
}
