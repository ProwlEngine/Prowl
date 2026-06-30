using System.Text.Json;
using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>Alpha blending mode for a <see cref="Material"/>, matching glTF semantics.</summary>
public enum MaterialAlphaMode
{
    /// <summary>The rendered output is fully opaque; any alpha channel is ignored.</summary>
    Opaque,

    /// <summary>The rendered output is either fully opaque or fully transparent, depending on
    /// whether the alpha sample crosses <see cref="Material.AlphaCutoff"/>.</summary>
    Mask,

    /// <summary>The alpha value is used to composite the source and destination colors.</summary>
    Blend,
}

/// <summary>
/// Physically-based material description with a metallic-roughness core plus optional extension surfaces.
/// </summary>
/// <remarks>
/// All textures are referenced by index into <see cref="Model.Textures"/>. Per-slot UV transforms
/// (<see cref="MaterialTextureSlot.Offset"/>/<c>Scale</c>/<c>Rotation</c>) carry the result of
/// <c>KHR_texture_transform</c>.
/// </remarks>
public sealed class Material
{
    /// <summary>Material name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Alpha treatment for this material.</summary>
    public MaterialAlphaMode AlphaMode { get; init; } = MaterialAlphaMode.Opaque;

    /// <summary>Cutoff used when <see cref="AlphaMode"/> is <see cref="MaterialAlphaMode.Mask"/>.</summary>
    public float AlphaCutoff { get; init; } = 0.5f;

    /// <summary>True when the material should be rendered without back-face culling.</summary>
    public bool DoubleSided { get; init; }

    /// <summary>True when the material should ignore lighting (KHR_materials_unlit).</summary>
    public bool Unlit { get; init; }

    /// <summary>Base color factor (linear). Multiplied with <see cref="BaseColorTexture"/> when present.</summary>
    public Color BaseColor { get; init; } = new Color(1f, 1f, 1f, 1f);

    /// <summary>Albedo texture slot.</summary>
    public MaterialTextureSlot? BaseColorTexture { get; init; }

    /// <summary>Metallic factor (0 = dielectric, 1 = metal). Multiplied with the blue channel of <see cref="MetallicRoughnessTexture"/>.</summary>
    public float Metallic { get; init; } = 1f;

    /// <summary>Roughness factor (0 = smooth, 1 = fully rough). Multiplied with the green channel of <see cref="MetallicRoughnessTexture"/>.</summary>
    public float Roughness { get; init; } = 1f;

    /// <summary>Packed metallic-roughness texture (glTF convention: G=roughness, B=metallic).</summary>
    public MaterialTextureSlot? MetallicRoughnessTexture { get; init; }

    /// <summary>Tangent-space normal map.</summary>
    public MaterialTextureSlot? NormalTexture { get; init; }

    /// <summary>Scale applied to the tangent-space normal.</summary>
    public float NormalScale { get; init; } = 1f;

    /// <summary>Ambient-occlusion map (R channel).</summary>
    public MaterialTextureSlot? OcclusionTexture { get; init; }

    /// <summary>Strength of the occlusion contribution.</summary>
    public float OcclusionStrength { get; init; } = 1f;

    /// <summary>Emissive color factor (linear).</summary>
    public Color EmissiveFactor { get; init; } = new Color(0f, 0f, 0f, 1f);

    /// <summary>Emissive texture.</summary>
    public MaterialTextureSlot? EmissiveTexture { get; init; }

    /// <summary>Emissive strength multiplier (KHR_materials_emissive_strength).</summary>
    public float EmissiveStrength { get; init; } = 1f;

    /// <summary>Clearcoat layer parameters, or <c>null</c> if KHR_materials_clearcoat was not present.</summary>
    public ClearcoatExtension? Clearcoat { get; init; }

    /// <summary>Sheen layer parameters (KHR_materials_sheen).</summary>
    public SheenExtension? Sheen { get; init; }

    /// <summary>Optical transmission parameters (KHR_materials_transmission).</summary>
    public TransmissionExtension? Transmission { get; init; }

    /// <summary>Volume parameters (KHR_materials_volume); only meaningful with transmission.</summary>
    public VolumeExtension? Volume { get; init; }

    /// <summary>Index-of-refraction override (KHR_materials_ior).</summary>
    public IorExtension? Ior { get; init; }

    /// <summary>Specular reflectance tint and intensity (KHR_materials_specular).</summary>
    public SpecularExtension? Specular { get; init; }

    /// <summary>Legacy specular-glossiness workflow (KHR_materials_pbrSpecularGlossiness).</summary>
    public SpecularGlossinessExtension? SpecularGlossiness { get; init; }

    /// <summary>
    /// Extension data we do not model with typed surfaces (including VRMC_*).
    /// Keys are extension names; values are the raw JSON elements as parsed.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> RawExtensions { get; init; } =
        new Dictionary<string, JsonElement>();
}

/// <summary>
/// A reference to a <see cref="Texture"/> along with the UV channel and per-slot UV transform.
/// </summary>
public sealed class MaterialTextureSlot
{
    /// <summary>Index into <see cref="Model.Textures"/>.</summary>
    public required int TextureIndex { get; init; }

    /// <summary>UV channel (0..7) used to sample the texture.</summary>
    public int UVChannel { get; init; }

    /// <summary>UV translation applied before sampling (KHR_texture_transform offset).</summary>
    public Float2 Offset { get; init; } = Float2.Zero;

    /// <summary>UV scale applied before sampling.</summary>
    public Float2 Scale { get; init; } = Float2.One;

    /// <summary>UV rotation, in radians, applied around the origin.</summary>
    public float Rotation { get; init; }
}
