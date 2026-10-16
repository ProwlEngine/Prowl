// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Resources;

/// <summary>
/// Default shaders embedded in the runtime
/// </summary>
public enum DefaultShader
{
    /// <summary>Graphite's standard physically based surface, its surface type and inputs set per material.</summary>
    Lit,

    /// <summary>Graphite's standard unlit surface.</summary>
    Unlit,

    /// <summary>Graphite's skies, for scene sky materials.</summary>
    ProceduralSky,
    GradientSky,
    CubemapSky,
    PanoramaSky,
    SixSidedSky,

    Sprite,
    Line,
    Invalid,
    Gizmos,
    GizmoIcon,
    Particle,
    Terrain,
    Grass,
    Refraction,
    DefaultUI,
    DefaultText,
    DefaultTextMesh,
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
    CloudNoise,

    // UI
    UIPanel,

    // Scene helpers
    Handle,

    // Gizmo icons
    IconCamera,
    IconLight,
}

/// <summary>
/// Default 3D textures embedded in the runtime
/// </summary>
public enum DefaultTexture3D
{
    CloudNoise,
}

/// <summary>
/// Default fonts embedded in the runtime
/// </summary>
public enum DefaultFont
{
    Default, // Geist-Regular
}
