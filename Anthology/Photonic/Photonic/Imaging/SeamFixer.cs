// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Photonic.Imaging;

/// <summary>
/// Stitches the two sides of every UV seam together. Each seam edge is walked in atlas pixel space
/// on both of its sides at once; the two sides are read and pulled toward their average, repeatedly,
/// until the visible discontinuity along the seam is gone.
/// </summary>
/// <remarks>
/// This is the cheap alternative to solving the whole atlas as a least-squares system: only the
/// texels that actually sit on a seam are touched, and because each pass reads a snapshot of the
/// atlas and writes a partial blend, several passes converge on matching values without smearing
/// the correction into the interior of the chart.
/// </remarks>
internal static class SeamFixer
{
    /// <summary>One pair of atlas positions (in pixels) that must end up the same colour.</summary>
    internal struct Sample
    {
        public float AX, AY, BX, BY;
    }

    public static void Run(float[] rgb, bool[] covered, int width, int height, Sample[] samples, int passes, float strength,
                           float[] snapshot)
    {
        if (samples.Length == 0 || passes <= 0 || strength <= 0f) return;
        strength = System.Math.Clamp(strength, 0f, 1f);

        for (int pass = 0; pass < passes; pass++)
        {
            System.Array.Copy(rgb, snapshot, rgb.Length);
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                if (!SampleCovered(snapshot, covered, width, height, s.AX, s.AY, out var a)) continue;
                if (!SampleCovered(snapshot, covered, width, height, s.BX, s.BY, out var b)) continue;
                var target = (a + b) * 0.5f;
                BlendNearest(rgb, covered, width, height, s.AX, s.AY, target, strength);
                BlendNearest(rgb, covered, width, height, s.BX, s.BY, target, strength);
            }
        }
    }

    /// <summary>Bilinear read that ignores uncovered taps, so gutter pixels cannot darken a seam.</summary>
    private static bool SampleCovered(float[] rgb, bool[] covered, int width, int height, float px, float py, out Float3 color)
    {
        color = Float3.Zero;
        float fx = px - 0.5f, fy = py - 0.5f;
        int x0 = (int)System.MathF.Floor(fx), y0 = (int)System.MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0;

        float total = 0f;
        for (int dy = 0; dy <= 1; dy++)
        for (int dx = 0; dx <= 1; dx++)
        {
            int x = x0 + dx, y = y0 + dy;
            if (x < 0 || y < 0 || x >= width || y >= height) continue;
            int idx = y * width + x;
            if (!covered[idx]) continue;
            float w = (dx == 0 ? 1f - tx : tx) * (dy == 0 ? 1f - ty : ty);
            if (w <= 0f) continue;
            color += new Float3(rgb[idx * 3], rgb[idx * 3 + 1], rgb[idx * 3 + 2]) * w;
            total += w;
        }

        if (total <= 1e-6f) return false;
        color = color * (1f / total);
        return true;
    }

    private static void BlendNearest(float[] rgb, bool[] covered, int width, int height, float px, float py, Float3 target, float strength)
    {
        int x = (int)px, y = (int)py;
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        int idx = y * width + x;
        if (!covered[idx]) return;
        rgb[idx * 3] += (target.X - rgb[idx * 3]) * strength;
        rgb[idx * 3 + 1] += (target.Y - rgb[idx * 3 + 1]) * strength;
        rgb[idx * 3 + 2] += (target.Z - rgb[idx * 3 + 2]) * strength;
    }
}
