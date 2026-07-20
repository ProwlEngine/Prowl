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
    private ulong _inFlightFrameId;
    private ulong _lastOrphanFrameId;
    private bool _transientWrites;

    /// <summary>
    /// Log a warning if reallocations happen again within this many frames.
    /// </summary>
    private const ulong OrphanWarningFrameWindow = 10;

    internal void SetTransientWrites(bool transientWrites)
    {
        _transientWrites = transientWrites;
    }

    /// <summary>
    /// Marks the buffer as GPU-read this frame. Call when actually bound/used, not just recorded.
    /// </summary>
    internal void MarkInFlight(GraphicsDevice device, ulong frameId)
    {
        if (_transientWrites)
            return;

        _inFlightDevice = device;
        _inFlightFrameId = frameId;
    }

    private bool IsInFlight =>
        _inFlightDevice != null
        && _inFlightFrameId != 0
        && !_inFlightDevice.IsFrameOpen(_inFlightFrameId)
        && !_inFlightDevice.IsFrameComplete(_inFlightFrameId);

    /// <summary>
    /// Call before a CPU write to this buffer. If GPU might still be reading it from an earlier frame,
    /// orphans the native resource and allocates a fresh one so the write can't race the GPU read.
    /// </summary>
    internal void EnsureWritable()
    {
        if (!IsInFlight)
            return;

        GraphicsDevice device = _inFlightDevice;
        ulong frameId = _inFlightFrameId;
        if (_lastOrphanFrameId != 0 && frameId - _lastOrphanFrameId < OrphanWarningFrameWindow)
        {
            device.OnWarning?.Invoke(
                $"DeviceBuffer '{Name}' was implicitly reallocated {frameId - _lastOrphanFrameId} frames after its previous reallocation. " +
                "This buffer is being written to while still in flight on the GPU, which forces a hidden reallocation on every such write. " +
                "If this buffer is rewritten every frame, use a StreamingBuffer instead.");
        }

        OrphanCore(device, frameId);

        _lastOrphanFrameId = frameId;
        _inFlightDevice = null;
        _inFlightFrameId = 0;
    }

    /// <summary>
    /// Recreates the native resource in place, keeping the same buffer identity. Don't free the old
    /// resource right away - GPU might still read it, so defer disposal until the frame completes.
    /// </summary>
    /// <param name="device">Device that last used this buffer.</param>
    /// <param name="inFlightFrameId">Frame that may still be reading the old resource.</param>
    protected internal abstract void OrphanCore(GraphicsDevice device, ulong inFlightFrameId);
}
