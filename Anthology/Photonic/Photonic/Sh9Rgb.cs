using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// Order-2 (9-coefficient) real spherical-harmonic projection of an RGB radiance field. This is
/// the standard light-probe representation: it captures the full directional ambient environment
/// at a point so dynamic (non-lightmapped) objects can reconstruct irradiance for any normal.
/// </summary>
/// <remarks>
/// <para>Stores the raw radiance projection coefficients (NOT pre-convolved with the cosine lobe),
/// nine per channel, in the conventional ordering: band 0 (1 coeff), band 1 (3), band 2 (5). Use
/// <see cref="GetCoefficients"/> to read them out for GPU upload, or <see cref="EvaluateIrradiance"/>
/// to reconstruct <c>E/π</c> for a given normal on the CPU (same units the lightmap stores, so a
/// Lambert surface multiplies by albedo at runtime).</para>
/// </remarks>
public struct Sh9Rgb
{
    // Real-SH basis constants (Stupid Spherical Harmonics Tricks, Sloan 2008).
    private const float K0  = 0.2820947918f;            // band 0
    private const float K1  = 0.4886025119f;            // band 1
    private const float K2a = 1.0925484306f;            // band 2: xy, yz, xz
    private const float K2b = 0.3153915652f;            // band 2: 3z^2-1
    private const float K2c = 0.5462742153f;            // band 2: x^2-y^2

    // Cosine-lobe convolution weights A_l/π for diffuse irradiance reconstruction
    // (Ramamoorthi & Hanrahan 2001): A0=π, A1=2π/3, A2=π/4 -> divided by π.
    private const float A0 = 1.0f;
    private const float A1 = 2.0f / 3.0f;
    private const float A2 = 1.0f / 4.0f;

    // 9 coefficients per channel.
    public Float3 C0, C1, C2, C3, C4, C5, C6, C7, C8;

    /// <summary>
    /// Fold one radiance sample arriving from unit direction <paramref name="dir"/> into the
    /// projection, scaled by <paramref name="weight"/> (the reciprocal sampling PDF). The caller
    /// divides by the sample count afterwards (or passes <c>weight = 4π / sampleCount</c>).
    /// </summary>
    public void Accumulate(Float3 dir, Float3 radiance, float weight)
    {
        float x = (float)dir.X, y = (float)dir.Y, z = (float)dir.Z;
        Float3 r = radiance * weight;

        C0 += r * K0;
        C1 += r * (K1 * y);
        C2 += r * (K1 * z);
        C3 += r * (K1 * x);
        C4 += r * (K2a * (x * y));
        C5 += r * (K2a * (y * z));
        C6 += r * (K2b * (3f * z * z - 1f));
        C7 += r * (K2a * (x * z));
        C8 += r * (K2c * (x * x - y * y));
    }

    public void Add(in Sh9Rgb o)
    {
        C0 += o.C0; C1 += o.C1; C2 += o.C2; C3 += o.C3; C4 += o.C4;
        C5 += o.C5; C6 += o.C6; C7 += o.C7; C8 += o.C8;
    }

    /// <summary>This projection scaled by <paramref name="s"/> (used to turn a running sum into a mean).</summary>
    public readonly Sh9Rgb Scaled(float s) => new Sh9Rgb
    {
        C0 = C0 * s, C1 = C1 * s, C2 = C2 * s, C3 = C3 * s, C4 = C4 * s,
        C5 = C5 * s, C6 = C6 * s, C7 = C7 * s, C8 = C8 * s,
    };

    /// <summary>The nine raw radiance coefficients (band 0, 1, 2 order), for serialization / GPU upload.</summary>
    public readonly Float3[] GetCoefficients() => new[] { C0, C1, C2, C3, C4, C5, C6, C7, C8 };

    /// <summary>
    /// Reconstruct the diffuse irradiance response (<c>E/π</c>) for a surface oriented along
    /// <paramref name="normal"/>, applying the cosine-lobe convolution. Negative lobes (SH ringing
    /// under sparse sampling) are clamped to zero.
    /// </summary>
    public readonly Float3 EvaluateIrradiance(Float3 normal)
    {
        float x = (float)normal.X, y = (float)normal.Y, z = (float)normal.Z;

        Float3 e =
              C0 * (A0 * K0)
            + C1 * (A1 * K1 * y)
            + C2 * (A1 * K1 * z)
            + C3 * (A1 * K1 * x)
            + C4 * (A2 * K2a * (x * y))
            + C5 * (A2 * K2a * (y * z))
            + C6 * (A2 * K2b * (3f * z * z - 1f))
            + C7 * (A2 * K2a * (x * z))
            + C8 * (A2 * K2c * (x * x - y * y));

        if (e.X < 0) e.X = 0;
        if (e.Y < 0) e.Y = 0;
        if (e.Z < 0) e.Z = 0;
        return e;
    }
}
