using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Prowl.Runtime.Utils;
using Veldrid;
using System.Linq;

namespace Prowl.Runtime
{
    public sealed class ShaderVariant
    {
        [SerializeField, HideInInspector]
        private KeywordState variantKeywords;

        
        [HideInInspector]
        public StageInput[] VertexInputs;
        
        [HideInInspector]
        public Uniform[] Uniforms;

        [HideInInspector]
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
            this.variantKeywords = keywords;
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
    }
}