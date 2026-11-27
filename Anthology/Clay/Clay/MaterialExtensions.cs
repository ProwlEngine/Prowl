// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// Optional clearcoat layer on a <see cref="Material"/>, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_clearcoat/README.md">KHR_materials_clearcoat</see>.
/// </summary>
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
    /// <summary>Scale of <see cref="NormalTexture"/>'s X and Y. Default 1.</summary>
    public float NormalScale { get; init; } = 1f;
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

/// <summary>
/// Thin film interference on the surface, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_iridescence/README.md">KHR_materials_iridescence</see>.
/// </summary>
public sealed record IridescenceExtension
{
    /// <summary>Thin film intensity (0..1). Default 0.</summary>
    public float Factor { get; init; }
    /// <summary>Texture whose R channel scales <see cref="Factor"/>.</summary>
    public MaterialTextureSlot? FactorTexture { get; init; }
    /// <summary>Index of refraction of the film. Default 1.3.</summary>
    public float Ior { get; init; } = 1.3f;
    /// <summary>Film thickness in nanometers where the thickness texture reads 0. Default 100.</summary>
    public float ThicknessMinimum { get; init; } = 100f;
    /// <summary>Film thickness in nanometers where the thickness texture reads 1, and without a texture. Default 400.</summary>
    public float ThicknessMaximum { get; init; } = 400f;
    /// <summary>Texture whose G channel blends between the minimum and maximum thickness.</summary>
    public MaterialTextureSlot? ThicknessTexture { get; init; }
}

/// <summary>
/// Anisotropic specular roughness, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_anisotropy/README.md">KHR_materials_anisotropy</see>.
/// </summary>
public sealed record AnisotropyExtension
{
    /// <summary>Anisotropy strength (0..1). Default 0.</summary>
    public float Strength { get; init; }
    /// <summary>Rotation of the anisotropy direction in radians, counter clockwise from the tangent. Default 0.</summary>
    public float Rotation { get; init; }
    /// <summary>Texture whose RG channels hold a tangent space direction in -1..1 and whose B channel scales <see cref="Strength"/>.</summary>
    public MaterialTextureSlot? Texture { get; init; }
}

/// <summary>
/// Chromatic dispersion of refracted light, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_dispersion/README.md">KHR_materials_dispersion</see>.
/// Only meaningful with <see cref="TransmissionExtension"/> and <see cref="VolumeExtension"/>.
/// </summary>
public sealed record DispersionExtension
{
    /// <summary>Dispersion as 20 over the Abbe number. Default 0, no dispersion.</summary>
    public float Dispersion { get; init; }

    /// <summary>The Abbe number this dispersion stands for, or <c>+Infinity</c> without dispersion.</summary>
    public float AbbeNumber => Dispersion > 0f ? 20f / Dispersion : float.PositiveInfinity;
}

/// <summary>
/// Diffuse light passing through thin surfaces such as leaves or paper, mapping
/// <see href="https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_diffuse_transmission/README.md">KHR_materials_diffuse_transmission</see>.
/// </summary>
public sealed record DiffuseTransmissionExtension
{
    /// <summary>Share of diffuse light transmitted (0..1). Default 0.</summary>
    public float Factor { get; init; }
    /// <summary>Texture whose A channel scales <see cref="Factor"/>.</summary>
    public MaterialTextureSlot? FactorTexture { get; init; }
    /// <summary>Colour of the transmitted light (linear RGB).</summary>
    public Color ColorFactor { get; init; } = new(1f, 1f, 1f, 1f);
    /// <summary>Texture whose RGB channels tint the transmitted light.</summary>
    public MaterialTextureSlot? ColorTexture { get; init; }
}
