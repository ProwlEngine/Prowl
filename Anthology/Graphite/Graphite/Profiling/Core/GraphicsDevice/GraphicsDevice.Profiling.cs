using System;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// Attached profiler, null if none. Set at construction from <see cref="GraphicsDeviceOptions.Profiler"/>,
    /// and swappable afterward via <see cref="SetProfiler"/>.
    /// </summary>
    public IProfiler? Profiler { get; private set; }

    /// <summary>Attached profiler's command capability, null if it does not implement <see cref="ICommandProfiler"/>.</summary>
    internal ICommandProfiler? CommandProfiler { get; private set; }

    /// <summary>Attached profiler's graph capability, null if it does not implement <see cref="IGraphProfiler"/>.</summary>
    internal IGraphProfiler? GraphProfiler { get; private set; }

    /// <summary>Attached profiler's GPU stats capability, null if it does not implement <see cref="IGpuStatsProfiler"/>.</summary>
    internal IGpuStatsProfiler? GpuStatsProfiler { get; private set; }

    private int _graphDispatchDepth;

    private void InitializeFrameOptions_InitializeProfiling(in GraphicsDeviceOptions options)
    {
        AttachProfiler(options.Profiler);
    }

    private void AttachProfiler(object? profiler)
    {
        Profiler = profiler as IProfiler;
        CommandProfiler = profiler as ICommandProfiler;
        GraphProfiler = profiler as IGraphProfiler;
        GpuStatsProfiler = profiler as IGpuStatsProfiler;
    }

    /// <summary>
    /// Swaps the active profiler. Must only be called at a frame boundary - between
    /// <see cref="DispatchGraph{T}"/> calls, never mid-pass or mid-submit. The caller is assumed to be
    /// single-threaded with respect to frame dispatch, so this performs no locking. Implementing a capability
    /// interface is the opt-in for that category.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown while a <see cref="DispatchGraph{T}"/> is executing.</exception>
    public void SetProfiler(object? profiler)
    {
        if (_graphDispatchDepth != 0)
            throw new InvalidOperationException("SetProfiler cannot be called while a graph is dispatching.");

        AttachProfiler(profiler);
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

    /// <summary>Records a buffer creation: DeviceBuffer allocation plus role gauges.</summary>
    internal void RecordBufferAllocation(BufferUsage usage, long bytes)
    {
        if (Profiler is not { } profiler)
            return;

        profiler.Allocate(AllocBin.DeviceBuffer, bytes);
        ForEachBufferRole(usage, bytes, profiler, allocate: true);
    }

    /// <summary>Records a buffer destruction: DeviceBuffer free plus role gauges.</summary>
    internal void RecordBufferFree(BufferUsage usage, long bytes)
    {
        if (Profiler is not { } profiler)
            return;

        profiler.Free(AllocBin.DeviceBuffer, bytes);
        ForEachBufferRole(usage, bytes, profiler, allocate: false);
    }
}
