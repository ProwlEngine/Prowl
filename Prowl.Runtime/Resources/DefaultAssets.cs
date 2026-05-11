// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Resources;

/// <summary>
/// Default shaders embedded in the runtime
/// </summary>
public enum DefaultShader
{
    Standard,
    StandardTransparent,
    StandardAnisotropic,
    Unlit,
    Line,
    Invalid,
    UI,
    Gizmos,
    Blit,
    Particle,
    Terrain,
    Grass,
    Refraction,
    GameUI,

    ProceduralSkybox,
    GradientSkybox,
    CubemapSkybox,
    Tonemapper,
    SSR,
    FXAA,
    Bloom,
    BokehDoF,
    GTAO,
    Grid,
    CinematicEffects,
    VolumetricFog,
    SDFRaymarch,
    TAA,
    MotionBlur,
    GizmoIcon
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

    // Gizmo icons
    IconCamera,
    IconLight,
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
    StandardSurface,
    FastNoiseLite,
    SimplexNoise4D
}
