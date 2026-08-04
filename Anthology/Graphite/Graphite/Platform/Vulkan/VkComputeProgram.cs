using System;

using Silk.NET.Vulkan;

using VkPipelineHandle = Silk.NET.Vulkan.Pipeline;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkComputeProgram : ComputeProgram, IVkDescriptorProgram
{
    DescriptorSetLayout[] IVkDescriptorProgram.DescriptorSetLayouts => DescriptorSetLayouts;
    DescriptorResourceCounts[] IVkDescriptorProgram.PerSetCounts => PerSetCounts;
    PipelineLayout IVkDescriptorProgram.PipelineLayout => PipelineLayout;
    uint IVkDescriptorProgram.ResourceSetCount => ResourceSetCount;
    VkDescriptorSetCache IVkDescriptorProgram.DescriptorCache => DescriptorCache;

    private readonly VkGraphicsDevice _gd;
    private readonly ShaderModule _module;

    /// <summary>DSLs by set index. Empty DSL for gaps.</summary>
    internal readonly DescriptorSetLayout[] DescriptorSetLayouts;

    /// <summary>Per-set resource counts, parallel to DescriptorSetLayouts.</summary>
    internal readonly DescriptorResourceCounts[] PerSetCounts;

    internal readonly PipelineLayout PipelineLayout;
    internal readonly VkPipelineHandle DevicePipeline;
    internal readonly uint ResourceSetCount;
    internal readonly int TotalDynamicUboCount;
    internal readonly ResourceRefCount RefCount;

    /// <summary>
    /// Cross-frame descriptor set cache for this program, keyed by bound resources.
    /// </summary>
    internal readonly VkDescriptorSetCache DescriptorCache;

    private readonly DescriptorSetLayout _emptyDescriptorSetLayout;

    public VkComputeProgram(VkGraphicsDevice gd, ref ComputeDescription description)
        : base(ref description)
    {
        _gd = gd;
        RefCount = new ResourceRefCount(DestroyNative);

        ShaderStageDescription stage = description.Stage;
        ShaderModuleCreateInfo shaderModuleCI = new() { SType = StructureType.ShaderModuleCreateInfo };
        fixed (byte* codePtr = stage.ShaderBytes)
        {
            shaderModuleCI.CodeSize = (UIntPtr)stage.ShaderBytes.Length;
            shaderModuleCI.PCode = (uint*)codePtr;
            _gd.Vk.CreateShaderModule(gd.Device, in shaderModuleCI, null, out _module).CheckResult();
        }

        (DescriptorSetLayouts, PerSetCounts, PipelineLayout, ResourceSetCount, TotalDynamicUboCount, _emptyDescriptorSetLayout)
            = VkDescriptorLayoutBuilder.Build(_gd, ResourceLayoutsArray);

        ComputePipelineCreateInfo pipelineCI = new() { SType = StructureType.ComputePipelineCreateInfo };
        pipelineCI.Layout = PipelineLayout;

        PipelineShaderStageCreateInfo stageCI = new() { SType = StructureType.PipelineShaderStageCreateInfo };
        stageCI.Module = _module;
        stageCI.Stage = VkFormats.ToVkShaderStages(ShaderStages.Compute);
        stageCI.PName = CommonStrings.mainUtf8;
        pipelineCI.Stage = stageCI;

        _gd.Vk.CreateComputePipelines(_gd.Device, default, 1, in pipelineCI, null, out VkPipelineHandle pipeline).CheckResult();
        DevicePipeline = pipeline;

        DescriptorCache = new VkDescriptorSetCache(_gd);

        Constructor_RecordAllocations(stage);
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore() => RefCount.Decrement();

    private void DestroyNative()
    {
        _gd.Vk.DestroyPipeline(_gd.Device, DevicePipeline, null);
        _gd.Vk.DestroyShaderModule(_gd.Device, _module, null);
        VkDescriptorLayoutBuilder.Destroy(_gd, DescriptorSetLayouts, _emptyDescriptorSetLayout, PipelineLayout);
        DescriptorCache.Destroy();
        DisposeCore_RecordFrees();
    }
}
