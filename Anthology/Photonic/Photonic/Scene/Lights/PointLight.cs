using Prowl.Vector;

namespace Prowl.Photonic.Scene.Lights;

/// <summary>Omnidirectional point light with a finite range and pluggable attenuation curve.</summary>
public sealed class PointLight : Light
{
    /// <summary>World-space range. Past this distance the light contributes nothing.</summary>
    public float Range { get; set; }

    /// <summary>
    /// Optional per-light attenuation override. When null, the scene's
    /// <see cref="BakeScene.DefaultAttenuation"/> is used.
    /// </summary>
    public Sampling.IAttenuation? Attenuation { get; set; }

    internal PointLight(string name, Float4x4 xform, Float3 color, float range)
        : base(name, xform, color)
    {
        Range = range;
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

        var atten = Attenuation ?? defaultAttenuation;
        float a = atten.Evaluate(dist, Range);
        radiance = Color * a;
        return a > 0;
    }
}
