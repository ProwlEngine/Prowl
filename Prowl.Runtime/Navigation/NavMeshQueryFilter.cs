// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;

namespace Prowl.Runtime;

/// <summary>
/// Filters navmesh queries by area mask and applies per-area path costs. Implements Detour's
/// filter interface directly against the 32-bit Prowl area mask, so all 32 areas are usable
/// (Detour's default filter only supports 16 flag bits). Cost overrides set here take
/// precedence over the project-wide defaults in <see cref="NavMeshAreas"/>.
/// </summary>
public sealed class NavMeshQueryFilter : IDtQueryFilter
{
    /// <summary>Bitmask of traversable areas (bit i = area index i). Defaults to everything.</summary>
    public int AreaMask = NavMeshAreas.AllAreas;

    /// <summary>The agent type whose navmesh this filter queries.</summary>
    public int AgentTypeId = 0;

    private float[]? _costOverrides;

    /// <summary>Path cost multiplier for an area: the override set on this filter, or the
    /// project default.</summary>
    public float GetAreaCost(int areaIndex)
    {
        if (_costOverrides != null && areaIndex >= 0 && areaIndex < _costOverrides.Length && _costOverrides[areaIndex] > 0f)
            return _costOverrides[areaIndex];
        return NavMeshAreas.GetAreaCost(areaIndex);
    }

    /// <summary>Override the path cost for an area on this filter only. Clamped to >= 1:
    /// Detour's A* heuristic is only admissible when no traversal is cheaper than distance,
    /// so costs below 1 would silently produce suboptimal paths. To prefer an area, raise the
    /// other areas' costs instead.</summary>
    public void SetAreaCost(int areaIndex, float cost)
    {
        if (areaIndex < 0 || areaIndex >= NavMeshAreas.MaxAreas) return;
        _costOverrides ??= new float[NavMeshAreas.MaxAreas];
        _costOverrides[areaIndex] = Math.Max(1f, cost);
    }

    /// <summary>Remove all per-filter cost overrides, falling back to project defaults.</summary>
    public void ClearAreaCosts() => _costOverrides = null;

    /// <summary>Raw override table (0 = no override), or null when none were ever set. For
    /// crowd filter-slot matching — treat as read-only.</summary>
    internal float[]? CostOverrides => _costOverrides;

    /// <summary>Replace this filter's overrides with a copy of <paramref name="source"/>
    /// (null clears). Used when a crowd filter slot takes on an agent's configuration —
    /// a copy, so the agent mutating its own filter later can't skew a shared slot.</summary>
    internal void CopyCostOverridesFrom(float[]? source)
    {
        if (source == null)
        {
            _costOverrides = null;
            return;
        }
        _costOverrides ??= new float[NavMeshAreas.MaxAreas];
        Array.Clear(_costOverrides);
        Array.Copy(source, _costOverrides, Math.Min(source.Length, _costOverrides.Length));
    }

    bool IDtQueryFilter.PassFilter(long refs, DtMeshTile tile, DtPoly poly)
    {
        if (poly.flags == 0) return false;
        int area = NavMeshAreas.FromDetourArea(poly.GetArea());
        return (AreaMask & (1 << area)) != 0;
    }

    float IDtQueryFilter.GetCost(RcVec3f pa, RcVec3f pb, long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
        long curRef, DtMeshTile curTile, DtPoly curPoly, long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
    {
        int area = NavMeshAreas.FromDetourArea(curPoly.GetArea());
        return RcVec3f.Distance(pa, pb) * GetAreaCost(area);
    }
}
