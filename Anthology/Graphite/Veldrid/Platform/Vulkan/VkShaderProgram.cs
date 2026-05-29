using Silk.NET.Vulkan;

using static Prowl.Veldrid.Vk.VulkanUtil;

using System;
using System.Collections.Generic;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkShaderProgram : ShaderProgram
{
    private readonly VkGraphicsDevice _gd;
    private readonly Dictionary<ShaderStages, ShaderModule> _modules = new();
    private readonly Dictionary<ShaderStages, string> _entryPoints = new();

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
    private readonly Dictionary<VkPipelineCacheKey, VkPipelineCacheEntry> _pipelineCache = new();
    private readonly object _pipelineCacheLock = new object();

    private DescriptorSetLayout _emptyDescriptorSetLayout;
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
            throw new RenderException($"ShaderProgram does not contain a module for stage {stage}.");
        return module;
    }

    internal string GetEntryPoint(ShaderStages stage) => _entryPoints[stage];

    public VkShaderProgram(VkGraphicsDevice gd, ref ShaderDescription description)
        : base(ref description)
    {
        _gd = gd;
        RefCount = new ResourceRefCount(DisposeCore);

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
        (DescriptorSetLayouts, PerSetCounts, ResourceSetCount, TotalDynamicUboCount) =
            BuildDescriptorSetLayouts(descs);

        PipelineLayout = BuildPipelineLayout(DescriptorSetLayouts, ResourceSetCount);
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

        uint uniformBufferDynamic = 0;
        uint sampledImage = 0;
        uint sampler = 0;
        uint storageBuffer = 0;
        uint storageImage = 0;

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

        DescriptorSetLayoutCreateInfo dslCI = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)elems.Length,
            PBindings = bindings,
        };
        Result result = _gd.Vk.CreateDescriptorSetLayout(_gd.Device, in dslCI, null, out DescriptorSetLayout dsl);
        CheckResult(result);

        return (dsl, new DescriptorResourceCounts(
            0, uniformBufferDynamic, sampledImage, sampler, storageBuffer, 0, storageImage));
    }

    /// <summary>
    /// Returns the Vulkan descriptor type for the given <see cref="ResourceKind"/> per Stage 7 rules.
    /// All uniform buffers are <c>UNIFORM_BUFFER_DYNAMIC</c>; textures use separate image/sampler descriptors.
    /// </summary>
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

        _gd.Vk.DestroyPipelineLayout(_gd.Device, PipelineLayout, null);

        if (_emptyDescriptorSetLayout.Handle != 0)
            _gd.Vk.DestroyDescriptorSetLayout(_gd.Device, _emptyDescriptorSetLayout, null);

        foreach (DescriptorSetLayout dsl in DescriptorSetLayouts)
        {
            if (dsl.Handle != 0 && dsl.Handle != _emptyDescriptorSetLayout.Handle)
                _gd.Vk.DestroyDescriptorSetLayout(_gd.Device, dsl, null);
        }
    }
}
