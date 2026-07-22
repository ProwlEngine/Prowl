using System;

namespace Prowl.Graphite;

/// <summary>
/// Device resource storing graphics data. Fixed size, no resizing.
/// </summary>
public abstract partial class DeviceBuffer : DeviceResource, BindableResource, MappableResource, IDisposable
{
    /// <summary>
    /// Capacity in bytes. Fixed at creation.
    /// </summary>
    public abstract uint SizeInBytes { get; }

    /// <summary>
    /// Allowed uses bitmask.
    /// </summary>
    public abstract BufferUsage Usage { get; }

    /// <summary>
    /// Debug name.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// Disposed?
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged resources.
    /// </summary>
    public abstract void Dispose();

    private required GraphicsDevice _inFlightDevice;
    private ulong _inFlightExecutionId;
    private ulong _lastOrphanExecutionId;
    private bool _transientWrites;

    /// <summary>
    /// Warn if reallocated again within this many executions.
    /// </summary>
    private const ulong OrphanWarningExecutionWindow = 10;

    internal void SetTransientWrites(bool transientWrites)
    {
        _transientWrites = transientWrites;
    }

    /// <summary>
    /// Marks buffer as GPU-read by this execution. Call on actual bind/use, not just record.
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
    /// Call before a CPU write. If GPU might still be reading it, orphans the native resource and
    /// allocates a fresh one so the write can't race the read.
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
                "If this buffer is rewritten every execution, rent a transient graph buffer per execution instead.");
        }

        OrphanCore(device, executionId);

        _lastOrphanExecutionId = executionId;
        _inFlightDevice = null;
        _inFlightExecutionId = 0;
    }

    /// <summary>
    /// Recreates native resource in place, same buffer identity. Don't free the old one yet - GPU might
    /// still read it, defer disposal until that execution completes.
    /// </summary>
    /// <param name="device">Device last using this buffer.</param>
    /// <param name="inFlightExecutionId">Execution that may still read the old resource.</param>
    protected internal abstract void OrphanCore(GraphicsDevice device, ulong inFlightExecutionId);
}
