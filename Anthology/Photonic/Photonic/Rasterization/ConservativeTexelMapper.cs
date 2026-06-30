using Prowl.Vector;

namespace Prowl.Photonic.Rasterization;

/// <summary>
/// Walks every triangle in atlas UV space, marks the pixels it conservatively covers, and fills
/// the per-texel world-space sample (position, normal, source material).
/// </summary>
/// <remarks>
/// Conservative = a pixel is marked covered whenever the triangle touches its 1x1 square in any
/// way, even by a sub-pixel sliver. This is what stops seams from cracking. The clipping check
/// is the same triangle/AABB SAT test used in software rasterisers for conservative coverage.
/// </remarks>
internal static class ConservativeTexelMapper
{
    public static void MapTriangle(TargetWorkspace ws, BakeInstance instance, int instanceIndex,
                                   Float3 v0L, Float3 v1L, Float3 v2L,
                                   Float3 n0, Float3 n1, Float3 n2,
                                   Float2 uvBake0, Float2 uvBake1, Float2 uvBake2,
                                   Float2 uv0_0, Float2 uv1_0, Float2 uv2_0,
                                   int materialGroupIndex)
    {
        // map bake UVs into pixel space: apply per-instance offset + scale into [0,1], then multiply by W,H.
        var offset = instance.UVOffset;
        var scale = instance.UVScale;
        Float2 p0 = new Float2((uvBake0.X * scale.X + offset.X) * ws.Width,
                               (uvBake0.Y * scale.Y + offset.Y) * ws.Height);
        Float2 p1 = new Float2((uvBake1.X * scale.X + offset.X) * ws.Width,
                               (uvBake1.Y * scale.Y + offset.Y) * ws.Height);
        Float2 p2 = new Float2((uvBake2.X * scale.X + offset.X) * ws.Width,
                               (uvBake2.Y * scale.Y + offset.Y) * ws.Height);

        // pixel-space bounding box, expanded by half a pixel for conservative coverage
        float minX = System.Math.Min(p0.X, System.Math.Min(p1.X, p2.X)) - 0.5f;
        float minY = System.Math.Min(p0.Y, System.Math.Min(p1.Y, p2.Y)) - 0.5f;
        float maxX = System.Math.Max(p0.X, System.Math.Max(p1.X, p2.X)) + 0.5f;
        float maxY = System.Math.Max(p0.Y, System.Math.Max(p1.Y, p2.Y)) + 0.5f;

        // Defensive: if a triangle's UV1 puts it absurdly outside the atlas (typical when callers
        // accidentally fed in texture-repeating UV0 as the bake layer), skip it instead of iterating
        // hundreds of thousands of pixels.
        if (maxX < 0 || minX > ws.Width || maxY < 0 || minY > ws.Height) return;
        if ((maxX - minX) > ws.Width * 4 || (maxY - minY) > ws.Height * 4) return;

        int ix0 = System.Math.Max(0, (int)System.Math.Floor(minX));
        int iy0 = System.Math.Max(0, (int)System.Math.Floor(minY));
        int ix1 = System.Math.Min(ws.Width  - 1, (int)System.Math.Ceiling(maxX));
        int iy1 = System.Math.Min(ws.Height - 1, (int)System.Math.Ceiling(maxY));

        // pre-compute triangle area & edge sign for barycentric lookups
        float areaTimes2 = EdgeFunction(p0, p1, p2);
        if (System.Math.Abs(areaTimes2) < 1e-10f) return; // degenerate UV: skip
        float invArea = 1f / areaTimes2;

        // world transform for positions, inverse-transpose for normals
        var W = instance.WorldTransform;
        var NM = NormalMatrix(W);

        // Per-texel world radius: how big this triangle's pixels are in world space. Used by the
        // denoiser as the per-texel footprint that scales its position bandwidth, regardless of
        // mesh scale or atlas density.
        var w_v0 = Raytracing.RayMath.Transform(W, v0L, 1f);
        var w_v1 = Raytracing.RayMath.Transform(W, v1L, 1f);
        var w_v2 = Raytracing.RayMath.Transform(W, v2L, 1f);
        float worldAreaTwice = Float3.Length(Float3.Cross(w_v1 - w_v0, w_v2 - w_v0));
        float pixelAreaTwice = System.Math.Abs(areaTimes2);
        float worldPerPixel = pixelAreaTwice > 1e-12f ? worldAreaTwice / pixelAreaTwice : 0f;
        float texelRadius = (float)System.Math.Sqrt(worldPerPixel) * 0.5f;

        for (int py = iy0; py <= iy1; py++)
        for (int px = ix0; px <= ix1; px++)
        {
            // conservative coverage: triangle vs pixel-square SAT
            Float2 pmin = new Float2(px, py);
            Float2 pmax = new Float2(px + 1, py + 1);
            if (!TrianglePixelOverlap(p0, p1, p2, pmin, pmax)) continue;

            // sample barycentrics at the pixel centre; if outside, snap to the closest valid point
            Float2 sample = new Float2(px + 0.5f, py + 0.5f);
            float w0 = EdgeFunction(p1, p2, sample) * invArea;
            float w1 = EdgeFunction(p2, p0, sample) * invArea;
            float w2 = 1f - w0 - w1;
            bool strictlyInside = w0 >= 0 && w1 >= 0 && w2 >= 0;
            if (!strictlyInside)
            {
                ClampToTriangle(p0, p1, p2, sample, out w0, out w1, out w2);
            }

            // Inset toward triangle centroid by a tiny fraction. Without this, texels right at
            // chart boundaries have sample positions on triangle edges/vertices, which puts the
            // ray origin (position + normal*bias) close enough to an adjacent surface that every
            // shadow ray hits it -> dim "halo" lines tracing every chart edge. Position error is
            // ~2% of the texel footprint (mm-scale at Sponza), invisible in the bake.
            const float CentroidBias = 0.02f;
            const float OneThird = 1f / 3f;
            w0 = w0 * (1f - CentroidBias) + OneThird * CentroidBias;
            w1 = w1 * (1f - CentroidBias) + OneThird * CentroidBias;
            w2 = w2 * (1f - CentroidBias) + OneThird * CentroidBias;

            var pL = v0L * w0 + v1L * w1 + v2L * w2;
            var nL = n0 * w0 + n1 * w1 + n2 * w2;
            if (Float3.Dot(nL, nL) < 1e-10f) continue; // skip degenerate normal

            // to world
            var pW = Raytracing.RayMath.Transform(W, pL, 1f);
            var nW = Float3.Normalize(Raytracing.RayMath.Transform(NM, nL, 0f));

            var uv0 = uv0_0 * w0 + uv1_0 * w1 + uv2_0 * w2;

            int idx = py * ws.Width + px;
            // Strict-inside wins. A new claimant takes the texel if:
            //   - nobody has claimed it yet, OR
            //   - this writer is strictly inside but the existing writer was conservative-only.
            // Two strict writers (which would mean overlapping non-degenerate triangles in atlas space)
            // still falls back to first-writer-wins; same for two conservative-only writers.
            bool existing = ws.Covered[idx];
            bool shouldWrite = !existing
                || (strictlyInside && !ws.Samples[idx].StrictlyInside);
            if (shouldWrite)
            {
                ws.Covered[idx] = true;
                ws.Samples[idx] = new TexelSample
                {
                    Position = pW,
                    Normal = nW,
                    InstanceIndex = instanceIndex,
                    MaterialGroupIndex = materialGroupIndex,
                    UV0 = uv0,
                    WorldRadius = texelRadius,
                    StrictlyInside = strictlyInside,
                };
            }
        }
    }

    /// <summary>2D signed edge function: positive when (a, b, c) is CCW.</summary>
    private static float EdgeFunction(Float2 a, Float2 b, Float2 c)
        => (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);

    /// <summary>SAT-based triangle/AABB overlap in 2D (conservative coverage).</summary>
    private static bool TrianglePixelOverlap(Float2 v0, Float2 v1, Float2 v2, Float2 bmin, Float2 bmax)
    {
        // Tri AABB vs pixel AABB
        float trMinX = System.Math.Min(v0.X, System.Math.Min(v1.X, v2.X));
        float trMaxX = System.Math.Max(v0.X, System.Math.Max(v1.X, v2.X));
        float trMinY = System.Math.Min(v0.Y, System.Math.Min(v1.Y, v2.Y));
        float trMaxY = System.Math.Max(v0.Y, System.Math.Max(v1.Y, v2.Y));
        if (trMaxX < bmin.X || trMinX > bmax.X || trMaxY < bmin.Y || trMinY > bmax.Y) return false;

        // Each triangle edge: project pixel corners onto the edge normal; if all four corners
        // are on the "outside" side, the pixel doesn't overlap.
        if (EdgeReject(v0, v1, v2, bmin, bmax)) return false;
        if (EdgeReject(v1, v2, v0, bmin, bmax)) return false;
        if (EdgeReject(v2, v0, v1, bmin, bmax)) return false;
        return true;
    }

    private static bool EdgeReject(Float2 a, Float2 b, Float2 opp, Float2 bmin, Float2 bmax)
    {
        // edge direction
        float ex = b.X - a.X, ey = b.Y - a.Y;
        // inward normal (rotate edge 90° toward opp)
        float nx = -ey, ny = ex;
        // ensure normal points toward opp
        if ((opp.X - a.X) * nx + (opp.Y - a.Y) * ny < 0) { nx = -nx; ny = -ny; }
        // pick the AABB corner most inside (toward opp). If even that corner is outside, reject.
        float cx = nx > 0 ? bmax.X : bmin.X;
        float cy = ny > 0 ? bmax.Y : bmin.Y;
        return (cx - a.X) * nx + (cy - a.Y) * ny < 0;
    }

    /// <summary>Closest barycentric coordinates inside the triangle to the given 2D point.</summary>
    private static void ClampToTriangle(Float2 a, Float2 b, Float2 c, Float2 p, out float wa, out float wb, out float wc)
    {
        // Closest point on each edge, pick the nearest of the three edge results.
        float dab = ClosestParam(a, b, p, out var qab);
        float dbc = ClosestParam(b, c, p, out var qbc);
        float dca = ClosestParam(c, a, p, out var qca);
        Float2 q;
        if (dab <= dbc && dab <= dca) q = qab;
        else if (dbc <= dca)          q = qbc;
        else                          q = qca;
        // recompute barycentrics for q
        float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        wb = ((q.X - a.X) * (c.Y - a.Y) - (q.Y - a.Y) * (c.X - a.X)) / area;
        wc = ((b.X - a.X) * (q.Y - a.Y) - (b.Y - a.Y) * (q.X - a.X)) / area;
        wa = 1f - wb - wc;
        // Floor negatives then renormalize to preserve sum=1. Per-component Clamp(0,1) would
        // leave wa+wb+wc != 1 and produce world positions slightly *outside* the triangle.
        if (wa < 0) wa = 0;
        if (wb < 0) wb = 0;
        if (wc < 0) wc = 0;
        float sum = wa + wb + wc;
        if (sum > 0)
        {
            float inv = 1f / sum;
            wa *= inv; wb *= inv; wc *= inv;
        }
        else
        {
            wa = wb = wc = 1f / 3f;
        }
    }

    private static float ClosestParam(Float2 a, Float2 b, Float2 p, out Float2 q)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        float t = len2 > 0 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2 : 0;
        t = System.Math.Clamp(t, 0f, 1f);
        q = new Float2(a.X + t * dx, a.Y + t * dy);
        float ex = p.X - q.X, ey = p.Y - q.Y;
        return ex * ex + ey * ey;
    }

    /// <summary>
    /// "Normal matrix": for rigid + uniform-scale transforms it's <c>M</c> itself; for general
    /// non-uniform scale it's the inverse-transpose of the upper 3x3. Photonic uses uniform
    /// scaling for instances, so the simple form is fine; we still build inverse-transpose for
    /// robustness.
    /// </summary>
    private static Float4x4 NormalMatrix(Float4x4 m)
    {
        // upper-3x3 inverse-transpose via cofactor: cheap & branchless
        Float3 c0 = new Float3(m.c0.X, m.c0.Y, m.c0.Z);
        Float3 c1 = new Float3(m.c1.X, m.c1.Y, m.c1.Z);
        Float3 c2 = new Float3(m.c2.X, m.c2.Y, m.c2.Z);
        var r0 = Float3.Cross(c1, c2);
        var r1 = Float3.Cross(c2, c0);
        var r2 = Float3.Cross(c0, c1);
        float det = Float3.Dot(c0, r0);
        if (System.Math.Abs(det) < 1e-20f) return Float4x4.Identity;
        float invDet = 1f / det;
        // r0,r1,r2 (scaled) are the ROWS of inverse(W); the normal matrix is inverse-transpose(W),
        // so they must go in as COLUMNS. The 16-arg Float4x4 ctor is row-major-named, so lay them
        // out transposed: column k = (r0[k], r1[k], r2[k]).
        return new Float4x4(
            r0.X * invDet, r1.X * invDet, r2.X * invDet, 0,
            r0.Y * invDet, r1.Y * invDet, r2.Y * invDet, 0,
            r0.Z * invDet, r1.Z * invDet, r2.Z * invDet, 0,
            0,             0,             0,             1);
    }
}
