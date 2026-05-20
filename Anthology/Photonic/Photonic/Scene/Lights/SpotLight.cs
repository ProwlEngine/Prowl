using Prowl.Vector;

namespace Prowl.Photonic.Scene.Lights;

/// <summary>Cone-shaped point light. <see cref="ConeAngle"/> is the full cone in radians.</summary>
public sealed class SpotLight : Light
{
    /// <summary>Maximum distance the spot reaches.</summary>
    public float Range { get; set; }

    /// <summary>Full cone angle in radians. Half of this is the half-angle from the axis to the edge.</summary>
    public float ConeAngle { get; set; }

    /// <summary>
    /// Inner cone (radians) used for soft-edge falloff. Below this angle, full intensity; between
    /// the inner and outer cone the intensity is smoothstep-faded; outside, zero.
    /// </summary>
    public float InnerConeAngle { get; set; }

    /// <summary>Optional per-light attenuation override.</summary>
    public Sampling.IAttenuation? Attenuation { get; set; }

    internal SpotLight(string name, Float4x4 xform, Float3 color, float range, float coneAngle)
        : base(name, xform, color)
    {
        Range = range;
        ConeAngle = coneAngle;
        InnerConeAngle = coneAngle * 0.8f;
    }

    /// <inheritdoc />
    public override bool Sample(Float3 position, out Float3 toLight, out Float3 radiance, out float maxDist,
                                Sampling.IAttenuation defaultAttenuation)
    {
        var lp = WorldPosition;
        var d = lp - position;
        float dist = Float3.Length(d);
        if (dist <= 1e-6f) { toLight = Float3.UnitY; radiance = Float3.Zero; maxDist = 0; return false; }
        toLight = d / dist;
        maxDist = dist;
        if (dist >= Range) { radiance = Float3.Zero; return false; }

        // angle between light axis (-forward = direction the light *shines*) and (-toLight, the
        // direction from the light to the surface).
        var axis = WorldForward; // points away from the light
        float cosA = Float3.Dot(axis, -toLight);
        float cosOuter = (float)System.Math.Cos(ConeAngle * 0.5f);
        if (cosA < cosOuter) { radiance = Float3.Zero; return false; }
        float cosInner = (float)System.Math.Cos(InnerConeAngle * 0.5f);

        float coneFade = 1f;
        if (cosA < cosInner)
        {
            float t = (cosA - cosOuter) / System.Math.Max(1e-5f, cosInner - cosOuter);
            coneFade = t * t * (3f - 2f * t); // smoothstep
        }

        var atten = Attenuation ?? defaultAttenuation;
        float a = atten.Evaluate(dist, Range);
        radiance = Color * (a * coneFade);
        return radiance.X + radiance.Y + radiance.Z > 0;
    }
}
