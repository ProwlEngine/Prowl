using System.Collections.Generic;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite;

internal sealed class ProfilerSet
{
    public static readonly ProfilerSet Empty = new([]);

    private readonly IProfiler[] _all;

    public IGraphProfiler? Graph { get; }

    public IGpuStatsProfiler? GpuStats { get; }

    public ICaptureProfiler? Capture { get; }

    public ICommandStreamProfiler? CommandStream { get; }

    public bool IsEmpty => _all.Length == 0;

    public ProfilerSet(IReadOnlyList<IProfiler> profilers)
    {
        List<IProfiler> distinct = new(profilers.Count);
        foreach (IProfiler profiler in profilers)
        {
            if (!distinct.Contains(profiler))
                distinct.Add(profiler);
        }

        _all = distinct.ToArray();
        Graph = Resolve<IGraphProfiler>(distinct, sinks => new CompositeGraphProfiler(sinks));
        GpuStats = Resolve<IGpuStatsProfiler>(distinct, sinks => new CompositeGpuStatsProfiler(sinks));
        Capture = Resolve<ICaptureProfiler>(distinct, sinks => new CompositeCaptureProfiler(sinks));
        CommandStream = Resolve<ICommandStreamProfiler>(distinct, sinks => new CompositeCommandStreamProfiler(sinks));
    }

    public void BeginExecution(ulong executionId, string graphName)
    {
        foreach (IProfiler profiler in _all)
            profiler.BeginExecution(executionId, graphName);
    }

    public void EndExecution()
    {
        foreach (IProfiler profiler in _all)
            profiler.EndExecution();
    }

    private static T? Resolve<T>(List<IProfiler> profilers, System.Func<T[], T> composite) where T : class, IProfiler
    {
        List<T> matches = new();
        foreach (IProfiler profiler in profilers)
        {
            if (profiler is T match)
                matches.Add(match);
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => composite(matches.ToArray()),
        };
    }
}
