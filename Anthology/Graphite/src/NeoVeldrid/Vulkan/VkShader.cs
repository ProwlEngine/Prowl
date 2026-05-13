using Silk.NET.Vulkan;
using static NeoVeldrid.Vk.VulkanUtil;
using System;

namespace NeoVeldrid.Vk
{
    internal unsafe class VkShader : Shader
    {
        private readonly VkGraphicsDevice _gd;
        private readonly ShaderModule _shaderModule;
        private bool _disposed;
        private string _name;

        public ShaderModule ShaderModule => _shaderModule;

        public override bool IsDisposed => _disposed;

        public VkShader(VkGraphicsDevice gd, ref ShaderDescription description)
            : base(description.Stage, description.EntryPoint)
        {
            _gd = gd;

            ShaderModuleCreateInfo shaderModuleCI = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo
            };
            fixed (byte* codePtr = description.ShaderBytes)
            {
                shaderModuleCI.CodeSize = (UIntPtr)description.ShaderBytes.Length;
                shaderModuleCI.PCode = (uint*)codePtr;
                Result result = _gd.Vk.CreateShaderModule(gd.Device, in shaderModuleCI, null, out _shaderModule);
                CheckResult(result);
            }
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                _gd.SetResourceName(this, value);
            }
        }

        public override void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _gd.Vk.DestroyShaderModule(_gd.Device, ShaderModule, null);
            }
        }
    }
}
