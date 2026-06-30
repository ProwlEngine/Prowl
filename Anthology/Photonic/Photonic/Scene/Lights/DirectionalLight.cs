using Prowl.Vector;

namespace Prowl.Photonic.Scene.Lights;

/// <summary>Parallel light source (no falloff). Radiance is constant; shadow ray goes to infinity.</summary>
public sealed class DirectionalLight : Light
{
    internal DirectionalLight(string name, Float4x4 xform, Float3 color) : base(name, xform, color) { }

    /// <inheritdoc />
    public override bool Sample(Float3 position, out Float3 toLight, out Float3 radiance, out float maxDist,
                                Sampling.IAttenuation defaultAttenuation)
    {
        // The transform's forward is the direction the light *travels*. Surfaces receive from -forward.
        toLight = -WorldForward;
        radiance = Color;
        maxDist = float.PositiveInfinity;
        return true;
    }
}
