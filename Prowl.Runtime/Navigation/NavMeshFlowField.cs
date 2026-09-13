// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// A shared route to one goal, built once and followed by any number of agents - <see cref="NavMeshAgent.FollowFlowField"/>
/// is the alternative to <see cref="NavMeshAgent.SetDestination"/> this backs, for a crowd that would
/// otherwise mean one <see cref="NavMeshQuery.FindPath"/> per agent for the same destination. Built by
/// <see cref="NavMeshQuery.GetOrBuildFlowField"/>/<see cref="NavMeshSystem.BuildFlowField"/> as a single
/// flood search from the goal outward across the polygon graph (Detour's own <c>FindPolysAroundCircle</c>,
/// which already accumulates cost through the same area-cost/cost-layer-aware filter every other query on
/// this engine uses) rather than a per-voxel-cell grid: a polygon is Detour's own natural unit of
/// adjacency, and building over it needs nothing beyond a primitive this engine already relies on
/// elsewhere, at the cost of one thing a true per-cell field would give for free - sub-polygon direction
/// resolution inside one large, flat polygon, where every position resolves to the same single direction
/// regardless of exactly where within it an agent stands. Crowd separation/avoidance still layers on top
/// of whatever direction a field reports, the same as it would for a plain path-following agent - this
/// only ever supplies a "which way to the goal" answer, never bypasses the crowd itself.
/// </summary>
public sealed class NavMeshFlowField
{
    private readonly NavMeshQuery _query;
    private readonly Dictionary<long, Float3> _directions;
    private readonly Dictionary<long, float> _distances;

    internal NavMeshFlowField(NavMeshQuery query, Dictionary<long, Float3> directions, Dictionary<long, float> distances)
    {
        _query = query;
        _directions = directions;
        _distances = distances;
    }

    /// <summary>The direction to steer from <paramref name="worldPos"/> toward this field's goal, or
    /// false if <paramref name="worldPos"/> doesn't resolve to a polygon this field actually covers (off
    /// the mesh entirely, or beyond the radius the field was built with). Zero (not false) right at the
    /// goal's own polygon - there is nowhere closer to steer toward from there.</summary>
    public bool TryGetDirection(Float3 worldPos, out Float3 direction)
    {
        if (_query.TryFindNearestPolyRef(worldPos, out long polyRef, out _) && _directions.TryGetValue(polyRef, out direction))
            return true;

        direction = Float3.Zero;
        return false;
    }

    /// <summary>The remaining distance along the polygon graph from <paramref name="worldPos"/> to this
    /// field's goal - the same accumulated cost <see cref="TryGetDirection"/>'s search already computed,
    /// exposed for a caller that wants "how far still to go" without a second query (a progress bar, an
    /// arrival check looser than exact position comparison). False under the same conditions as
    /// <see cref="TryGetDirection"/>.</summary>
    public bool TryGetDistanceToGoal(Float3 worldPos, out float distance)
    {
        if (_query.TryFindNearestPolyRef(worldPos, out long polyRef, out _) && _distances.TryGetValue(polyRef, out distance))
            return true;

        distance = 0f;
        return false;
    }
}
