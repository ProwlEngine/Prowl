namespace Prowl.Graphite;

/// <summary>
/// Handle to one dispatched work graph. Owns a ring slot, a transient bump allocator, and a fence that
/// signals when all its GPU work is done.
/// </summary>
public abstract partial class ExecutionTask
{
    /// <summary>Monotonic task ID.</summary>
    public abstract ulong Id { get; }

    /// <summary>This execution's slot index in the device ring.</summary>
    public abstract uint RingSlot { get; }

    /// <summary>
    /// Signals when all commands for this execution finish on GPU. Owned by the device, recycled on ring
    /// slot reuse. Don't reset or hold it, just wait on it.
    /// </summary>
    public abstract Fence CompletionFence { get; }

    /// <summary>The device that owns this execution.</summary>
    public abstract GraphicsDevice Device { get; }

    /// <summary>Submits a recorded command buffer for this task. Must have called End() first.</summary>
    /// <param name="commandList">Command buffer to submit.</param>
    internal abstract void SubmitCommandsInternal(CommandBuffer commandList);

    /// <summary>
    /// Records that a command buffer was rented for this execution so the owning execution can reclaim it
    /// once its GPU work retires. Backends without pooling may ignore this.
    /// </summary>
    /// <param name="commandBuffer">The command buffer rented via the render context.</param>
    internal virtual void TrackRentedCommandBuffer(CommandBuffer commandBuffer) { }

    /// <summary>
    /// Allocates a transient uniform buffer range from this task's bump allocator. Valid until the
    /// completion fence signals, then recycled.
    /// </summary>
    /// <remarks>Uniform buffers only. Don't bind the result as vertex, index, or structured buffer.</remarks>
    /// <param name="sizeInBytes">Bytes to allocate.</param>
    /// <returns>Range into the task's transient buffer.</returns>
    internal abstract DeviceBufferRange AllocateTransientInternal(uint sizeInBytes);
}
