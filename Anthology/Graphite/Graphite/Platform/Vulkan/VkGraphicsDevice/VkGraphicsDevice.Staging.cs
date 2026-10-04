using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private const int SharedCommandPoolCount = 4;
    private readonly Stack<SharedCommandPool> _sharedGraphicsCommandPools = new();
    private readonly object _graphicsCommandPoolLock = new();

    private SharedCommandPool GetFreeCommandPool()
    {
        lock (_graphicsCommandPoolLock)
        {
            if (_sharedGraphicsCommandPools.Count > 0)
                return _sharedGraphicsCommandPools.Pop();
        }

        return new SharedCommandPool(this, false);
    }

    private void DisposeStagingResources()
    {
        lock (_graphicsCommandPoolLock)
        {
            while (_sharedGraphicsCommandPools.Count > 0)
            {
                SharedCommandPool sharedPool = _sharedGraphicsCommandPools.Pop();
                sharedPool.Destroy();
            }
        }
    }
}
