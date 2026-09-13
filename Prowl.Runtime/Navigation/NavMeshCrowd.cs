// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;
using Prowl.Recast.Detour.Crowd;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// One Detour crowd simulation for a single agent type's navmesh, owned by a scene's
/// <see cref="NavMeshSystem"/> (one per agent type, never one per agent). An agent joins with
/// <see cref="AddAgent"/> exactly once, on enable, and every later destination change reuses that same
/// slot through <see cref="SetDestination"/> - this is the fix for the "Move() tears down and rebuilds
/// the crowd agent" bug PR #335's review flagged: adding/removing an agent here only ever happens on
/// enable/disable, never on a per-frame destination update.
/// <para/>
/// Like <see cref="RecastNavMeshBuilder"/> and <see cref="NavMeshQuery"/>, this is one of the few files
/// that touches a Detour type directly; everything crossing its public boundary is an engine-owned type.
/// </summary>
public sealed class NavMeshCrowd
{
    /// <summary>How many distinct query filters Detour's own crowd config allows - see
    /// <c>DtCrowdConst.DT_CROWD_MAX_QUERY_FILTER_TYPE</c>. A filter is built for every slot 0..15 up
    /// front, eagerly, the moment <see cref="_crowd"/> is constructed (confirmed empirically - Detour
    /// does not build these lazily), which is why <see cref="ResolveFilterSlot"/> mutates an
    /// already-built filter's flags in place instead of trying to have the factory build the right one
    /// on demand: no mask is known yet at crowd-construction time, since agents join afterward.</summary>
    private const int MaxFilterSlots = 16;

    /// <summary>The Detour crowd this class wraps.</summary>
    private readonly DtCrowd _crowd;

    /// <summary>Which crowd filter slot (0..<see cref="MaxFilterSlots"/>-1) an <see cref="NavMeshAgent.AreaMask"/>
    /// value has been assigned to. Slot 0 is reserved for the all-areas default, the common case, so it
    /// is always ready before any agent asks for it.</summary>
    private readonly Dictionary<uint, int> _filterSlotsByMask = new() { [uint.MaxValue] = 0 };

    /// <summary>The next unclaimed filter slot. Once every slot is claimed, a new distinct mask falls
    /// back to slot 0 (all areas) rather than silently reusing another mask's filter - see
    /// <see cref="ResolveFilterSlot"/>.</summary>
    private int _nextFreeFilterSlot = 1;

    /// <summary>The inverse of <see cref="_filterSlotsByMask"/> - an agent's own <c>queryFilterType</c>
    /// is all <see cref="SetDestination"/> has to identify which mask it should snap a destination with.</summary>
    private readonly Dictionary<int, uint> _maskByFilterSlot = new() { [0] = uint.MaxValue };

    // Detour's own DtCrowdAgent.targetPos tracks internal path-queue/steering state, not simply "the
    // last destination requested" - reading it back for visualization would mean depending on exactly
    // when in that internal lifecycle a caller happens to look. Tracking what was actually requested
    // ourselves, keyed by agent identity, sidesteps that entirely.
    /// <summary>Each active agent's own last-requested destination, exactly as given (not snapped to
    /// the mesh). See <see cref="GetDestination"/>.</summary>
    private readonly Dictionary<DtCrowdAgent, Float3> _requestedDestinations = [];

    /// <summary>Converts an engine-space vector to Detour's own vector type.</summary>
    private static RcVec3f ToRc(Float3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Converts a Detour vector back to engine space.</summary>
    private static Float3 ToFloat3(RcVec3f v) => new(v.X, v.Y, v.Z);

    /// <summary>The query this crowd's Detour navmesh was built from. A <see cref="NavMeshSystem"/> uses
    /// this to notice when a surface's query has been rebuilt (rebake, link change) out from under an
    /// existing crowd, since that leaves this crowd's navmesh stale. Also where <see cref="SetDestination"/>
    /// resolves a destination's polygon - not <c>DtCrowd</c>'s own internal query, which isn't reliably
    /// usable for a lookup before the crowd's first <see cref="Update"/> has run.</summary>
    internal NavMeshQuery Query { get; }

    /// <summary>Builds a crowd simulation for <paramref name="query"/>'s navmesh.</summary>
    /// <param name="query">The query (and its underlying Detour navmesh) this crowd simulates agents on.</param>
    /// <param name="maxAgentRadius">The largest radius any agent joining this crowd will use - Detour
    /// sizes its internal spatial structures from this up front.</param>
    /// <param name="agentHeight">Unused by Detour's crowd config itself; kept as a parameter so callers
    /// building a crowd from an agent type's info don't need to special-case leaving it out.</param>
    internal NavMeshCrowd(NavMeshQuery query, float maxAgentRadius, float agentHeight)
    {
        Query = query;
        // Every slot starts as "all areas allowed" - ResolveFilterSlot narrows one in place once an
        // agent actually asks for a restricted mask, since no mask is known yet at this point.
        _crowd = new DtCrowd(new DtCrowdConfig(maxAgentRadius), query.DetourNavMesh, _ => query.CreateFilter(uint.MaxValue));
    }

    /// <summary>The filter slot backing <paramref name="areaMask"/>, assigning and configuring a fresh
    /// one the first time this exact mask is seen. Every slot is built "all areas allowed" at crowd
    /// construction (see the constructor's own comment); narrowing one is a one-time in-place mutation
    /// of the already-built <see cref="DtQueryDefaultFilter"/>; instance <see cref="DtCrowd.GetFilter"/>
    /// returns for that slot from then on, not a replacement, since Detour never rebuilds a slot after
    /// construction.</summary>
    private int ResolveFilterSlot(uint areaMask)
    {
        if (_filterSlotsByMask.TryGetValue(areaMask, out int slot)) return slot;

        if (_nextFreeFilterSlot >= MaxFilterSlots)
        {
            // More than 16 distinct AreaMask values joined this crowd - Detour has no slot left for
            // this one. Falling back to "all areas" is wrong in the permissive direction (an agent
            // might cross ground it shouldn't) but not in the catastrophic one (it never becomes
            // stuck with no usable filter at all).
            Debug.LogWarning("[NavMeshCrowd] More than 16 distinct AreaMask values joined this crowd; falling back to unrestricted for the extra ones.");
            return 0;
        }

        slot = _nextFreeFilterSlot++;
        var wrapped = (NavMeshCostAwareFilter)_crowd.GetFilter(slot);
        ((DtQueryDefaultFilter)wrapped.Inner).SetIncludeFlags(unchecked((int)areaMask));
        _filterSlotsByMask[areaMask] = slot;
        _maskByFilterSlot[slot] = areaMask;
        return slot;
    }

    /// <summary>How many agents currently occupy a slot in this crowd. Exists so a test can assert that
    /// repeated <see cref="SetDestination"/> calls never grow this - see this class's own doc comment.</summary>
    public int ActiveAgentCount => _crowd.GetActiveAgents().Count;

    /// <summary>Joins the crowd at <paramref name="position"/>, occupying a new slot. Call once per
    /// agent (on enable), not on every destination change.</summary>
    /// <param name="position">World-space position to join at.</param>
    /// <param name="radius">The agent's collision radius.</param>
    /// <param name="height">The agent's height.</param>
    /// <param name="speed">Top walking speed, in world units per second.</param>
    /// <param name="acceleration">How quickly <paramref name="speed"/> is reached.</param>
    /// <param name="areaMask">Which <see cref="NavMeshAreas"/> this agent may path across, one bit per
    /// area index - see <see cref="NavMeshAgent.AreaMask"/>. Fixed for the agent's whole time in this
    /// crowd; changing it means leaving (<see cref="RemoveAgent"/>) and rejoining.</param>
    public NavMeshCrowdHandle AddAgent(Float3 position, float radius, float height, float speed, float acceleration, uint areaMask = uint.MaxValue)
    {
        var option = new DtCrowdAgentParams
        {
            radius = radius,
            height = height,
            maxAcceleration = acceleration,
            maxSpeed = speed,
            collisionQueryRange = radius * 12f,
            pathOptimizationRange = radius * 30f,
            separationWeight = 2f,
            queryFilterType = ResolveFilterSlot(areaMask),
            updateFlags = DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS
                | DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE
                | DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION
                | DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_VIS
                | DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_TOPO,
        };
        return new NavMeshCrowdHandle(_crowd.AddAgent(ToRc(position), option));
    }

    /// <summary>Joins this crowd as an immovable neighbour rather than a walking agent - a
    /// <see cref="NavMeshObstacle"/> with <see cref="NavMeshObstacle.Carve"/> off rides this instead of
    /// carving: the mesh stays intact and paths still lead through <paramref name="position"/>, but real
    /// agents' own separation/avoidance treats this slot as a stationary neighbour to steer around, at
    /// zero cost when it moves (see <see cref="MoveStaticObstacle"/>) since nothing here ever touches the
    /// tile cache. <paramref name="radius"/>/<paramref name="height"/> size how far other agents keep
    /// clear of it, not a navmesh hole.</summary>
    public NavMeshCrowdHandle AddStaticObstacle(Float3 position, float radius, float height)
    {
        var option = new DtCrowdAgentParams
        {
            radius = radius,
            height = height,
            maxAcceleration = 0f,
            maxSpeed = 0f,
            collisionQueryRange = radius * 12f,
            pathOptimizationRange = radius * 30f,
            separationWeight = 2f,
            queryFilterType = 0,
            updateFlags = DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION,
        };
        return new NavMeshCrowdHandle(_crowd.AddAgent(ToRc(position), option));
    }

    /// <summary>Repositions a slot added via <see cref="AddStaticObstacle"/> in place - no path request,
    /// no corridor replan, just the raw simulated position other agents perceive as a neighbour.</summary>
    public void MoveStaticObstacle(NavMeshCrowdHandle handle, Float3 position)
    {
        DtCrowdAgent agent = handle.Agent;
        agent.npos = ToRc(position);
    }

    /// <summary>Frees an agent's slot. Call once per agent (on disable), not on every destination change.</summary>
    public void RemoveAgent(NavMeshCrowdHandle handle)
    {
        _requestedDestinations.Remove(handle.Agent);
        _crowd.RemoveAgent(handle.Agent);
    }

    /// <summary>Retargets an already-joined agent toward <paramref name="destination"/>, snapped to the
    /// nearest polygon. Does not touch the agent's crowd slot or corridor allocation - safe to call every
    /// frame if a caller wants to, unlike re-adding the agent would be.</summary>
    /// <returns>False if <paramref name="destination"/> has no navmesh polygon within snapping range, or
    /// Detour otherwise rejected the request.</returns>
    public bool SetDestination(NavMeshCrowdHandle handle, Float3 destination)
    {
        uint areaMask = _maskByFilterSlot.TryGetValue(handle.Agent.option.queryFilterType, out uint mask) ? mask : uint.MaxValue;

        if (!Query.TryFindNearestPolyRef(destination, areaMask, out long polyRef, out Float3 nearest) || polyRef == 0)
            return false;

        if (!_crowd.RequestMoveTarget(handle.Agent, polyRef, ToRc(nearest))) return false;

        // The caller's own requested point, not the snapped "nearest" one above: near a small mesh's
        // edge, a generous search extent can legitimately reach past the mesh boundary, and Detour's own
        // FindNearestPoly can degrade there (still succeeding, but with a degenerate result) - harmless
        // for steering, since RequestMoveTarget resolves the real position from polyRef, but wrong for
        // display if used here instead of what was actually asked for.
        _requestedDestinations[handle.Agent] = destination;
        return true;
    }

    /// <summary>Sets the agent's desired velocity directly - <see cref="NavMeshAgent.FollowFlowField"/>'s
    /// own primitive, Detour's peer to <see cref="SetDestination"/> for a caller steering an agent
    /// externally (from a <see cref="NavMeshFlowField"/> here) rather than handing it one fixed target to
    /// path to itself. Crowd separation/avoidance still blends with whatever velocity is requested here,
    /// exactly as it would for a plain path-following agent - this never bypasses the crowd, only supplies
    /// its steering input. Meant to be called every tick a caller wants it to keep applying, not once.</summary>
    public bool RequestVelocity(NavMeshCrowdHandle handle, Float3 velocity) => _crowd.RequestMoveVelocity(handle.Agent, ToRc(velocity));

    /// <summary>Clears the agent's current destination in place - it stops where it is rather than
    /// continuing toward wherever it was last headed. Does not touch the agent's crowd slot or corridor
    /// allocation, unlike <see cref="RemoveAgent"/>.</summary>
    public void ResetMoveTarget(NavMeshCrowdHandle handle)
    {
        _crowd.ResetMoveTarget(handle.Agent);
        _requestedDestinations.Remove(handle.Agent);
    }

    /// <summary>The agent's current simulated position.</summary>
    public Float3 GetPosition(NavMeshCrowdHandle handle) => ToFloat3(handle.Agent.npos);

    /// <summary>The agent's current simulated velocity.</summary>
    public Float3 GetVelocity(NavMeshCrowdHandle handle) => ToFloat3(handle.Agent.vel);

    /// <summary>The destination a prior successful <see cref="SetDestination"/> requested, exactly as
    /// given (not snapped to the mesh), or null if none is active - never requested one, or its slot was
    /// freed by <see cref="RemoveAgent"/>. Exists for visualization - <see cref="NavMeshAgent"/>'s
    /// selected-gizmo draws a path to it. Does not track whether the destination has since been reached;
    /// pair with <see cref="HasArrived"/> for that.</summary>
    public Float3? GetDestination(NavMeshCrowdHandle handle) =>
        _requestedDestinations.TryGetValue(handle.Agent, out Float3 destination) ? destination : null;

    /// <summary>Whether the agent has a valid destination and has closed to within
    /// <paramref name="stoppingDistance"/> of it, measured on the horizontal plane (height is ignored,
    /// the same way Detour's own corridor logic treats "reached" for a walking agent).</summary>
    public bool HasArrived(NavMeshCrowdHandle handle, float stoppingDistance)
    {
        DtCrowdAgent agent = handle.Agent;
        if (agent.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID) return false;

        float dx = agent.npos.X - agent.targetPos.X;
        float dz = agent.npos.Z - agent.targetPos.Z;
        return dx * dx + dz * dz <= stoppingDistance * stoppingDistance;
    }

    /// <summary>True while the agent is currently crossing an off-mesh connection (a <see cref="NavMeshLink"/>)
    /// rather than walking ordinary ground.</summary>
    public bool IsOnOffMeshConnection(NavMeshCrowdHandle handle) =>
        handle.Agent.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;

    /// <summary>How far through crossing its current off-mesh connection the agent is, from 0 (just
    /// started) to 1 (about to finish walking it), or null while it isn't on one. Detour drives this as
    /// part of its own off-mesh traversal animation; a caller can layer a visual effect (a jump arc,
    /// say - see <see cref="NavMeshAgent.JumpHeight"/>) on top of <see cref="GetPosition"/> using it.</summary>
    public float? GetOffMeshConnectionProgress(NavMeshCrowdHandle handle)
    {
        DtCrowdAgentAnimation animation = handle.Agent.animation;
        if (!animation.active || animation.tmax <= 0f) return null;

        float t = animation.t / animation.tmax;
        return t < 0f ? 0f : t > 1f ? 1f : t;
    }

    /// <summary>Resolves the off-mesh connection the agent is currently crossing back to the
    /// <see cref="NavMeshLink"/> it came from, via the user id <see cref="NavMeshQuery"/> stamped onto
    /// each connection when it built this crowd's navmesh. Null if the agent isn't on one right now, or
    /// this query wasn't built with link source tracking.</summary>
    public NavMeshLink? ResolveCurrentLink(NavMeshCrowdHandle handle)
    {
        if (!IsOnOffMeshConnection(handle)) return null;

        long polyRef = handle.Agent.animation.polyRef;
        return polyRef == 0 ? null : ResolveLinkByPolyRef(polyRef);
    }

    /// <summary>The <see cref="NavMeshLink"/> whose off-mesh connection lives at polygon reference
    /// <paramref name="polyRef"/>, or null if that polygon isn't an off-mesh connection endpoint at all
    /// (or this query wasn't built with link source tracking). Shared by <see cref="ResolveCurrentLink"/>
    /// (the connection an agent is presently crossing) and <see cref="TryGetApproachingLink"/> (one still
    /// ahead of it in the corridor).</summary>
    private NavMeshLink? ResolveLinkByPolyRef(long polyRef)
    {
        DtNavMesh navMesh = Query.DetourNavMesh;
        DtStatus status = navMesh.GetTileAndPolyByRef(polyRef, out DtMeshTile tile, out DtPoly _);
        if (!status.Succeeded()) return null;

        int localPolyIndex = DtDetour.DecodePolyIdPoly(polyRef);
        foreach (DtOffMeshConnection connection in tile.data.offMeshCons)
        {
            if (connection.poly == localPolyIndex)
                return Query.GetLinkByOffMeshUserId(connection.userId);
        }
        return null;
    }

    /// <summary>Copies up to <paramref name="destination"/>'s length of the agent's own live corridor
    /// corners - the same ones Detour is already steering it toward, not a fresh <see cref="NavMeshQuery.FindPath"/>
    /// call - into it, nearest first, each paired with the corridor distance to reach it. Returns how many
    /// were actually copied (0 if the agent has no active path). No allocation beyond what
    /// <paramref name="destination"/> itself is.</summary>
    public int GetUpcomingCorners(NavMeshCrowdHandle handle, Span<NavMeshCorridorCorner> destination)
    {
        DtCrowdAgent agent = handle.Agent;
        int count = Maths.Min(agent.ncorners, destination.Length);

        Float3 previous = ToFloat3(agent.npos);
        float distance = 0f;
        for (int i = 0; i < count; i++)
        {
            Float3 corner = ToFloat3(agent.corners[i].pos);
            distance += Float3.Distance(previous, corner);
            destination[i] = new NavMeshCorridorCorner(corner, distance);
            previous = corner;
        }
        return count;
    }

    /// <summary>Straight-line distance from the agent's current position to the very next corridor
    /// corner, or 0 if it has none (no active path, or it has already reached the last one this tick).</summary>
    public float GetNextCornerDistance(NavMeshCrowdHandle handle)
    {
        DtCrowdAgent agent = handle.Agent;
        if (agent.ncorners == 0) return 0f;
        return Float3.Distance(ToFloat3(agent.npos), ToFloat3(agent.corners[0].pos));
    }

    /// <summary>The angle, in degrees, the path bends at the next corner - between the segment arriving
    /// there and the segment leaving it toward the corner after that. 0 if fewer than two corners are
    /// currently known (nothing to measure a bend between yet).</summary>
    public float GetNextTurnAngleDegrees(NavMeshCrowdHandle handle)
    {
        DtCrowdAgent agent = handle.Agent;
        if (agent.ncorners < 2) return 0f;

        Float3 position = ToFloat3(agent.npos);
        Float3 corner0 = ToFloat3(agent.corners[0].pos);
        Float3 corner1 = ToFloat3(agent.corners[1].pos);

        Float3 incoming = corner0 - position;
        Float3 outgoing = corner1 - corner0;
        if (Float3.Length(incoming) < 0.0001f || Float3.Length(outgoing) < 0.0001f) return 0f;

        float cos = Maths.Clamp(Float3.Dot(Float3.Normalize(incoming), Float3.Normalize(outgoing)), -1f, 1f);
        return MathF.Acos(cos) * (180f / MathF.PI);
    }

    /// <summary>Whether an off-mesh connection (a <see cref="NavMeshLink"/>) appears anywhere in the
    /// agent's currently-known corridor corners, ahead of it but not yet reached - distinct from
    /// <see cref="ResolveCurrentLink"/>, which only reports one already being crossed. <paramref name="distance"/>
    /// is the corridor distance to reach it (summed corner-to-corner, not a straight line), for a caller
    /// deciding whether it's close enough to start telegraphing a jump animation, say.</summary>
    public bool TryGetApproachingLink(NavMeshCrowdHandle handle, out NavMeshLink? link, out float distance)
    {
        link = null;
        distance = 0f;

        DtCrowdAgent agent = handle.Agent;
        if (agent.ncorners == 0) return false;

        Float3 previous = ToFloat3(agent.npos);
        for (int i = 0; i < agent.ncorners; i++)
        {
            DtStraightPath corner = agent.corners[i];
            Float3 cornerPos = ToFloat3(corner.pos);
            distance += Float3.Distance(previous, cornerPos);
            previous = cornerPos;

            if ((corner.flags & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) == 0) continue;

            link = ResolveLinkByPolyRef(corner.refs);
            return link != null;
        }
        return false;
    }

    /// <summary>Advances every agent in this crowd by one simulation step. Call exactly once per crowd
    /// per fixed tick (see <see cref="NavMeshSystem"/>'s scene hook) - never per agent, since Detour's
    /// crowd update simulates neighbor avoidance across the whole crowd together.</summary>
    /// <param name="dt">Elapsed simulation time, in seconds.</param>
    public void Update(float dt) => _crowd.Update(dt, null);
}
