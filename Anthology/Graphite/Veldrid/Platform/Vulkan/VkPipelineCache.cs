using System;
using System.Collections.Generic;

namespace Prowl.Veldrid.Vk;

/// <summary>
/// Per-device managed cache of resolved graphics <see cref="Silk.NET.Vulkan.Pipeline"/> handles,
/// keyed on <see cref="VkPipelineCacheKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// Lookup + factory invocation are guarded by a single coarse lock so
/// <c>vkCreateGraphicsPipelines</c> never runs twice concurrently for the same key.
/// </para>
/// <para>
/// The cache holds a hard reference to every <see cref="VkShaderProgram"/> used as a key.
/// When a program is disposed, <see cref="EvictByProgram(VkShaderProgram)"/> removes and destroys
/// every entry referencing it. The pipeline layout stored in each entry is owned by the
/// program, not the cache, and is not destroyed here.
/// </para>
/// </remarks>
internal sealed unsafe class VkPipelineCache : IDisposable
{
    private readonly VkGraphicsDevice _gd;
    private readonly object _lock = new object();

    private readonly Dictionary<VkPipelineCacheKey, VkPipelineCacheEntry> _entries
        = new Dictionary<VkPipelineCacheKey, VkPipelineCacheEntry>();

    private readonly Dictionary<VkShaderProgram, List<VkPipelineCacheKey>> _byProgram
        = new Dictionary<VkShaderProgram, List<VkPipelineCacheKey>>(ReferenceEqualityComparer.Instance);

    public VkPipelineCache(VkGraphicsDevice gd)
    {
        _gd = gd;
    }

    /// <summary>
    /// Returns the cached entry for <paramref name="key"/>, building and inserting one if missing.
    /// </summary>
    public VkPipelineCacheEntry GetOrAdd(in VkPipelineCacheKey key)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out VkPipelineCacheEntry entry))
                return entry;

            entry = VkPipelineCacheFactory.Build(_gd, in key);
            _entries.Add(key, entry);

            if (!_byProgram.TryGetValue(key.Program, out List<VkPipelineCacheKey>? list))
            {
                list = new List<VkPipelineCacheKey>(4);
                _byProgram.Add(key.Program, list);
            }
            list.Add(key);

            return entry;
        }
    }

    /// <summary>
    /// Removes and destroys every entry whose key references <paramref name="program"/>.
    /// </summary>
    /// <remarks>
    /// Must be called from <see cref="VkShaderProgram.Dispose"/> before the program tears down
    /// its pipeline layout, so destroyed pipelines no longer reference a dead layout.
    /// </remarks>
    public void EvictByProgram(VkShaderProgram program)
    {
        lock (_lock)
        {
            if (!_byProgram.TryGetValue(program, out List<VkPipelineCacheKey>? list))
                return;

            foreach (VkPipelineCacheKey key in list)
            {
                if (_entries.Remove(key, out VkPipelineCacheEntry entry))
                {
                    _gd.Vk.DestroyPipeline(_gd.Device, entry.Pipeline, null);
                    _gd.Vk.DestroyRenderPass(_gd.Device, entry.CompatRenderPass, null);
                }
            }
            _byProgram.Remove(program);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (VkPipelineCacheEntry entry in _entries.Values)
            {
                _gd.Vk.DestroyPipeline(_gd.Device, entry.Pipeline, null);
                _gd.Vk.DestroyRenderPass(_gd.Device, entry.CompatRenderPass, null);
            }
            _entries.Clear();
            _byProgram.Clear();
        }
    }
}
