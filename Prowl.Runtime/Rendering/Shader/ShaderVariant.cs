// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Veldrid;

namespace Prowl.Runtime
{
    public sealed class ShaderVariant : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector]
        private readonly KeywordState variantKeywords;


        [HideInInspector]
        public VertexInput[] VertexInputs;

        [HideInInspector]
        public ShaderUniform[] Uniforms;

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
            var backend = Graphics.Device.BackendType;

            Exception invalidBackend = new Exception($"No compiled shaders for backend: {backend}");

            return backend switch
            {
                GraphicsBackend.Direct3D11 => Direct3D11Shaders ?? throw invalidBackend,
                GraphicsBackend.Vulkan => VulkanShaders ?? throw invalidBackend,
                GraphicsBackend.OpenGL => OpenGLShaders ?? throw invalidBackend,
                GraphicsBackend.OpenGLES => OpenGLESShaders ?? throw invalidBackend,
                GraphicsBackend.Metal => MetalShaders ?? throw invalidBackend,
                _ => throw invalidBackend,
            };
        }


        [SerializeField]
        private byte[] _serializedShaderStages;

        public void OnBeforeSerialize() => _serializedShaderStages = UniformStages.Select(x => (byte)x).ToArray();
        public void OnAfterDeserialize() => UniformStages = _serializedShaderStages.Select(x => (ShaderStages)x).ToArray();
    }
}
