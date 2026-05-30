using Prowl.Vector;
using Prowl.Photonic.Rasterization;

namespace Prowl.Photonic.Imaging;

/// <summary>
/// Edge-avoiding a-trous wavelet denoiser for baked lightmaps. Smooths the
/// Monte-Carlo noise left on a surface without bleeding across chart seams or geometric / lighting
/// edges, by weighting each tap with the rasterizer's per-texel world position + normal guides.
/// Operates on covered texels only, in linear HDR, and is meant to run once on the converged atlas
/// just before the final dilation.
/// </summary>
/// <remarks>
/// Each pass reads a frozen snapshot and writes a ping-pong buffer; rows are independent so the
/// row loop runs through <see cref="System.Threading.Tasks.Parallel.For(int,int,System.Action{int})"/>.
/// The tap spacing doubles every pass (1, 2, 4, ...), so a handful of passes covers a wide kernel
/// at a few taps each.
/// </remarks>
internal static class LightmapDenoiser
{
    // B3-spline a-trous kernel (5-tap, separable -> 5x5).
    private static readonly float[] Kernel = { 1f / 16f, 1f / 4f, 3f / 8f, 1f / 4f, 1f / 16f };

    /// <summary>
    /// Denoise <paramref name="rgb"/> (R,G,B interleaved HDR, length <c>width*height*3</c>) in place.
    /// <paramref name="covered"/> / <paramref name="samples"/> are the rasterizer's per-texel coverage
    /// + guides (row-major <c>y*width + x</c>). Uncovered texels are left untouched for dilation.
    /// </summary>
    public static void Run(float[] rgb, bool[] covered, TexelSample[] samples, int width, int height,
                           int iterations, float colorPhi, float normalPhi, float positionScale)
    {
        if (iterations <= 0) return;

        int n = width * height;
        var src = new float[rgb.Length];
        var dst = new float[rgb.Length];
        System.Array.Copy(rgb, src, rgb.Length);

        for (int pass = 0; pass < iterations; pass++)
        {
            int step = 1 << pass;
            System.Threading.Tasks.Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int p = y * width + x;
                    int p3 = p * 3;
                    if (!covered[p])
                    {
                        dst[p3] = src[p3]; dst[p3 + 1] = src[p3 + 1]; dst[p3 + 2] = src[p3 + 2];
                        continue;
                    }

                    Float3 np = samples[p].Normal;
                    Float3 xp = samples[p].Position;
                    float lp = Lum(src[p3], src[p3 + 1], src[p3 + 2]);

                    float footprint = System.MathF.Max(samples[p].WorldRadius, 1e-4f);
                    // Split spatial proximity into in-plane vs off-plane. The in-plane bandwidth grows
                    // with the tap spacing so a big flat surface still smooths at large steps; the
                    // off-plane bandwidth stays tight and step-independent so taps from a perpendicular
                    // wall, a convex edge, or an adjacent UV island never leak across the corner.
                    float sigmaPar = footprint * positionScale * step;
                    float sigmaPerp = footprint * positionScale;
                    float invPar2 = 1f / (2f * sigmaPar * sigmaPar);
                    float invPerp2 = 1f / (2f * sigmaPerp * sigmaPerp);
                    // Radiance edge-stop is relative to local luminance so it behaves the same in
                    // bright and dim HDR regions (preserves shadow boundaries either way).
                    float cPhi = colorPhi * (lp + 1e-2f);

                    float sumR = 0f, sumG = 0f, sumB = 0f, wsum = 0f;
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int qx = x + dx * step, qy = y + dy * step;
                        if (qx < 0 || qx >= width || qy < 0 || qy >= height) continue;
                        int q = qy * width + qx;
                        if (!covered[q]) continue;

                        float ndot = Float3.Dot(np, samples[q].Normal);
                        if (ndot <= 0f) continue;

                        int q3 = q * 3;
                        float h = Kernel[dx + 2] * Kernel[dy + 2];
                        float wN = System.MathF.Pow(ndot, normalPhi);

                        Float3 d = samples[q].Position - xp;
                        float perp = Float3.Dot(d, np);                 // distance off the centre texel's plane
                        float par2 = System.MathF.Max(0f, Float3.Dot(d, d) - perp * perp); // distance along it
                        float wP = System.MathF.Exp(-par2 * invPar2 - perp * perp * invPerp2);

                        float lq = Lum(src[q3], src[q3 + 1], src[q3 + 2]);
                        float wC = System.MathF.Exp(-System.MathF.Abs(lp - lq) / cPhi);

                        float w = h * wN * wP * wC;
                        sumR += src[q3] * w; sumG += src[q3 + 1] * w; sumB += src[q3 + 2] * w;
                        wsum += w;
                    }

                    if (wsum > 1e-8f)
                    {
                        dst[p3] = sumR / wsum; dst[p3 + 1] = sumG / wsum; dst[p3 + 2] = sumB / wsum;
                    }
                    else
                    {
                        dst[p3] = src[p3]; dst[p3 + 1] = src[p3 + 1]; dst[p3 + 2] = src[p3 + 2];
                    }
                }
            });

            (src, dst) = (dst, src);
        }

        // src holds the latest result after the final swap. Write covered texels back; the dilation
        // pass refreshes the padding around them.
        for (int p = 0; p < n; p++)
        {
            if (!covered[p]) continue;
            int p3 = p * 3;
            rgb[p3] = src[p3]; rgb[p3 + 1] = src[p3 + 1]; rgb[p3 + 2] = src[p3 + 2];
        }
    }

    private static float Lum(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;
}
