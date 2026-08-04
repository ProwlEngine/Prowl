using System;
using System.Collections.Generic;

namespace Prowl.Editor;

/// <summary>
/// Tracks forward and reverse dependencies between assets.
/// Forward: asset -> what it depends on. Reverse: asset -> what depends on it.
/// </summary>
public class DependencyGraph
{
    private readonly Dictionary<Guid, HashSet<Guid>> _forward = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _reverse = new();

    public void SetDependencies(Guid asset, IEnumerable<Guid> dependencies)
    {
        // Remove old reverse links
        if (_forward.TryGetValue(asset, out var oldDeps))
        {
            foreach (var dep in oldDeps)
                _reverse.GetValueOrDefault(dep)?.Remove(asset);
        }

        // Set new forward links
        var depSet = new HashSet<Guid>(dependencies);
        _forward[asset] = depSet;

        // Build reverse links
        foreach (var dep in depSet)
        {
            if (!_reverse.TryGetValue(dep, out var dependents))
            {
                dependents = new HashSet<Guid>();
                _reverse[dep] = dependents;
            }
            dependents.Add(asset);
        }
    }

    // Drop what this asset depends on. What depends on IT is left alone: those assets really do still
    // reference this GUID, and only re-importing them (via <see cref="SetDependencies"/>) can say
    // otherwise. Erasing their edge here silently unlinked anything whose sub-asset went away and came
    // back with the same GUID - a re-sliced sprite, or a file an external tool rewrote as delete+create -
    // leaving the graph disagreeing with the dependencies persisted on the entry until the next restart.
    public void RemoveAsset(Guid asset)
    {
        if (_forward.Remove(asset, out var deps))
        {
            foreach (var dep in deps)
            {
                if (_reverse.TryGetValue(dep, out var dependents) && dependents.Remove(asset) && dependents.Count == 0)
                    _reverse.Remove(dep);
            }
        }

        if (_reverse.TryGetValue(asset, out var stillReferencing) && stillReferencing.Count == 0)
            _reverse.Remove(asset);
    }

    /// <summary>What does this asset depend on?</summary>
    public IReadOnlySet<Guid> GetDependencies(Guid asset)
        => _forward.GetValueOrDefault(asset) ?? (IReadOnlySet<Guid>)new HashSet<Guid>();

    /// <summary>What assets depend on this one? (Find References)</summary>
    public IReadOnlySet<Guid> GetDependents(Guid asset)
        => _reverse.GetValueOrDefault(asset) ?? (IReadOnlySet<Guid>)new HashSet<Guid>();

    /// <summary>Get all assets that transitively depend on the given roots.</summary>
    public HashSet<Guid> GetTransitiveDependents(IEnumerable<Guid> roots)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>(roots);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            foreach (var dep in GetDependents(current))
                queue.Enqueue(dep);
        }
        return visited;
    }

    /// <summary>Get all assets that the given roots transitively depend on (forward walk).</summary>
    public HashSet<Guid> GetTransitiveDependencies(IEnumerable<Guid> roots)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>(roots);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            foreach (var dep in GetDependencies(current))
                queue.Enqueue(dep);
        }
        return visited;
    }

    public void Clear()
    {
        _forward.Clear();
        _reverse.Clear();
    }
}
