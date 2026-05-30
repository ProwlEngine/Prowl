using System;

using Silk.NET.Vulkan;

using VkPipelineHandle = Silk.NET.Vulkan.Pipeline;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkComputeProgram : ComputeProgram
{
    private readonly VkGraphicsDevice _gd;
    private readonly ShaderModule _module;

    /// <summary>Descriptor-set layouts indexed by set index. Empty-DSL for gaps.</summary>
    internal readonly DescriptorSetLayout[] DescriptorSetLayouts;

    /// <summary>Per-set descriptor resource counts parallel to <see cref="DescriptorSetLayouts"/>.</summary>
    internal readonly DescriptorResourceCounts[] PerSetCounts;

    internal readonly PipelineLayout PipelineLayout;
    internal readonly VkPipelineHandle DevicePipeline;
    internal readonly uint ResourceSetCount;
    internal readonly int TotalDynamicUboCount;
    internal readonly ResourceRefCount RefCount;

    private DescriptorSetLayout _emptyDescriptorSetLayout;
    private bool _disposed;
    private string _name;

    public override bool IsDisposed => _disposed;

    public VkComputeProgram(VkGraphicsDevice gd, ref ComputeDescription description)
        : base(ref description)
    {
        _gd = gd;
        RefCount = new ResourceRefCount(DisposeCore);

        ShaderStageDescription stage = description.Stage;
        ShaderModuleCreateInfo shaderModuleCI = new() { SType = StructureType.ShaderModuleCreateInfo };
        fixed (byte* codePtr = stage.ShaderBytes)
        {
            shaderModuleCI.CodeSize = (UIntPtr)stage.ShaderBytes.Length;
            shaderModuleCI.PCode = (uint*)codePtr;
            _gd.Vk.CreateShaderModule(gd.Device, in shaderModuleCI, null, out _module).CheckResult();
        }

        ResourceLayoutDescription[] descs = ResourceLayoutsArray;
        (DescriptorSetLayouts, PerSetCounts, ResourceSetCount, TotalDynamicUboCount) =
            BuildDescriptorSetLayouts(descs);

        PipelineLayout = BuildPipelineLayout(DescriptorSetLayouts, ResourceSetCount);

        ComputePipelineCreateInfo pipelineCI = new() { SType = StructureType.ComputePipelineCreateInfo };
        pipelineCI.Layout = PipelineLayout;

        PipelineShaderStageCreateInfo stageCI = new() { SType = StructureType.PipelineShaderStageCreateInfo };
        stageCI.Module = _module;
        stageCI.Stage = VkFormats.VdToVkShaderStages(ShaderStages.Compute);
        stageCI.PName = CommonStrings.main;
        pipelineCI.Stage = stageCI;

        _gd.Vk.CreateComputePipelines(_gd.Device, default, 1, in pipelineCI, null, out VkPipelineHandle pipeline).CheckResult();
        DevicePipeline = pipeline;
    }

    private (DescriptorSetLayout[] dsls, DescriptorResourceCounts[] counts, uint setCount, int dynamicUboTotal)
        BuildDescriptorSetLayouts(ResourceLayoutDescription[] descs)
    {
        uint maxSet = 0;
        bool any = false;
        for (int i = 0; i < descs.Length; i++)
        {
            if (!any || descs[i].Set > maxSet) maxSet = descs[i].Set;
            any = true;
        }
        uint setCount = any ? maxSet + 1 : 0;
        DescriptorSetLayout[] dsls = new DescriptorSetLayout[setCount];
        DescriptorResourceCounts[] counts = new DescriptorResourceCounts[setCount];

        for (int i = 0; i < descs.Length; i++)
        {
            uint set = descs[i].Set;
            if (dsls[set].Handle != 0)
                throw new RenderException($"Multiple ResourceLayouts share Set index {set}.");
            (dsls[set], counts[set]) = CreateDescriptorSetLayout(ref descs[i]);
        }

        int dynamicTotal = 0;
        for (int i = 0; i < setCount; i++)
        {
            if (dsls[i].Handle == 0)
                dsls[i] = GetOrCreateEmptyDescriptorSetLayout();
            dynamicTotal += (int)counts[i].UniformBufferDynamicCount;
        }

        return (dsls, counts, setCount, dynamicTotal);
    }

    private (DescriptorSetLayout dsl, DescriptorResourceCounts counts) CreateDescriptorSetLayout(
        ref ResourceLayoutDescription desc)
    {
        ResourceLayoutElementDescription[] elems = desc.Elements;
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[elems.Length];

        uint uniformBufferDynamic = 0, sampledImage = 0, sampler = 0, storageBuffer = 0, storageImage = 0;

        for (int i = 0; i < elems.Length; i++)
        {
            ref ResourceLayoutElementDescription elem = ref elems[i];
            DescriptorType descType = GetDescriptorType(elem.Kind);
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)elem.BindingIndex,
                DescriptorType = descType,
                DescriptorCount = 1,
                StageFlags = VkFormats.VdToVkShaderStages(elem.Stages),
            };
            switch (descType)
            {
                case DescriptorType.UniformBufferDynamic: uniformBufferDynamic++; break;
                case DescriptorType.SampledImage: sampledImage++; break;
                case DescriptorType.Sampler: sampler++; break;
                case DescriptorType.StorageBuffer: storageBuffer++; break;
                case DescriptorType.StorageImage: storageImage++; break;
            }
        }

        DescriptorSetLayoutCreateInfo dslCI = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)elems.Length,
            PBindings = bindings,
        };
        _gd.Vk.CreateDescriptorSetLayout(_gd.Device, in dslCI, null, out DescriptorSetLayout dsl).CheckResult();

        return (dsl, new DescriptorResourceCounts(0, uniformBufferDynamic, sampledImage, sampler, storageBuffer, 0, storageImage));
    }

    private static DescriptorType GetDescriptorType(ResourceKind kind) => kind switch
    {
        ResourceKind.UniformBuffer => DescriptorType.UniformBufferDynamic,
        ResourceKind.StructuredBufferReadOnly => DescriptorType.StorageBuffer,
        ResourceKind.StructuredBufferReadWrite => DescriptorType.StorageBuffer,
        ResourceKind.TextureReadOnly => DescriptorType.SampledImage,
        ResourceKind.TextureReadWrite => DescriptorType.StorageImage,
        ResourceKind.Sampler => DescriptorType.Sampler,
        _ => throw Illegal.Value<ResourceKind>(),
    };

    private PipelineLayout BuildPipelineLayout(DescriptorSetLayout[] dsls, uint setCount)
    {
        DescriptorSetLayout* dslsPtr = stackalloc DescriptorSetLayout[(int)setCount];
        for (int i = 0; i < setCount; i++) dslsPtr[i] = dsls[i];

        PipelineLayoutCreateInfo plCI = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = setCount,
            PSetLayouts = dslsPtr,
        };
        _gd.Vk.CreatePipelineLayout(_gd.Device, in plCI, null, out PipelineLayout layout).CheckResult();
        return layout;
    }

    private DescriptorSetLayout GetOrCreateEmptyDescriptorSetLayout()
    {
        if (_emptyDescriptorSetLayout.Handle != 0) return _emptyDescriptorSetLayout;
        DescriptorSetLayoutCreateInfo dslCI = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 0,
            PBindings = null,
        };
        _gd.Vk.CreateDescriptorSetLayout(_gd.Device, in dslCI, null, out _emptyDescriptorSetLayout).CheckResult();
        return _emptyDescriptorSetLayout;
    }

    public override string Name
    {
        get => _name;
        set { _name = value; _gd.SetResourceName(this, value); }
    }

    public override void Dispose() => RefCount.Decrement();

    private void DisposeCore()
    {
        if (_disposed) return;
        _disposed = true;
        _gd.Vk.DestroyPipeline(_gd.Device, DevicePipeline, null);
        _gd.Vk.DestroyPipelineLayout(_gd.Device, PipelineLayout, null);
        _gd.Vk.DestroyShaderModule(_gd.Device, _module, null);
        if (_emptyDescriptorSetLayout.Handle != 0)
            _gd.Vk.DestroyDescriptorSetLayout(_gd.Device, _emptyDescriptorSetLayout, null);
        foreach (DescriptorSetLayout dsl in DescriptorSetLayouts)
        {
            if (dsl.Handle != 0 && dsl.Handle != _emptyDescriptorSetLayout.Handle)
                _gd.Vk.DestroyDescriptorSetLayout(_gd.Device, dsl, null);
        }
    }
}
