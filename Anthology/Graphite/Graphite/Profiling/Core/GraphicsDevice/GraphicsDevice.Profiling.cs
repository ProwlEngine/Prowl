using System;
using System.Threading;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>Always-on counters of what the backend is doing.</summary>
    public GraphicsCounters Counters { get; } = new();

    /// <summary>Factories that each create one profiler for every execution started without profilers of its own.</summary>
    public GlobalProfilerList GlobalProfilers { get; } = new();

    private long _pipelineIdCounter;

    internal ulong NextPipelineId() => (ulong)Interlocked.Increment(ref _pipelineIdCounter);

    private ProfilerSet ResolveProfilers(IProfiler[] profilers)
    {
        if (profilers.Length == 0)
            return GlobalProfilers.Create();

        foreach (IProfiler profiler in profilers)
        {
            if (profiler == null)
                throw new ArgumentException("Profiler list contains null.", nameof(profilers));
        }

        return new ProfilerSet(profilers);
    }
}
