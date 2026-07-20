using System;

namespace Prowl.Graphite;

/// <summary>
/// A device resource that stores graphics data. Size is fixed at creation, no resizing.
/// </summary>
public abstract partial class DeviceBuffer : DeviceResource, BindableResource, MappableResource, IDisposable
{
    /// <summary>
    /// Total capacity in bytes. Fixed at creation.
    /// </summary>
    public abstract uint SizeInBytes { get; }

    /// <summary>
    /// Bitmask of allowed uses.
    /// </summary>
    public abstract BufferUsage Usage { get; }

    /// <summary>
    /// Debug name, shows up in graphics debuggers.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// True if disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources.
    /// </summary>
    public abstract void Dispose();

    private GraphicsDevice _inFlightDevice;
    private ulong _inFlightExecutionId;
    private ulong _lastOrphanExecutionId;
    private bool _transientWrites;

    /// <summary>
    /// Log a warning if reallocations happen again within this many executions.
    /// </summary>
    private const ulong OrphanWarningExecutionWindow = 10;

    internal void SetTransientWrites(bool transientWrites)
    {
        _transientWrites = transientWrites;
    }

    /// <summary>
    /// Marks the buffer as GPU-read by this execution. Call when actually bound/used, not just recorded.
    /// </summary>
    internal void MarkInFlight(GraphicsDevice device, ulong executionId)
    {
        if (_transientWrites)
            return;

        _inFlightDevice = device;
        _inFlightExecutionId = executionId;
    }

    private bool IsInFlight =>
        _inFlightDevice != null
        && _inFlightExecutionId != 0
        && !_inFlightDevice.IsExecutionIdComplete(_inFlightExecutionId);

    /// <summary>
    /// Call before a CPU write to this buffer. If GPU might still be reading it from an earlier execution,
    /// orphans the native resource and allocates a fresh one so the write can't race the GPU read.
    /// </summary>
    internal void EnsureWritable()
    {
        if (!IsInFlight)
            return;

        GraphicsDevice device = _inFlightDevice;
        ulong executionId = _inFlightExecutionId;
        if (_lastOrphanExecutionId != 0 && executionId - _lastOrphanExecutionId < OrphanWarningExecutionWindow)
        {
            device.OnWarning?.Invoke(
                $"DeviceBuffer '{Name}' was implicitly reallocated {executionId - _lastOrphanExecutionId} executions after its previous reallocation. " +
                "This buffer is being written to while still in flight on the GPU, which forces a hidden reallocation on every such write. " +
                "If this buffer is rewritten every execution, use a StreamingBuffer instead.");
        }

        OrphanCore(device, executionId);

        _lastOrphanExecutionId = executionId;
        _inFlightDevice = null;
        _inFlightExecutionId = 0;
    }

    /// <summary>
    /// Recreates the native resource in place, keeping the same buffer identity. Don't free the old
    /// resource right away - GPU might still read it, so defer disposal until the execution completes.
    /// </summary>
    /// <param name="device">Device that last used this buffer.</param>
    /// <param name="inFlightExecutionId">Execution that may still be reading the old resource.</param>
    protected internal abstract void OrphanCore(GraphicsDevice device, ulong inFlightExecutionId);
}
