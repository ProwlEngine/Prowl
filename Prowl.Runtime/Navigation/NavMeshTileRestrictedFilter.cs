// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Wraps another <see cref="IDtQueryFilter"/> and additionally excludes any polygon whose tile isn't in
/// <paramref name="allowedTiles"/> - <see cref="NavMeshQuery"/>'s own hierarchical pathfinding fast path,
/// which uses this to bound a detailed Detour search to the narrow tile corridor a coarse tile-level A*
/// already found, so the underlying search visits a small fraction of the polygons an unrestricted one
/// would across a large navmesh.
/// </summary>
internal sealed class NavMeshTileRestrictedFilter(IDtQueryFilter inner, HashSet<(int X, int Y)> allowedTiles) : IDtQueryFilter
{
    public bool PassFilter(long polyRef, DtMeshTile tile, DtPoly poly) =>
        allowedTiles.Contains((tile.data.header.x, tile.data.header.y)) && inner.PassFilter(polyRef, tile, poly);

    public float GetCost(
        RcVec3f pa, RcVec3f pb,
        long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
        long curRef, DtMeshTile curTile, DtPoly curPoly,
        long nextRef, DtMeshTile nextTile, DtPoly nextPoly) =>
        inner.GetCost(pa, pb, prevRef, prevTile, prevPoly, curRef, curTile, curPoly, nextRef, nextTile, nextPoly);
}
