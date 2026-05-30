using Prowl.Vector;

namespace Prowl.Photonic.Scene.Lights;

/// <summary>
/// Base type for all bake lights. Sub-types define the shape (point, directional, spot).
/// </summary>
public abstract class Light
{
    /// <summary>Light name (informational).</summary>
    public string Name { get; }

    /// <summary>Linear-RGB radiant intensity. Combined with attenuation to produce irradiance at a hit.</summary>
    public Float3 Color { get; set; }

    /// <summary>Object-to-world transform. The +Z column is the light's forward axis (direction the light travels).</summary>
    public Float4x4 Transform { get; set; }

    /// <summary>Whether this light contributes shadow rays to the bake.</summary>
    public bool CastsShadows { get; set; } = true;

    /// <summary>
    /// Whether this light's <b>direct</b> contribution is written into the lightmap at the baked
    /// texel itself. Its <i>indirect</i> (bounced) contribution is always baked regardless.
    /// <para>
    /// Set <c>true</c> (default) for fully-baked lights. Set <c>false</c> for "mixed" lights whose
    /// direct lighting + shadows are applied in realtime by the runtime shader while only their
    /// bounced GI is baked. Realtime-only lights should simply not be added to the bake scene.
    /// </para>
    /// Gated by <see cref="BakeOptions.IncludeDirectLighting"/>: when that master switch is off, no
    /// light's direct is baked at the texel regardless of this flag.
    /// </summary>
    public bool BakeDirect { get; set; } = true;

    /// <summary>Indirect-only scale applied during bounce sampling (1.0 = no bias).</summary>
    public float IndirectScale { get; set; } = 1.0f;

    protected Light(string name, Float4x4 transform, Float3 color)
    {
        Name = name;
        Transform = transform;
        Color = color;
    }

    /// <summary>World-space position read from the translation column of <see cref="Transform"/>.</summary>
    public Float3 WorldPosition
    {
        get
        {
            var c3 = Transform.c3;
            return new Float3(c3.X, c3.Y, c3.Z);
        }
    }

    /// <summary>
    /// World-space forward direction (the +Z column, i.e. the direction the light *travels*).
    /// For directional lights this is the direction the photons move; surfaces receive radiance
    /// from -WorldForward. Built to match the OpenGL/right-handed convention Prowl.Vector uses.
    /// </summary>
    public Float3 WorldForward
    {
        get
        {
            var c2 = Transform.c2;
            return Float3.Normalize(new Float3(c2.X, c2.Y, c2.Z));
        }
    }

    /// <summary>
    /// Sample the irradiance arriving at <paramref name="position"/> from this light along
    /// <paramref name="toLight"/>. Returns false if the light contributes nothing (out of range,
    /// outside cone, behind). The shadow ray (origin, target, maxDist) is filled regardless.
    /// </summary>
    public abstract bool Sample(Float3 position, out Float3 toLight, out Float3 radiance, out float maxDist,
                                Sampling.IAttenuation defaultAttenuation);
}
