using Prowl.Vector;
using System;
using System.Drawing;

namespace Prowl.Quill
{
    // Dedicated fast paths for stroked (border/outline) shapes, mirroring the *Filled family. A border is
    // an anti-aliased ring: a solid band between an inner and outer edge, with a one-pixel coverage fringe
    // on BOTH sides (coverage rides in uv.x - 1 = solid, 0 = fringe - exactly like the fills). The stroke
    // is centred on the shape's outline, matching a centred Stroke() of the same path, so these drop in
    // wherever a path is built and stroked. Corners are axis-aligned 90-degree miters, so no join maths is
    // needed. The fringe is expressed in logical units scaled by the transform (FringeHalfLogical) so it
    // stays ~1 physical pixel on screen at any zoom.
    public partial class Canvas
    {
        /// <summary>
        /// Paints a hardware-accelerated rectangle border (outline), centred on the rect's edge.
        /// This does not modify or use the current path.
        /// </summary>
        /// <remarks>Significantly faster than building a path and calling <see cref="Stroke"/>.</remarks>
        public void RectBorder(float x, float y, float width, float height, float thickness, Color32 color)
            => RoundedRectBorder(x, y, width, height, 0, 0, 0, 0, thickness, color);

        /// <summary>
        /// Paints a hardware-accelerated rounded-rectangle border (outline), centred on the rect's edge.
        /// This does not modify or use the current path.
        /// </summary>
        /// <remarks>Significantly faster than building a path and calling <see cref="Stroke"/>.</remarks>
        public void RoundedRectBorder(float x, float y, float width, float height, float radius, float thickness, Color32 color)
            => RoundedRectBorder(x, y, width, height, radius, radius, radius, radius, thickness, color);

        /// <summary>
        /// Paints a hardware-accelerated rounded-rectangle border (outline) with per-corner radii, centred
        /// on the rect's edge. This does not modify or use the current path.
        /// </summary>
        /// <param name="thickness">Total border width; the stroke straddles the outline by half each side.</param>
        /// <remarks>Significantly faster than building a path and calling <see cref="Stroke"/>.</remarks>
        public void RoundedRectBorder(float x, float y, float width, float height,
                                      float tlRadii, float trRadii, float brRadii, float blRadii,
                                      float thickness, Color32 color)
        {
            if (width <= 0 || height <= 0 || thickness <= 0)
                return;

            float maxRadius = Maths.Min(width, height) * 0.5f;
            tlRadii = Maths.Min(tlRadii, maxRadius);
            trRadii = Maths.Min(trRadii, maxRadius);
            brRadii = Maths.Min(brRadii, maxRadius);
            blRadii = Maths.Min(blRadii, maxRadius);

            // Centred stroke: extends half the thickness inside and out. Clamp so opposite inner edges
            // never cross (a thicker border just fills the rect).
            float half = Maths.Min(thickness * 0.5f, maxRadius);
            float hp = FringeHalfLogical();
            float coreHalf = Maths.Min(hp, half); // for sub-pixel borders the solid core collapses to a line
            // A border narrower than the fringe (sub-pixel) can't reach full coverage: fade the peak
            // instead of over-covering, so a 0.5px border renders at ~50% alpha rather than fully opaque.
            float covCore = (hp > 0f && half < hp) ? half / hp : 1f;

            int SegCount(float r) => r > 0 ? Maths.Max(1, (int)Maths.Ceiling(Maths.PI * (r + half) / 2 / _state.roundingMinDistance)) : 0;

            // Affine transform basis about the rect centre (T(p) = c + (p - centre) . [ex, ey]).
            float ccx = x + width / 2, ccy = y + height / 2;
            Float2 c = TransformPoint(new Float2(ccx, ccy));
            Float2 ex = TransformPoint(new Float2(ccx + 1, ccy)) - c;
            Float2 ey = TransformPoint(new Float2(ccx, ccy + 1)) - c;
            Float2 ToPx(double px, double py) => c + ex * (float)(px - ccx) + ey * (float)(py - ccy);

            Float2 solid = new Float2(covCore, 0f); // full coverage, or a faded peak for sub-pixel borders
            Float2 edge = new Float2(0f, 0f);
            // The whole border is one colour, so premultiply it once and append vertices raw (skips the
            // per-vertex premultiply that AddVertex does), matching how Stroke() feeds the polyline mesher.
            Color32 pm = PremultiplyColor(color);
            uint b = (uint)_vertices.Count;
            int ring = 0;

            // Vertex/index counts are known from the segment counts, so reserve both buffers once up front
            // instead of letting them grow incrementally as the ring is built.
            int tlS = SegCount(tlRadii), trS = SegCount(trRadii), brS = SegCount(brRadii), blS = SegCount(blRadii);
            int columns = (tlRadii > 0 ? tlS + 1 : 1) + (trRadii > 0 ? trS + 1 : 1)
                        + (brRadii > 0 ? brS + 1 : 1) + (blRadii > 0 ? blS + 1 : 1);
            Reserve(_vertices, columns * 4);  // 4 verts per column
            Reserve(_indices, columns * 18);  // 6 triangles (18 indices) per segment

            // Appends one outline vertex's 4-vertex column (screen-space positions), in radial order
            // outer->inner: outer fringe (coverage 0), outer core (1), inner core (1), inner fringe (0).
            void Column(Float2 of, Float2 oc, Float2 ic, Float2 inf)
            {
                _vertices.Add(new Vertex(of, edge, pm));
                _vertices.Add(new Vertex(oc, solid, pm));
                _vertices.Add(new Vertex(ic, solid, pm));
                _vertices.Add(new Vertex(inf, edge, pm));
                ring++;
            }

            // cxc/cyc: corner arc centre. For a square corner (radius 0) the outline vertex is the sharp
            // corner (sharpX, sharpY) and offsets go along the axes (sgn) for a 90-degree miter. Positions
            // are built in screen space from a once-transformed centre plus a per-step radial direction, so
            // no full affine transform runs per vertex.
            void EmitCorner(double cxc, double cyc, double radius, double startAngle, int segs,
                            double sharpX, double sharpY, float sgnX, float sgnY)
            {
                if (radius > 0)
                {
                    float rOF = (float)(radius + half + hp);
                    float rOC = (float)(radius + half - coreHalf);
                    float rIC = (float)Math.Max(0, radius - half + coreHalf);
                    float rIF = (float)Math.Max(0, radius - half - hp);

                    Float2 center = ToPx(cxc, cyc);
                    double da = (Math.PI / 2) / segs;
                    double dgx = Math.Cos(startAngle), dgy = Math.Sin(startAngle);
                    double cda = Math.Cos(da), sda = Math.Sin(da);
                    for (int j = 0; j <= segs; j++)
                    {
                        Float2 radial = ex * (float)dgx + ey * (float)dgy; // once per step, shared by 4 verts
                        Column(center + radial * rOF, center + radial * rOC,
                               center + radial * rIC, center + radial * rIF);
                        double ndgx = dgx * cda - dgy * sda, ndgy = dgx * sda + dgy * cda;
                        dgx = ndgx; dgy = ndgy;
                    }
                }
                else
                {
                    Float2 sharp = ToPx(sharpX, sharpY);
                    Float2 diag = ex * sgnX + ey * sgnY; // outward miter direction in screen space
                    float oMag = half + hp, ocMag = half - coreHalf;
                    Column(sharp + diag * oMag, sharp + diag * ocMag,
                           sharp - diag * ocMag, sharp - diag * oMag);
                }
            }

            // Corners in ring order (TL -> TR -> BR -> BL), each arc sweeping +90 degrees.
            EmitCorner(x + tlRadii, y + tlRadii, tlRadii, Maths.PI, tlS, x, y, -1, -1);
            EmitCorner(x + width - trRadii, y + trRadii, trRadii, Maths.PI * 1.5f, trS, x + width, y, 1, -1);
            EmitCorner(x + width - brRadii, y + height - brRadii, brRadii, 0, brS, x + width, y + height, 1, 1);
            EmitCorner(x + blRadii, y + height - blRadii, blRadii, Maths.PI * 0.5f, blS, x, y + height, -1, 1);

            EmitBand(b, ring);
        }

        /// <summary>
        /// Paints a hardware-accelerated circle border (ring), centred on the circle's outline.
        /// This does not modify or use the current path.
        /// </summary>
        /// <remarks>Significantly faster than building a path and calling <see cref="Stroke"/>.</remarks>
        public void CircleBorder(float x, float y, float radius, float thickness, Color32 color, int segments = -1)
        {
            if (radius <= 0 || thickness <= 0)
                return;

            float half = Maths.Min(thickness * 0.5f, radius);
            float hp = FringeHalfLogical();
            float coreHalf = Maths.Min(hp, half);
            float covCore = (hp > 0f && half < hp) ? half / hp : 1f; // fade peak for sub-pixel borders

            if (segments <= 0)
            {
                float circumference = Maths.PI * 2 * (radius + half);
                segments = Maths.Max(3, (int)Maths.Ceiling(circumference / _state.roundingMinDistance));
            }

            // Screen-space centre + per-step radial direction, so no full affine transform runs per vertex.
            Float2 c = TransformPoint(new Float2(x, y));
            Float2 ex = TransformPoint(new Float2(x + 1, y)) - c;
            Float2 ey = TransformPoint(new Float2(x, y + 1)) - c;

            float rOF = (float)(radius + half + hp), rOC = (float)(radius + half - coreHalf);
            float rIC = (float)Math.Max(0, radius - half + coreHalf), rIF = (float)Math.Max(0, radius - half - hp);

            Float2 solid = new Float2(covCore, 0f); // full coverage, or a faded peak for sub-pixel borders
            Float2 edge = new Float2(0f, 0f);
            Color32 pm = PremultiplyColor(color); // premultiply once; append raw (see RoundedRectBorder)
            uint b = (uint)_vertices.Count;
            Reserve(_vertices, segments * 4);  // 4 verts per column
            Reserve(_indices, segments * 18);  // 6 triangles (18 indices) per segment

            double da = Math.PI * 2 / segments, ca = Math.Cos(da), sa = Math.Sin(da);
            double dx = 1, dy = 0;
            for (int i = 0; i < segments; i++)
            {
                Float2 radial = ex * (float)dx + ey * (float)dy; // once per step, shared by 4 verts
                _vertices.Add(new Vertex(c + radial * rOF, edge, pm));
                _vertices.Add(new Vertex(c + radial * rOC, solid, pm));
                _vertices.Add(new Vertex(c + radial * rIC, solid, pm));
                _vertices.Add(new Vertex(c + radial * rIF, edge, pm));
                double ndx = dx * ca - dy * sa, ndy = dx * sa + dy * ca;
                dx = ndx; dy = ndy;
            }

            EmitBand(b, segments);
        }

        // Stitches a closed ring of 4-vertex columns (outer fringe, outer core, inner core, inner fringe)
        // into three bands: outer fringe, solid, inner fringe. Column k lives at b + k*4.
        private void EmitBand(uint b, int ring)
        {
            for (int k = 0; k < ring; k++)
            {
                int n = (k + 1) % ring;
                uint of0 = b + (uint)(k * 4), oc0 = of0 + 1, ic0 = of0 + 2, if0 = of0 + 3;
                uint of1 = b + (uint)(n * 4), oc1 = of1 + 1, ic1 = of1 + 2, if1 = of1 + 3;

                // Outer fringe (coverage 0 -> 1).
                _indices.Add(of0); _indices.Add(oc0); _indices.Add(oc1);
                _indices.Add(of0); _indices.Add(oc1); _indices.Add(of1);
                // Solid core (coverage 1).
                _indices.Add(oc0); _indices.Add(ic0); _indices.Add(ic1);
                _indices.Add(oc0); _indices.Add(ic1); _indices.Add(oc1);
                // Inner fringe (coverage 1 -> 0).
                _indices.Add(ic0); _indices.Add(if0); _indices.Add(if1);
                _indices.Add(ic0); _indices.Add(if1); _indices.Add(ic1);
            }

            AddTriangleCount(ring * 6);
        }
    }
}
