using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// Optional clearcoat layer on a <see cref="Material"/>, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_clearcoat/README.md">KHR_materials_clearcoat</see>.
/// </summary>
/// <remarks>
/// Maps onto HDRP Lit's clearcoat layer and URP Lit's "Clear Coat" feature.
/// </remarks>
public sealed record ClearcoatExtension
{
    /// <summary>Clearcoat layer intensity (0..1). Default 0.</summary>
    public float Factor { get; init; }
    /// <summary>Clearcoat roughness (0..1). Default 0.</summary>
    public float Roughness { get; init; }

    /// <summary>Texture whose R channel scales <see cref="Factor"/>.</summary>
    public MaterialTextureSlot? FactorTexture { get; init; }
    /// <summary>Texture whose G channel scales <see cref="Roughness"/>.</summary>
    public MaterialTextureSlot? RoughnessTexture { get; init; }
    /// <summary>Tangent-space normal map applied to the clearcoat layer.</summary>
    public MaterialTextureSlot? NormalTexture { get; init; }
}

/// <summary>
/// Velvet-like microfiber surface, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_sheen/README.md">KHR_materials_sheen</see>.
/// </summary>
public sealed record SheenExtension
{
    /// <summary>Sheen color (linear RGB).</summary>
    public Color ColorFactor { get; init; } = new(0f, 0f, 0f, 1f);
    /// <summary>Sheen roughness (0..1).</summary>
    public float RoughnessFactor { get; init; }
    /// <summary>Sheen color texture (RGB).</summary>
    public MaterialTextureSlot? ColorTexture { get; init; }
    /// <summary>Sheen roughness texture (A channel).</summary>
    public MaterialTextureSlot? RoughnessTexture { get; init; }
}

/// <summary>
/// Optical transmission (thin-walled), mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_transmission/README.md">KHR_materials_transmission</see>.
/// </summary>
public sealed record TransmissionExtension
{
    /// <summary>Transmission factor (0..1). Default 0.</summary>
    public float Factor { get; init; }
    /// <summary>Texture whose R channel scales <see cref="Factor"/>.</summary>
    public MaterialTextureSlot? FactorTexture { get; init; }
}

/// <summary>
/// Volumetric (thick) surface properties, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_volume/README.md">KHR_materials_volume</see>.
/// Only meaningful when paired with <see cref="TransmissionExtension"/>.
/// </summary>
public sealed record VolumeExtension
{
    /// <summary>Geometric thickness factor in meters. Default 0.</summary>
    public float ThicknessFactor { get; init; }
    /// <summary>Texture whose G channel scales <see cref="ThicknessFactor"/>.</summary>
    public MaterialTextureSlot? ThicknessTexture { get; init; }
    /// <summary>
    /// Distance at which light passing through the volume is attenuated to 1/e of its original
    /// strength. <c>+Infinity</c> means no attenuation.
    /// </summary>
    public float AttenuationDistance { get; init; } = float.PositiveInfinity;
    /// <summary>Color of the attenuation tint (linear RGB).</summary>
    public Color AttenuationColor { get; init; } = new(1f, 1f, 1f, 1f);
}

/// <summary>
/// Index-of-refraction override, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_ior/README.md">KHR_materials_ior</see>.
/// </summary>
public sealed record IorExtension
{
    /// <summary>The IOR value. Default 1.5 (window glass).</summary>
    public float Ior { get; init; } = 1.5f;
}

/// <summary>
/// Per-channel specular reflectance override, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_specular/README.md">KHR_materials_specular</see>.
/// </summary>
public sealed record SpecularExtension
{
    /// <summary>Specular reflectance intensity (0..1).</summary>
    public float Factor { get; init; } = 1f;
    /// <summary>Tint applied to the specular reflectance (linear RGB).</summary>
    public Color ColorFactor { get; init; } = new(1f, 1f, 1f, 1f);
    /// <summary>Texture whose A channel scales <see cref="Factor"/>.</summary>
    public MaterialTextureSlot? FactorTexture { get; init; }
    /// <summary>Texture whose RGB channels tint the specular reflectance.</summary>
    public MaterialTextureSlot? ColorTexture { get; init; }
}

/// <summary>
/// Legacy specular-glossiness workflow, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Archived/KHR_materials_pbrSpecularGlossiness/README.md">KHR_materials_pbrSpecularGlossiness</see>.
/// </summary>
/// <remarks>
/// Archived by Khronos but still produced by many tools (Substance Painter pre-2020, Maya, etc.).
/// </remarks>
public sealed record SpecularGlossinessExtension
{
    /// <summary>Diffuse color factor (linear RGBA).</summary>
    public Color DiffuseFactor { get; init; } = new(1f, 1f, 1f, 1f);
    /// <summary>Specular color factor (linear RGB).</summary>
    public Color SpecularFactor { get; init; } = new(1f, 1f, 1f, 1f);
    /// <summary>Glossiness (0..1).</summary>
    public float GlossinessFactor { get; init; } = 1f;
    /// <summary>Diffuse texture (RGB).</summary>
    public MaterialTextureSlot? DiffuseTexture { get; init; }
    /// <summary>Packed specular (RGB) + glossiness (A) texture.</summary>
    public MaterialTextureSlot? SpecularGlossinessTexture { get; init; }
}
