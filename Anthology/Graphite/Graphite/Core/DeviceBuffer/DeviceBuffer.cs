using System;

namespace Prowl.Graphite;

/// <summary>
/// GPU-side data buffer. Fixed size, no resizing.
/// </summary>
public abstract partial class DeviceBuffer : GraphicsResource, BindableResource, MappableResource
{
    private protected BufferDescription _description;

    private protected DeviceBuffer(in BufferDescription description)
    {
        _description = description;
    }

    /// <summary>
    /// Size in bytes, fixed at creation.
    /// </summary>
    public uint SizeInBytes => _description.SizeInBytes;

    /// <summary>
    /// Allowed uses.
    /// </summary>
    public BufferUsage Usage => _description.Usage;

    private GraphicsDevice _inFlightDevice;
    private ulong _inFlightExecutionId;
    private ulong _lastOrphanExecutionId;

    private const ulong OrphanWarningExecutionWindow = 10;

    /// <summary>
    /// Bumps on every content change (CPU write or GPU copy in). Use to skip re-snapshotting unchanged data.
    /// Doesn't catch GPU compute writes to read-write bound buffers.
    /// </summary>
    public uint ContentVersion { get; private set; }

    internal void MarkContentChanged()
    {
        unchecked { ContentVersion++; }
    }

    internal void SetTransientWrites(bool transientWrites)
    {
        _description.TransientWrites = transientWrites;
    }

    internal void MarkInFlight(GraphicsDevice device, ulong executionId)
    {
        if (_description.TransientWrites)
            return;

        _inFlightDevice = device;
        _inFlightExecutionId = executionId;
    }

    private bool IsInFlight =>
        _inFlightDevice != null
        && _inFlightExecutionId != 0
        && !_inFlightDevice.IsExecutionIdComplete(_inFlightExecutionId);

    internal void EnsureWritable()
    {
        MarkContentChanged();

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

    protected internal abstract void OrphanCore(GraphicsDevice device, ulong inFlightExecutionId);
}
