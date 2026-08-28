// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Resources;

/// <summary>
/// Default shaders embedded in the runtime
/// </summary>
public enum DefaultShader
{
    // Standard PBR family. One shader per alpha mode and cull mode combination, the way the
    // legacy fixed-function sets were split, so render state stays static per shader.
    Standard,
    StandardDoubleSided,
    StandardCutout,
    StandardCutoutDoubleSided,
    StandardTransparent,
    StandardTransparentDoubleSided,
    // Anisotropic specular (brushed metal, hair), same split.
    StandardAnisotropic,
    StandardAnisotropicDoubleSided,
    StandardAnisotropicCutout,
    StandardAnisotropicCutoutDoubleSided,
    StandardAnisotropicTransparent,
    StandardAnisotropicTransparentDoubleSided,

    // Unlit family, same split.
    Unlit,
    UnlitDoubleSided,
    UnlitCutout,
    UnlitCutoutDoubleSided,
    UnlitTransparent,
    UnlitTransparentDoubleSided,

    Sprite,
    Line,
    Invalid,
    UI,
    Gizmos,
    Blit,
    Particle,
    Terrain,
    Grass,
    Refraction,
    DefaultUI,
    DefaultText,
    DefaultTextMesh,

    ProceduralSkybox,
    GradientSkybox,
    CubemapSkybox,
    Tonemapper,
    SSR,
    FXAA,
    SMAA,
    Bloom,
    BokehDoF,
    GTAO,
    Grid,
    CinematicEffects,
    VolumetricFog,
    TAA,
    MotionBlur,
    GizmoIcon,
    AutoExposure
}

/// <summary>
/// Default models/meshes embedded in the runtime
/// </summary>
public enum DefaultModel
{
    Cube,
    Sphere,
    Cylinder,
    Plane,
    SkyDome,
}

/// <summary>
/// Default materials embedded in the runtime
/// </summary>
public enum DefaultMaterial
{
    Standard,
    Particle,
    Terrain,
    Grass,
}

/// <summary>
/// Default sprites embedded in the runtime
/// </summary>
public enum DefaultSprite
{
    /// <summary>A rounded nine-slice panel (white fill, subtle border) used as the default UI background.</summary>
    UIPanel,
}

/// <summary>
/// Default textures embedded in the runtime
/// </summary>
public enum DefaultTexture
{
    White,
    Gray18, // Also accessible as Gray
    Normal,
    Surface,
    Emission,
    Grid,
    Noise,

    // UI
    UIPanel,

    // Scene helpers
    Handle,

    // Gizmo icons
    IconCamera,
    IconLight,
}

/// <summary>
/// Default fonts embedded in the runtime
/// </summary>
public enum DefaultFont
{
    Default, // Geist-Regular
}

/// <summary>
/// Default shader include files (GLSL)
/// </summary>
public enum DefaultShaderInclude
{
    ProwlCG,
    PBR,
    Random,
    ShaderVariables,
    Shadow,
    VertexAttributes,
    Lighting,
    LightBVH,
    StandardCore,
    FastNoiseLite,
    SimplexNoise4D
}
