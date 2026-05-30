using Prowl.Vector;

namespace Prowl.Photonic.Surfels;

/// <summary>
/// Order-1 (4-coefficient) real spherical-harmonic projection of an RGB radiance field: the
/// band-0 (constant) term plus the three band-1 directional terms, per colour channel.
/// </summary>
/// <remarks>
/// This is what makes a surfel directional. Instead of freezing one scalar irradiance value to
/// the normal it was integrated against, a surfel stores the SH of the radiance arriving over its
/// hemisphere and reconstructs irradiance for an arbitrary normal at lookup time
/// (<see cref="IrradianceOverPi"/>). That lets a single surfel feed texels - and other surfels -
/// whose normals differ from its own without leaking irradiance across orientation changes.
/// L1 (4 coefficients) is the standard sweet spot: it captures the dominant light direction at a
/// fraction of the storage of L2, and reconstructs cleanly under the cosine lobe.
/// </remarks>
public struct ShL1Rgb
{
    /// <summary>Band-0 real-SH basis constant, 0.5 * sqrt(1/π).</summary>
    public const float Y0 = 0.2820947918f;
    /// <summary>Band-1 real-SH basis constant, 0.5 * sqrt(3/π).</summary>
    public const float Y1 = 0.4886025119f;

    // Cosine-lobe convolution coefficients (Ramamoorthi & Hanrahan 2001): A0 = π, A1 = 2π/3.
    // Reconstruction divides irradiance by π (see IrradianceOverPi), so the band weights fold to
    // A0/π = 1 and A1/π = 2/3.
    private const float ReconBand0 = Y0;                 // (A0/π) * Y0
    private const float ReconBand1 = (2f / 3f) * Y1;     // (A1/π) * Y1

    public Float3 C0; // band 0 (constant)
    public Float3 Cx; // band 1, x
    public Float3 Cy; // band 1, y
    public Float3 Cz; // band 1, z

    /// <summary>
    /// Fold one radiance sample arriving from unit direction <paramref name="dir"/> into the
    /// projection, scaled by <paramref name="weight"/> (the reciprocal sampling PDF). The caller
    /// divides the accumulated coefficients by the sample count to get the estimated projection.
    /// </summary>
    public void Accumulate(Float3 dir, Float3 radiance, float weight)
    {
        Float3 r = radiance * weight;
        C0 += r * Y0;
        Cx += r * (Y1 * (float)dir.X);
        Cy += r * (Y1 * (float)dir.Y);
        Cz += r * (Y1 * (float)dir.Z);
    }

    public void Add(in ShL1Rgb o)
    {
        C0 += o.C0; Cx += o.Cx; Cy += o.Cy; Cz += o.Cz;
    }

    /// <summary>This projection scaled by <paramref name="s"/> (used to turn the running sum into a mean).</summary>
    public readonly ShL1Rgb Scaled(float s) =>
        new ShL1Rgb { C0 = C0 * s, Cx = Cx * s, Cy = Cy * s, Cz = Cz * s };

    /// <summary>
    /// Reconstruct the diffuse irradiance response (E / π, i.e. the cosine-weighted mean incoming
    /// radiance) for a surface oriented along <paramref name="normal"/>. The /π keeps the result in
    /// the same units the per-texel path stores - a Lambert surface multiplies this by its albedo at
    /// runtime. Negative lobes (SH ringing under sparse samples) are clamped to zero.
    /// </summary>
    public readonly Float3 IrradianceOverPi(Float3 normal)
    {
        Float3 e = C0 * ReconBand0
                 + (Cx * (float)normal.X + Cy * (float)normal.Y + Cz * (float)normal.Z) * ReconBand1;
        if (e.X < 0) e.X = 0;
        if (e.Y < 0) e.Y = 0;
        if (e.Z < 0) e.Z = 0;
        return e;
    }
}
