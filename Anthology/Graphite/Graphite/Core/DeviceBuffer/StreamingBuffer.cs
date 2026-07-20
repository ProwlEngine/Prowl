using System;

namespace Prowl.Graphite;

/// <summary>
/// A buffer for data rewritten every execution, like per-view uniforms.
/// <para>
/// Writing one buffer every execution races the GPU still reading a previous execution's data.
/// StreamingBuffer fixes this by keeping one backing buffer per in-flight execution, exposed via
/// ForExecution. Write and bind that buffer while recording; rotation is automatic.
/// </para>
/// <para>
/// Create via ResourceFactory.CreateStreamingBuffer.
/// </para>
/// </summary>
public sealed class StreamingBuffer : IDisposable
{
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
    /// Number of backing buffers, set to MaxExecutingTasks at creation.
    /// </summary>
    public int BufferCount => _buffers.Length;

    internal StreamingBuffer(GraphicsDevice device, ref BufferDescription description)
    {
        SizeInBytes = description.SizeInBytes;
        Usage = description.Usage;

        _buffers = new DeviceBuffer[device.MaxExecutingTasks];
        for (int i = 0; i < _buffers.Length; i++)
            _buffers[i] = device.ResourceFactory.CreateBuffer(ref description);
    }

    /// <summary>
    /// Backing buffer for the execution's ring slot. Write and bind this while recording that execution.
    /// </summary>
    /// <param name="task">Execution whose ring slot picks the buffer.</param>
    public DeviceBuffer ForExecution(ExecutionTask task) => _buffers[task.RingSlot];

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
