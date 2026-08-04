using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Device-owned full-range views, one per texture, alive until the device is disposed.
/// </summary>
internal sealed class VkDefaultTextureViewCache
{
    private readonly ResourceFactory _factory;
    private readonly Dictionary<Texture, VkTextureView> _views = [];
    private readonly object _lock = new();

    public VkDefaultTextureViewCache(ResourceFactory factory)
    {
        _factory = factory;
    }

    public VkTextureView GetOrCreate(VkTexture texture)
    {
        lock (_lock)
        {
            if (!_views.TryGetValue(texture, out VkTextureView? view))
            {
                view = (VkTextureView)_factory.CreateTextureView(texture);
                _views[texture] = view;
            }
            return view;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (VkTextureView view in _views.Values)
                view.Dispose();
            _views.Clear();
        }
    }
}
