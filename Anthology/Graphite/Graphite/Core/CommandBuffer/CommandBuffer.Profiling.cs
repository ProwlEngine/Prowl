using System.Collections.Generic;

namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    /// <summary>Execution this buffer was rented for. Null if not tied to one.</summary>
    internal ExecutionTask? Execution { get; set; }

    /// <summary>Pass this buffer was rented during, for profiler timing. Null outside a pass.</summary>
    internal PassInfo? Pass { get; set; }

    /// <summary>Bound execution's id, or 0.</summary>
    internal ulong ExecutionId => Execution?.Id ?? 0;

    /// <summary>Fresh id stamped per rental, so profiler can tell reused instances apart.</summary>
    internal ulong RentalId { get; set; }

    internal CommandBufferInfo ProfilerInfo => new(RentalId, Name, Pass);

    /// <summary>
    /// True if profiler wants metadata via RecordMetadata. Check before building a metadata object.
    /// </summary>
    public bool WantsMetadata => Execution?.Device.Profiler?.RequestMetadata ?? false;

    /// <summary>
    /// Attaches metadata to every draw/dispatch since last call (or since open).
    /// </summary>
    public void RecordMetadata(object metadata) => Execution?.Device.Profiler?.RecordDrawMetadata(ProfilerInfo, metadata);

    /// <summary>Reports a resource-set bind to the profiler, if any.</summary>
    internal void RecordResourceSetBind(uint setCount) => Execution?.Device.Profiler?.RecordResourceSetBind(setCount);

    private readonly List<BufferBindingInfo> _capturedVertexBuffers = new();
    private BufferBindingInfo? _capturedIndexBuffer;

    /// <summary>
    /// True if profiler wants draw-time buffer bindings captured. Check before reporting via
    /// CaptureResolvedVertexBinding/CaptureResolvedIndexBinding, building BufferBindingInfo unread is wasted work.
    /// </summary>
    internal bool WantsDrawBufferCapture => Execution?.Device.Profiler?.RequestCapture ?? false;

    /// <summary>Clears capture state before backend resolves a new draw's buffers. Only if WantsDrawBufferCapture.</summary>
    internal void BeginDrawBufferCapture()
    {
        _capturedVertexBuffers.Clear();
        _capturedIndexBuffer = null;
    }

    /// <summary>
    /// Reports the vertex buffer backend just resolved and bound for the current draw. Only if
    /// WantsDrawBufferCapture - must be same resolution used for the real GPU bind, not a second query.
    /// </summary>
    internal void CaptureResolvedVertexBinding(in VertexBinding binding)
    {
        _capturedVertexBuffers.Add(new BufferBindingInfo(
            binding.Buffer.Name, binding.Buffer, binding.Offset, binding.Buffer.SizeInBytes - binding.Offset,
            binding.Buffer.ContentVersion, readOnly: true));
    }

    /// <summary>
    /// Reports the index buffer backend just resolved and bound for the current draw. Only if
    /// WantsDrawBufferCapture - must be same resolution used for the real GPU bind, not a second query.
    /// </summary>
    internal void CaptureResolvedIndexBinding(DeviceBuffer buffer, IndexFormat format, uint indexCount)
    {
        uint indexSize = format == IndexFormat.UInt16 ? 2u : 4u;
        _capturedIndexBuffer = new BufferBindingInfo(buffer.Name, buffer, offset: 0, indexSize * indexCount, buffer.ContentVersion, readOnly: true);
    }

    /// <summary>
    /// Reports buffers already bound for the just-recorded draw (captured earlier) plus any buffer-kind
    /// entries in active properties, to the profiler. No-op unless profiler requested a capture.
    /// </summary>
    private void RecordDrawBuffersIfRequested()
    {
        if (Execution?.Device.Profiler is not { RequestCapture: true } profiler)
            return;

        var boundBuffers = new List<BufferBindingInfo>();
        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in _activeProperties.Entries)
        {
            if (kv.Value.Kind == PropertyEntryKind.Buffer && kv.Value.Buffer is { } range)
            {
                string name = PropertyID.ToString(kv.Key) ?? kv.Key.ToString();
                boundBuffers.Add(new BufferBindingInfo(
                    name, range.Buffer, range.Offset, range.SizeInBytes, range.Buffer.ContentVersion, kv.Value.ReadOnly));
            }
        }

        var vertexBuffers = new List<BufferBindingInfo>(_capturedVertexBuffers);
        profiler.RecordDrawBuffers(ProfilerInfo, new DrawBufferInfo(vertexBuffers, _capturedIndexBuffer, boundBuffers));
    }
}
