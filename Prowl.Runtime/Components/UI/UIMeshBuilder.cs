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

        // Positions (Z=0 — model matrix supplies world Z)
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
        // Starting angles for each corner (TL=180°, TR=270°, BR=0°, BL=90°).
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
    /// Adds a nine-slice quad: <paramref name="outer"/> is the on-screen rectangle,
    /// <paramref name="inner"/> is the unstretched center region in *pixel* offsets
    /// from the outer rect's edges (left, top, right, bottom). UV space is 0–1 for
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

    // ---------- Bake / Reset ----------

    /// <summary>
    /// Writes the accumulated buffers into <paramref name="m"/>, recalculates bounds,
    /// and uploads to the GPU. After Bake the builder is *not* automatically reset —
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
