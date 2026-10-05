namespace Prowl.Graphite;

/// <summary>
/// One dispatched work graph. Owns a ring slot and a transient bump allocator.
/// </summary>
public abstract partial class ExecutionTask
{
    /// <summary>Monotonic task ID.</summary>
    public abstract ulong Id { get; }

    /// <summary>Slot index in the device ring.</summary>
    public abstract uint RingSlot { get; }

    /// <summary>Owning device.</summary>
    public abstract GraphicsDevice Device { get; }

    /// <summary>Queues a recorded command buffer for this execution's submit. Call End() first.</summary>
    /// <param name="commandList">Buffer to submit.</param>
    internal abstract void SubmitCommandsInternal(CommandBuffer commandList);

    private CommandBuffer? _openTail;

    internal CommandBuffer? OpenTail => _openTail;

    internal void QueueOpen(CommandBuffer commandBuffer)
    {
        CloseTail();
        commandBuffer.SealRenderPass();
        _openTail = commandBuffer;
        Device.Profiler?.RecordSubmit(commandBuffer.ProfilerInfo, isTransfer: false);
    }

    internal void CloseTail()
    {
        CommandBuffer? tail = _openTail;
        if (tail == null)
            return;

        _openTail = null;
        tail.End();
        SubmitCommandsInternal(tail);
    }

    /// <summary>
    /// Submits everything queued so far, so later work on the queue is ordered after it.
    /// </summary>
    internal virtual void FlushSubmissions() { }

    /// <summary>
    /// Allocates a transient uniform buffer range from the bump allocator. Valid until the execution completes.
    /// </summary>
    /// <remarks>Uniform buffers only. Don't bind as vertex, index, or structured buffer.</remarks>
    /// <param name="sizeInBytes">Bytes to allocate.</param>
    /// <returns>Range into the transient buffer.</returns>
    internal abstract DeviceBufferRange AllocateTransientInternal(uint sizeInBytes);
}
