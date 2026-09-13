// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Wraps another <see cref="IDtQueryFilter"/> (an area-mask/area-cost filter <see cref="NavMeshQuery.CreateFilter"/>
/// already builds) and adds one agent type's <see cref="NavMeshCostLayer"/> on top of whatever cost the
/// wrapped filter already charges - the one place a polygon's area cost and its transient overlay cost
/// actually combine. Polygon inclusion (<see cref="PassFilter"/>) is untouched: a cost layer never makes
/// a polygon impassable, only more expensive to path through, so an area mask still does all the actual
/// exclusion.
/// </summary>
internal sealed class NavMeshCostAwareFilter(IDtQueryFilter inner, NavMeshCostLayer costLayer) : IDtQueryFilter
{
    /// <summary>The wrapped filter - for <see cref="NavMeshCrowd.ResolveFilterSlot"/>, which needs to
    /// narrow a crowd filter slot's include flags in place after the fact and can only reach the
    /// concrete <c>DtQueryDefaultFilter</c> that lives through this wrapper, not around it.</summary>
    internal IDtQueryFilter Inner => inner;

    public bool PassFilter(long polyRef, DtMeshTile tile, DtPoly poly) => inner.PassFilter(polyRef, tile, poly);

    public float GetCost(
        RcVec3f pa, RcVec3f pb,
        long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
        long curRef, DtMeshTile curTile, DtPoly curPoly,
        long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
    {
        float baseCost = inner.GetCost(pa, pb, prevRef, prevTile, prevPoly, curRef, curTile, curPoly, nextRef, nextTile, nextPoly);

        // Charged against whichever of the two polygons this edge actually arrives at (nextRef) has the
        // higher overlay cost, falling back to curRef for the final edge of a search (where nextRef is 0,
        // meaning "the goal itself" rather than another polygon) - confirmed via direct instrumentation
        // that this reaches Detour's own search with the correct value every time it's called.
        float extra = System.MathF.Max(costLayer.GetAdditiveCost(curRef), costLayer.GetAdditiveCost(nextRef));
        if (extra <= 0f) return baseCost;

        float dx = pa.X - pb.X, dy = pa.Y - pb.Y, dz = pa.Z - pb.Z;
        float distance = System.MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        return baseCost + extra * distance;
    }
}
