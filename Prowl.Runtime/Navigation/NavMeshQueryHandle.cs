// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// A Detour query engine rented from a <see cref="NavMeshQuery"/> via <see cref="NavMeshQuery.TryRentQuery"/>,
/// held for as long as this handle lives rather than re-rented on every call the way <see cref="NavMeshQuery"/>'s
/// own <c>FindPath</c>/<c>Raycast</c>/etc. already do internally. Mirrors that same public surface so a
/// caller doing several queries back to back can pay the rental's read-lock/pool overhead once; a Detour
/// type still never crosses this struct's own public boundary, matching every other file in this
/// namespace. Dispose returns the rented engine and releases the read-lock side that came with it -
/// always dispose one of these, ideally in a <c>using</c>, since a rental left open blocks
/// <see cref="NavMeshQuery"/>'s write side (obstacle carving, <c>RebuildTiles</c>) forever.
/// </summary>
public readonly struct NavMeshQueryHandle : System.IDisposable
{
    private readonly NavMeshQuery _owner;
    private readonly DtNavMeshQuery _query;

    internal NavMeshQueryHandle(NavMeshQuery owner, DtNavMeshQuery query)
    {
        _owner = owner;
        _query = query;
    }

    /// <summary>Same as <see cref="NavMeshQuery.FindPath"/>, against this handle's own rented engine.</summary>
    public NavMeshPath FindPath(Float3 start, Float3 end, uint areaMask = uint.MaxValue) =>
        _owner.FindPathUsing(_query, start, end, areaMask);

    /// <summary>Same as <see cref="NavMeshQuery.Raycast"/>, against this handle's own rented engine.</summary>
    public bool Raycast(Float3 start, Float3 end, out Float3 hitPoint) =>
        _owner.RaycastUsing(_query, start, end, out hitPoint);

    /// <summary>Same as <see cref="NavMeshQuery.FindClosestEdge"/>, against this handle's own rented engine.</summary>
    public bool FindClosestEdge(Float3 point, float maxRadius, out Float3 hitPosition, out Float3 hitNormal, out float distance) =>
        _owner.FindClosestEdgeUsing(_query, point, maxRadius, out hitPosition, out hitNormal, out distance);

    /// <summary>Same as <see cref="NavMeshQuery.SamplePosition"/>, against this handle's own rented engine.</summary>
    public bool SamplePosition(Float3 point, out Float3 result) =>
        _owner.SamplePositionUsing(_query, point, out result);

    /// <summary>Returns the rented engine to its pool and releases the read-lock side of the rental.</summary>
    public void Dispose() => _owner.ReturnRentedQuery(_query);
}
