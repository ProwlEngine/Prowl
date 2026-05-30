using System;
using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkGraphicsProgram : GraphicsProgram
{
    private readonly VkGraphicsDevice _gd;
    private readonly Dictionary<ShaderStages, ShaderModule> _modules = [];
    private readonly Dictionary<ShaderStages, string> _entryPoints = [];

    /// <summary>
    /// Descriptor-set layouts indexed by set index (0..maxSet). Empty-DSL for gaps.
    /// </summary>
    internal readonly DescriptorSetLayout[] DescriptorSetLayouts;

    /// <summary>
    /// Per-set descriptor resource counts, indexed parallel to <see cref="DescriptorSetLayouts"/>.
    /// Used to size the per-frame descriptor pool allocation.
    /// </summary>
    internal readonly DescriptorResourceCounts[] PerSetCounts;

    internal readonly PipelineLayout PipelineLayout;

    /// <summary>Total number of UNIFORM_BUFFER_DYNAMIC bindings across all sets.</summary>
    internal readonly int TotalDynamicUboCount;

    /// <summary>Total number of set slots (max set index + 1).</summary>
    internal readonly uint ResourceSetCount;

    internal readonly ResourceRefCount RefCount;

    /// <summary>
    /// Per-program cache of resolved graphics pipelines, keyed on
    /// <c>(OutputDescription, PrimitiveTopology)</c>. Lookup and factory invocation are guarded by
    /// <see cref="_pipelineCacheLock"/> so <c>vkCreateGraphicsPipelines</c> never runs twice
    /// concurrently for the same key.
    /// </summary>
    private readonly Dictionary<VkPipelineCacheKey, VkPipelineCacheEntry> _pipelineCache = [];
    private readonly object _pipelineCacheLock = new();

    private readonly DescriptorSetLayout _emptyDescriptorSetLayout;
    private bool _disposed;
    private string _name;

    public override bool IsDisposed => _disposed;

    internal IReadOnlyDictionary<ShaderStages, ShaderModule> Modules => _modules;

    /// <summary>
    /// Returns the cached pipeline entry for <paramref name="key"/>, building and inserting one if
    /// missing. The compatibility render pass and pipeline handle live for the program's lifetime.
    /// </summary>
    internal VkPipelineCacheEntry GetOrAddPipeline(in VkPipelineCacheKey key)
    {
        lock (_pipelineCacheLock)
        {
            if (_pipelineCache.TryGetValue(key, out VkPipelineCacheEntry entry))
                return entry;

            entry = VkPipelineCacheFactory.Build(_gd, this, in key);
            _pipelineCache.Add(key, entry);
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

        foreach (VkPipelineCacheEntry entry in _pipelineCache.Values)
        {
            _gd.Vk.DestroyPipeline(_gd.Device, entry.Pipeline, null);
            _gd.Vk.DestroyRenderPass(_gd.Device, entry.CompatRenderPass, null);
        }
        _pipelineCache.Clear();

        foreach (ShaderModule m in _modules.Values)
            _gd.Vk.DestroyShaderModule(_gd.Device, m, null);

        VkDescriptorLayoutBuilder.Destroy(_gd, DescriptorSetLayouts, _emptyDescriptorSetLayout, PipelineLayout);
    }
}
