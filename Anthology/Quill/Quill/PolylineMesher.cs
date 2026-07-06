using Prowl.Vector;
using System;
using System.Collections.Generic;
using Math = System.Math;

// Based on the work by thedmd in his AddPolyline() implementation for Dear ImGui.

namespace Prowl.Quill
{
    /// <summary>
    /// Specifies the style used to join line segments at corners.
    /// </summary>
    public enum JointStyle
    {
        /// <summary>
        /// Creates a beveled corner with a flat edge connecting the outer corners of the line segments.
        /// </summary>
        Bevel,

        /// <summary>
        /// Creates a sharp corner by extending the outer edges of the line segments until they meet.
        /// Subject to the miter limit; beyond it the joint falls back to a bevel.
        /// </summary>
        Miter,

        /// <summary>
        /// Creates a rounded corner with a circular arc connecting the outer edges of the line segments.
        /// </summary>
        Round,

        /// <summary>
        /// Like <see cref="Miter"/>, but instead of falling back to a bevel past the miter limit the
        /// sharp tip is clipped flat at the limit distance, which reduces visual artifacts at very
        /// sharp junctions.
        /// </summary>
        MiterClip
    }

    /// <summary>
    /// Specifies the style used for the end caps of open paths.
    /// </summary>
    public enum EndCapStyle
    {
        /// <summary>
        /// No cap is added; the stroke ends at the endpoint (with an anti-aliasing fringe).
        /// </summary>
        Butt,

        /// <summary>
        /// A square cap is added, extending the stroke by half the stroke width.
        /// </summary>
        Square,

        /// <summary>
        /// A semicircular cap is added with a radius equal to half the stroke width.
        /// </summary>
        Round,

        /// <summary>
        /// A beveled cap.
        /// </summary>
        Bevel
    }

    /// <summary>
    /// Builds anti-aliased triangle meshes for stroked polylines.
    /// </summary>
    /// <remarks>
    /// Anti-aliasing is baked into the geometry: a one-pixel fringe ribbon surrounds the solid core,
    /// with the core vertices fully covered and the outer fringe vertices at zero coverage. Coverage
    /// is written into <see cref="Vertex"/>.u (uv.x) and multiplied in by the shader after the brush,
    /// so it composes with gradients and textures. The vertex colour is the stroke colour throughout.
    /// </remarks>
    internal static class PolylineMesher
    {
        private struct Arc
        {
            public Float2 Center;
            public Float2 From; // unit direction of the arc start
            public Float2 To;   // unit direction of the arc end
        }

        private const float MiterAngleLimitCos = -0.9999619f; // cos(179.5 degrees)

        // Per-call scratch state (one set per thread).
        [ThreadStatic] private static List<Float2> _points;
        [ThreadStatic] private static List<Float2> _normals;
        [ThreadStatic] private static List<float> _segLenSqr;
        [ThreadStatic] private static List<Arc> _arcs;
        [ThreadStatic] private static List<Vertex> _vtx;
        [ThreadStatic] private static List<uint> _idx;

        private static Color32 _color;
        private static float _covCore; // peak coverage of the solid core (1, or < 1 for sub-pixel lines)
        private static int _base;       // base vertex index of the current sliding window (slot 0)
        private static float _roundingMinDistance = 2.0f;

        private static List<Float2> Points => _points ??= new List<Float2>();
        private static List<Float2> Normals => _normals ??= new List<Float2>();
        private static List<float> SegLenSqr => _segLenSqr ??= new List<float>();
        private static List<Arc> Arcs => _arcs ??= new List<Arc>();
        private static List<Vertex> Vtx => _vtx ??= new List<Vertex>();
        private static List<uint> Idx => _idx ??= new List<uint>();

        // netstandard2.1 has no List.EnsureCapacity, so grow via the Capacity setter.
        private static void Reserve<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity) list.Capacity = capacity;
        }

        /// <summary>
        /// Meshes a stroked polyline into anti-aliased, indexed geometry.
        /// </summary>
        /// <param name="points">Path points, already in physical-pixel space.</param>
        /// <param name="thickness">Stroke width in physical pixels.</param>
        /// <param name="fringeWidth">Anti-aliasing fringe width in physical pixels (typically 1).</param>
        /// <param name="color">Stroke colour (not premultiplied; the canvas premultiplies on add).</param>
        /// <param name="joint">Corner join style.</param>
        /// <param name="miterLimit">Miter limit ratio for <see cref="JointStyle.Miter"/>/<see cref="JointStyle.MiterClip"/>.</param>
        /// <param name="startCap">Cap style for the start of an open path.</param>
        /// <param name="endCap">Cap style for the end of an open path.</param>
        /// <param name="roundingMinDistance">Minimum spacing between generated arc points, in pixels.</param>
        /// <param name="vertices">Receives the generated vertices (0-based; coverage in uv.x).</param>
        /// <param name="indices">Receives the generated triangle indices, relative to the first vertex.</param>
        public static void Create(List<Float2> points, float thickness, float fringeWidth, Color32 color,
            JointStyle joint, float miterLimit, EndCapStyle startCap, EndCapStyle endCap,
            float roundingMinDistance, out List<Vertex> vertices, out List<uint> indices)
        {
            var pts = Points; pts.Clear();
            Vtx.Clear();
            Idx.Clear();
            Arcs.Clear();
            vertices = Vtx;
            indices = Idx;

            if (points.Count < 2 || thickness <= 0 || color.A == 0)
                return;

            _roundingMinDistance = roundingMinDistance > 0.5f ? roundingMinDistance : 2.0f;

            // Drop consecutive duplicate points (zero-length segments break the normals).
            for (int i = 0; i < points.Count; i++)
            {
                if (pts.Count == 0 || Float2.LengthSquared(points[i] - pts[pts.Count - 1]) > 1e-10f)
                    pts.Add(points[i]);
            }

            // A path that returns to its start is a closed polyline; drop the duplicate end point.
            bool closed = false;
            if (pts.Count > 2 && Float2.LengthSquared(pts[0] - pts[pts.Count - 1]) < 1e-10f)
            {
                pts.RemoveAt(pts.Count - 1);
                closed = true;
            }
            if (pts.Count < 2)
                return;

            // Reserve so the buffers don't repeatedly grow mid-build. Round joins/caps add the most,
            // so estimate generously; the buffers are reused across calls, so this only grows once.
            Reserve(Vtx, pts.Count * 10);
            Reserve(Idx, pts.Count * 15);

            ComputeNormalsAndLengths(closed);

            // Set up colour, thickness and fringe exactly like the reference dispatcher.
            _color = color;
            float coreThickness = thickness - fringeWidth;
            float fringeThickness;
            _covCore = 1.0f;

            if (coreThickness < 0.0f)
            {
                // Sub-pixel line: keep a one-pixel-wide footprint and fade the peak coverage instead
                // of shrinking the geometry to nothing. This keeps thin lines crisp while letting them
                // fade out smoothly as they get thinner than a pixel.
                _covCore = thickness / fringeWidth;
                if (_covCore <= 0.0f)
                    return;
                coreThickness = 0.0f;
                fringeThickness = fringeWidth * 2.0f;
            }
            else
            {
                fringeThickness = coreThickness + fringeWidth * 2.0f;
            }

            if (miterLimit < 1.0f)
                miterLimit = 1.0f;

            if (coreThickness <= 0.0f)
                ThinAntiAliased(pts, closed, joint, miterLimit, startCap, endCap, coreThickness, fringeThickness, fringeWidth);
            else
                ThickAntiAliased(pts, closed, joint, miterLimit, startCap, endCap, coreThickness, fringeThickness, fringeWidth);

            EmitArcsIfAny(coreThickness, fringeThickness, fringeWidth, closed);
        }

        private static void ComputeNormalsAndLengths(bool closed)
        {
            var pts = Points;
            int count = pts.Count;
            var normals = Normals; normals.Clear();
            var segLen = SegLenSqr; segLen.Clear();

            for (int i = 0; i < count; i++) normals.Add(Float2.Zero);
            for (int i = 0; i <= count; i++) segLen.Add(0f);

            for (int i = 0; i < count - 1; i++)
            {
                float dx = pts[i + 1].X - pts[i].X;
                float dy = pts[i + 1].Y - pts[i].Y;
                float d2 = dx * dx + dy * dy;
                float invLen = d2 > 0 ? 1.0f / MathF.Sqrt(d2) : 0.0f;
                normals[i] = F(-dy * invLen, dx * invLen);
                segLen[i + 1] = (float)d2;
            }

            if (closed)
            {
                float dx = pts[0].X - pts[count - 1].X;
                float dy = pts[0].Y - pts[count - 1].Y;
                float d2 = dx * dx + dy * dy;
                float invLen = d2 > 0 ? 1.0f / MathF.Sqrt(d2) : 0.0f;
                normals[count - 1] = F(-dy * invLen, dx * invLen);
                segLen[0] = (float)d2;
            }
            else
            {
                normals[count - 1] = normals[count - 2];
                segLen[0] = segLen[count - 1];
            }
            segLen[count] = segLen[0];
        }

        #region Sliding-window emit helpers

        private static void SetSlot(int slot, float x, float y, float coverage)
        {
            int a = _base + slot;
            var v = new Vertex(F(x, y), F(coverage, 0f), _color);
            if (a < Vtx.Count)
                Vtx[a] = v;
            else
            {
                while (Vtx.Count < a) Vtx.Add(default);
                Vtx.Add(v);
            }
        }

        private static void Vc(int slot, float x, float y) => SetSlot(slot, x, y, _covCore); // core vertex
        private static void Vf(int slot, float x, float y) => SetSlot(slot, x, y, 0f);        // fringe vertex

        private static void Tri(int a, int b, int c)
        {
            Idx.Add((uint)(_base + a));
            Idx.Add((uint)(_base + b));
            Idx.Add((uint)(_base + c));
        }

        private static void Commit(int n) => _base += n;

        private static Float2 SlotPos(int slot)
        {
            var v = Vtx[_base + slot];
            return F(v.x, v.y);
        }

        private static void OffsetSlot(int slot, float dx, float dy)
        {
            int a = _base + slot;
            var v = Vtx[a];
            v.x += (float)dx;
            v.y += (float)dy;
            Vtx[a] = v;
        }

        #endregion

        #region Geometry helpers

        // Builds a Float2 from float components (Float2 itself is float-backed).
        private static Float2 F(float x, float y) => new Float2((float)x, (float)y);

        // Normalize (over zero): unit vector, or zero for a zero-length input.
        private static Float2 Norm(Float2 v)
        {
            float d2 = v.X * v.X + v.Y * v.Y;
            if (d2 <= 0) return Float2.Zero;
            float inv = 1.0f / MathF.Sqrt(d2);
            return F(v.X * inv, v.Y * inv);
        }

        // Reciprocal-length "fix normal" used by the bevel helpers (IM_FIXNORMAL2F): scales the
        // vector by 1/len^2, clamped, turning an averaged direction into a miter-length offset.
        private static Float2 FixNormal(Float2 v)
        {
            float d2 = v.X * v.X + v.Y * v.Y;
            if (d2 > 1e-6f)
            {
                float inv = 1.0f / d2;
                if (inv > 100.0f) inv = 100.0f;
                return F(v.X * inv, v.Y * inv);
            }
            return v;
        }

        #endregion

        #region Arc emission (round joins and caps)

        private static int FullCircleSegments(float radius)
        {
            if (radius <= 0) return 12;
            // Tessellate by on-screen size: roughly one segment per PixelsPerSegment pixels of
            // circumference. Tiny arcs (icons) get just a couple of segments, which is imperceptible
            // at that size; larger arcs scale up smoothly. Cheaper than the old fixed-12 floor.
            const float PixelsPerSegment = 2.0f;
            int n = (int)MathF.Ceiling(2.0f * MathF.PI * radius / PixelsPerSegment);
            return Math.Clamp(n, 2, 512);
        }

        private static void EmitArcsIfAny(float coreThickness, float fringeThickness, float fringeWidth, bool closed)
        {
            if (Arcs.Count == 0)
                return;

            // Thin lines have no core, so the round cap/join is a single fan to the fringe radius.
            if (coreThickness <= 0.0f)
                EmitArcs(0.0f, fringeThickness * 0.5f);
            else
                EmitArcs(coreThickness * 0.5f, fringeThickness * 0.5f);
        }

        private static void EmitArcs(float coreRadius, float fringeRadius)
        {
            float maxRadius = Math.Max(coreRadius, fringeRadius);
            int maxArcSeg = Math.Max((FullCircleSegments(maxRadius) + 1) / 2, 2);
            int arms = (coreRadius > 0.0f ? 1 : 0) + (fringeRadius > 0.0f ? 1 : 0);
            if (arms == 0)
                return;

            float covInner = coreRadius > 0.0f ? _covCore : 0f;
            float covOuter = fringeRadius > 0.0f ? 0f : _covCore;

            foreach (var arc in Arcs)
            {
                float cosTheta = arc.From.X * arc.To.X + arc.From.Y * arc.To.Y;
                float arcLength = MathF.Acos(Math.Clamp((float)cosTheta, -1f, 1f));
                int seg = Math.Max((int)MathF.Ceiling(maxArcSeg * arcLength / MathF.PI), cosTheta > 0.707f ? 1 : 2);
                float step = arcLength / seg;
                float stepCos = MathF.Cos((float)step);
                float stepSin = MathF.Sin((float)step);

                uint b = (uint)Vtx.Count;

                if (arms == 1)
                {
                    float dx = arc.From.X * maxRadius;
                    float dy = arc.From.Y * maxRadius;

                    // Center (full coverage) + ring (fringe coverage).
                    Vtx.Add(new Vertex(arc.Center, F(_covCore, 0f), _color));
                    for (int j = 0; j <= seg; j++)
                    {
                        Vtx.Add(new Vertex(F(arc.Center.X + dx, arc.Center.Y + dy), F(covInner, 0f), _color));
                        float nx = dx * stepCos - dy * stepSin;
                        float ny = dx * stepSin + dy * stepCos;
                        dx = nx; dy = ny;
                    }
                    for (int j = 0; j < seg; j++)
                    {
                        Idx.Add(b);
                        Idx.Add(b + (uint)(j + 2));
                        Idx.Add(b + (uint)(j + 1));
                    }
                }
                else
                {
                    float dx = arc.From.X;
                    float dy = arc.From.Y;

                    Vtx.Add(new Vertex(arc.Center, F(_covCore, 0f), _color)); // 0 = center
                    for (int j = 0; j <= seg; j++)
                    {
                        Vtx.Add(new Vertex(F(arc.Center.X + dx * coreRadius, arc.Center.Y + dy * coreRadius), F(covInner, 0f), _color));
                        Vtx.Add(new Vertex(F(arc.Center.X + dx * fringeRadius, arc.Center.Y + dy * fringeRadius), F(covOuter, 0f), _color));
                        float nx = dx * stepCos - dy * stepSin;
                        float ny = dx * stepSin + dy * stepCos;
                        dx = nx; dy = ny;
                    }
                    for (int j = 0; j < seg; j++)
                    {
                        uint inner0 = b + (uint)(j * 2 + 1);
                        uint outer0 = b + (uint)(j * 2 + 2);
                        uint inner1 = b + (uint)(j * 2 + 3);
                        uint outer1 = b + (uint)(j * 2 + 4);
                        Idx.Add(b); Idx.Add(inner1); Idx.Add(inner0);       // inner fan
                        Idx.Add(inner0); Idx.Add(inner1); Idx.Add(outer1);  // fringe strip
                        Idx.Add(inner0); Idx.Add(outer1); Idx.Add(outer0);
                    }
                }
            }
        }

        private static void PushArc(Float2 center, Float2 from, Float2 to)
        {
            Arcs.Add(new Arc { Center = center, From = from, To = to });
        }

        #endregion

        private enum CapType { Butt, Square, Round }

        private static CapType MapCap(EndCapStyle cap)
        {
            switch (cap)
            {
                case EndCapStyle.Square: return CapType.Square;
                case EndCapStyle.Round: return CapType.Round;
                case EndCapStyle.Bevel: return CapType.Round; // no native bevel cap; round is the closest
                default: return CapType.Butt;
            }
        }

        #region Thin anti-aliased path (sub-pixel and 1px lines)

        private static void ThinAntiAliased(List<Float2> pts, bool closed, JointStyle joint, float miterLimit,
            EndCapStyle startCapStyle, EndCapStyle endCapStyle, float coreThickness, float fringeThickness, float fringeWidth)
        {
            int count = pts.Count;
            var normals = Normals;
            var segLen = SegLenSqr;

            JointStyle defaultJoin = joint == JointStyle.Bevel ? JointStyle.Bevel : (joint == JointStyle.Round ? JointStyle.Round : JointStyle.Miter);
            JointStyle defaultJoinLimit = joint == JointStyle.Round ? JointStyle.Round : JointStyle.Bevel;

            float halfThickness = fringeThickness * 0.5f;
            float miterDistLimit = halfThickness * miterLimit;
            float miterDistLimitSqr = miterDistLimit * miterDistLimit;

            CapType startCap = MapCap(startCapStyle);
            CapType endCap = MapCap(endCapStyle);

            int b0 = Vtx.Count;
            _base = b0;

            Float2 p0 = pts[closed ? count - 1 : 0];
            Float2 n0 = normals[closed ? count - 1 : 0];

            Vf(0, p0.X - n0.X * halfThickness, p0.Y - n0.Y * halfThickness);
            Vc(1, p0.X, p0.Y);
            Vf(2, p0.X + n0.X * halfThickness, p0.Y + n0.Y * halfThickness);

            if (!closed)
                ThinCap(p0, n0, segLen[1], halfThickness, fringeWidth, +1.0f, startCap);

            Float2 p1, n1;
            for (int i = closed ? 0 : 1; i < count; i++, p0 = p1, n0 = n1)
            {
                p1 = pts[i];
                n1 = normals[i];

                float cosTheta = (float)(n0.X * n1.X + n0.Y * n1.Y);
                if (cosTheta > 1f) cosTheta = 1f;
                float miterScale = cosTheta > MiterAngleLimitCos ? halfThickness / (1.0f + cosTheta) : float.MaxValue;
                float miterOffX = (n0.X + n1.X) * miterScale;
                float miterOffY = (n0.Y + n1.Y) * miterScale;
                float miterDistSqr = miterOffX * miterOffX + miterOffY * miterOffY;

                bool overlap = segLen[i] < miterDistSqr || segLen[i + 1] < miterDistSqr || cosTheta <= MiterAngleLimitCos;
                bool continuous = closed || i != count - 1;

                JointStyle preferred = continuous ? (miterDistSqr > miterDistLimitSqr ? defaultJoinLimit : defaultJoin) : JointStyle.Bevel;
                int joinKind = overlap ? (continuous ? 4 /*ThickButt*/ : 1 /*Butt*/) : JoinKind(preferred);

                if (joinKind == 0) // Miter
                {
                    Vf(3, p1.X - miterOffX, p1.Y - miterOffY);
                    Vc(4, p1.X, p1.Y);
                    Vf(5, p1.X + miterOffX, p1.Y + miterOffY);
                    ThinQuad();
                    Commit(3);
                }
                else if (joinKind == 1) // Butt
                {
                    Vf(3, p1.X - n1.X * halfThickness, p1.Y - n1.Y * halfThickness);
                    Vc(4, p1.X, p1.Y);
                    Vf(5, p1.X + n1.X * halfThickness, p1.Y + n1.Y * halfThickness);
                    ThinQuad();
                    Commit(3);
                }
                else if (joinKind == 2) // Bevel
                {
                    float sinTheta = n0.Y * n1.X - n0.X * n1.Y;
                    Float2 bevelN = Norm(F(n0.X + n1.X, n0.Y + n1.Y));
                    Float2 d0 = FixNormal(F((n0.X + bevelN.X) * 0.5f, (n0.Y + bevelN.Y) * 0.5f)); d0 *= (float)halfThickness;
                    Float2 d1 = FixNormal(F((n1.X + bevelN.X) * 0.5f, (n1.Y + bevelN.Y) * 0.5f)); d1 *= (float)halfThickness;

                    if (sinTheta < 0.0f)
                    {
                        Vf(3, p1.X - d0.X, p1.Y - d0.Y);
                        Vf(4, p1.X - d1.X, p1.Y - d1.Y);
                        Vc(5, p1.X, p1.Y);
                        Vf(6, p1.X + miterOffX, p1.Y + miterOffY);
                        Tri(0, 1, 5); Tri(0, 5, 3); Tri(1, 2, 5); Tri(2, 6, 5); Tri(3, 5, 4);
                    }
                    else
                    {
                        Vf(3, p1.X + d0.X, p1.Y + d0.Y);
                        Vf(4, p1.X - miterOffX, p1.Y - miterOffY);
                        Vc(5, p1.X, p1.Y);
                        Vf(6, p1.X + d1.X, p1.Y + d1.Y);
                        Tri(0, 1, 5); Tri(0, 5, 4); Tri(1, 2, 5); Tri(2, 3, 5); Tri(3, 5, 6);
                    }
                    Commit(4);
                }
                else if (joinKind == 3) // Round
                {
                    float sinTheta = n0.Y * n1.X - n0.X * n1.Y;
                    if (sinTheta < 0.0f)
                    {
                        PushArc(p1, F(-n0.X, -n0.Y), F(-n1.X, -n1.Y));
                        Vf(3, p1.X - n0.X * halfThickness, p1.Y - n0.Y * halfThickness);
                        Vf(4, p1.X - n1.X * halfThickness, p1.Y - n1.Y * halfThickness);
                        Vc(5, p1.X, p1.Y);
                        Vf(6, p1.X + miterOffX, p1.Y + miterOffY);
                        Tri(0, 1, 5); Tri(0, 5, 3); Tri(1, 2, 5); Tri(2, 6, 5);
                    }
                    else
                    {
                        PushArc(p1, F(n1.X, n1.Y), F(n0.X, n0.Y));
                        Vf(3, p1.X + n0.X * halfThickness, p1.Y + n0.Y * halfThickness);
                        Vf(4, p1.X - miterOffX, p1.Y - miterOffY);
                        Vc(5, p1.X, p1.Y);
                        Vf(6, p1.X + n1.X * halfThickness, p1.Y + n1.Y * halfThickness);
                        Tri(0, 1, 5); Tri(0, 5, 4); Tri(1, 2, 5); Tri(2, 3, 5);
                    }
                    Commit(4);
                }
                else // ThickButt (overlap)
                {
                    float sinTheta = n0.Y * n1.X - n0.X * n1.Y;

                    Vf(3, p1.X - n0.X * halfThickness, p1.Y - n0.Y * halfThickness);
                    Vf(4, p1.X + n0.X * halfThickness, p1.Y + n0.Y * halfThickness);
                    Vc(5, p1.X, p1.Y);
                    Vf(6, p1.X - n1.X * halfThickness, p1.Y - n1.Y * halfThickness);
                    Vc(7, p1.X, p1.Y);
                    Vf(8, p1.X + n1.X * halfThickness, p1.Y + n1.Y * halfThickness);
                    Tri(0, 1, 7); Tri(0, 7, 3); Tri(1, 2, 7); Tri(2, 4, 7);

                    JointStyle pj = preferred;
                    if (pj == JointStyle.Miter || pj == JointStyle.MiterClip)
                    {
                        if (cosTheta > MiterAngleLimitCos)
                        {
                            if (sinTheta < 0.0f)
                            {
                                Vf(5, p1.X - miterOffX, p1.Y - miterOffY);
                                Tri(3, 7, 5); Tri(5, 7, 6);
                            }
                            else
                            {
                                Vf(5, p1.X + miterOffX, p1.Y + miterOffY);
                                Tri(4, 5, 7); Tri(5, 8, 7);
                            }
                        }
                    }
                    else if (pj == JointStyle.Bevel)
                    {
                        if (cosTheta > MiterAngleLimitCos)
                        {
                            Float2 bevelN = Norm(F(n0.X + n1.X, n0.Y + n1.Y));
                            Float2 d0 = FixNormal(F((n0.X + bevelN.X) * 0.5f, (n0.Y + bevelN.Y) * 0.5f)); d0 *= (float)halfThickness;
                            Float2 d1 = FixNormal(F((n1.X + bevelN.X) * 0.5f, (n1.Y + bevelN.Y) * 0.5f)); d1 *= (float)halfThickness;
                            if (sinTheta < 0.0f)
                            {
                                Vf(3, p1.X - d0.X, p1.Y - d0.Y);
                                Vf(6, p1.X - d1.X, p1.Y - d1.Y);
                                Tri(3, 7, 6);
                            }
                            else
                            {
                                Vf(4, p1.X + d0.X, p1.Y + d0.Y);
                                Vf(8, p1.X + d1.X, p1.Y + d1.Y);
                                Tri(7, 4, 8);
                            }
                        }
                        else
                        {
                            // Near-180-degree fold: close the gap with a thin square cap.
                            OffsetSlot(6, -n1.Y * fringeWidth, n1.X * fringeWidth);
                            OffsetSlot(8, -n1.Y * fringeWidth, n1.X * fringeWidth);
                            Tri(6, 8, 7);
                        }
                    }
                    else if (pj == JointStyle.Round)
                    {
                        if (sinTheta < 0.0f)
                            PushArc(p1, F(-n0.X, -n0.Y), F(-n1.X, -n1.Y));
                        else
                            PushArc(p1, F(n1.X, n1.Y), F(n0.X, n0.Y));
                    }

                    Commit(6);
                }
            }

            if (closed)
            {
                CopyWindowToStart(b0, 3);
            }
            else
            {
                ThinCap(p0, n0, segLen[count - 1], halfThickness, fringeWidth, -1.0f, endCap);
            }
        }

        private static void ThinQuad()
        {
            Tri(0, 1, 4); Tri(0, 4, 3); Tri(1, 2, 4); Tri(2, 5, 4);
        }

        private static void ThinCap(Float2 p, Float2 n, float segLenSqr, float halfThickness, float fringeWidth, float sign, CapType cap)
        {
            if (cap == CapType.Round)
            {
                PushArc(p, F(n.X * sign, n.Y * sign), F(-n.X * sign, -n.Y * sign));
                return;
            }

            float scale = (cap == CapType.Butt ? 0.5f : 1.0f) * sign;
            if (cap == CapType.Butt)
            {
                float hs2 = halfThickness * halfThickness;
                float s = segLenSqr;
                float t = (s < hs2 ? MathF.Sqrt((float)s) : halfThickness) * scale;
                float dx = n.X * t, dy = n.Y * t;
                OffsetSlot(0, -dy, dx);
                OffsetSlot(1, dy, -dx);
                OffsetSlot(2, -dy, dx);
            }
            else // Square
            {
                float dx = n.X * halfThickness * scale, dy = n.Y * halfThickness * scale;
                OffsetSlot(0, -dy, dx);
                OffsetSlot(2, -dy, dx);
            }

            if (sign > 0.0f) Tri(0, 1, 2);
            else Tri(0, 2, 1);
        }

        #endregion

        #region Thick anti-aliased path

        private static void ThickAntiAliased(List<Float2> pts, bool closed, JointStyle joint, float miterLimit,
            EndCapStyle startCapStyle, EndCapStyle endCapStyle, float coreThickness, float fringeThickness, float fringeWidth)
        {
            int count = pts.Count;
            var normals = Normals;
            var segLen = SegLenSqr;

            JointStyle defaultJoin = joint == JointStyle.Bevel ? JointStyle.Bevel
                : (joint == JointStyle.Round ? JointStyle.Round
                : (joint == JointStyle.MiterClip ? JointStyle.MiterClip : JointStyle.Miter));
            JointStyle defaultJoinLimit = joint == JointStyle.Round ? JointStyle.Round
                : (joint == JointStyle.MiterClip ? JointStyle.MiterClip : JointStyle.Bevel);

            float halfThickness = coreThickness * 0.5f;
            float halfThicknessSqr = halfThickness * halfThickness;
            float miterDistLimit = halfThickness * miterLimit;
            float miterDistLimitSqr = miterDistLimit * miterDistLimit;

            float halfFringe = fringeThickness * 0.5f;
            float fringeMiterDistLimit = halfFringe * miterLimit;
            float fringeMiterDistLimitSqr = fringeMiterDistLimit * fringeMiterDistLimit;

            CapType startCap = MapCap(startCapStyle);
            CapType endCap = MapCap(endCapStyle);

            int b0 = Vtx.Count;
            _base = b0;

            Float2 p0 = pts[closed ? count - 1 : 0];
            Float2 n0 = normals[closed ? count - 1 : 0];

            Vf(0, p0.X - n0.X * halfFringe, p0.Y - n0.Y * halfFringe);
            Vc(1, p0.X - n0.X * halfThickness, p0.Y - n0.Y * halfThickness);
            Vc(2, p0.X + n0.X * halfThickness, p0.Y + n0.Y * halfThickness);
            Vf(3, p0.X + n0.X * halfFringe, p0.Y + n0.Y * halfFringe);

            if (!closed)
                ThickCap(p0, n0, segLen[1], halfThickness, halfFringe, fringeWidth, +1.0f, startCap);

            Float2 p1, n1;
            for (int i = closed ? 0 : 1; i < count; i++, p0 = p1, n0 = n1)
            {
                p1 = pts[i];
                n1 = normals[i];

                float cosTheta = (float)(n0.X * n1.X + n0.Y * n1.Y);
                if (cosTheta > 1f) cosTheta = 1f;
                float miterScale = cosTheta > MiterAngleLimitCos ? 1.0f / (1.0f + cosTheta) : float.MaxValue;
                float miterOffX = (n0.X + n1.X) * halfThickness * miterScale;
                float miterOffY = (n0.Y + n1.Y) * halfThickness * miterScale;
                float miterDistSqr = miterOffX * miterOffX + miterOffY * miterOffY;

                float fringeMiterOffX = (n0.X + n1.X) * halfFringe * miterScale;
                float fringeMiterOffY = (n0.Y + n1.Y) * halfFringe * miterScale;
                float fringeMiterDistSqr = fringeMiterOffX * fringeMiterOffX + fringeMiterOffY * fringeMiterOffY;

                bool overlap = segLen[i] < fringeMiterDistSqr || segLen[i + 1] < fringeMiterDistSqr || cosTheta <= MiterAngleLimitCos;
                bool continuous = closed || i != count - 1;

                JointStyle preferred = JointStyle.Bevel;
                if (continuous)
                {
                    preferred = miterDistSqr > miterDistLimitSqr ? defaultJoinLimit : defaultJoin;
                    if (preferred == JointStyle.MiterClip)
                    {
                        float miterClipMinDistSqr = 0.5f * halfThicknessSqr * (cosTheta + 1);
                        if (miterDistLimitSqr < miterClipMinDistSqr)
                            preferred = JointStyle.Bevel;
                        else if (miterDistSqr > 0 && miterDistSqr < miterDistLimitSqr)
                            preferred = JointStyle.Miter;
                        else if (fringeMiterDistSqr > 0 && fringeMiterDistSqr < fringeMiterDistLimitSqr)
                            preferred = JointStyle.Miter;
                    }
                }
                int joinKind = overlap ? (continuous ? 5 /*ThickButt*/ : 1 /*Butt*/) : ThickJoinKind(preferred);

                if (joinKind == 0 || joinKind == 1) // Miter or Butt (same topology, different vertex placement)
                {
                    if (joinKind == 0)
                    {
                        Vf(4, p1.X - fringeMiterOffX, p1.Y - fringeMiterOffY);
                        Vc(5, p1.X - miterOffX, p1.Y - miterOffY);
                        Vc(6, p1.X + miterOffX, p1.Y + miterOffY);
                        Vf(7, p1.X + fringeMiterOffX, p1.Y + fringeMiterOffY);
                    }
                    else
                    {
                        Vf(4, p1.X - n1.X * halfFringe, p1.Y - n1.Y * halfFringe);
                        Vc(5, p1.X - n1.X * halfThickness, p1.Y - n1.Y * halfThickness);
                        Vc(6, p1.X + n1.X * halfThickness, p1.Y + n1.Y * halfThickness);
                        Vf(7, p1.X + n1.X * halfFringe, p1.Y + n1.Y * halfFringe);
                    }
                    Tri(0, 1, 5); Tri(0, 5, 4); Tri(1, 2, 6); Tri(1, 6, 5); Tri(2, 3, 7); Tri(2, 7, 6);
                    Commit(4);
                }
                else if (joinKind == 2 || joinKind == 3) // Bevel or MiterClip
                {
                    float sinTheta = n0.Y * n1.X - n0.X * n1.Y;
                    Float2 bevelN = Norm(F(n0.X + n1.X, n0.Y + n1.Y));
                    Float2 dir0 = FixNormal(F((n0.X + bevelN.X) * 0.5f, (n0.Y + bevelN.Y) * 0.5f)); dir0 *= (float)fringeWidth;
                    Float2 dir1 = FixNormal(F((n1.X + bevelN.X) * 0.5f, (n1.Y + bevelN.Y) * 0.5f)); dir1 *= (float)fringeWidth;

                    Float2 pt; Float2 d0; Float2 d1;
                    if (joinKind == 2) // Bevel
                    {
                        pt = p1;
                        d0 = F(n0.X * halfThickness, n0.Y * halfThickness);
                        d1 = F(n1.X * halfThickness, n1.Y * halfThickness);
                    }
                    else // MiterClip
                    {
                        ClippedBevelGeometry(p1, n0, bevelN, sinTheta, halfThickness, miterDistLimit, out pt, out d0, out d1);
                    }

                    if (sinTheta < 0.0f)
                    {
                        Vf(4, pt.X - dir0.X - d0.X, pt.Y - dir0.Y - d0.Y);
                        Vc(5, pt.X - d0.X, pt.Y - d0.Y);
                        Vf(6, pt.X - dir1.X - d1.X, pt.Y - dir1.Y - d1.Y);
                        Vc(7, pt.X - d1.X, pt.Y - d1.Y);
                        Vc(8, p1.X + miterOffX, p1.Y + miterOffY);
                        Vf(9, p1.X + fringeMiterOffX, p1.Y + fringeMiterOffY);
                        Tri(0, 1, 5); Tri(0, 5, 4); Tri(1, 2, 8); Tri(1, 8, 5); Tri(2, 3, 9);
                        Tri(2, 9, 8); Tri(5, 8, 7); Tri(4, 5, 7); Tri(4, 7, 6);
                    }
                    else
                    {
                        Vc(4, pt.X + d0.X, pt.Y + d0.Y);
                        Vf(5, pt.X + dir0.X + d0.X, pt.Y + dir0.Y + d0.Y);
                        Vf(6, p1.X - fringeMiterOffX, p1.Y - fringeMiterOffY);
                        Vc(7, p1.X - miterOffX, p1.Y - miterOffY);
                        Vc(8, pt.X + d1.X, pt.Y + d1.Y);
                        Vf(9, pt.X + dir1.X + d1.X, pt.Y + dir1.Y + d1.Y);
                        Tri(0, 1, 7); Tri(0, 7, 6); Tri(1, 2, 4); Tri(1, 4, 7); Tri(2, 3, 5);
                        Tri(2, 5, 4); Tri(7, 4, 8); Tri(4, 5, 9); Tri(4, 9, 8);
                    }
                    Commit(6);
                }
                else if (joinKind == 4) // Round
                {
                    float sinTheta = n0.Y * n1.X - n0.X * n1.Y;
                    if (sinTheta < 0.0f)
                    {
                        PushArc(p1, F(-n0.X, -n0.Y), F(-n1.X, -n1.Y));
                        Vf(4, p1.X - n0.X * halfFringe, p1.Y - n0.Y * halfFringe);
                        Vc(5, p1.X - n0.X * halfThickness, p1.Y - n0.Y * halfThickness);
                        Vc(6, p1.X, p1.Y);
                        Vf(7, p1.X - n1.X * halfFringe, p1.Y - n1.Y * halfFringe);
                        Vc(8, p1.X - n1.X * halfThickness, p1.Y - n1.Y * halfThickness);
                        Vc(9, p1.X + miterOffX, p1.Y + miterOffY);
                        Vf(10, p1.X + fringeMiterOffX, p1.Y + fringeMiterOffY);
                        Tri(0, 1, 5); Tri(0, 5, 4); Tri(1, 2, 9); Tri(1, 9, 5); Tri(5, 9, 6);
                        Tri(2, 3, 10); Tri(2, 10, 9); Tri(6, 9, 8);
                    }
                    else
                    {
                        PushArc(p1, F(n1.X, n1.Y), F(n0.X, n0.Y));
                        Vc(4, p1.X, p1.Y);
                        Vc(5, p1.X + n0.X * halfThickness, p1.Y + n0.Y * halfThickness);
                        Vf(6, p1.X + n0.X * halfFringe, p1.Y + n0.Y * halfFringe);
                        Vf(7, p1.X - fringeMiterOffX, p1.Y - fringeMiterOffY);
                        Vc(8, p1.X - miterOffX, p1.Y - miterOffY);
                        Vc(9, p1.X + n1.X * halfThickness, p1.Y + n1.Y * halfThickness);
                        Vf(10, p1.X + n1.X * halfFringe, p1.Y + n1.Y * halfFringe);
                        Tri(0, 1, 7); Tri(1, 8, 7); Tri(1, 2, 8); Tri(8, 2, 5); Tri(8, 5, 4);
                        Tri(8, 4, 9); Tri(2, 3, 5); Tri(3, 6, 5);
                    }
                    Commit(7);
                }
                else // ThickButt (overlap)
                {
                    ThickButt(p1, n0, n1, cosTheta, sinThetaOf(n0, n1), preferred, miterOffX, miterOffY,
                        fringeMiterOffX, fringeMiterOffY, halfThickness, halfFringe, fringeWidth, miterDistLimit);
                }
            }

            if (closed)
            {
                CopyWindowToStart(b0, 4);
            }
            else
            {
                ThickCap(p0, n0, segLen[count - 1], halfThickness, halfFringe, fringeWidth, -1.0f, endCap);
            }
        }

        private static float sinThetaOf(Float2 n0, Float2 n1) => n0.Y * n1.X - n0.X * n1.Y;

        private static void ThickButt(Float2 p1, Float2 n0, Float2 n1, float cosTheta, float sinTheta,
            JointStyle preferred, float miterOffX, float miterOffY, float fringeMiterOffX, float fringeMiterOffY,
            float halfThickness, float halfFringe, float fringeWidth, float miterDistLimit)
        {
            // Base: end one segment and begin the next, both with butt caps (overlapping cores).
            Vf(4, p1.X - n0.X * halfFringe, p1.Y - n0.Y * halfFringe);
            Vc(5, p1.X - n0.X * halfThickness, p1.Y - n0.Y * halfThickness);
            Vc(6, p1.X + n0.X * halfThickness, p1.Y + n0.Y * halfThickness);
            Vf(7, p1.X + n0.X * halfFringe, p1.Y + n0.Y * halfFringe);
            Vc(8, p1.X, p1.Y);
            Vc(9, p1.X, p1.Y);
            Vc(10, p1.X, p1.Y);
            Vc(11, p1.X, p1.Y);
            Vc(12, p1.X, p1.Y);
            Vf(13, p1.X - n1.X * halfFringe, p1.Y - n1.Y * halfFringe);
            Vc(14, p1.X - n1.X * halfThickness, p1.Y - n1.Y * halfThickness);
            Vc(15, p1.X + n1.X * halfThickness, p1.Y + n1.Y * halfThickness);
            Vf(16, p1.X + n1.X * halfFringe, p1.Y + n1.Y * halfFringe);

            Tri(0, 1, 5); Tri(0, 5, 4); Tri(1, 2, 6); Tri(1, 6, 5); Tri(2, 3, 7); Tri(2, 7, 6);

            if (preferred == JointStyle.Miter)
            {
                if (sinTheta < 0.0f)
                {
                    Vf(8, p1.X - fringeMiterOffX, p1.Y - fringeMiterOffY);
                    Vc(9, p1.X - miterOffX, p1.Y - miterOffY);
                    Tri(5, 14, 10); Tri(5, 14, 9); Tri(4, 5, 9); Tri(4, 9, 8); Tri(9, 13, 8); Tri(9, 14, 13);
                }
                else
                {
                    Vf(8, p1.X + fringeMiterOffX, p1.Y + fringeMiterOffY);
                    Vc(9, p1.X + miterOffX, p1.Y + miterOffY);
                    Tri(10, 6, 9); Tri(10, 9, 15); Tri(6, 7, 8); Tri(6, 8, 9); Tri(9, 8, 15); Tri(15, 8, 16);
                }
            }
            else if (preferred == JointStyle.Bevel || preferred == JointStyle.MiterClip)
            {
                if (cosTheta <= MiterAngleLimitCos)
                {
                    if (preferred == JointStyle.Bevel)
                    {
                        // Near-180-degree fold: thin square cap.
                        Vf(8, p1.X - n1.X * halfFringe - n1.Y * fringeWidth, p1.Y - n1.Y * halfFringe + n1.X * fringeWidth);
                        Vf(9, p1.X + n1.X * halfFringe - n1.Y * fringeWidth, p1.Y + n1.Y * halfFringe + n1.X * fringeWidth);
                        Tri(8, 14, 13); Tri(8, 15, 14); Tri(8, 9, 15); Tri(9, 16, 15);
                    }
                    else
                    {
                        // Clipped square cap.
                        float inner = miterDistLimit;
                        float outer = inner + fringeWidth;
                        Vf(8, p1.X - n1.X * halfFringe - n1.Y * outer, p1.Y - n1.Y * halfFringe + n1.X * outer);
                        Vc(9, p1.X - n1.X * halfThickness - n1.Y * inner, p1.Y - n1.Y * halfThickness + n1.X * inner);
                        Vc(10, p1.X + n1.X * halfThickness - n1.Y * inner, p1.Y + n1.Y * halfThickness + n1.X * inner);
                        Vf(11, p1.X + n1.X * halfFringe - n1.Y * outer, p1.Y + n1.Y * halfFringe + n1.X * outer);
                        Tri(9, 14, 13); Tri(8, 9, 13); Tri(8, 10, 9); Tri(8, 11, 10);
                        Tri(10, 11, 16); Tri(10, 16, 15); Tri(9, 10, 15); Tri(9, 15, 14);
                    }
                }
                else if (preferred == JointStyle.Bevel)
                {
                    Float2 bevelN = Norm(F(n0.X + n1.X, n0.Y + n1.Y));
                    Float2 dir0 = FixNormal(F((n0.X + bevelN.X) * 0.5f, (n0.Y + bevelN.Y) * 0.5f)); dir0 *= (float)fringeWidth;
                    Float2 dir1 = FixNormal(F((n1.X + bevelN.X) * 0.5f, (n1.Y + bevelN.Y) * 0.5f)); dir1 *= (float)fringeWidth;
                    Float2 pt = p1;
                    Float2 d0 = F(n0.X * halfThickness, n0.Y * halfThickness);
                    Float2 d1 = F(n1.X * halfThickness, n1.Y * halfThickness);

                    if (sinTheta < 0.0f)
                    {
                        Vf(8, pt.X - dir0.X - d0.X, pt.Y - dir0.Y - d0.Y);
                        Vf(9, pt.X - dir1.X - d1.X, pt.Y - dir1.Y - d1.Y);
                        Tri(5, 10, 14); Tri(5, 8, 4); Tri(9, 14, 13); Tri(5, 14, 9); Tri(5, 9, 8);
                    }
                    else
                    {
                        Vf(8, pt.X + dir0.X + d0.X, pt.Y + dir0.Y + d0.Y);
                        Vf(9, pt.X + dir1.X + d1.X, pt.Y + dir1.Y + d1.Y);
                        Tri(6, 15, 10); Tri(6, 7, 8); Tri(9, 16, 15); Tri(6, 8, 9); Tri(6, 9, 15);
                    }
                }
                else // MiterClip
                {
                    Float2 bevelN = Norm(F(n0.X + n1.X, n0.Y + n1.Y));
                    Float2 dir0 = FixNormal(F((n0.X + bevelN.X) * 0.5f, (n0.Y + bevelN.Y) * 0.5f)); dir0 *= (float)fringeWidth;
                    Float2 dir1 = FixNormal(F((n1.X + bevelN.X) * 0.5f, (n1.Y + bevelN.Y) * 0.5f)); dir1 *= (float)fringeWidth;
                    ClippedBevelGeometry(p1, n0, bevelN, sinTheta, halfThickness, miterDistLimit, out Float2 pt, out Float2 d0, out Float2 d1);

                    if (sinTheta < 0.0f)
                    {
                        Vf(8, pt.X - dir0.X - d0.X, pt.Y - dir0.Y - d0.Y);
                        Vc(9, pt.X - d0.X, pt.Y - d0.Y);
                        Vf(10, pt.X - dir1.X - d1.X, pt.Y - dir1.Y - d1.Y);
                        Vc(11, pt.X - d1.X, pt.Y - d1.Y);
                        Tri(12, 14, 11); Tri(12, 11, 9); Tri(12, 9, 5); Tri(5, 9, 8); Tri(5, 8, 4);
                        Tri(14, 13, 10); Tri(14, 10, 11); Tri(8, 9, 11); Tri(8, 11, 10);
                    }
                    else
                    {
                        Vf(8, pt.X + dir0.X + d0.X, pt.Y + dir0.Y + d0.Y);
                        Vc(9, pt.X + d0.X, pt.Y + d0.Y);
                        Vf(10, pt.X + dir1.X + d1.X, pt.Y + dir1.Y + d1.Y);
                        Vc(11, pt.X + d1.X, pt.Y + d1.Y);
                        Tri(12, 6, 9); Tri(12, 9, 11); Tri(12, 11, 15); Tri(6, 7, 8); Tri(6, 8, 9);
                        Tri(15, 11, 10); Tri(15, 10, 16); Tri(11, 9, 8); Tri(11, 8, 10);
                    }
                }
            }
            else if (preferred == JointStyle.Round)
            {
                if (sinTheta < 0.0f)
                    PushArc(p1, F(-n0.X, -n0.Y), F(-n1.X, -n1.Y));
                else
                    PushArc(p1, F(n1.X, n1.Y), F(n0.X, n0.Y));
            }

            Commit(13);
        }

        private static void ClippedBevelGeometry(Float2 p1, Float2 n0, Float2 bevelN, float sinTheta,
            float thickness, float limit, out Float2 pt, out Float2 d0, out Float2 d1)
        {
            float signedLimit = sinTheta < 0.0f ? limit : -limit;
            float denom = n0.Y * bevelN.X - n0.X * bevelN.Y;
            float offset = denom != 0.0f
                ? (n0.X * (bevelN.X * limit - n0.X * thickness) + n0.Y * (bevelN.Y * limit - n0.Y * thickness)) / denom
                : 0.0f;

            pt = F(p1.X - bevelN.X * signedLimit, p1.Y - bevelN.Y * signedLimit);
            d0 = F(offset * bevelN.Y, -offset * bevelN.X);
            d1 = F(-offset * bevelN.Y, offset * bevelN.X);
        }

        private static void ThickCap(Float2 p, Float2 n, float segLenSqr, float halfThickness, float halfFringe, float fringeWidth, float sign, CapType cap)
        {
            if (cap == CapType.Round)
            {
                PushArc(p, F(n.X * sign, n.Y * sign), F(-n.X * sign, -n.Y * sign));
                return;
            }

            float scale = (cap == CapType.Butt ? 0.5f : 1.0f) * sign;
            if (cap == CapType.Butt)
            {
                float fw2 = fringeWidth * fringeWidth;
                float s = segLenSqr;
                float t = (s < fw2 ? MathF.Sqrt((float)s) : fringeWidth) * scale;
                float dx = n.X * t, dy = n.Y * t;
                OffsetSlot(0, -dy, dx);
                OffsetSlot(1, dy, -dx);
                OffsetSlot(2, dy, -dx);
                OffsetSlot(3, -dy, dx);
            }
            else // Square
            {
                float dix = n.X * halfThickness * scale, diy = n.Y * halfThickness * scale;
                float dox = n.X * halfFringe * scale, doy = n.Y * halfFringe * scale;
                OffsetSlot(0, -doy, dox);
                OffsetSlot(1, -diy, dix);
                OffsetSlot(2, -diy, dix);
                OffsetSlot(3, -doy, dox);
            }

            if (sign > 0.0f) { Tri(0, 1, 3); Tri(1, 2, 3); }
            else { Tri(0, 3, 1); Tri(1, 3, 2); }
        }

        #endregion

        // Copy the final sliding-window cross-section back onto the very first vertices so a closed
        // polyline's seam joins seamlessly (positions only; coverage/colour are unchanged).
        private static void CopyWindowToStart(int startBase, int slotCount)
        {
            for (int k = 0; k < slotCount; k++)
            {
                var src = Vtx[_base + k];
                var dst = Vtx[startBase + k];
                dst.x = src.x;
                dst.y = src.y;
                Vtx[startBase + k] = dst;
            }
        }

        private static int JoinKind(JointStyle s)
        {
            switch (s)
            {
                case JointStyle.Miter: return 0;
                case JointStyle.Bevel: return 2;
                case JointStyle.Round: return 3;
                default: return 0; // MiterClip degrades to Miter in the thin path
            }
        }

        private static int ThickJoinKind(JointStyle s)
        {
            switch (s)
            {
                case JointStyle.Miter: return 0;
                case JointStyle.Bevel: return 2;
                case JointStyle.MiterClip: return 3;
                case JointStyle.Round: return 4;
                default: return 0;
            }
        }
    }
}
