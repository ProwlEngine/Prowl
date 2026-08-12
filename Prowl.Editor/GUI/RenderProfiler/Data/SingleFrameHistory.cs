using System.Collections.Generic;

using Prowl.Editor.Profiling;

namespace Prowl.Editor.GUI.RenderProfiler.Data;

public sealed class SingleFrameHistory : IProfilerHistory
{
    private static readonly Dictionary<string, int> CounterIndex = BuildCounterIndex();

    private readonly ProfiledFrame[] _frames;

    public SingleFrameHistory(ProfiledFrame frame)
    {
        _frames = new[] { frame };
    }

    public IReadOnlyList<ProfiledFrame> Frames => _frames;

    public IReadOnlyList<double> Counter(string name)
    {
        double value = CounterIndex.TryGetValue(name, out int index) ? _frames[0].Counters[index].Value : 0d;
        return new[] { value };
    }

    private static Dictionary<string, int> BuildCounterIndex()
    {
        IReadOnlyList<CounterDef> registry = CountersCollector.Registry;
        var map = new Dictionary<string, int>(registry.Count);
        for (int i = 0; i < registry.Count; i++)
            map[registry[i].Name] = i;
        return map;
    }
}
