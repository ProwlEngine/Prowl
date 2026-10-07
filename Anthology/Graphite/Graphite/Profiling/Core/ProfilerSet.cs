using System.Collections.Generic;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite;

internal sealed class ProfilerSet
{
    public static readonly ProfilerSet Empty = new([]);

    public IGraphProfiler? Graph { get; }

    public IGpuStatsProfiler? GpuStats { get; }

    public ICaptureProfiler? Capture { get; }

    public ICommandStreamProfiler? CommandStream { get; }

    public ProfilerSet(IReadOnlyList<IProfiler> profilers)
    {
        List<IProfiler> leaves = new();
        foreach (IProfiler profiler in profilers)
        {
            if (profiler is CompositeProfiler composite)
                leaves.AddRange(composite.Leaves);
            else
                leaves.Add(profiler);
        }

        Graph = Resolve<IGraphProfiler>(leaves);
        GpuStats = Resolve<IGpuStatsProfiler>(leaves);
        Capture = First<ICaptureProfiler>(leaves);
        CommandStream = First<ICommandStreamProfiler>(leaves);
    }

    private static T? Resolve<T>(List<IProfiler> leaves) where T : class, IProfiler
    {
        List<IProfiler> matches = leaves.FindAll(p => p is T);
        return matches.Count switch
        {
            0 => null,
            1 => (T)matches[0],
            _ => (T)(IProfiler)new CompositeProfiler(matches.ToArray()),
        };
    }

    private static T? First<T>(List<IProfiler> leaves) where T : class, IProfiler
    {
        foreach (IProfiler profiler in leaves)
        {
            if (profiler is T match)
                return match;
        }
        return null;
    }
}
