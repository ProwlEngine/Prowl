using Silk.NET.Vulkan;

using static Prowl.Veldrid.Vk.VulkanUtil;

using System;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkShaderProgram : ShaderProgram
{
    private readonly VkGraphicsDevice _gd;
    private readonly Dictionary<ShaderStages, ShaderModule> _modules = new();
    private readonly Dictionary<ShaderStages, string> _entryPoints = new();
    private readonly VkResourceLayout[] _materializedLayouts;
    private readonly DescriptorSetLayout[] _descriptorSetLayouts;
    private readonly PipelineLayout _pipelineLayout;
    private DescriptorSetLayout _emptyDescriptorSetLayout;
    private readonly uint _resourceSetCount;
    private readonly int _dynamicOffsetsCount;
    private bool _disposed;
    private string _name;

    public override bool IsDisposed => _disposed;

    internal IReadOnlyDictionary<ShaderStages, ShaderModule> Modules => _modules;
    internal ShaderModule GetModule(ShaderStages stage)
    {
        if (!_modules.TryGetValue(stage, out ShaderModule module))
        {
            throw new RenderException($"ShaderProgram does not contain a module for stage {stage}.");
        }
        return module;
    }

    internal string GetEntryPoint(ShaderStages stage) => _entryPoints[stage];

    internal VkResourceLayout[] MaterializedResourceLayouts => _materializedLayouts;
    internal DescriptorSetLayout[] DescriptorSetLayouts => _descriptorSetLayouts;
    internal PipelineLayout PipelineLayout => _pipelineLayout;
    internal uint ResourceSetCount => _resourceSetCount;
    internal int DynamicOffsetsCount => _dynamicOffsetsCount;

    public VkShaderProgram(VkGraphicsDevice gd, ref ShaderDescription description)
        : base(ref description)
    {
        _gd = gd;

        ShaderStageDescription[] stages = description.Stages;
        for (int i = 0; i < stages.Length; i++)
        {
            ShaderStageDescription sd = stages[i];
            ShaderModuleCreateInfo shaderModuleCI = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo
            };
            fixed (byte* codePtr = sd.ShaderBytes)
            {
                shaderModuleCI.CodeSize = (UIntPtr)sd.ShaderBytes.Length;
                shaderModuleCI.PCode = (uint*)codePtr;
                Result result = _gd.Vk.CreateShaderModule(gd.Device, in shaderModuleCI, null, out ShaderModule module);
                CheckResult(result);
                _modules[sd.Stage] = module;
                _entryPoints[sd.Stage] = sd.EntryPoint;
            }
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

        _pipelineLayout = CreatePipelineLayout(_materializedLayouts, out _resourceSetCount, out _descriptorSetLayouts);
    }

    private PipelineLayout CreatePipelineLayout(VkResourceLayout[] layouts, out uint setCount, out DescriptorSetLayout[] perSetDsl)
    {
        uint maxSet = 0;
        bool any = false;
        for (int i = 0; i < layouts.Length; i++)
        {
            uint set = layouts[i].Description.Set;
            if (!any || set > maxSet) maxSet = set;
            any = true;
        }
        setCount = any ? maxSet + 1 : 0;

        perSetDsl = new DescriptorSetLayout[setCount];
        for (int i = 0; i < layouts.Length; i++)
        {
            uint set = layouts[i].Description.Set;
            if (perSetDsl[set].Handle != 0)
            {
                throw new RenderException($"Multiple ResourceLayouts share Set index {set}.");
            }
            perSetDsl[set] = layouts[i].DescriptorSetLayout;
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

        PipelineLayoutCreateInfo pipelineLayoutCI = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = setCount,
            PSetLayouts = dslsPtr,
        };
        Result result = _gd.Vk.CreatePipelineLayout(_gd.Device, in pipelineLayoutCI, null, out PipelineLayout layout);
        CheckResult(result);
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
        Result result = _gd.Vk.CreateDescriptorSetLayout(_gd.Device, in dslCI, null, out _emptyDescriptorSetLayout);
        CheckResult(result);
        return _emptyDescriptorSetLayout;
    }

    public override string Name
    {
        get => _name;
        set { _name = value; _gd.SetResourceName(this, value); }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ShaderModule m in _modules.Values)
        {
            _gd.Vk.DestroyShaderModule(_gd.Device, m, null);
        }
        _gd.Vk.DestroyPipelineLayout(_gd.Device, _pipelineLayout, null);
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
