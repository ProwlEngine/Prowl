using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Prowl.Graphite;

/// <summary>Thread-safe list of profiler factories, read once when each execution starts. A factory returns null to skip that execution.</summary>
public sealed class GlobalProfilerList : Collection<Func<IProfiler?>>
{
    private readonly object _lock = new();

    internal GlobalProfilerList()
    {
    }

    internal ProfilerSet Create()
    {
        Func<IProfiler?>[] factories;
        lock (_lock)
        {
            if (Count == 0)
                return ProfilerSet.Empty;

            factories = new Func<IProfiler?>[Count];
            CopyTo(factories, 0);
        }

        List<IProfiler> profilers = new(factories.Length);
        foreach (Func<IProfiler?> factory in factories)
        {
            if (factory() is { } profiler)
                profilers.Add(profiler);
        }

        return profilers.Count == 0 ? ProfilerSet.Empty : new ProfilerSet(profilers);
    }

    protected override void InsertItem(int index, Func<IProfiler?> item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock)
            base.InsertItem(index, item);
    }

    protected override void SetItem(int index, Func<IProfiler?> item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock)
            base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        lock (_lock)
            base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        lock (_lock)
            base.ClearItems();
    }
}
