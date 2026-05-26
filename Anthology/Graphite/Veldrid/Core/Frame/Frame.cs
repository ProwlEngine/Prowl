namespace Prowl.Veldrid;

/// <summary>
/// Represents a single unit of GPU work. A frame has a monotonic ID, a ring slot, a completion fence,
/// and methods for submitting command buffers and allocating transient buffer memory.
/// Obtain a <see cref="Frame"/> by calling <see cref="GraphicsDevice.BeginFrame"/>;
/// finalize it by calling <see cref="GraphicsDevice.EndFrame()"/>.
/// </summary>
public abstract partial class Frame
{
    /// <summary>
    /// Gets the unique monotonic identifier for this frame. Starts at 1 and never wraps.
    /// A value of 0 is the sentinel for "no frame".
    /// </summary>
    public abstract ulong FrameId { get; }

    /// <summary>
    /// Gets the index of this frame's slot in the ring buffer.
    /// The value is in the range [0, MaxFramesInFlight).
    /// </summary>
    public abstract uint RingSlot { get; }

    /// <summary>
    /// Gets the <see cref="Fence"/> that becomes signaled when all commands submitted during this frame
    /// have fully completed on the GPU.
    /// <para>
    /// This fence is owned and managed by the frame system and is recycled when the ring slot is reused.
    /// Do NOT call <see cref="Fence.Reset"/> on this fence; doing so results in undefined behavior.
    /// Do NOT hold this fence reference past the next <see cref="GraphicsDevice.BeginFrame"/> call for
    /// the same ring slot.
    /// </para>
    /// <para>
    /// On the D3D11 backend, the underlying event is set lazily during <see cref="GraphicsDevice.IsFrameComplete(ulong)"/>,
    /// <see cref="GraphicsDevice.WaitForFrame(ulong)"/>, or <see cref="GraphicsDevice.BeginFrame"/> calls.
    /// Polling <see cref="Fence.Signaled"/> directly without going through these entry points will not
    /// reflect completion.
    /// </para>
    /// </summary>
    public abstract Fence CompletionFence { get; }

    /// <summary>
    /// Gets the <see cref="GraphicsDevice"/> that owns this frame.
    /// </summary>
    public abstract GraphicsDevice Device { get; }

    /// <summary>
    /// Submits a recorded <see cref="CommandBuffer"/> for execution within this frame.
    /// <see cref="CommandBuffer.End"/> must have been called on <paramref name="commandList"/> prior to
    /// calling this method.
    /// This method may only be called on the frame currently returned by
    /// <see cref="GraphicsDevice.BeginFrame"/>.
    /// </summary>
    /// <param name="commandList">The recorded <see cref="CommandBuffer"/> to submit for GPU execution.</param>
    public abstract void SubmitCommands(CommandBuffer commandList);

    /// <summary>
    /// Allocates a transient uniform <see cref="DeviceBufferRange"/> from this frame's per-frame UBO bump allocator.
    /// The returned range is backed by GPU-visible memory that is valid for GPU use until this frame's
    /// <see cref="CompletionFence"/> signals. After that the underlying memory is recycled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// To write data into the allocation, call <see cref="GraphicsDevice.Map(MappableResource, MapMode)"/> on
    /// <see cref="DeviceBufferRange.Buffer"/> with <see cref="MapMode.Write"/> and write at
    /// <see cref="DeviceBufferRange.Offset"/>, or use <see cref="GraphicsDevice.UpdateBuffer"/> where
    /// supported by the backend.
    /// </para>
    /// </remarks>
    /// <param name="sizeInBytes">The number of bytes to allocate.</param>
    /// <returns>A <see cref="DeviceBufferRange"/> pointing into the frame's transient buffer.</returns>
    public abstract DeviceBufferRange AllocateTransient(uint sizeInBytes);
}
