// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Jitter2.Collision;

namespace Prowl.Runtime;

/// <summary>
/// Composite filter that chains multiple IBroadPhaseFilter instances together.
/// All filters must return true for the collision to be allowed.
/// Filters are evaluated in the order they were added.
/// </summary>
public class CompositeBroadPhaseFilter : IBroadPhaseFilter
{
    private readonly List<IBroadPhaseFilter> _filters = new();

    /// <summary>
    /// Adds a filter to the chain.
    /// </summary>
    public void AddFilter(IBroadPhaseFilter filter)
    {
        if (filter != null && !_filters.Contains(filter))
        {
            _filters.Add(filter);
        }
    }

    /// <summary>
    /// Removes a filter from the chain.
    /// </summary>
    public void RemoveFilter(IBroadPhaseFilter filter)
    {
        _filters.Remove(filter);
    }

    /// <summary>
    /// Clears all filters from the chain.
    /// </summary>
    public void ClearFilters()
    {
        _filters.Clear();
    }

    /// <summary>
    /// Filters the collision by running all registered filters.
    /// Returns true only if all filters return true.
    /// If any filter returns false, processing stops immediately (short-circuit evaluation).
    /// </summary>
    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        foreach (var filter in _filters)
        // In reverse order
        //for (int i = 0; i < _filters.Count; i++)
        {
            //var filter = _filters[i];
            if (!filter.Filter(proxyA, proxyB))
                return false;
        }

        return true;
    }
}
