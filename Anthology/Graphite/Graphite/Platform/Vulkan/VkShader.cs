using System;
using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

/// <summary>Shared per-variant shader: modules, set layouts, pipeline layout and descriptor cache. Refcounted by <see cref="VkShaderCache"/>.</summary>
internal sealed unsafe class VkShader
{
    private readonly VkGraphicsDevice _gd;
    private readonly Dictionary<ShaderStages, ShaderModule> _modules = [];
    private readonly Dictionary<ShaderStages, string> _entryPoints = [];
    private readonly DescriptorSetLayout _emptyDescriptorSetLayout;
    private readonly long _profiledShaderBytes;

    internal readonly VkShaderKey Key;
    internal readonly DescriptorSetLayout[] DescriptorSetLayouts;
    internal readonly DescriptorResourceCounts[] PerSetCounts;
    internal readonly PipelineLayout PipelineLayout;
    internal readonly uint ResourceSetCount;
    internal readonly VkDescriptorSetCache DescriptorCache;

    internal int RefCount;

    internal IReadOnlyDictionary<ShaderStages, ShaderModule> Modules => _modules;

    internal VkShader(VkGraphicsDevice gd, VkShaderKey key, ShaderStageDescription[] stages, ResourceLayoutDescription[] layouts)
    {
        _gd = gd;
        Key = key;

        long bytes = 0;
        for (int i = 0; i < stages.Length; i++)
        {
            ShaderStageDescription sd = stages[i];
            ShaderModuleCreateInfo shaderModuleCI = new() { SType = StructureType.ShaderModuleCreateInfo };
            fixed (byte* codePtr = sd.ShaderBytes)
            {
                shaderModuleCI.CodeSize = (UIntPtr)sd.ShaderBytes.Length;
                shaderModuleCI.PCode = (uint*)codePtr;
                _gd.Vk.CreateShaderModule(gd.Device, in shaderModuleCI, null, out ShaderModule module).CheckResult();
                _modules[sd.Stage] = module;
                _entryPoints[sd.Stage] = sd.EntryPoint;
            }

            bytes += sd.ShaderBytes.Length;
        }

        (DescriptorSetLayouts, PerSetCounts, PipelineLayout, ResourceSetCount, _emptyDescriptorSetLayout)
            = VkDescriptorLayoutBuilder.Build(_gd, layouts);

        DescriptorCache = new VkDescriptorSetCache(_gd);

        _profiledShaderBytes = bytes;
        _gd.Counters.Allocate(AllocBin.Shader, bytes);
    }

    internal ShaderModule GetModule(ShaderStages stage)
    {
        if (!_modules.TryGetValue(stage, out ShaderModule module))
            throw new RenderException($"GraphicsProgram does not contain a module for stage {stage}.");
        return module;
    }

    internal string GetEntryPoint(ShaderStages stage) => _entryPoints[stage];

    internal void Destroy()
    {
        _gd.Counters.Free(AllocBin.Shader, _profiledShaderBytes);

        DescriptorCache.Destroy();

        foreach (ShaderModule m in _modules.Values)
            _gd.Vk.DestroyShaderModule(_gd.Device, m, null);

        VkDescriptorLayoutBuilder.Destroy(_gd, DescriptorSetLayouts, _emptyDescriptorSetLayout, PipelineLayout);
    }
}
