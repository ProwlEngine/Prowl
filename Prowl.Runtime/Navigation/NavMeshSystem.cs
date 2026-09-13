// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Per-scene owner of the scene's registered <see cref="NavMeshSurface"/>s and <see cref="NavMeshAgent"/>s,
/// keyed by agent type id. Mirrors <c>Rendering.SceneLightSystem</c>'s ownership pattern: one instance
/// per <see cref="Scene"/>, kept in a <see cref="ConditionalWeakTable{Scene, NavMeshSystem}"/> so it
/// goes away with the scene without an explicit unload hook.
/// <para/>
/// An agent asks this for its own type's navmesh rather than holding a direct reference to a
/// <see cref="NavMeshSurface"/>: that sidesteps needing a serialized cross-object reference for the
/// common case, and it means "which navmesh serves this agent type" is a decision this class makes
/// once, in one place, rather than every agent independently guessing. Two surfaces of different agent
/// types in the same scene are tracked, baked and queried entirely independently of each other; an
/// agent of one type can never resolve another type's query.
/// </summary>
public sealed class NavMeshSystem
{
    /// <summary>One system per scene, discarded automatically once the scene is.</summary>
    private static readonly ConditionalWeakTable<Scene, NavMeshSystem> s_systems = new();

    /// <summary>Shared empty result for <see cref="GetSurfaces"/> when nothing is registered for a type.</summary>
    private static readonly List<NavMeshSurface> s_emptySurfaces = [];

    /// <summary>Shared empty result for <see cref="GetAgents"/> when nothing is registered for a type.</summary>
    private static readonly List<NavMeshAgent> s_emptyAgents = [];

    /// <summary>Shared empty result for <see cref="GetLinks"/> when no link applies to a type.</summary>
    private static readonly List<NavMeshLink> s_emptyLinks = [];

    /// <summary>Registered surfaces, keyed by <see cref="NavMeshSurface.AgentTypeId"/>.</summary>
    private readonly Dictionary<int, List<NavMeshSurface>> _surfacesByType = [];

    /// <summary>Registered agents, keyed by <see cref="NavMeshAgent.AgentTypeId"/>.</summary>
    private readonly Dictionary<int, List<NavMeshAgent>> _agentsByType = [];

    /// <summary>Every registered link in this scene, regardless of which agent types it applies to.</summary>
    private readonly List<NavMeshLink> _links = [];

    /// <summary>Lazily created crowd simulations, one per agent type. See <see cref="GetOrCreateCrowd"/>.</summary>
    private readonly Dictionary<int, NavMeshCrowd> _crowdsByType = [];

    /// <summary>Lazily created transient cost overlays, one per agent type. See <see cref="GetOrCreateCostLayer"/>.
    /// Deliberately independent of <see cref="_surfacesByType"/>/<see cref="_crowdsByType"/> - a cost
    /// entry has nothing to do with which surface or crowd happens to exist for a type at any given
    /// moment, and must survive either being rebuilt out from under it.</summary>
    private readonly Dictionary<int, NavMeshCostLayer> _costLayersByType = [];

    /// <summary>Gets (creating if needed) the list for an agent type in <paramref name="map"/>.</summary>
    private static List<T> GetOrCreateList<T>(Dictionary<int, List<T>> map, int agentTypeId)
    {
        if (!map.TryGetValue(agentTypeId, out List<T>? list))
        {
            list = [];
            map[agentTypeId] = list;
        }
        return list;
    }

    // A link's own registration doesn't matter to a query that already built without it - only a
    // rebuild picks it up. Rebuilding just the surfaces the link could plausibly affect (rather than
    // every surface in the scene) keeps adding/removing/toggling a link proportional to how many
    // agent types it actually reaches, not to how many surfaces the scene happens to have.
    /// <summary>Rebuilds the query of every surface <paramref name="link"/> could affect. Internal so
    /// <see cref="NavMeshLink.RequestRebuild"/> can call it directly - the manual counterpart to
    /// <see cref="NavMeshLink.AutoRebuild"/>, for a link whose automatic rebuild-on-change is turned off.</summary>
    internal void RebuildQueriesAffectedBy(NavMeshLink link)
    {
        if (link.AffectAllAgentTypes)
        {
            foreach (List<NavMeshSurface> surfaces in _surfacesByType.Values)
                foreach (NavMeshSurface surface in surfaces)
                    if (surface.IsValid()) surface.RebuildQuery();
        }
        else
        {
            foreach (int agentTypeId in link.AgentTypeIds)
                foreach (NavMeshSurface surface in GetSurfaces(agentTypeId))
                    if (surface.IsValid()) surface.RebuildQuery();
        }
    }

    /// <summary>Adds <paramref name="surface"/> to its agent type's registered list, if not already present.</summary>
    internal void RegisterSurface(NavMeshSurface surface)
    {
        List<NavMeshSurface> list = GetOrCreateList(_surfacesByType, surface.AgentTypeId);
        if (!list.Contains(surface))
            list.Add(surface);
    }

    /// <summary>Removes <paramref name="surface"/> from its agent type's registered list.</summary>
    internal void UnregisterSurface(NavMeshSurface surface)
    {
        if (_surfacesByType.TryGetValue(surface.AgentTypeId, out List<NavMeshSurface>? list))
            list.Remove(surface);
    }

    /// <summary>Adds <paramref name="agent"/> to its agent type's registered list, if not already present.</summary>
    internal void RegisterAgent(NavMeshAgent agent)
    {
        List<NavMeshAgent> list = GetOrCreateList(_agentsByType, agent.AgentTypeId);
        if (!list.Contains(agent))
            list.Add(agent);
    }

    /// <summary>Removes <paramref name="agent"/> from its agent type's registered list.</summary>
    internal void UnregisterAgent(NavMeshAgent agent)
    {
        if (_agentsByType.TryGetValue(agent.AgentTypeId, out List<NavMeshAgent>? list))
            list.Remove(agent);
    }

    /// <summary>Adds <paramref name="link"/> to this scene's link list and, unless
    /// <see cref="NavMeshLink.AutoRebuild"/> is off, rebuilds every query it affects.</summary>
    internal void RegisterLink(NavMeshLink link)
    {
        if (!_links.Contains(link))
            _links.Add(link);
        if (link.AutoRebuild) RebuildQueriesAffectedBy(link);
    }

    /// <summary>Removes <paramref name="link"/> from this scene's link list and, unless
    /// <see cref="NavMeshLink.AutoRebuild"/> is off, rebuilds every query it used to affect.</summary>
    internal void UnregisterLink(NavMeshLink link)
    {
        _links.Remove(link);
        if (link.AutoRebuild) RebuildQueriesAffectedBy(link);
    }

    /// <summary>The crowd simulation for an agent type, creating it on first use. Null if the type has
    /// no usable query yet (nothing baked). If the type's query has since been rebuilt (a rebake, or a
    /// link changing) out from under an existing crowd, that crowd is dropped and a fresh one is built
    /// against the new query - agents that were mid-move through the old one simply stop being ticked
    /// (see <see cref="NavMeshAgent"/>'s own re-join handling) rather than this silently querying a
    /// navmesh that no longer matches what surfaces of this type actually serve.</summary>
    internal NavMeshCrowd? GetOrCreateCrowd(int agentTypeId)
    {
        NavMeshQuery? query = GetQuery(agentTypeId);
        if (query == null) return null;

        if (_crowdsByType.TryGetValue(agentTypeId, out NavMeshCrowd? crowd))
        {
            if (crowd.Query == query) return crowd;
            _crowdsByType.Remove(agentTypeId);
        }

        NavMeshAgentTypeInfo type = NavMeshAgentTypes.GetById(agentTypeId) ?? NavMeshAgentTypeInfo.CreateHumanoid();
        var created = new NavMeshCrowd(query, type.AgentRadius, type.AgentHeight);
        _crowdsByType[agentTypeId] = created;
        return created;
    }

    /// <summary>Advances every agent-type crowd this scene has created by one simulation step, and
    /// pumps every agent-type query's own <c>DtTileCache</c> obstacle queue (see
    /// <see cref="NavMeshQuery.TickTileCache"/>) by a small, bounded budget - the mechanism
    /// <see cref="NavMeshObstacle"/> rides to carve without ever blocking a frame on it. Called once per
    /// fixed tick from <see cref="Resources.Scene.FixedUpdate"/>, mirroring how that same method drives
    /// <see cref="PhysicsWorld"/> - a crowd is a per-scene simulation like physics is, not something
    /// individual components should each be stepping themselves.</summary>
    internal void TickCrowds(float deltaTime)
    {
        foreach (NavMeshCrowd crowd in _crowdsByType.Values)
            crowd.Update(deltaTime);

        foreach (NavMeshCostLayer costLayer in _costLayersByType.Values)
            costLayer.Decay(deltaTime);

        foreach (List<NavMeshSurface> surfaces in _surfacesByType.Values)
            foreach (NavMeshSurface surface in surfaces)
                if (surface.IsValid() && surface.Query != null)
                    surface.Query.TickTileCache();
    }

    /// <summary>The transient cost overlay for an agent type, creating an empty one on first use - see
    /// <see cref="NavMeshCostLayer"/>'s own doc comment for why this, not <see cref="NavMeshQuery"/>,
    /// owns it.</summary>
    public NavMeshCostLayer GetOrCreateCostLayer(int agentTypeId)
    {
        if (!_costLayersByType.TryGetValue(agentTypeId, out NavMeshCostLayer? layer))
        {
            layer = new NavMeshCostLayer();
            _costLayersByType[agentTypeId] = layer;
        }
        return layer;
    }

    /// <summary>Adds a transient cost over every polygon within <paramref name="radius"/> of
    /// <paramref name="worldPos"/> on an agent type's navmesh, fading back to nothing over
    /// <paramref name="decayPerSecond"/> cost/second (0 to never fade on its own - see
    /// <see cref="NavMeshCostLayer"/>). False if that type has no usable navmesh yet, or nothing on it is
    /// near enough to <paramref name="worldPos"/> to start from.</summary>
    public bool AddCost(int agentTypeId, Float3 worldPos, float radius, float cost, float decayPerSecond)
    {
        NavMeshQuery? query = GetQuery(agentTypeId);
        return query != null && query.AddCost(worldPos, radius, cost, decayPerSecond);
    }

    /// <summary>Removes every cost entry <see cref="AddCost"/> has added for an agent type, without
    /// waiting for each one's own decay.</summary>
    public void ClearCosts(int agentTypeId) => GetOrCreateCostLayer(agentTypeId).Clear();

    /// <summary>A shared route to <paramref name="goal"/> on an agent type's navmesh, for any number of
    /// agents to follow via <see cref="NavMeshAgent.FollowFlowField"/> instead of each pathing to the
    /// same destination independently - see <see cref="NavMeshFlowField"/>'s own doc comment. Reuses an
    /// already-built field for a nearby goal rather than rebuilding one from scratch every call. Null if
    /// that type has no usable navmesh yet.</summary>
    /// <param name="maxRadius">How far, along the polygon graph, the field extends from the goal -
    /// bounds the cost of building it; an agent outside this range gets no direction from it at all.</param>
    public NavMeshFlowField? BuildFlowField(int agentTypeId, Float3 goal, float maxRadius) =>
        GetQuery(agentTypeId)?.GetOrBuildFlowField(goal, maxRadius);

    /// <summary>Ranks candidate navmesh positions within <paramref name="radius"/> of <paramref name="origin"/>
    /// on an agent type's navmesh, scored by <paramref name="scorers"/> - a cover-point or flanking-spot
    /// query, say. A candidate any scorer disqualifies (a negative <see cref="ITacticalScorer.Score"/>)
    /// never appears in the result; every other candidate is ranked by the sum of every scorer's own
    /// score for it, highest first. Empty (not null) if that type has no usable navmesh yet, or nothing
    /// is within reach of <paramref name="origin"/>.</summary>
    public IReadOnlyList<NavMeshTacticalResult> QueryTacticalPositions(int agentTypeId, Float3 origin, float radius, IReadOnlyList<ITacticalScorer> scorers) =>
        GetQuery(agentTypeId)?.QueryTacticalPositions(origin, radius, scorers) ?? [];

    /// <summary>Removes every currently-live tile overlapping the box [<paramref name="center"/> &#177;
    /// <paramref name="size"/>/2] from an agent type's navmesh at runtime, without discarding it from the
    /// baked asset's own stored layers - <see cref="LoadTiles"/> is what brings one back. A no-op if that
    /// type has no usable navmesh yet.</summary>
    public void UnloadTiles(int agentTypeId, Float3 center, Float3 size) =>
        GetQuery(agentTypeId)?.UnloadTilesInRegion(center - size * 0.5f, center + size * 0.5f);

    /// <summary>Re-adds every tile overlapping the box [<paramref name="center"/> &#177; <paramref name="size"/>/2]
    /// that <see cref="UnloadTiles"/> previously removed, straight from the baked asset's own stored
    /// layers - no rebake. A no-op if that type has no usable navmesh yet.</summary>
    public void LoadTiles(int agentTypeId, Float3 center, Float3 size) =>
        GetQuery(agentTypeId)?.LoadTilesInRegion(center - size * 0.5f, center + size * 0.5f);

    /// <summary>Rebuilds only the tiles overlapping the box [<paramref name="center"/> &#177; <paramref name="size"/>/2]
    /// against current scene geometry, on every registered surface of this agent type - see
    /// <see cref="NavMeshSurface.RebuildTiles(Float3, Float3)"/>. True if any surface actually applied a
    /// change.</summary>
    public bool RebuildTiles(int agentTypeId, Float3 center, Float3 size)
    {
        bool any = false;
        foreach (NavMeshSurface surface in GetSurfaces(agentTypeId))
            if (surface.IsValid() && surface.RebuildTiles(center, size))
                any = true;
        return any;
    }

    /// <summary>Async counterpart to <see cref="RebuildTiles(int, Float3, Float3)"/> - see
    /// <see cref="NavMeshSurface.RebuildTilesAsync"/> for what actually runs on a background thread and
    /// what must stay on the caller's.</summary>
    public async Task<bool> RebuildTilesAsync(int agentTypeId, Float3 center, Float3 size)
    {
        bool any = false;
        foreach (NavMeshSurface surface in GetSurfaces(agentTypeId))
            if (surface.IsValid() && await surface.RebuildTilesAsync(center, size))
                any = true;
        return any;
    }

    /// <summary>Looks up a scene's navmesh system without creating one. Used by <see cref="Resources.Scene.FixedUpdate"/>
    /// to tick crowds only for scenes that actually have any - ticking is a per-frame hook, so it must not
    /// force every scene in the game to allocate a system it never otherwise needed.</summary>
    internal static bool TryGet(Scene scene, out NavMeshSystem? system) => s_systems.TryGetValue(scene, out system);

    /// <summary>Get (or create) the navmesh system bound to a scene. Creating one is free; it holds
    /// nothing until a surface or agent registers.</summary>
    public static NavMeshSystem GetOrCreate(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        return s_systems.GetValue(scene, _ => new NavMeshSystem());
    }

    /// <summary>Every surface registered for an agent type, in registration order. Read-only view;
    /// empty (not null) when nothing is registered for that type.</summary>
    public IReadOnlyList<NavMeshSurface> GetSurfaces(int agentTypeId) =>
        _surfacesByType.TryGetValue(agentTypeId, out List<NavMeshSurface>? list) ? list : s_emptySurfaces;

    /// <summary>Every agent registered for an agent type, in registration order.</summary>
    public IReadOnlyList<NavMeshAgent> GetAgents(int agentTypeId) =>
        _agentsByType.TryGetValue(agentTypeId, out List<NavMeshAgent>? list) ? list : s_emptyAgents;

    /// <summary>Every registered link that applies to an agent type, in registration order. A linear
    /// scan over this scene's own (typically small) link list, not the scene at large.</summary>
    public IReadOnlyList<NavMeshLink> GetLinks(int agentTypeId)
    {
        if (_links.Count == 0) return s_emptyLinks;

        var matches = new List<NavMeshLink>();
        foreach (NavMeshLink link in _links)
            if (link.IsValid() && link.AppliesTo(agentTypeId)) matches.Add(link);
        return matches;
    }

    /// <summary>The query for a given agent type: the first enabled, successfully-built surface
    /// registered for that type, in registration order. Null if no surface of that type is registered,
    /// or none of them has a usable bake yet.</summary>
    public NavMeshQuery? GetQuery(int agentTypeId)
    {
        if (!_surfacesByType.TryGetValue(agentTypeId, out List<NavMeshSurface>? list)) return null;

        for (int i = 0; i < list.Count; i++)
        {
            NavMeshSurface surface = list[i];
            if (surface.IsValid() && surface.Enabled && surface.Query != null) return surface.Query;
        }
        return null;
    }
}
