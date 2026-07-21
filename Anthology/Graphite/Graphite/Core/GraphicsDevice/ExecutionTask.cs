namespace Prowl.Graphite;

/// <summary>
/// One dispatched work graph. Owns a ring slot, a transient bump allocator, and a completion fence.
/// </summary>
public abstract partial class ExecutionTask
{
    /// <summary>Monotonic task ID.</summary>
    public abstract ulong Id { get; }

    /// <summary>Slot index in the device ring.</summary>
    public abstract uint RingSlot { get; }

    /// <summary>
    /// Signals when this execution's GPU work is done. Owned by device, recycled on ring slot reuse. Just wait on it.
    /// </summary>
    public abstract Fence CompletionFence { get; }

    /// <summary>Owning device.</summary>
    public abstract GraphicsDevice Device { get; }

    /// <summary>Submits a recorded command buffer. Call End() first.</summary>
    /// <param name="commandList">Buffer to submit.</param>
    internal abstract void SubmitCommandsInternal(CommandBuffer commandList);

    /// <summary>
    /// Tracks a rented command buffer so it's reclaimed when GPU work retires. No-op if backend doesn't pool.
    /// </summary>
    /// <param name="commandBuffer">Buffer rented via the render context.</param>
    internal virtual void TrackRentedCommandBuffer(CommandBuffer commandBuffer) { }

    /// <summary>
    /// Allocates a transient uniform buffer range from the bump allocator. Valid until completion fence signals.
    /// </summary>
    /// <remarks>Uniform buffers only. Don't bind as vertex, index, or structured buffer.</remarks>
    /// <param name="sizeInBytes">Bytes to allocate.</param>
    /// <returns>Range into the transient buffer.</returns>
    internal abstract DeviceBufferRange AllocateTransientInternal(uint sizeInBytes);
}
