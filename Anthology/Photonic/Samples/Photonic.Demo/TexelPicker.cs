using Prowl.Vector;

namespace Photonic.Demo;

/// <summary>
/// Brute-force ray-vs-Sponza picker for the debug viewer. Tests the mouse ray against every
/// triangle (Sponza is ~100k tris, so ~5M tests/s × 60Hz is fine on a desktop CPU), returns the
/// closest hit's interpolated UV1 → texel coords.
/// </summary>
internal static class TexelPicker
{
    public static bool Pick(CombinedSponza model, Float3 rayOrigin, Float3 rayDir,
                            int atlasWidth, int atlasHeight,
                            out int texelX, out int texelY, out Float3 hitPos)
    {
        texelX = -1; texelY = -1; hitPos = Float3.Zero;
        if (model.Indices.Length == 0) return false;

        var positions = model.Vertices;
        var uv1 = model.UV1;
        var indices = model.Indices;

        float bestT = float.PositiveInfinity;
        int bestI = -1;
        float bestU = 0, bestV = 0;
        for (int i = 0; i < indices.Length; i += 3)
        {
            var v0 = positions[indices[i    ]];
            var v1 = positions[indices[i + 1]];
            var v2 = positions[indices[i + 2]];
            if (RayTriangle(rayOrigin, rayDir, v0, v1, v2, out float tt, out float u, out float v)
                && tt > 1e-4f && tt < bestT)
            {
                bestT = tt; bestI = i; bestU = u; bestV = v;
            }
        }
        if (bestI < 0) return false;

        var uvA = uv1[indices[bestI    ]];
        var uvB = uv1[indices[bestI + 1]];
        var uvC = uv1[indices[bestI + 2]];
        float w = 1f - bestU - bestV;
        var hitUV = uvA * w + uvB * bestU + uvC * bestV;

        // Wrap into [0, 1) — UV unwrap should already be in range but the bilinear may show edge cases.
        float u01 = hitUV.X - (float)System.Math.Floor(hitUV.X);
        float v01 = hitUV.Y - (float)System.Math.Floor(hitUV.Y);
        texelX = System.Math.Clamp((int)(u01 * atlasWidth), 0, atlasWidth - 1);
        texelY = System.Math.Clamp((int)(v01 * atlasHeight), 0, atlasHeight - 1);
        hitPos = rayOrigin + rayDir * bestT;
        return true;
    }

    private static bool RayTriangle(Float3 ro, Float3 rd, Float3 v0, Float3 v1, Float3 v2,
                                    out float t, out float u, out float v)
    {
        var e1 = v1 - v0;
        var e2 = v2 - v0;
        var p = Float3.Cross(rd, e2);
        float det = Float3.Dot(e1, p);
        if (System.Math.Abs(det) < 1e-12f) { t = u = v = 0; return false; }
        float invDet = 1f / det;
        var tv = ro - v0;
        u = Float3.Dot(tv, p) * invDet;
        if (u < 0 || u > 1) { t = 0; v = 0; return false; }
        var q = Float3.Cross(tv, e1);
        v = Float3.Dot(rd, q) * invDet;
        if (v < 0 || u + v > 1) { t = 0; return false; }
        t = Float3.Dot(e2, q) * invDet;
        return t > 0;
    }
}
