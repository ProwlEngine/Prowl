namespace Prowl.Photonic.Sampling;

/// <summary>
/// User-pluggable light falloff model. Photonic queries <see cref="Evaluate"/> for the unitless
/// multiplier applied to a light's radiance at a given surface distance.
/// </summary>
/// <remarks>
/// Implement your own for any specific falloff curve you need (physically-plausible inverse
/// square, smooth normalised quadratic, hard cutoff, etc).
/// </remarks>
public interface IAttenuation
{
    /// <summary>Evaluate the multiplier at <paramref name="distance"/> for a light of <paramref name="range"/>.</summary>
    float Evaluate(float distance, float range);
}

/// <summary>Inverse-square falloff with a smooth range cutoff. Matches a physically-plausible point light.</summary>
public sealed class InverseSquareAttenuation : IAttenuation
{
    /// <inheritdoc />
    public float Evaluate(float distance, float range)
    {
        if (distance >= range) return 0f;
        float d2 = distance * distance + 1e-4f;
        float att = 1f / d2;
        // smooth window the upper 20% so it doesn't pop at the range boundary
        float t = distance / range;
        float w = 1f - t * t * t * t;
        if (w < 0) w = 0;
        return att * (w * w);
    }
}

/// <summary>
/// Normalised quadratic falloff: <c>att = 1 / (1 + 25 * (d/r)^2)</c>. A cheap closed-form
/// alternative to a ramp lookup that stays bright near the source and tails off smoothly.
/// </summary>
public sealed class NormalizedQuadraticAttenuation : IAttenuation
{
    /// <inheritdoc />
    public float Evaluate(float distance, float range)
    {
        if (distance >= range) return 0f;
        float t = distance / range;
        return 1f / (1f + 25f * t * t);
    }
}

/// <summary>No attenuation at all (full intensity until the light's <c>range</c>). Useful for directional-like spots.</summary>
public sealed class ConstantAttenuation : IAttenuation
{
    /// <inheritdoc />
    public float Evaluate(float distance, float range) => distance < range ? 1f : 0f;
}
