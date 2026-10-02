using System;

using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// The per frame half of the procedural sky. Everything that only depends on the sun is computed here and passed to
/// ProceduralSkybox.shader, which does the per pixel half. The fitted constants were tuned against a raymarched reference.
/// </summary>
public static class ProceduralSky
{
    static readonly Float3 Rayleigh = new(5.802e-6f, 13.558e-6f, 33.1e-6f);
    static readonly Float3 Ozone = new(0.650e-6f, 1.881e-6f, 0.085e-6f);
    const float MieScatter = 3.996e-6f, MieExtinction = 4.44e-6f, MieG = 0.8f;
    const float RayleighHeight = 8000f, RayleighK = 8.0e-4f;
    const float MieHeight = 1200f, MieK = 1.2e-4f;
    const float OzoneColumn = 15000f, OzoneK = 0.0078f;
    const float DepressionScale = 28.2f;

    const float TwilightFade = 0.61172f;
    const float TwilightFadeCurve = 0.040113f;
    const float TwilightAwayFade = -0.18102f;
    const float TwilightAwayCurve = 0.3826f;
    const float TwilightHighFade = -0.13711f;
    const float TwilightHeightFade = -0.44557f;
    const float TwilightHeightCurve = 0.53126f;
    const float TwilightAirTint = 4239.3f;
    const float TwilightOzoneTint = 144770.0f;
    const float TwilightGlowCurve = 0.035289f;
    const float GlowSoftening = 0.045142f;
    const float TwilightGlowFade = 1.7157f;

    const float SunFarShift = 0.005083f;

    const float ShadowLead = 0.0077169f;
    const float ShadowRise = 0.033087f;
    const float ShadowRiseCurve = 0.027632f;
    const float ShadowSoftening = 0.01849f;
    const float ShadowSpread = 0.28058f;
    const float ShadowRamp = 0.79053f;

    static readonly Float3 BouncePerSun = new(0.052524f, 0.05829f, 0.071902f);
    static readonly Float3 BounceAtSunset = new(0.0022233f, 0.0012501f, 0.0034871f);
    const float BounceFade = 1.0157f;
    const float BounceFadeCurve = 0.076503f;
    const float BounceAirTint = 8758.3f;
    const float BounceSide = 0.15012f;
    const float BounceSunsetRedden = 0.045581f;
    const float BounceSunsetReddenFade = 26.027f;
    const float BounceReddenCurve = 0.0065451f;
    const float BounceTwilightRedden = 0.0082749f;

    /// <summary>Sets every sun dependent uniform of the sky material, toSun is the unit direction toward the sun.</summary>
    public static void Apply(Material material, Float3 toSun)
    {
        float sunUp = MathF.Max(toSun.Y, 0);
        float depression = MathF.Max(-toSun.Y, 0) * DepressionScale;
        float shadowDepth = MathF.Max(ShadowLead - toSun.Y, 0) * DepressionScale;
        float flatLength = MathF.Sqrt(MathF.Max(toSun.X * toSun.X + toSun.Z * toSun.Z, 1e-12f));

        Float3 depth = Rayleigh * (RayleighHeight * AirMass(sunUp, RayleighK)) + new Float3(MieExtinction * MieHeight * AirMass(sunUp, MieK));
        Float3 depthSlope = (Rayleigh * (RayleighHeight * AirMassSlope(sunUp, RayleighK))
            + new Float3(MieExtinction * MieHeight * AirMassSlope(sunUp, MieK))) * SunFarShift;

        // The ozone layer sits above nearly all the air, so it filters the sunlight the same for every point on the view.
        Float3 transmittance = Exp(-depth);
        Float3 ozone = Exp(-Ozone * (OzoneColumn / MathF.Sqrt(sunUp * sunUp + OzoneK)));
        float glowG = MieG - GlowSoftening / (1 + 30 * sunUp);
        float mieScale = MieScatter * (1 - glowG * glowG) / (4 * MathF.PI) * MathF.Exp(-(TwilightGlowFade + TwilightGlowCurve * depression) * depression);

        Float3 bounceRedden = Rayleigh * (BounceSunsetRedden / (1 + BounceSunsetReddenFade * sunUp) + (BounceTwilightRedden + BounceReddenCurve * depression) * depression);
        Float3 bounceFade = new Float3(BounceFade + BounceFadeCurve * depression) + Rayleigh * BounceAirTint;
        Float3 bounce = Rayleigh * (BouncePerSun * sunUp + BounceAtSunset) * Exp(bounceRedden * RayleighHeight - bounceFade * depression);

        float awayFade = (TwilightAwayFade + TwilightAwayCurve * depression) * depression;
        Float3 fadeBase = (new Float3(TwilightFade + TwilightFadeCurve * depression) + Rayleigh * TwilightAirTint + Ozone * TwilightOzoneTint) * depression
            + new Float3(awayFade);

        material.SetVector("_SkySunDir", toSun);
        material.SetVector("_SkySunFlat", new Float2(toSun.X / flatLength, toSun.Z / flatLength));
        material.SetVector("_SkySunDepth", depth);
        material.SetVector("_SkySunTransmittance", transmittance);
        material.SetVector("_SkySunColor", transmittance * ozone);
        material.SetVector("_SkySunDepthSlope", depthSlope);
        material.SetVector("_SkyRayleighScale", Rayleigh * ozone * (3 / (16 * MathF.PI)));
        material.SetVector("_SkyMieScale", ozone * mieScale);
        material.SetVector("_SkyMiePhase", new Float2(1 + glowG * glowG, 2 * glowG));
        material.SetVector("_SkyBounce", bounce);
        material.SetVector("_SkyBounceRedden", bounceRedden);
        material.SetFloat("_SkyBounceSide", BounceSide * depression);

        material.SetFloat("_SkyTwilight", shadowDepth > 0 ? 1 : 0);
        material.SetVector("_SkyFadeBase", fadeBase);
        material.SetFloat("_SkyAwayFade", awayFade);
        material.SetFloat("_SkyHighFade", TwilightHighFade * depression);
        material.SetFloat("_SkyHeightFade", (TwilightHeightFade + TwilightHeightCurve * depression) * depression);

        material.SetFloat("_SkyShadowRise", (ShadowRise + ShadowRiseCurve * shadowDepth) * shadowDepth);
        material.SetFloat("_SkyInverseShadowWidth", 1 / (ShadowSoftening * shadowDepth + 1e-4f));
        material.SetFloat("_SkyRampStart", (shadowDepth - ShadowSpread) / ShadowRamp);
        material.SetFloat("_SkyRampSpread", ShadowSpread / ShadowRamp);
    }

    /// <summary>Air along a direction relative to straight up, with the earth's curve.</summary>
    static float AirMass(float mu, float k) => 1 / (0.641433f * mu + MathF.Sqrt(0.128567f * mu * mu + k));

    /// <summary>How fast AirMass grows as the direction lowers.</summary>
    static float AirMassSlope(float mu, float k)
    {
        float root = MathF.Sqrt(0.128567f * mu * mu + k);
        float mass = 1 / (0.641433f * mu + root);
        return (0.641433f + 0.128567f * mu / root) * mass * mass;
    }

    static Float3 Exp(Float3 v) => new(MathF.Exp(v.X), MathF.Exp(v.Y), MathF.Exp(v.Z));
}
