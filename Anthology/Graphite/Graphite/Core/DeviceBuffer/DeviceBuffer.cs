using System;

namespace Prowl.Graphite;

/// <summary>
/// A device resource used to store arbitrary graphics data in various formats.
/// The size of a <see cref="DeviceBuffer"/> is fixed upon creation, and resizing is not possible.
/// See <see cref="BufferDescription"/>.
/// </summary>
public abstract partial class DeviceBuffer : DeviceResource, BindableResource, MappableResource, IDisposable
{
    /// <summary>
    /// The total capacity, in bytes, of the buffer. This value is fixed upon creation.
    /// </summary>
    public abstract uint SizeInBytes { get; }

    /// <summary>
    /// A bitmask indicating how this instance is permitted to be used.
    /// </summary>
    public abstract BufferUsage Usage { get; }

    /// <summary>
    /// A string identifying this instance. Can be used to differentiate between objects in graphics debuggers and other
    /// tools.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// A bool indicating whether this instance has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources controlled by this instance.
    /// </summary>
    public abstract void Dispose();

    private GraphicsDevice _inFlightDevice;
    private ulong _inFlightFrameId;
    private ulong _lastOrphanFrameId;
    private bool _transientWrites;

    /// <summary>
    /// If reallocations happen again within this many frames of the previous one, a warning is logged.
    /// </summary>
    private const ulong OrphanWarningFrameWindow = 10;

    internal void SetTransientWrites(bool transientWrites)
    {
        _transientWrites = transientWrites;
    }

    /// <summary>
    /// Marks this buffer as read by the GPU during the given frame. Called at the point a buffer is actually
    /// bound or referenced for GPU use, not merely recorded.
    /// </summary>
    internal void MarkInFlight(GraphicsDevice device, ulong frameId)
    {
        if (_transientWrites)
            return;

        _inFlightDevice = device;
        _inFlightFrameId = frameId;
    }

    private bool IsInFlight => _inFlightDevice != null && _inFlightFrameId != 0 && !_inFlightDevice.IsFrameComplete(_inFlightFrameId);

    /// <summary>
    /// Called before a CPU-side write to this buffer (via <see cref="GraphicsDevice.Map(MappableResource, MapMode)"/>
    /// or <see cref="GraphicsDevice.UpdateBuffer(DeviceBuffer, uint, IntPtr, uint)"/>). If the buffer's contents may
    /// still be read by the GPU from an earlier frame, this orphans the underlying native resource and allocates a
    /// fresh one in its place, so the write cannot race the in-flight GPU read.
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
    /// Recreates this buffer's underlying native resource in place, keeping the same <see cref="DeviceBuffer"/>
    /// identity. The old native resource must not be freed immediately: it may still be read by the GPU, so its
    /// disposal must be deferred (via <see cref="GraphicsDevice.DisposeWhenFrameComplete"/>) until
    /// <paramref name="inFlightFrameId"/> completes.
    /// </summary>
    /// <param name="device">The device that last used this buffer.</param>
    /// <param name="inFlightFrameId">The frame that may still be reading the old native resource.</param>
    protected internal abstract void OrphanCore(GraphicsDevice device, ulong inFlightFrameId);
}
