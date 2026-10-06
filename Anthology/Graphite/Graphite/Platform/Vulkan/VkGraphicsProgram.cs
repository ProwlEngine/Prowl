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
    private readonly VkShader _shader;

    /// <summary>
    /// Cache of resolved pipelines keyed on (OutputDescription, PrimitiveTopology). Lock guards against
    /// double vkCreateGraphicsPipelines for the same key.
    /// </summary>
    private readonly Dictionary<VkPipelineCacheKey, VkPipelineCacheEntry> _pipelineCache = [];
    private readonly object _pipelineCacheLock = new();

    internal DescriptorSetLayout[] DescriptorSetLayouts => _shader.DescriptorSetLayouts;

    internal DescriptorResourceCounts[] PerSetCounts => _shader.PerSetCounts;

    internal PipelineLayout PipelineLayout => _shader.PipelineLayout;

    /// <summary>Set slot count (max set index + 1).</summary>
    internal uint ResourceSetCount => _shader.ResourceSetCount;

    /// <summary>
    /// Cross-frame descriptor set cache, shared by every program using the same shader.
    /// </summary>
    internal VkDescriptorSetCache DescriptorCache => _shader.DescriptorCache;

    internal IReadOnlyDictionary<ShaderStages, ShaderModule> Modules => _shader.Modules;

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
            _gd.Counters.Allocate(AllocBin.Pipeline);
            return entry;
        }
    }

    internal ShaderModule GetModule(ShaderStages stage) => _shader.GetModule(stage);

    internal string GetEntryPoint(ShaderStages stage) => _shader.GetEntryPoint(stage);

    public VkGraphicsProgram(VkGraphicsDevice gd, in ShaderDescription description)
        : base(description)
    {
        _gd = gd;
        _shader = gd.ShaderCache.Acquire(description.Stages, ResourceLayoutsArray);
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore() => _gd.DisposeWhenRetired(DestroyNative);

    private void DestroyNative()
    {
        int pipelineCount = _pipelineCache.Count;
        foreach (VkPipelineCacheEntry entry in _pipelineCache.Values)
        {
            _gd.Vk.DestroyPipeline(_gd.Device, entry.Pipeline, null);
            _gd.Vk.DestroyRenderPass(_gd.Device, entry.CompatRenderPass, null);
        }
        _pipelineCache.Clear();
        DisposeCore_RecordFrees(pipelineCount);

        _gd.ShaderCache.Release(_shader);
    }
}
