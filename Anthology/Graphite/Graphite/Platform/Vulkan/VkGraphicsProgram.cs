using System;
using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsProgram : GraphicsProgram, IVkDescriptorProgram
{
    DescriptorSetLayout[] IVkDescriptorProgram.DescriptorSetLayouts => DescriptorSetLayouts;
    DescriptorResourceCounts[] IVkDescriptorProgram.PerSetCounts => PerSetCounts;
    PipelineLayout IVkDescriptorProgram.PipelineLayout => PipelineLayout;
    uint IVkDescriptorProgram.ResourceSetCount => ResourceSetCount;
    VkDescriptorSetCache IVkDescriptorProgram.DescriptorCache => DescriptorCache;

    private readonly VkGraphicsDevice _gd;
    private readonly Dictionary<ShaderStages, ShaderModule> _modules = [];
    private readonly Dictionary<ShaderStages, string> _entryPoints = [];

    /// <summary>
    /// Descriptor-set layouts by set index. Empty DSL fills gaps.
    /// </summary>
    internal readonly DescriptorSetLayout[] DescriptorSetLayouts;

    /// <summary>
    /// Per-set descriptor resource counts, parallel to DescriptorSetLayouts. Sizes the per-frame pool.
    /// </summary>
    internal readonly DescriptorResourceCounts[] PerSetCounts;

    internal readonly PipelineLayout PipelineLayout;

    /// <summary>Total UNIFORM_BUFFER_DYNAMIC bindings across all sets.</summary>
    internal readonly int TotalDynamicUboCount;

    /// <summary>Set slot count (max set index + 1).</summary>
    internal readonly uint ResourceSetCount;

    internal readonly ResourceRefCount RefCount;

    /// <summary>
    /// Cross-frame descriptor set cache, content-addressed by bound resources.
    /// </summary>
    internal readonly VkDescriptorSetCache DescriptorCache;

    /// <summary>
    /// Cache of resolved pipelines keyed on (OutputDescription, PrimitiveTopology). Lock guards against
    /// double vkCreateGraphicsPipelines for the same key.
    /// </summary>
    private readonly Dictionary<VkPipelineCacheKey, VkPipelineCacheEntry> _pipelineCache = [];
    private readonly object _pipelineCacheLock = new();

    private readonly DescriptorSetLayout _emptyDescriptorSetLayout;
    private bool _disposed;
    private string _name;

    public override bool IsDisposed => _disposed;

    internal IReadOnlyDictionary<ShaderStages, ShaderModule> Modules => _modules;

    /// <summary>
    /// Gets the cached pipeline for key, building and inserting one if missing. Lives for the program's lifetime.
    /// </summary>
    internal VkPipelineCacheEntry GetOrAddPipeline(in VkPipelineCacheKey key)
    {
        lock (_pipelineCacheLock)
        {
            if (_pipelineCache.TryGetValue(key, out VkPipelineCacheEntry entry))
                return entry;

            entry = VkPipelineCacheFactory.Build(_gd, this, in key);
            _pipelineCache.Add(key, entry);
            _gd.Profiler?.Allocate(AllocBin.Pipeline, 0);
            return entry;
        }
    }

    internal ShaderModule GetModule(ShaderStages stage)
    {
        if (!_modules.TryGetValue(stage, out ShaderModule module))
            throw new RenderException($"GraphicsProgram does not contain a module for stage {stage}.");
        return module;
    }

    internal string GetEntryPoint(ShaderStages stage) => _entryPoints[stage];

    public VkGraphicsProgram(VkGraphicsDevice gd, ref ShaderDescription description)
        : base(ref description)
    {
        _gd = gd;
        RefCount = new ResourceRefCount(DisposeCore);

        ShaderStageDescription[] stages = description.Stages;
        for (int i = 0; i < stages.Length; i++)
        {
            ShaderStageDescription sd = stages[i];
            ShaderModuleCreateInfo shaderModuleCI = new()
            {
                SType = StructureType.ShaderModuleCreateInfo
            };
            fixed (byte* codePtr = sd.ShaderBytes)
            {
                shaderModuleCI.CodeSize = (UIntPtr)sd.ShaderBytes.Length;
                shaderModuleCI.PCode = (uint*)codePtr;
                _gd.Vk.CreateShaderModule(gd.Device, in shaderModuleCI, null, out ShaderModule module).CheckResult();
                _modules[sd.Stage] = module;
                _entryPoints[sd.Stage] = sd.EntryPoint;
            }
        }

        (DescriptorSetLayouts, PerSetCounts, PipelineLayout, ResourceSetCount, TotalDynamicUboCount, _emptyDescriptorSetLayout)
            = VkDescriptorLayoutBuilder.Build(_gd, ResourceLayoutsArray);

        DescriptorCache = new VkDescriptorSetCache(_gd);

        Constructor_RecordShaderAllocation(stages);
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

        int pipelineCount = _pipelineCache.Count;
        foreach (VkPipelineCacheEntry entry in _pipelineCache.Values)
        {
            _gd.Vk.DestroyPipeline(_gd.Device, entry.Pipeline, null);
            _gd.Vk.DestroyRenderPass(_gd.Device, entry.CompatRenderPass, null);
        }
        _pipelineCache.Clear();
        DisposeCore_RecordFrees(pipelineCount);

        DescriptorCache.Destroy();

        foreach (ShaderModule m in _modules.Values)
            _gd.Vk.DestroyShaderModule(_gd.Device, m, null);

        VkDescriptorLayoutBuilder.Destroy(_gd, DescriptorSetLayouts, _emptyDescriptorSetLayout, PipelineLayout);
    }
}
