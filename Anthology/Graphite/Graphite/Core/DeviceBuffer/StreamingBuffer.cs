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
/// Create via ResourceFactory.CreateStreamingBuffer.
/// </para>
/// </summary>
public sealed class StreamingBuffer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly DeviceBuffer[] _buffers;

    /// <summary>
    /// Capacity in bytes of each backing buffer. Fixed at creation.
    /// </summary>
    public uint SizeInBytes { get; }

    /// <summary>
    /// Bitmask of allowed uses for each backing buffer.
    /// </summary>
    public BufferUsage Usage { get; }

    /// <summary>
    /// Number of backing buffers, set to MaxExecutingGraphs at creation.
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
    /// Backing buffer for the execution's ring slot. Write and bind this while recording that execution.
    /// </summary>
    /// <param name="task">Execution whose ring slot picks the buffer.</param>
    public DeviceBuffer ForExecution(GraphExecutionTask task) => _buffers[task.RingSlot];

    /// <summary>
    /// Backing buffer for a given ring slot.
    /// </summary>
    /// <param name="ringSlot">Ring slot index, 0 to BufferCount.</param>
    public DeviceBuffer this[uint ringSlot] => _buffers[ringSlot];

    /// <summary>
    /// Sets debug name on every backing buffer, suffixed with ring slot index.
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
    /// Frees unmanaged resources of every backing buffer.
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < _buffers.Length; i++)
            _buffers[i].Dispose();
    }
}
