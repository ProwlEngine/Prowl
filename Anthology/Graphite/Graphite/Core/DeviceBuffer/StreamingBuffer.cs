using System;

namespace Prowl.Graphite;

/// <summary>
/// A buffer intended for data that is rewritten by the CPU every frame, such as per-frame uniform data.
/// <para>
/// Writing to a single <see cref="DeviceBuffer"/> every frame races with the frames-in-flight system: the GPU
/// may still be reading the buffer for a previous frame when the CPU overwrites it. A <see cref="StreamingBuffer"/>
/// sidesteps this by holding one backing <see cref="DeviceBuffer"/> per frame-in-flight and exposing the buffer
/// belonging to the currently active frame's ring slot through <see cref="Current"/>. Write to and bind
/// <see cref="Current"/> each frame; the rotation across the in-flight buffers is handled implicitly.
/// </para>
/// <para>
/// <see cref="Current"/> requires an active frame, since the ring slot is taken from
/// <see cref="GraphicsDevice.CurrentFrame"/>. Create one via
/// <see cref="ResourceFactory.CreateStreamingBuffer(BufferDescription)"/>.
/// </para>
/// </summary>
public sealed class StreamingBuffer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly DeviceBuffer[] _buffers;

    /// <summary>
    /// The total capacity, in bytes, of each backing buffer. This value is fixed upon creation.
    /// </summary>
    public uint SizeInBytes { get; }

    /// <summary>
    /// A bitmask indicating how each backing buffer is permitted to be used.
    /// </summary>
    public BufferUsage Usage { get; }

    /// <summary>
    /// The number of backing buffers, equal to <see cref="GraphicsDevice.MaxFramesInFlight"/> at creation time.
    /// </summary>
    public int BufferCount => _buffers.Length;

    internal StreamingBuffer(GraphicsDevice device, ref BufferDescription description)
    {
        _device = device;
        SizeInBytes = description.SizeInBytes;
        Usage = description.Usage;

        _buffers = new DeviceBuffer[device.MaxFramesInFlight];
        for (int i = 0; i < _buffers.Length; i++)
            _buffers[i] = device.ResourceFactory.CreateBuffer(ref description);
    }

    /// <summary>
    /// The backing <see cref="DeviceBuffer"/> for the currently active frame's ring slot. Write to and bind this
    /// buffer for the current frame. Requires an active frame.
    /// </summary>
    /// <exception cref="RenderException">Thrown if no frame is currently active.</exception>
    public DeviceBuffer Current => _buffers[_device.CurrentFrame.RingSlot];

    /// <summary>
    /// Gets the backing <see cref="DeviceBuffer"/> for the given ring slot.
    /// </summary>
    /// <param name="ringSlot">The ring slot index, in the range [0, <see cref="BufferCount"/>).</param>
    public DeviceBuffer this[uint ringSlot] => _buffers[ringSlot];

    /// <summary>
    /// Sets the debug name of every backing buffer, suffixed with the ring slot index.
    /// </summary>
    public string Name
    {
        set
        {
            for (int i = 0; i < _buffers.Length; i++)
                _buffers[i].Name = $"{value}[{i}]";
        }
    }

    /// <summary>
    /// Frees the unmanaged device resources of every backing buffer.
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < _buffers.Length; i++)
            _buffers[i].Dispose();
    }
}
