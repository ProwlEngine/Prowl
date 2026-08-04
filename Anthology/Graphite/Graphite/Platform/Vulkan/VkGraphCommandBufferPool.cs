using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Pool of recyclable graph command buffers. Rented by render-graph passes and returned once their
/// GPU work retires, so a pass no longer allocates a fresh command pool + buffers every frame.
/// </summary>
internal sealed class VkGraphCommandBufferPool
{
    private readonly VkGraphicsDevice _gd;
    private readonly object _lock = new();
    private readonly Stack<VkCommandBuffer> _free = new();
    private readonly List<VkCommandBuffer> _all = [];
    private bool _disposed;

    public VkGraphCommandBufferPool(VkGraphicsDevice gd)
    {
        _gd = gd;
    }

    /// <summary>Total distinct command buffers ever allocated. Test hook.</summary>
    public int AllocatedCount
    {
        get { lock (_lock) { return _all.Count; } }
    }

    public VkCommandBuffer Rent()
    {
        lock (_lock)
        {
            if (_free.Count > 0)
                return _free.Pop();
        }

        CommandBufferDescription desc = new();
        VkCommandBuffer cb = new(_gd, ref desc);
        lock (_lock)
        {
            _all.Add(cb);
        }
        return cb;
    }

    // Returns a rented buffer once its owning execution has retired (GPU-complete). Clean buffers go
    // back to the free list for reuse; a buffer left mid-recording can't be reset, so dispose it.
    public void Return(VkCommandBuffer cb)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            if (cb.CanRecycle)
            {
                _free.Push(cb);
            }
            else
            {
                _all.Remove(cb);
                cb.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            foreach (VkCommandBuffer cb in _all)
                cb.Dispose();
            _all.Clear();
            _free.Clear();
        }
    }
}
