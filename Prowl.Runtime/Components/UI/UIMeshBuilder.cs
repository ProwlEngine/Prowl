// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.UI;

public sealed class UIMeshBuilder
{
    // ---------- Storage ----------
    private readonly List<Float3>  _verts   = new(64);
    private readonly List<Float2>  _uvs     = new(64);
    private readonly List<Color32> _colors  = new(64);
    private readonly List<uint>    _indices = new(96);

    public int  VertexCount => _verts.Count;
    public int  IndexCount  => _indices.Count;
    public bool IsEmpty     => _verts.Count == 0;

    // ---------- Primitives ----------

    /// <summary>
    /// Adds a textured quad spanning <paramref name="r"/> in canvas-local pixel space.
    /// Vertex order: TL, TR, BR, BL (counter-clockwise when looking down -Z, matching
    /// <see cref="Mesh.GetFullscreenQuad"/>'s winding so backface culling stays consistent).
    /// </summary>
    public void AddQuad(Rect r, Color tint, Float2 uv0, Float2 uv1)
    {
        uint baseIdx = (uint)_verts.Count;
        var c = (Color32)tint;

        // Positions (Z=0 - model matrix supplies world Z)
        _verts.Add(new Float3(r.Min.X, r.Min.Y, 0));   // TL
        _verts.Add(new Float3(r.Max.X, r.Min.Y, 0));   // TR
        _verts.Add(new Float3(r.Max.X, r.Max.Y, 0));   // BR
        _verts.Add(new Float3(r.Min.X, r.Max.Y, 0));   // BL

        // UVs follow the rect's aspect from (uv0) at TL to (uv1) at BR
        _uvs.Add(new Float2(uv0.X, uv0.Y));
        _uvs.Add(new Float2(uv1.X, uv0.Y));
        _uvs.Add(new Float2(uv1.X, uv1.Y));
        _uvs.Add(new Float2(uv0.X, uv1.Y));

        _colors.Add(c); _colors.Add(c); _colors.Add(c); _colors.Add(c);

        // Two triangles: (TL, BR, TR) and (TL, BL, BR)
        _indices.Add(baseIdx + 0); _indices.Add(baseIdx + 2); _indices.Add(baseIdx + 1);
        _indices.Add(baseIdx + 0) ;_indices.Add(baseIdx + 3); _indices.Add(baseIdx + 2);
    }

    // ---------- Raw triangle-list append ----------
    // Used to bridge Scribe's DrawQuads output (per-vertex position/uv/colour + an index list) into
    // the mesh, so text rendering reuses Scribe's geometry instead of re-deriving it. Colour is kept
    // per vertex so multi-colour rich text works, not just single-colour text.

    /// <summary>Index the next appended vertex will get; add this to Scribe's local indices.</summary>
    public uint NextVertex => (uint)_verts.Count;

    /// <summary>Append one vertex (position in local pixel space, atlas UV, colour).</summary>
    public void AddVertex(Float3 position, Float2 uv, Color32 color)
    {
        _verts.Add(position);
        _uvs.Add(uv);
        _colors.Add(color);
    }

    /// <summary>Append one triangle index (already offset by <see cref="NextVertex"/> at append time).</summary>
    public void AddIndex(uint index) => _indices.Add(index);

    /// <summary>
    /// Tessellates a rounded rectangle by replacing each corner with an arc of
    /// <paramref name="cornerSegments"/> triangles. The result is a triangle fan
    /// from the rect's center. Use <paramref name="radius"/> = 0 for a sharp rect
    /// (callers should prefer <see cref="AddQuad"/> in that case).
    /// </summary>
    public unsafe void AddRoundedRect(Rect r, float radius, Color tint, int cornerSegments = 6)
    {
        radius = Maths.Min(radius, Maths.Min(r.Size.X, r.Size.Y) * 0.5f);
        if (radius <= 0.5f) { AddQuad(r, tint, Float2.Zero, Float2.One); return; }

        Float2 center = (r.Min + r.Max) * 0.5f;
        var c = (Color32)tint;

        uint centerIdx = (uint)_verts.Count;
        _verts.Add(new Float3(center.X, center.Y, 0));
        _uvs.Add(new Float2(0.5f, 0.5f));
        _colors.Add(c);

        // Generate perimeter vertices clockwise starting at top-left arc.
        // Each corner contributes (cornerSegments + 1) points.
        var corners = stackalloc Float2[4]
        {
            new Float2(r.Min.X + radius, r.Min.Y + radius),  // TL arc center
            new Float2(r.Max.X - radius, r.Min.Y + radius),  // TR arc center
            new Float2(r.Max.X - radius, r.Max.Y - radius),  // BR arc center
            new Float2(r.Min.X + radius, r.Max.Y - radius),  // BL arc center
        };
        // Starting angles for each corner (TL=180 deg, TR=270 deg, BR=0 deg, BL=90 deg).
        ReadOnlySpan<float> startAngles = stackalloc float[] { MathF.PI, 1.5f * MathF.PI, 0f, 0.5f * MathF.PI };

        uint firstPerimeter = (uint)_verts.Count;
        for (int corner = 0; corner < 4; corner++)
        {
            for (int s = 0; s <= cornerSegments; s++)
            {
                float a = startAngles[corner] + (MathF.PI * 0.5f) * (s / (float)cornerSegments);
                Float2 p = corners[corner] + new Float2(MathF.Cos(a), MathF.Sin(a)) * radius;
                _verts.Add(new Float3(p.X, p.Y, 0));
                // UV (0,0) at the rect's bottom-left, (1,1) at its top-right (+Y up).
                _uvs.Add(new Float2((p.X - r.Min.X) / r.Size.X, (p.Y - r.Min.Y) / r.Size.Y));
                _colors.Add(c);
            }
        }
        uint perimCount = (uint)_verts.Count - firstPerimeter;

        // Triangle fan: (center, p[i], p[(i+1) % perim])
        for (uint i = 0; i < perimCount; i++)
        {
            _indices.Add(centerIdx);
            _indices.Add(firstPerimeter + i);
            _indices.Add(firstPerimeter + (i + 1) % perimCount);
        }
    }

    /// <summary>
    /// Tiles a texture across <paramref name="outer"/> using tiles of <paramref name="tileSize"/>
    /// in design pixels. Partial tiles at the right/top edges get UV-clipped so the texture
    /// is never visually stretched. Falls back to a single quad if the tile size is invalid.
    /// </summary>
    public void AddTiled(Rect outer, Float2 tileSize, Color tint)
    {
        if (tileSize.X <= 0 || tileSize.Y <= 0)
        {
            AddQuad(outer, tint, Float2.Zero, Float2.One);
            return;
        }

        int tilesX = (int)MathF.Ceiling(outer.Size.X / tileSize.X);
        int tilesY = (int)MathF.Ceiling(outer.Size.Y / tileSize.Y);
        if (tilesX <= 0 || tilesY <= 0) return;

        for (int ty = 0; ty < tilesY; ty++)
        {
            float y0 = outer.Min.Y + ty * tileSize.Y;
            float y1 = MathF.Min(y0 + tileSize.Y, outer.Max.Y);
            float v1 = (y1 - y0) / tileSize.Y;
            for (int tx = 0; tx < tilesX; tx++)
            {
                float x0 = outer.Min.X + tx * tileSize.X;
                float x1 = MathF.Min(x0 + tileSize.X, outer.Max.X);
                float u1 = (x1 - x0) / tileSize.X;
                AddQuad(new Rect(x0, y0, x1, y1), tint, Float2.Zero, new Float2(u1, v1));
            }
        }
    }

    /// <summary>
    /// Emits a triangle fan that fills a fraction of <paramref name="r"/> per the image fill
    /// modes. Horizontal/Vertical clip the rect on one axis; Radial90/180/360 sweep an
    /// arc from a corner / edge-midpoint / center. <paramref name="amount"/> is clamped to
    /// [0,1]. UV is mapped 1:1 to the original rect so the texture appears un-stretched.
    /// </summary>
    public void AddFilled(Rect r, Color tint, UI.FillMethod method, int origin, float amount, bool clockwise)
    {
        amount = Maths.Clamp(amount, 0f, 1f);
        if (amount <= 0f) return;
        if (amount >= 1f) { AddQuad(r, tint, Float2.Zero, Float2.One); return; }

        if (method == UI.FillMethod.Horizontal || method == UI.FillMethod.Vertical)
        {
            AddFilledLinear(r, tint, method, origin, amount);
            return;
        }
        AddFilledRadial(r, tint, method, origin, amount, clockwise);
    }

    private void AddFilledLinear(Rect r, Color tint, UI.FillMethod method, int origin, float amount)
    {
        if (method == UI.FillMethod.Horizontal)
        {
            float w = r.Size.X * amount;
            if (origin == 0) // Left
            {
                AddQuad(new Rect(r.Min.X, r.Min.Y, r.Min.X + w, r.Max.Y), tint,
                        Float2.Zero, new Float2(amount, 1f));
            }
            else // Right
            {
                AddQuad(new Rect(r.Max.X - w, r.Min.Y, r.Max.X, r.Max.Y), tint,
                        new Float2(1f - amount, 0f), Float2.One);
            }
        }
        else // Vertical
        {
            float h = r.Size.Y * amount;
            if (origin == 0) // Bottom (+Y up: Min.Y is the bottom edge)
            {
                AddQuad(new Rect(r.Min.X, r.Min.Y, r.Max.X, r.Min.Y + h), tint,
                        Float2.Zero, new Float2(1f, amount));
            }
            else // Top
            {
                AddQuad(new Rect(r.Min.X, r.Max.Y - h, r.Max.X, r.Max.Y), tint,
                        new Float2(0f, 1f - amount), Float2.One);
            }
        }
    }

    private void AddFilledRadial(Rect r, Color tint, UI.FillMethod method, int origin, float amount, bool clockwise)
    {
        Float2 pivot;
        float startAngle;
        float totalAngle;

        // All angles use math convention (Y-up): 0 = +X, PI/2 = +Y. Default sweep is CCW (counter-clockwise)
        // and the `clockwise` flag negates the direction.
        switch (method)
        {
            case UI.FillMethod.Radial90:
                totalAngle = MathF.PI * 0.5f;
                switch (origin)
                {
                    case 0: pivot = new Float2(r.Min.X, r.Min.Y); startAngle = 0f; break;                  // BottomLeft
                    case 1: pivot = new Float2(r.Min.X, r.Max.Y); startAngle = -MathF.PI * 0.5f; break;   // TopLeft
                    case 2: pivot = new Float2(r.Max.X, r.Max.Y); startAngle = MathF.PI; break;           // TopRight
                    case 3: pivot = new Float2(r.Max.X, r.Min.Y); startAngle = MathF.PI * 0.5f; break;    // BottomRight
                    default: return;
                }
                break;
            case UI.FillMethod.Radial180:
                totalAngle = MathF.PI;
                switch (origin)
                {
                    case 0: pivot = new Float2((r.Min.X + r.Max.X) * 0.5f, r.Min.Y); startAngle = 0f; break;                 // Bottom
                    case 1: pivot = new Float2(r.Min.X, (r.Min.Y + r.Max.Y) * 0.5f); startAngle = -MathF.PI * 0.5f; break;  // Left
                    case 2: pivot = new Float2((r.Min.X + r.Max.X) * 0.5f, r.Max.Y); startAngle = MathF.PI; break;          // Top
                    case 3: pivot = new Float2(r.Max.X, (r.Min.Y + r.Max.Y) * 0.5f); startAngle = MathF.PI * 0.5f; break;   // Right
                    default: return;
                }
                break;
            case UI.FillMethod.Radial360:
                totalAngle = MathF.PI * 2f;
                pivot = (r.Min + r.Max) * 0.5f;
                switch (origin)
                {
                    case 0: startAngle = -MathF.PI * 0.5f; break; // Bottom
                    case 1: startAngle = 0f; break;               // Right
                    case 2: startAngle = MathF.PI * 0.5f; break;  // Top
                    case 3: startAngle = MathF.PI; break;         // Left
                    default: return;
                }
                break;
            default: return;
        }

        // For bounded wedges (Radial90/180), `startAngle` sits at one edge of the wedge so the
        // default CCW sweep walks inward. To go clockwise we have to start at the *other* edge,
        // otherwise the first ray immediately exits the rect. (For Radial360 totalAngle is 2PI,
        // so this shift is angle-equivalent and harmless.)
        if (clockwise) startAngle += totalAngle;

        float sweep = totalAngle * amount;
        float dir = clockwise ? -1f : 1f;
        const float twoPi = MathF.PI * 2f;
        const float eps = 1e-4f;

        // The rect's perimeter is piecewise-linear, so a geometrically exact fan only needs the
        // two sweep endpoints plus each rect corner that lies strictly inside the wedge - that's
        // what prevents the corners from being shaved off by a diagonal triangle edge.
        Span<float> stops = stackalloc float[6];
        int stopCount = 0;
        stops[stopCount++] = 0f;

        Span<Float2> corners = stackalloc Float2[]
        {
            new Float2(r.Min.X, r.Min.Y),
            new Float2(r.Max.X, r.Min.Y),
            new Float2(r.Max.X, r.Max.Y),
            new Float2(r.Min.X, r.Max.Y),
        };
        for (int i = 0; i < 4; i++)
        {
            Float2 v = corners[i] - pivot;
            if (MathF.Abs(v.X) < eps && MathF.Abs(v.Y) < eps) continue; // pivot sits on this corner
            float rel = (MathF.Atan2(v.Y, v.X) - startAngle) * dir;
            rel -= twoPi * MathF.Floor(rel / twoPi); // normalize to [0, 2*PI)
            if (rel > eps && rel < sweep - eps)
                stops[stopCount++] = rel;
        }
        stops[stopCount++] = sweep;

        // Insertion sort (<= 6 entries).
        for (int i = 1; i < stopCount; i++)
        {
            float v = stops[i];
            int j = i - 1;
            while (j >= 0 && stops[j] > v) { stops[j + 1] = stops[j]; j--; }
            stops[j + 1] = v;
        }

        var c = (Color32)tint;
        Float2 invSize = new Float2(1f / r.Size.X, 1f / r.Size.Y);

        uint pivotIdx = (uint)_verts.Count;
        _verts.Add(new Float3(pivot.X, pivot.Y, 0f));
        _uvs.Add(new Float2((pivot.X - r.Min.X) * invSize.X, (pivot.Y - r.Min.Y) * invSize.Y));
        _colors.Add(c);

        uint firstPerim = (uint)_verts.Count;
        for (int i = 0; i < stopCount; i++)
        {
            float a = startAngle + dir * stops[i];
            Float2 p = RayToRectEdge(pivot, a, r);
            _verts.Add(new Float3(p.X, p.Y, 0f));
            _uvs.Add(new Float2((p.X - r.Min.X) * invSize.X, (p.Y - r.Min.Y) * invSize.Y));
            _colors.Add(c);
        }

        // Flip winding when sweeping clockwise so triangles keep the same orientation as AddQuad.
        for (int i = 0; i < stopCount - 1; i++)
        {
            _indices.Add(pivotIdx);
            if (clockwise)
            {
                _indices.Add(firstPerim + (uint)i + 1);
                _indices.Add(firstPerim + (uint)i);
            }
            else
            {
                _indices.Add(firstPerim + (uint)i);
                _indices.Add(firstPerim + (uint)i + 1);
            }
        }
    }

    // Intersect a ray from an interior/edge point of <paramref name="r"/> with the rect's edges.
    private static Float2 RayToRectEdge(Float2 origin, float angle, Rect r)
    {
        float cx = MathF.Cos(angle);
        float cy = MathF.Sin(angle);
        float tx = float.PositiveInfinity;
        float ty = float.PositiveInfinity;
        if (cx > 1e-6f)       tx = (r.Max.X - origin.X) / cx;
        else if (cx < -1e-6f) tx = (r.Min.X - origin.X) / cx;
        if (cy > 1e-6f)       ty = (r.Max.Y - origin.Y) / cy;
        else if (cy < -1e-6f) ty = (r.Min.Y - origin.Y) / cy;
        float t = MathF.Min(tx, ty);
        return new Float2(origin.X + cx * t, origin.Y + cy * t);
    }

    /// <summary>
    /// Adds a nine-slice quad: <paramref name="outer"/> is the on-screen rectangle,
    /// <paramref name="inner"/> is the unstretched center region in *pixel* offsets
    /// from the outer rect's edges (left, top, right, bottom). UV space is 0-1 for
    /// the source texture, with the same four borders given as normalized fractions
    /// in <paramref name="uvBorders"/>.
    /// </summary>
    public void AddNineSlice(Rect outer, Float4 inner, Float4 uvBorders, Color tint)
    {
        // Compute the 4 horizontal x-positions and 4 vertical y-positions.
        // inner / uvBorders are ordered (left, top, right, bottom). +Y is up, so the
        // bottom border (W) sits next to Min.Y and the top border (Y) next to Max.Y.
        float x0 = outer.Min.X, x3 = outer.Max.X;
        float x1 = x0 + inner.X, x2 = x3 - inner.Z;
        float y0 = outer.Min.Y, y3 = outer.Max.Y;
        float y1 = y0 + inner.W, y2 = y3 - inner.Y;

        float ux0 = 0,           ux3 = 1;
        float ux1 = uvBorders.X, ux2 = 1 - uvBorders.Z;
        float uy0 = 0,           uy3 = 1;
        float uy1 = uvBorders.W, uy2 = 1 - uvBorders.Y;

        // Emit 9 quads via AddQuad (the redundant vertices cost ~36 floats per element;
        // acceptable for current scale, and keeps this method trivial).
        ReadOnlySpan<float> xs = stackalloc[] { x0, x1, x2, x3 };
        ReadOnlySpan<float> ys = stackalloc[] { y0, y1, y2, y3 };
        ReadOnlySpan<float> us = stackalloc[] { ux0, ux1, ux2, ux3 };
        ReadOnlySpan<float> vs = stackalloc[] { uy0, uy1, uy2, uy3 };
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 3; col++)
        {
            var sliceRect = new Rect(xs[col], ys[row], xs[col + 1], ys[row + 1]);
            AddQuad(sliceRect, tint,
                    new Float2(us[col],     vs[row]),
                    new Float2(us[col + 1], vs[row + 1]));
        }
    }

    // ---------- Rounded-corner clipping ----------

    // Reusable buffers for the source snapshot taken by ClipToRoundedRect. Cleared on each call.
    private static readonly List<Float3>  s_clipSrcVerts   = new(64);
    private static readonly List<Float2>  s_clipSrcUVs     = new(64);
    private static readonly List<Color32> s_clipSrcColors  = new(64);
    private static readonly List<uint>    s_clipSrcIndices = new(96);

    /// <summary>
    /// Intersects the currently-accumulated geometry with a rounded rectangle (Sutherland-Hodgman
    /// against a CCW polygon that has <paramref name="cornerSegments"/> straight edges per corner arc).
    /// Each source triangle is clipped independently; per-vertex UVs and colors are linearly
    /// interpolated at the clip intersections so any fill mode (sliced / tiled / filled) keeps its
    /// shading. Triangles fully inside take a fast path; triangles fully outside are dropped.
    /// </summary>
    public void ClipToRoundedRect(Rect rect, float radius, int cornerSegments = 6)
    {
        radius = MathF.Min(radius, MathF.Min(rect.Size.X, rect.Size.Y) * 0.5f);
        if (radius <= 0.5f || _indices.Count == 0) return;

        int clipCount = 4 * (cornerSegments + 1);
        Span<Float2> clip = stackalloc Float2[clipCount];
        BuildRoundedRectClipPolygon(rect, radius, cornerSegments, clip);

        // Snapshot current geometry; we re-emit clipped triangles into the (cleared) builder.
        s_clipSrcVerts.Clear();   s_clipSrcVerts.AddRange(_verts);
        s_clipSrcUVs.Clear();     s_clipSrcUVs.AddRange(_uvs);
        s_clipSrcColors.Clear();  s_clipSrcColors.AddRange(_colors);
        s_clipSrcIndices.Clear(); s_clipSrcIndices.AddRange(_indices);
        _verts.Clear(); _uvs.Clear(); _colors.Clear(); _indices.Clear();

        // Double-buffered workspace for S-H. A triangle clipped against `clipCount` edges has at most
        // (3 + clipCount) vertices, so 48 covers cornerSegments up to 11.
        const int Cap = 48;
        Span<Float3> pA = stackalloc Float3[Cap], pB = stackalloc Float3[Cap];
        Span<Float2> uA = stackalloc Float2[Cap], uB = stackalloc Float2[Cap];
        Span<Color32> cA = stackalloc Color32[Cap], cB = stackalloc Color32[Cap];

        int triCount = s_clipSrcIndices.Count;
        for (int t = 0; t < triCount; t += 3)
        {
            int i0 = (int)s_clipSrcIndices[t];
            int i1 = (int)s_clipSrcIndices[t + 1];
            int i2 = (int)s_clipSrcIndices[t + 2];

            pA[0] = s_clipSrcVerts[i0]; uA[0] = s_clipSrcUVs[i0]; cA[0] = s_clipSrcColors[i0];
            pA[1] = s_clipSrcVerts[i1]; uA[1] = s_clipSrcUVs[i1]; cA[1] = s_clipSrcColors[i1];
            pA[2] = s_clipSrcVerts[i2]; uA[2] = s_clipSrcUVs[i2]; cA[2] = s_clipSrcColors[i2];
            int polyCount = 3;

            Span<Float3> inP = pA, outP = pB;
            Span<Float2> inU = uA, outU = uB;
            Span<Color32> inC = cA, outC = cB;

            for (int e = 0; e < clipCount; e++)
            {
                if (polyCount == 0) break;
                Float2 ea = clip[e];
                Float2 eb = clip[(e + 1) % clipCount];
                float edx = eb.X - ea.X;
                float edy = eb.Y - ea.Y;

                int outCount = 0;
                int prev = polyCount - 1;
                // Signed area (= 2D cross) of (edge, prev-vertex). >= 0 means prev is on the inside of the CCW edge.
                float prevS = edx * (inP[prev].Y - ea.Y) - edy * (inP[prev].X - ea.X);
                bool prevIn = prevS >= 0f;

                for (int i = 0; i < polyCount; i++)
                {
                    float curS = edx * (inP[i].Y - ea.Y) - edy * (inP[i].X - ea.X);
                    bool curIn = curS >= 0f;

                    if (curIn)
                    {
                        if (!prevIn)
                        {
                            float ti = prevS / (prevS - curS);
                            outP[outCount] = LerpFloat3(inP[prev], inP[i], ti);
                            outU[outCount] = LerpFloat2(inU[prev], inU[i], ti);
                            outC[outCount] = LerpColor32(inC[prev], inC[i], ti);
                            outCount++;
                        }
                        outP[outCount] = inP[i];
                        outU[outCount] = inU[i];
                        outC[outCount] = inC[i];
                        outCount++;
                    }
                    else if (prevIn)
                    {
                        float ti = prevS / (prevS - curS);
                        outP[outCount] = LerpFloat3(inP[prev], inP[i], ti);
                        outU[outCount] = LerpFloat2(inU[prev], inU[i], ti);
                        outC[outCount] = LerpColor32(inC[prev], inC[i], ti);
                        outCount++;
                    }
                    prev = i;
                    prevS = curS;
                    prevIn = curIn;
                }

                polyCount = outCount;
                var tmpP = inP; inP = outP; outP = tmpP;
                var tmpU = inU; inU = outU; outU = tmpU;
                var tmpC = inC; inC = outC; outC = tmpC;
            }

            if (polyCount < 3) continue;

            uint baseIdx = (uint)_verts.Count;
            for (int i = 0; i < polyCount; i++)
            {
                _verts.Add(inP[i]);
                _uvs.Add(inU[i]);
                _colors.Add(inC[i]);
            }
            // Fan-triangulate the convex clipped polygon. Winding follows the source triangle (CCW).
            for (int i = 1; i < polyCount - 1; i++)
            {
                _indices.Add(baseIdx);
                _indices.Add(baseIdx + (uint)i);
                _indices.Add(baseIdx + (uint)i + 1);
            }
        }
    }

    private static void BuildRoundedRectClipPolygon(Rect rect, float radius, int segments, Span<Float2> output)
    {
        // Traverse CCW (Y-up): bottom-right arc -> up to TR arc -> left to TL arc -> down to BL arc.
        // Each arc emits `segments + 1` points; the straight edges of the rect are the chords
        // between consecutive arcs (polygon-edges between the last point of one and the first of the next).
        int idx = 0;
        AppendArc(output, ref idx, new Float2(rect.Max.X - radius, rect.Min.Y + radius), radius, -MathF.PI * 0.5f, segments); // BR
        AppendArc(output, ref idx, new Float2(rect.Max.X - radius, rect.Max.Y - radius), radius, 0f, segments);                // TR
        AppendArc(output, ref idx, new Float2(rect.Min.X + radius, rect.Max.Y - radius), radius, MathF.PI * 0.5f, segments);   // TL
        AppendArc(output, ref idx, new Float2(rect.Min.X + radius, rect.Min.Y + radius), radius, MathF.PI, segments);          // BL
    }

    private static void AppendArc(Span<Float2> dst, ref int idx, Float2 center, float radius, float startAngle, int segments)
    {
        float step = MathF.PI * 0.5f / segments;
        for (int s = 0; s <= segments; s++)
        {
            float a = startAngle + step * s;
            dst[idx++] = new Float2(center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius);
        }
    }

    private static Float3 LerpFloat3(in Float3 a, in Float3 b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    private static Float2 LerpFloat2(in Float2 a, in Float2 b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static Color32 LerpColor32(Color32 a, Color32 b, float t)
        => new(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)(a.A + (b.A - a.A) * t));

    // ---------- Bake / Reset ----------

    /// <summary>
    /// Writes the accumulated buffers into <paramref name="m"/>, recalculates bounds,
    /// and uploads to the GPU. After Bake the builder is *not* automatically reset -
    /// the canvas is responsible for calling <see cref="Return"/> which triggers a Reset.
    /// </summary>
    internal void Bake(Mesh m)
    {
        // Use ToArray() once per attribute. Engine `Mesh` setters validate length
        // consistency and (re)allocate GPU buffers when sizes change.
        m.Vertices = _verts.ToArray();
        m.UV       = _uvs.ToArray();
        m.Colors32 = _colors.ToArray();
        m.Indices  = _indices.ToArray();
        m.RecalculateBounds();   // populates `mesh.bounds` (lowercase field)
        m.Upload();
    }

    internal void Reset()
    {
        _verts.Clear(); _uvs.Clear(); _colors.Clear(); _indices.Clear();
    }

    // ---------- Pooling ----------

    private static readonly Stack<UIMeshBuilder> s_pool = new();
    public static UIMeshBuilder Rent() => s_pool.Count > 0 ? s_pool.Pop() : new UIMeshBuilder();
    public static void Return(UIMeshBuilder b) { b.Reset(); s_pool.Push(b); }
}
