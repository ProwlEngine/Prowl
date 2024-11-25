// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;
using Prowl.Echo;

namespace Prowl.Runtime.Rendering;

public sealed class ComputeVariant
{
    [SerializeField, HideInInspector]
    private readonly KeywordState variantKeywords;

    [HideInInspector]
    public Uniform[] Uniforms;

    [HideInInspector]
    public uint ThreadGroupSizeX;

    [HideInInspector]
    public uint ThreadGroupSizeY;

    [HideInInspector]
    public uint ThreadGroupSizeZ;


    [HideInInspector]
    public ShaderDescription VulkanShader;

    [HideInInspector]
    public ShaderDescription OpenGLShader;

    [HideInInspector]
    public ShaderDescription OpenGLESShader;

    [HideInInspector]
    public ShaderDescription MetalShader;

    [HideInInspector]
    public ShaderDescription Direct3D11Shader;


    public KeywordState VariantKeywords => variantKeywords;


    private ComputeVariant() { }

    public ComputeVariant(KeywordState keywords)
    {
        variantKeywords = keywords;
    }

    public ShaderDescription GetProgramForBackend()
    {
        GraphicsBackend backend = Graphics.Device.BackendType;

        ShaderDescription Validate(ShaderDescription shader)
        {
            if (shader.ShaderBytes == null)
                throw new Exception($"No compiled shaders for backend: {backend}");

            if (shader.Stage != ShaderStages.Compute)
                throw new Exception($"Invalid shader stage for compute shader: {shader.Stage}");

            return shader;
        }

        return backend switch
        {
            GraphicsBackend.Direct3D11 => Validate(Direct3D11Shader),
            GraphicsBackend.Vulkan => Validate(VulkanShader),
            GraphicsBackend.OpenGL => Validate(OpenGLShader),
            GraphicsBackend.OpenGLES => Validate(OpenGLESShader),
            GraphicsBackend.Metal => Validate(MetalShader),
            _ => throw new Exception($"Invalid backend: {backend}")
        };
    }
}
