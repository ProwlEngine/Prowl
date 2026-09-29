using System;
using System.Collections.Generic;

namespace Prowl.Editor;

/// <summary>
/// Tracks forward and reverse dependencies between assets.
/// Forward: asset -> what it depends on. Reverse: asset -> what depends on it.
/// Locked, since a build reads it on its own thread while the editor imports, and what it hands out is a copy.
/// </summary>
public class DependencyGraph
{
    private readonly Dictionary<Guid, HashSet<Guid>> _forward = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _reverse = new();
    private readonly object _lock = new();

    /// <summary> Sets the dependencies for the specified asset, replacing any existing dependencies. </summary>
    public void SetDependencies(Guid asset, IEnumerable<Guid> dependencies)
    {
        lock (_lock) SetDependenciesLocked(asset, dependencies);
    }

    private void SetDependenciesLocked(Guid asset, IEnumerable<Guid> dependencies)
    {
        // Remove old reverse links
        if (_forward.TryGetValue(asset, out var oldDeps))
        {
            foreach (var dep in oldDeps)
                _reverse.GetValueOrDefault(dep)?.Remove(asset);
        }

        // Set new forward links
        var depSet = new HashSet<Guid>(dependencies ?? []);
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
    /// <summary> Removes the asset and its forward dependencies from the graph. Reverse links pointing to this asset are preserved. </summary>
    public void RemoveAsset(Guid asset)
    {
        lock (_lock) RemoveAssetLocked(asset);
    }

    private void RemoveAssetLocked(Guid asset)
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

    /// <summary> Returns the set of assets that the given asset directly depends on. </summary>
    public IReadOnlySet<Guid> GetDependencies(Guid asset)
    {
        lock (_lock) return _forward.TryGetValue(asset, out var deps) ? new HashSet<Guid>(deps) : new HashSet<Guid>();
    }

    /// <summary> Returns the set of assets that directly depend on the given asset. </summary>
    public IReadOnlySet<Guid> GetDependents(Guid asset)
    {
        lock (_lock) return _reverse.TryGetValue(asset, out var dependents) ? new HashSet<Guid>(dependents) : new HashSet<Guid>();
    }

    /// <summary>Get all assets that transitively depend on the given roots.</summary>
    public HashSet<Guid> GetTransitiveDependents(IEnumerable<Guid> roots)
    {
        lock (_lock) return Transitive(roots, _reverse);
    }

    /// <summary>Get all assets that the given roots transitively depend on (forward walk).</summary>
    public HashSet<Guid> GetTransitiveDependencies(IEnumerable<Guid> roots)
    {
        lock (_lock) return Transitive(roots, _forward);
    }

    private static HashSet<Guid> Transitive(IEnumerable<Guid> roots, Dictionary<Guid, HashSet<Guid>> edges)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>(roots);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (edges.TryGetValue(current, out var next))
                foreach (var guid in next)
                    queue.Enqueue(guid);
        }
        return visited;
    }

    /// <summary> Removes all assets and dependencies from the graph. </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _forward.Clear();
            _reverse.Clear();
        }
    }
}
