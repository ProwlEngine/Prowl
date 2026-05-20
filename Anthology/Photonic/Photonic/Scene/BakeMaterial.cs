using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// Material used during baking. Only the channels Photonic actually consumes are exposed:
/// diffuse albedo (driving bounce colour), an optional emissive source, and per-channel UV
/// transforms / texture references.
/// </summary>
/// <remarks>
/// Plain mutable POCO; populate fields after <see cref="BakeScene.CreateMaterial"/>.
/// </remarks>
public sealed class BakeMaterial
{
    /// <summary>Material name as registered on the scene.</summary>
    public string Name { get; }

    /// <summary>Linear-space base colour. Used directly when no texture is bound.</summary>
    public Float3 DiffuseColor { get; set; } = new Float3(0.7f, 0.7f, 0.7f);

    /// <summary>Optional diffuse albedo texture. Sampled at the bake hit's <see cref="DiffuseUVLayer"/>.</summary>
    public BakeTexture? DiffuseTexture { get; set; }

    /// <summary>UV layer the diffuse texture is sampled from. Defaults to <c>"UV0"</c>.</summary>
    public string DiffuseUVLayer { get; set; } = "UV0";

    /// <summary>Linear-space emissive colour added to direct lighting at any bake hit on this material.</summary>
    public Float3 Emissive { get; set; } = Float3.Zero;

    /// <summary>Optional emissive texture (modulated by <see cref="Emissive"/>).</summary>
    public BakeTexture? EmissiveTexture { get; set; }

    /// <summary>UV layer the emissive texture is sampled from. Defaults to <c>"UV0"</c>.</summary>
    public string EmissiveUVLayer { get; set; } = "UV0";

    /// <summary>Optional alpha mask; below <see cref="AlphaCutoff"/> the shadow ray treats the hit as a miss.</summary>
    public BakeTexture? AlphaTexture { get; set; }

    /// <summary>Alpha cutoff for the alpha-mask shadow test.</summary>
    public float AlphaCutoff { get; set; } = 0.5f;

    /// <summary>True if the surface is double-sided; controls back-face shading.</summary>
    public bool DoubleSided { get; set; }

    internal BakeMaterial(string name) { Name = name; }
}
