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
    /// Free-list keyed by description, shared across executions. Buffer returns to the free-list once
    /// its execution finishes on GPU. Never reused while still in flight.
    /// </para>
    /// </summary>
    /// <param name="task">Execution renting the buffer. Returns to pool when it finishes on GPU.</param>
    /// <param name="desc">Buffer to rent.</param>
    /// <returns>The rented buffer.</returns>
    public DeviceBuffer RentTransientBuffer(ExecutionTask task, in BufferDescription desc)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(RentTransientBuffer));
        return TransientBufferPool.Rent(desc, task.Id);
    }
}
