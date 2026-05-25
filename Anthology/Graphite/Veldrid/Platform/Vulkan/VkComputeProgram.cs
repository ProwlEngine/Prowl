using Silk.NET.Vulkan;

using static Prowl.Veldrid.Vk.VulkanUtil;

using System;

using VkPipelineHandle = Silk.NET.Vulkan.Pipeline;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkComputeProgram : ComputeProgram
{
    private readonly VkGraphicsDevice _gd;
    private readonly ShaderModule _module;
    private readonly VkResourceLayout[] _materializedLayouts;
    private readonly DescriptorSetLayout[] _descriptorSetLayouts;
    private readonly PipelineLayout _pipelineLayout;
    private DescriptorSetLayout _emptyDescriptorSetLayout;
    private readonly VkPipelineHandle _computePipeline;
    private readonly uint _resourceSetCount;
    private readonly int _dynamicOffsetsCount;
    private readonly ResourceRefCount _refCount;
    private bool _disposed;
    private string _name;

    public override bool IsDisposed => _disposed;

    internal VkPipelineHandle DevicePipeline => _computePipeline;
    internal PipelineLayout PipelineLayout => _pipelineLayout;
    internal uint ResourceSetCount => _resourceSetCount;
    internal int DynamicOffsetsCount => _dynamicOffsetsCount;
    internal ResourceRefCount RefCount => _refCount;
    internal VkResourceLayout[] MaterializedResourceLayouts => _materializedLayouts;

    public VkComputeProgram(VkGraphicsDevice gd, ref ComputeDescription description)
        : base(ref description)
    {
        _gd = gd;
        _refCount = new ResourceRefCount(DisposeCore);

        ShaderStageDescription stage = description.Stage;
        ShaderModuleCreateInfo shaderModuleCI = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo };
        fixed (byte* codePtr = stage.ShaderBytes)
        {
            shaderModuleCI.CodeSize = (UIntPtr)stage.ShaderBytes.Length;
            shaderModuleCI.PCode = (uint*)codePtr;
            Result r = _gd.Vk.CreateShaderModule(gd.Device, in shaderModuleCI, null, out _module);
            CheckResult(r);
        }

        ResourceLayoutDescription[] descs = ResourceLayoutsArray;
        _materializedLayouts = new VkResourceLayout[descs.Length];
        int dynamicTotal = 0;
        for (int i = 0; i < descs.Length; i++)
        {
            _materializedLayouts[i] = new VkResourceLayout(_gd, ref descs[i]);
            dynamicTotal += _materializedLayouts[i].DynamicBufferCount;
        }
        _dynamicOffsetsCount = dynamicTotal;

        _pipelineLayout = CreatePipelineLayout(out _resourceSetCount, out _descriptorSetLayouts);

        ComputePipelineCreateInfo pipelineCI = new ComputePipelineCreateInfo { SType = StructureType.ComputePipelineCreateInfo };
        pipelineCI.Layout = _pipelineLayout;

        PipelineShaderStageCreateInfo stageCI = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo };
        stageCI.Module = _module;
        stageCI.Stage = VkFormats.VdToVkShaderStages(ShaderStages.Compute);
        stageCI.PName = CommonStrings.main;
        pipelineCI.Stage = stageCI;

        Result result = _gd.Vk.CreateComputePipelines(_gd.Device, default, 1, in pipelineCI, null, out _computePipeline);
        CheckResult(result);
    }

    private PipelineLayout CreatePipelineLayout(out uint setCount, out DescriptorSetLayout[] perSetDsl)
    {
        uint maxSet = 0;
        bool any = false;
        for (int i = 0; i < _materializedLayouts.Length; i++)
        {
            uint set = _materializedLayouts[i].Description.Set;
            if (!any || set > maxSet) maxSet = set;
            any = true;
        }
        setCount = any ? maxSet + 1 : 0;
        perSetDsl = new DescriptorSetLayout[setCount];

        for (int i = 0; i < _materializedLayouts.Length; i++)
        {
            uint set = _materializedLayouts[i].Description.Set;
            if (perSetDsl[set].Handle != 0)
            {
                throw new RenderException($"Multiple ResourceLayouts share Set index {set}.");
            }
            perSetDsl[set] = _materializedLayouts[i].DescriptorSetLayout;
        }
        for (int i = 0; i < setCount; i++)
        {
            if (perSetDsl[i].Handle == 0)
            {
                perSetDsl[i] = GetOrCreateEmptyDescriptorSetLayout();
            }
        }

        DescriptorSetLayout* dslsPtr = stackalloc DescriptorSetLayout[(int)setCount];
        for (int i = 0; i < setCount; i++) dslsPtr[i] = perSetDsl[i];

        PipelineLayoutCreateInfo plCI = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = setCount,
            PSetLayouts = dslsPtr,
        };
        Result r = _gd.Vk.CreatePipelineLayout(_gd.Device, in plCI, null, out PipelineLayout layout);
        CheckResult(r);
        return layout;
    }

    private DescriptorSetLayout GetOrCreateEmptyDescriptorSetLayout()
    {
        if (_emptyDescriptorSetLayout.Handle != 0) return _emptyDescriptorSetLayout;
        DescriptorSetLayoutCreateInfo dslCI = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 0,
            PBindings = null,
        };
        Result r = _gd.Vk.CreateDescriptorSetLayout(_gd.Device, in dslCI, null, out _emptyDescriptorSetLayout);
        CheckResult(r);
        return _emptyDescriptorSetLayout;
    }

    public override string Name
    {
        get => _name;
        set { _name = value; _gd.SetResourceName(this, value); }
    }

    public override void Dispose() => _refCount.Decrement();

    private void DisposeCore()
    {
        if (_disposed) return;
        _disposed = true;
        _gd.Vk.DestroyPipeline(_gd.Device, _computePipeline, null);
        _gd.Vk.DestroyPipelineLayout(_gd.Device, _pipelineLayout, null);
        _gd.Vk.DestroyShaderModule(_gd.Device, _module, null);
        if (_emptyDescriptorSetLayout.Handle != 0)
        {
            _gd.Vk.DestroyDescriptorSetLayout(_gd.Device, _emptyDescriptorSetLayout, null);
        }
        for (int i = 0; i < _materializedLayouts.Length; i++)
        {
            _materializedLayouts[i].Dispose();
        }
    }
}
