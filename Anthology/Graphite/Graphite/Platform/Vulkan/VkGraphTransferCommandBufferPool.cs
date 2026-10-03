using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

internal sealed class VkGraphTransferCommandBufferPool
{
    private readonly VkGraphicsDevice _gd;
    private readonly object _lock = new();
    private readonly Stack<VkTransferCommandBuffer> _free = new();
    private readonly List<VkTransferCommandBuffer> _all = [];
    private bool _disposed;

    public VkGraphTransferCommandBufferPool(VkGraphicsDevice gd)
    {
        _gd = gd;
    }

    public VkTransferCommandBuffer Rent()
    {
        lock (_lock)
        {
            if (_free.Count > 0)
                return _free.Pop();
        }

        VkTransferCommandBuffer cb = new(_gd);
        lock (_lock)
        {
            _all.Add(cb);
        }
        return cb;
    }

    public void Return(VkTransferCommandBuffer cb)
    {
        lock (_lock)
        {
            if (!_disposed)
                _free.Push(cb);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            foreach (VkTransferCommandBuffer cb in _all)
                cb.Dispose();
            _all.Clear();
            _free.Clear();
        }
    }
}
