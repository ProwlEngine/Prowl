// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Veldrid;

namespace Prowl.Runtime.Rendering;

public sealed class ShaderVariant : ISerializationCallbackReceiver
{
    [SerializeField, HideInInspector]
    private readonly KeywordState variantKeywords;


    [HideInInspector]
    public VertexInput[] VertexInputs;

    [HideInInspector]
    public Uniform[] Uniforms;

    [NonSerialized]
    public ShaderStages[] UniformStages;


    [HideInInspector]
    public ShaderDescription[] VulkanShaders;

    [HideInInspector]
    public ShaderDescription[] OpenGLShaders;

    [HideInInspector]
    public ShaderDescription[] OpenGLESShaders;

    [HideInInspector]
    public ShaderDescription[] MetalShaders;

    [HideInInspector]
    public ShaderDescription[] Direct3D11Shaders;


    public KeywordState VariantKeywords => variantKeywords;


    private ShaderVariant() { }

    public ShaderVariant(KeywordState keywords)
    {
        variantKeywords = keywords;
    }

    public ShaderDescription[] GetProgramsForBackend()
    {
        GraphicsBackend backend = Graphics.Device.BackendType;

        ShaderDescription[] Validate(ShaderDescription[] shaders)
        {
            if (shaders == null || shaders.Length == 0)
                throw new Exception($"No compiled shaders for backend: {backend}");

            return shaders;
        }

        return backend switch
        {
            GraphicsBackend.Direct3D11 => Validate(Direct3D11Shaders),
            GraphicsBackend.Vulkan => Validate(VulkanShaders),
            GraphicsBackend.OpenGL => Validate(OpenGLShaders),
            GraphicsBackend.OpenGLES => Validate(OpenGLESShaders),
            GraphicsBackend.Metal => Validate(MetalShaders),
            _ => throw new Exception($"Invalid backend: {backend}")
        };
    }


    [SerializeField]
    private byte[] _serializedShaderStages;

    public void OnBeforeSerialize() => _serializedShaderStages = UniformStages.Select(x => (byte)x).ToArray();
    public void OnAfterDeserialize() => UniformStages = _serializedShaderStages.Select(x => (ShaderStages)x).ToArray();
}
