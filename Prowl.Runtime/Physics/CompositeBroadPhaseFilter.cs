// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading;

using Jitter2.Collision;

namespace Prowl.Runtime;

/// <summary>
/// Composite filter that chains multiple IBroadPhaseFilter instances together.
/// All filters must return true for the collision to be allowed.
/// Filters are evaluated in the order they were added.
/// </summary>
public class CompositeBroadPhaseFilter : IBroadPhaseFilter
{
    private IBroadPhaseFilter[] _filters = [];

    /// <summary>
    /// Adds a filter to the chain.
    /// </summary>
    public void AddFilter(IBroadPhaseFilter filter)
    {
        if (filter == null) return;

        IBroadPhaseFilter[] current = Volatile.Read(ref _filters);
        if (Array.IndexOf(current, filter) >= 0) return;

        Volatile.Write(ref _filters, [.. current, filter]);
    }

    /// <summary>
    /// Removes a filter from the chain.
    /// </summary>
    public void RemoveFilter(IBroadPhaseFilter filter)
    {
        IBroadPhaseFilter[] current = Volatile.Read(ref _filters);
        int index = Array.IndexOf(current, filter);
        if (index < 0) return;

        var next = new IBroadPhaseFilter[current.Length - 1];
        Array.Copy(current, next, index);
        Array.Copy(current, index + 1, next, index, next.Length - index);
        Volatile.Write(ref _filters, next);
    }

    /// <summary>
    /// Clears all filters from the chain.
    /// </summary>
    public void ClearFilters() => Volatile.Write(ref _filters, []);

    /// <summary>
    /// Filters the collision by running all registered filters.
    /// Returns true only if all filters return true.
    /// If any filter returns false, processing stops immediately (short-circuit evaluation).
    /// </summary>
    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        IBroadPhaseFilter[] filters = Volatile.Read(ref _filters);

        for (int i = 0; i < filters.Length; i++)
            if (!filters[i].Filter(proxyA, proxyB))
                return false;

        return true;
    }
}
