// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Resources
{
    /// <summary>
    /// Default shaders embedded in the runtime
    /// </summary>
    public enum DefaultShader
    {
        Standard,
        Invalid,
        UI,
        Gizmos,
        Blit,
        Depth,
        ProceduralSkybox,
        Tonemapper,
        TAA,
        SSR,
        Bloom,
        BokehDoF,
        GBufferCombine,
        AmbientLight,
        DirectionalLight,
        PointLight,
        SpotLight
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
        UnitCube // 1mcube.obj - 1 meter cube
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
        Noise
    }

    /// <summary>
    /// Default shader include files (GLSL)
    /// </summary>
    public enum DefaultShaderInclude
    {
        Fragment,
        PBR,
        Random,
        ShaderVariables,
        Utilities,
        VertexAttributes
    }
}
