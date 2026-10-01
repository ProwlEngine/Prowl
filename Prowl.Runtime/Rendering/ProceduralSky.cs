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

    const float TwilightFade = 0.5178f;
    const float TwilightFadeCurve = 0.11575f;
    const float TwilightAwayFade = -0.25244f;
    const float TwilightAwayCurve = 0.61539f;
    const float TwilightHighFade = -0.069529f;
    const float TwilightHeightFade = -0.022207f;
    const float TwilightHeightCurve = 0.3201f;
    const float TwilightAirTint = 1225f;
    const float TwilightHighTint = 1503.2f;
    const float TwilightOzoneTint = 124360f;
    const float TwilightGlowFade = 4.8709f;

    const float SunAirSharpness = 0.66032f;
    const float SunFarShift = 0.0039793f;

    const float ShadowRise = 0.069179f;
    const float ShadowRiseCurve = 0.015505f;
    const float ShadowSoftness = 0.0018812f;
    const float ShadowSoftening = 0.023835f;
    const float ShadowRamp = 0.43425f;
    const float BeltFade = 0.072108f;

    static readonly Float3 BouncePerSun = new(0.053082f, 0.058143f, 0.085042f);
    static readonly Float3 BounceAtSunset = new(0.0024572f, 0.0014576f, 0.0039399f);
    const float BounceFade = 1.196f;
    const float BounceFadeCurve = 0.065588f;
    const float BounceAirTint = 9009.4f;

    /// <summary>Sets every sun dependent uniform of the sky material, toSun is the unit direction toward the sun.</summary>
    public static void Apply(Material material, Float3 toSun)
    {
        float sunUp = MathF.Max(toSun.Y, 0);
        float depression = MathF.Max(-toSun.Y, 0) * DepressionScale;
        float flatLength = MathF.Sqrt(MathF.Max(toSun.X * toSun.X + toSun.Z * toSun.Z, 1e-12f));

        float rayleighK = RayleighK * SunAirSharpness, mieK = MieK * SunAirSharpness;
        Float3 depth = Rayleigh * (RayleighHeight * AirMass(sunUp, rayleighK))
            + new Float3(MieExtinction * MieHeight * AirMass(sunUp, mieK))
            + Ozone * (OzoneColumn / MathF.Sqrt(sunUp * sunUp + OzoneK));
        Float3 depthSlope = (Rayleigh * (RayleighHeight * AirMassSlope(sunUp, rayleighK))
            + new Float3(MieExtinction * MieHeight * AirMassSlope(sunUp, mieK))) * SunFarShift;

        Float3 bounceFade = new Float3(BounceFade + BounceFadeCurve * depression) + Rayleigh * BounceAirTint;
        Float3 bounce = Rayleigh * (BouncePerSun * sunUp + BounceAtSunset) * Exp(-bounceFade * depression);

        float awayFade = 0.5f * (TwilightAwayFade + TwilightAwayCurve * depression) * depression;
        Float3 fadeBase = (new Float3(TwilightFade + TwilightFadeCurve * depression) + Rayleigh * TwilightAirTint + Ozone * TwilightOzoneTint) * depression
            + new Float3(awayFade);

        material.SetVector("_SkySunDir", toSun);
        material.SetVector("_SkySunFlat", new Float2(toSun.X / flatLength, toSun.Z / flatLength));
        material.SetVector("_SkySunDepth", depth);
        material.SetVector("_SkySunTransmittance", Exp(-depth));
        material.SetVector("_SkySunDepthSlope", depthSlope);
        material.SetFloat("_SkyMieScale", MieScatter * (1 - MieG * MieG) / (4 * MathF.PI) * MathF.Exp(-TwilightGlowFade * depression));
        material.SetVector("_SkyBounce", bounce);

        material.SetFloat("_SkyTwilight", toSun.Y < 0 ? 1 : 0);
        material.SetVector("_SkyFadeBase", fadeBase);
        material.SetFloat("_SkyAwayFade", awayFade);
        material.SetFloat("_SkyHighFade", TwilightHighFade * depression);
        material.SetFloat("_SkyHeightFade", (TwilightHeightFade + TwilightHeightCurve * depression) * depression);
        material.SetFloat("_SkyHighTint", TwilightHighTint * depression);

        material.SetFloat("_SkyShadowRise", (ShadowRise + ShadowRiseCurve * depression) * depression);
        material.SetFloat("_SkyInverseShadowWidth", 1 / (ShadowSoftness + ShadowSoftening * depression + 1e-4f));
        material.SetFloat("_SkyInverseBeltWidth", 1 / BeltFade);
        material.SetFloat("_SkyShadowRamp", SmoothStep(0, ShadowRamp, depression));
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

    static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    static Float3 Exp(Float3 v) => new(MathF.Exp(v.X), MathF.Exp(v.Y), MathF.Exp(v.Z));
}
