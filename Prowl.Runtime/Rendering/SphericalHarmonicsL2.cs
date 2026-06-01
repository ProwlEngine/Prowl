// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Order-2 (9-coefficient) RGB spherical harmonics — the light-probe representation for ambient
/// GI on dynamic objects. Layout matches <c>Prowl.Photonic.Sh9Rgb</c> (the editor copies the baked
/// coefficients straight across). Serialized as plain public fields.
/// </summary>
public struct SphericalHarmonicsL2
{
    // Real-SH basis constants (must match Prowl.Photonic.Sh9Rgb).
    private const float K0 = 0.2820947918f;
    private const float K1 = 0.4886025119f;
    private const float K2a = 1.0925484306f;
    private const float K2b = 0.3153915652f;
    private const float K2c = 0.5462742153f;
    // Cosine-lobe convolution weights A_l/π (A0=π, A1=2π/3, A2=π/4 over π).
    private const float A0 = 1.0f;
    private const float A1 = 2.0f / 3.0f;
    private const float A2 = 1.0f / 4.0f;

    // Raw radiance projection coefficients (band 0, 1, 2 order).
    public Float3 C0, C1, C2, C3, C4, C5, C6, C7, C8;

    /// <summary>A flat SH that reconstructs the constant ambient radiance <paramref name="color"/> for every normal.</summary>
    public static SphericalHarmonicsL2 FromConstant(Float3 color)
        => new SphericalHarmonicsL2 { C0 = color / (A0 * K0) };

    /// <summary>CPU diffuse irradiance response (E/π) for <paramref name="normal"/> — matches the GPU <c>ShadeSH9</c>.</summary>
    public readonly Float3 Evaluate(Float3 normal)
    {
        float x = (float)normal.X, y = (float)normal.Y, z = (float)normal.Z;
        Float3 e =
              C0 * (A0 * K0)
            + C1 * (A1 * K1 * y) + C2 * (A1 * K1 * z) + C3 * (A1 * K1 * x)
            + C4 * (A2 * K2a * (x * y)) + C5 * (A2 * K2a * (y * z))
            + C6 * (A2 * K2b * (3f * z * z - 1f))
            + C7 * (A2 * K2a * (x * z)) + C8 * (A2 * K2c * (x * x - y * y));
        if (e.X < 0) e.X = 0;
        if (e.Y < 0) e.Y = 0;
        if (e.Z < 0) e.Z = 0;
        return e;
    }

    public static SphericalHarmonicsL2 Lerp(in SphericalHarmonicsL2 a, in SphericalHarmonicsL2 b, float t)
    {
        SphericalHarmonicsL2 r;
        r.C0 = a.C0 + (b.C0 - a.C0) * t; r.C1 = a.C1 + (b.C1 - a.C1) * t; r.C2 = a.C2 + (b.C2 - a.C2) * t;
        r.C3 = a.C3 + (b.C3 - a.C3) * t; r.C4 = a.C4 + (b.C4 - a.C4) * t; r.C5 = a.C5 + (b.C5 - a.C5) * t;
        r.C6 = a.C6 + (b.C6 - a.C6) * t; r.C7 = a.C7 + (b.C7 - a.C7) * t; r.C8 = a.C8 + (b.C8 - a.C8) * t;
        return r;
    }

    /// <summary>Weighted sum (for barycentric probe blending). <paramref name="terms"/> need not sum to 1; pass normalized weights.</summary>
    public static SphericalHarmonicsL2 Blend(System.ReadOnlySpan<SphericalHarmonicsL2> probes, System.ReadOnlySpan<float> weights)
    {
        SphericalHarmonicsL2 r = default;
        for (int i = 0; i < probes.Length; i++)
        {
            float w = weights[i];
            ref readonly var p = ref probes[i];
            r.C0 += p.C0 * w; r.C1 += p.C1 * w; r.C2 += p.C2 * w; r.C3 += p.C3 * w; r.C4 += p.C4 * w;
            r.C5 += p.C5 * w; r.C6 += p.C6 * w; r.C7 += p.C7 * w; r.C8 += p.C8 * w;
        }
        return r;
    }

    /// <summary>Packed per-object SH uniforms for the GPU <c>ShadeSH9</c> (7 vec4: SHAr/g/b, SHBr/g/b, SHC).</summary>
    public struct Packed { public Float4 SHAr, SHAg, SHAb, SHBr, SHBg, SHBb, SHC; }

    /// <summary>
    /// Fold the cosine-lobe + basis constants into the coefficients and pack them into the 7
    /// vec4 the shader's <c>ShadeSH9</c> consumes (the standard 7-vec4 SH packing). The shader then
    /// reconstructs the same E/π as <see cref="Evaluate"/>.
    /// </summary>
    public readonly Packed ToShaderCoefficients()
    {
        // Scaled coefficients C' = C * (A_l/π) * basisConstant.
        Float3 c0 = C0 * (A0 * K0);
        Float3 c1 = C1 * (A1 * K1), c2 = C2 * (A1 * K1), c3 = C3 * (A1 * K1);
        Float3 c4 = C4 * (A2 * K2a), c5 = C5 * (A2 * K2a), c7 = C7 * (A2 * K2a);
        Float3 c6 = C6 * (A2 * K2b);
        Float3 c8 = C8 * (A2 * K2c);

        Packed p;
        // Linear (x, y, z) + (DC - z²-band constant) folded for one dot4 with (n.xyz, 1).
        p.SHAr = new Float4(c3.X, c1.X, c2.X, c0.X - c6.X);
        p.SHAg = new Float4(c3.Y, c1.Y, c2.Y, c0.Y - c6.Y);
        p.SHAb = new Float4(c3.Z, c1.Z, c2.Z, c0.Z - c6.Z);
        // Quadratic: (xy, yz, 3·z²coeff, zx).
        p.SHBr = new Float4(c4.X, c5.X, c6.X * 3f, c7.X);
        p.SHBg = new Float4(c4.Y, c5.Y, c6.Y * 3f, c7.Y);
        p.SHBb = new Float4(c4.Z, c5.Z, c6.Z * 3f, c7.Z);
        p.SHC  = new Float4(c8.X, c8.Y, c8.Z, 1f);
        return p;
    }
}
