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

    /// <summary>Always-on counters of what the backend is doing.</summary>
    public GraphicsCounters Counters { get; } = new();

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
}
