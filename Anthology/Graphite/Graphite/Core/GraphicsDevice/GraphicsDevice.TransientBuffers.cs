namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    private TransientBufferPool _transientBufferPool;
    private readonly object _transientBufferPoolLock = new();

    private TransientBufferPool TransientBufferPool
    {
        get
        {
            if (_transientBufferPool == null)
            {
                lock (_transientBufferPoolLock)
                {
                    _transientBufferPool ??= new TransientBufferPool(this);
                }
            }
            return _transientBufferPool;
        }
    }

    /// <summary>
    /// Rents a device buffer from the transient pool.
    /// <para>
    /// Backing buffers come from a device-level free-list keyed by description and survive across
    /// executions: once the rent-time execution finishes on GPU, the buffer goes back to the free-list and
    /// a later rent with an equal description reuses it. A buffer still in flight is never handed to another
    /// caller.
    /// </para>
    /// </summary>
    /// <param name="task">Execution renting the buffer; it returns to the free-list once this completes on GPU.</param>
    /// <param name="desc">Buffer to rent.</param>
    /// <returns>The rented device buffer.</returns>
    public DeviceBuffer RentTransientBuffer(ExecutionTask task, in BufferDescription desc)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(RentTransientBuffer));
        return TransientBufferPool.Rent(desc, task.Id);
    }
}
