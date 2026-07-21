namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// The profiler attached to this device, or null if none was supplied. Set once at construction.
    /// </summary>
    public IProfiler? Profiler { get; private set; }

    private void InitializeFrameOptions_InitializeProfiling(in GraphicsDeviceOptions options)
    {
        Profiler = options.Profiler;
    }

    // Fans a buffer allocation/free out to every BufferRoleBin matching a usage flag. Bins overlap
    // by design for multi-flag buffers - the caller's AllocBin.DeviceBuffer call is the
    // non-double-counted total, this is just the per-role gauges.
    internal static void ForEachBufferRole(BufferUsage usage, long bytes, IProfiler profiler, bool allocate)
    {
        if ((usage & BufferUsage.VertexBuffer) != 0)
            RecordRole(BufferRoleBin.Vertex);
        if ((usage & BufferUsage.IndexBuffer) != 0)
            RecordRole(BufferRoleBin.Index);
        if ((usage & BufferUsage.UniformBuffer) != 0)
            RecordRole(BufferRoleBin.Uniform);
        if ((usage & BufferUsage.StructuredBufferReadOnly) != 0)
            RecordRole(BufferRoleBin.StructuredReadOnly);
        if ((usage & BufferUsage.StructuredBufferReadWrite) != 0)
            RecordRole(BufferRoleBin.StructuredReadWrite);
        if ((usage & BufferUsage.IndirectBuffer) != 0)
            RecordRole(BufferRoleBin.Indirect);
        if ((usage & BufferUsage.Dynamic) != 0)
            RecordRole(BufferRoleBin.Dynamic);
        if ((usage & BufferUsage.Staging) != 0)
            RecordRole(BufferRoleBin.Staging);

        void RecordRole(BufferRoleBin role)
        {
            if (allocate)
                profiler.AllocateMemory(role, bytes);
            else
                profiler.FreeMemory(role, bytes);
        }
    }

    /// <summary>Records a buffer creation: the DeviceBuffer allocation plus the matching role gauges.</summary>
    internal void RecordBufferAllocation(BufferUsage usage, long bytes)
    {
        if (Profiler is not { } profiler)
            return;

        profiler.Allocate(AllocBin.DeviceBuffer, bytes);
        ForEachBufferRole(usage, bytes, profiler, allocate: true);
    }

    /// <summary>Records a buffer destruction: the DeviceBuffer free plus the matching role gauges.</summary>
    internal void RecordBufferFree(BufferUsage usage, long bytes)
    {
        if (Profiler is not { } profiler)
            return;

        profiler.Free(AllocBin.DeviceBuffer, bytes);
        ForEachBufferRole(usage, bytes, profiler, allocate: false);
    }
}
