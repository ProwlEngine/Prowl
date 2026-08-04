using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Live per-shader descriptor-set caches, swept each frame to enforce the retention window even for
/// shaders that have stopped rendering. Registration may come from asset-load threads, so guarded.
/// </summary>
internal sealed class VkDescriptorSetCacheRegistry
{
    private readonly List<VkDescriptorSetCache> _caches = [];
    private readonly object _lock = new();

    public void Register(VkDescriptorSetCache cache)
    {
        lock (_lock)
            _caches.Add(cache);
    }

    public void Unregister(VkDescriptorSetCache cache)
    {
        lock (_lock)
            _caches.Remove(cache);
    }

    /// <summary>
    /// Ages out descriptor sets unused past the retention window. Safe: anything freed is older than
    /// <paramref name="retention"/> executions and therefore already GPU-retired.
    /// </summary>
    public void SweepAll(ulong executionId, uint retention)
    {
        lock (_lock)
        {
            foreach (VkDescriptorSetCache cache in _caches)
                cache.Sweep(executionId, retention);
        }
    }
}
