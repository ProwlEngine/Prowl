// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;
using Prowl.Recast.Detour.Crowd;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A registered navmesh inside a <see cref="NavMeshWorld"/>: the instantiated Detour navmesh,
/// its query pool, and the lock that lets queries run from any thread while tile mutations
/// (rebakes, partial rebuilds) exclude them. Obtained from
/// <see cref="NavMeshWorld.AddNavMeshData"/>; advanced users can reach the raw Detour objects
/// through <see cref="NativeNavMesh"/>.
/// </summary>
public sealed class NavMeshInstance
{
    internal NavMeshData Data;
    internal DtNavMesh Mesh;
    internal readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.NoRecursion);
    internal readonly ConcurrentBag<DtNavMeshQuery> QueryPool = new();

    // Set when work is queued into the cache (an obstacle request, a tile swap), cleared once
    // the pump drains it. Doubles as the "this instance changed" signal: only a flagged
    // instance is pumped, so being pumped is itself proof there was something to report.
    // Registration seeds every tile synchronously, so a freshly registered instance starts
    // clean and a surface nothing ever carves costs nothing per frame. Main-thread only, like
    // registration itself.
    internal bool CachePending;

    internal NavMeshInstance(NavMeshData data, Prowl.Recast.Detour.TileCache.DtTileCache tileCache,
        NavMeshTileBuilder.ProwlTileCacheMeshProcess tileCacheLinks)
    {
        Data = data;
        Mesh = tileCache.GetNavMesh();
        TileCache = tileCache;
        TileCacheLinks = tileCacheLinks;
    }

    /// <summary>The link set this instance's cache re-injects whenever it rebuilds a tile.
    /// Mutate under the instance write lock and rebuild the affected tiles afterwards — see
    /// <see cref="NavMeshSurface.RebuildLinkTiles"/>.</summary>
    internal NavMeshTileBuilder.ProwlTileCacheMeshProcess TileCacheLinks { get; }

    /// <summary>The TileCache backing this instance. Obstacles queue through it and
    /// <see cref="NavMeshWorld.Update"/> pumps its incremental tile rebuilds. Queue work through
    /// <see cref="NavMeshWorld.MutateTileCache"/>, which flags the instance for you — the pump
    /// only runs for instances known to have pending work, and DtTileCache cannot be asked
    /// whether it has any, so a request enqueued behind its back waits forever. Code that
    /// queues on this handle directly must call <see cref="MarkCachePending"/>.</summary>
    public Prowl.Recast.Detour.TileCache.DtTileCache TileCache { get; }

    /// <summary>Tell the pump this cache has work waiting. Only needed after queuing on
    /// <see cref="TileCache"/> directly; <see cref="NavMeshWorld.MutateTileCache"/> and the
    /// navigation components already do it. Main thread only.</summary>
    public void MarkCachePending() => CachePending = true;

    /// <summary>The agent type this navmesh was built for.</summary>
    public int AgentTypeId => Data.Settings.AgentTypeId;

    /// <summary>The asset this instance was created from.</summary>
    public NavMeshData NavMeshData => Data;

    /// <summary>The underlying Detour navmesh, owned by <see cref="TileCache"/>. Advanced use;
    /// mutating it directly bypasses the query locking and desyncs it from the cache that built
    /// it — prefer <see cref="NavMeshWorld.MutateTileCache"/> for tile changes.</summary>
    public DtNavMesh NativeNavMesh => Mesh;

    // Off-mesh connection user ids present in the mesh, built lazily and invalidated on
    // mutation — turns per-link containment checks (every NavMeshLink at scene load) into
    // O(1) after one O(tiles) pass per instance. Main-thread only, like registration.
    private HashSet<int>? _linkIds;

    internal void InvalidateLinkIds() => _linkIds = null;

    /// <summary>Whether the mesh holds an off-mesh connection stamped with the given link id
    /// (see <see cref="NavMeshLink.LinkId"/>). Main thread.</summary>
    public bool ContainsLinkId(int linkId)
    {
        if (_linkIds == null)
        {
            _linkIds = [];
            for (int t = 0; t < Mesh.GetMaxTiles(); t++)
            {
                var cons = Mesh.GetTile(t)?.data?.offMeshCons;
                if (cons == null) continue;
                foreach (DtOffMeshConnection con in cons)
                    if (con.userId != 0)
                        _linkIds.Add(con.userId);
            }
        }
        return _linkIds.Contains(linkId);
    }
}

/// <summary>
/// One agent type's crowd: the Detour crowd, the navmesh instance it steers against, and the
/// 16 query-filter slots it was constructed over. Slot 0 is the shared default (all areas, no
/// cost overrides); slots 1..15 are allocated per distinct (AreaMask, cost-overrides) agent
/// configuration and refcounted, so agents with identical steering filters share a slot.
/// Slot numbers are NOT stable across release/re-acquire (a config can land on a different
/// free slot) — nothing outside this entry may key state on them. Main-thread only, like all
/// crowd state.
/// </summary>
internal sealed class NavMeshCrowdEntry
{
    public readonly DtCrowd Crowd;
    public readonly NavMeshInstance Instance;

    // The filter objects the crowd reads live each update — mutating one changes the steering
    // of every agent on that slot immediately.
    private readonly NavMeshQueryFilter[] _filters;
    private readonly int[] _refCounts = new int[DtCrowdConst.DT_CROWD_MAX_QUERY_FILTER_TYPE];

    // Once per entry: a crowd rebind makes every agent re-acquire, and a persistent overflow
    // population would otherwise warn per agent per rebake — log spam at destructible-world
    // frequency. The entry is recreated on rebind, so each new crowd re-warns exactly once.
    private bool _exhaustionWarned;

    public NavMeshCrowdEntry(DtCrowd crowd, NavMeshInstance instance, NavMeshQueryFilter[] filters)
    {
        Crowd = crowd;
        Instance = instance;
        _filters = filters;
    }

    /// <summary>
    /// Slot whose filter matches the configuration exactly, sharing where possible: the
    /// default config maps to slot 0, a config already in use bumps that slot's refcount, and
    /// a new config takes a free slot. On exhaustion (16 distinct steering configurations for
    /// one agent type) warns and falls back to slot 0.
    /// </summary>
    public int AcquireFilterSlot(int areaMask, float[]? costOverrides, string? agentName = null)
    {
        if (areaMask == NavMeshAreas.AllAreas && OverridesEqual(costOverrides, null))
            return 0;

        // Exact-match scan beats hashing here: at most 15 candidates, and comparing the full
        // config can never merge two different configurations the way a hash collision would.
        for (int slot = 1; slot < _filters.Length; slot++)
        {
            if (_refCounts[slot] > 0 && _filters[slot].AreaMask == areaMask
                && OverridesEqual(_filters[slot].CostOverrides, costOverrides))
            {
                _refCounts[slot]++;
                return slot;
            }
        }

        for (int slot = 1; slot < _filters.Length; slot++)
        {
            if (_refCounts[slot] == 0)
            {
                _filters[slot].AreaMask = areaMask;
                _filters[slot].CopyCostOverridesFrom(costOverrides);
                _refCounts[slot] = 1;
                return slot;
            }
        }

        if (!_exhaustionWarned)
        {
            _exhaustionWarned = true;
            string who = string.IsNullOrEmpty(agentName) ? "an agent" : $"agent '{agentName}'";
            Debug.LogWarning($"[Navigation] All {_filters.Length} crowd filter slots for agent type {Instance.AgentTypeId} are in use ({_filters.Length - 1} distinct AreaMask/cost configurations); {who} steers with the default filter instead. Explicit queries (CalculatePath etc.) are unaffected. Further overflows on this crowd will not be logged.");
        }
        return 0;
    }

    /// <summary>Release a slot returned by <see cref="AcquireFilterSlot"/>. Slot 0 is shared
    /// and never released. A slot's filter resets to defaults when its last user leaves.</summary>
    public void ReleaseFilterSlot(int slot)
    {
        if (slot <= 0 || slot >= _refCounts.Length || _refCounts[slot] == 0) return;
        if (--_refCounts[slot] == 0)
        {
            _filters[slot].AreaMask = NavMeshAreas.AllAreas;
            _filters[slot].ClearAreaCosts();
        }
    }

    private static bool OverridesEqual(float[]? a, float[]? b)
    {
        if (ReferenceEquals(a, b)) return true; // both null: the common mask-only case
        // 0 means "no override", so a null array equals an all-zero one.
        for (int i = 0; i < NavMeshAreas.MaxAreas; i++)
        {
            float av = a != null && i < a.Length ? a[i] : 0f;
            float bv = b != null && i < b.Length ? b[i] : 0f;
            if (av != bv) return false;
        }
        return true;
    }
}

/// <summary>
/// A rented thread-safe navmesh query. Dispose to return it to the pool. Leases hold a read
/// lock on the navmesh, so keep them short-lived — a lease held across frames blocks rebuilds.
/// </summary>
public readonly struct NavMeshQueryLease : IDisposable
{
    private readonly NavMeshInstance _instance;

    /// <summary>The Detour query, valid until this lease is disposed.</summary>
    public DtNavMeshQuery Query { get; }

    internal NavMeshQueryLease(NavMeshInstance instance, DtNavMeshQuery query)
    {
        _instance = instance;
        Query = query;
    }

    public void Dispose()
    {
        if (_instance == null) return;
        _instance.QueryPool.Add(Query);
        _instance.Lock.ExitReadLock();
    }
}

/// <summary>
/// Per-scene navigation state: the registered navmeshes, the query API over them, and (once
/// agents register) the crowd simulation. Owned by <see cref="Resources.Scene.Navigation"/> the
/// same way physics state is owned by <see cref="Resources.Scene.Physics"/>; the static
/// <see cref="NavMesh"/> facade forwards to the current scene's world.
/// <para/>
/// Queries are thread-safe: each takes a pooled Detour query under a read lock, so gameplay
/// code may path-find from worker threads. Tile mutations take the write lock and invalidate
/// pooled queries.
/// </summary>
public sealed class NavMeshWorld
{
    // Capacity limits for a single query, in polys/corners. Detour needs explicit maximums;
    // these match the sizes the Recast demos use for long paths.
    private const int MaxPolyPath = 1024;
    private const int MaxStraightPath = 256;

    private readonly List<NavMeshInstance> _instances = [];
    private readonly Lock _instancesLock = new();

    [ThreadStatic] private static NavMeshQueryFilter? t_scratchFilter;

    /// <summary>Default half-extents used to snap query positions onto the navmesh, in world
    /// units. Larger values tolerate more vertical mismatch but can snap to the wrong floor.</summary>
    public Float3 DefaultQueryExtents = new(1f, 2f, 1f);

    /// <summary>Maximum agent radius the crowds' proximity grids are sized for. Agents with a
    /// larger <c>Radius</c> degrade neighbour queries silently, so registration warns when one
    /// exceeds this. Set BEFORE the first agent of a type registers — each crowd is configured
    /// with it at creation (a later change applies after that crowd's next rebind).</summary>
    public float CrowdMaxAgentRadius = 2f;

    // One crowd per agent type, created when the first agent of that type registers and
    // dropped when the navmesh instance it steers against is removed (its agents rejoin the
    // replacement crowd via NavMeshChanged). Main-thread only, like registration.
    private readonly Dictionary<int, NavMeshCrowdEntry> _crowds = [];

    /// <summary>The crowd simulation for the default agent type (0). Sugar for
    /// <see cref="GetNativeCrowd"/>. Null until the first such agent registers.</summary>
    public DtCrowd? NativeCrowd => GetNativeCrowd(0);

    /// <summary>How many agent types currently have a crowd. Lets components notice cheaply
    /// that a crowd appeared (the first agent of a type registering) without walking the
    /// agent-type table every frame.</summary>
    internal int CrowdCount => _crowds.Count;

    /// <summary>
    /// Bumped whenever the SET of registered navmeshes changes — a surface registering,
    /// unregistering, or being replaced by a rebake. <see cref="NavMeshChanged"/> cannot stand
    /// in for this: it also fires for tile-content changes, which means every frame a carve is
    /// converging. Components that only care about instances appearing
    /// or dying (link catch-up, obstacle re-attachment) compare this instead, so gameplay-rate
    /// carving stops waking work that has nothing to do.
    /// </summary>
    public int StructureGeneration { get; private set; }

    /// <summary>The crowd steering agents of the given type, or null while none have
    /// registered. Advanced use — Prowl agents manage their crowd membership themselves.</summary>
    public DtCrowd? GetNativeCrowd(int agentTypeId = 0)
        => _crowds.TryGetValue(agentTypeId, out NavMeshCrowdEntry? entry) ? entry.Crowd : null;

    /// <summary>
    /// Get or create the crowd for the instance's agent type. Called by agents on
    /// registration; the crowd binds to the instance's Detour navmesh and is dropped with it.
    /// </summary>
    internal NavMeshCrowdEntry EnsureCrowd(NavMeshInstance instance)
    {
        int agentTypeId = instance.AgentTypeId;
        if (_crowds.TryGetValue(agentTypeId, out NavMeshCrowdEntry? existing)) return existing;

        // The factory runs for all 16 slots inside the DtCrowd constructor; every slot gets a
        // mutable NavMeshQueryFilter we keep, so slot configs can change without touching the crowd.
        var filters = new NavMeshQueryFilter[DtCrowdConst.DT_CROWD_MAX_QUERY_FILTER_TYPE];
        var crowd = new DtCrowd(new DtCrowdConfig(CrowdMaxAgentRadius), instance.NativeNavMesh,
            i => filters[i] = new NavMeshQueryFilter { AgentTypeId = agentTypeId });

        // Presets + any user overrides live on the world (survive crowd rebinds); slots 0..3
        // map to Low/Medium/Good/High quality.
        ApplyAvoidanceParams(crowd);

        var entry = new NavMeshCrowdEntry(crowd, instance, filters);
        _crowds[agentTypeId] = entry;
        return entry;
    }

    // Per-quality obstacle-avoidance overrides (slot = ObstacleAvoidanceType - 1). Null slots
    // use the built-in presets. Survive crowd rebinds: a replacement crowd re-applies them.
    private readonly DtObstacleAvoidanceParams?[] _avoidanceOverrides = new DtObstacleAvoidanceParams?[4];

    /// <summary>
    /// The obstacle-avoidance parameters agents of the given quality steer with — the
    /// override set via <see cref="SetObstacleAvoidanceParams"/>, or the built-in preset.
    /// </summary>
    public DtObstacleAvoidanceParams GetObstacleAvoidanceParams(ObstacleAvoidanceType quality)
    {
        int slot = AvoidanceSlot(quality);
        return _avoidanceOverrides[slot] ?? CreateDefaultAvoidanceParams(slot);
    }

    /// <summary>
    /// Replace the obstacle-avoidance tuning for a quality level. The built-in presets are
    /// Recast-demo values tuned for open levels; tight-corridor maps typically want a shorter
    /// horizon and more current-velocity damping (raise <c>weightCurVel</c>) to stop
    /// oscillation. Applies to the live crowd immediately and to any crowd created later.
    /// </summary>
    public void SetObstacleAvoidanceParams(ObstacleAvoidanceType quality, DtObstacleAvoidanceParams option)
    {
        ArgumentNullException.ThrowIfNull(option);
        int slot = AvoidanceSlot(quality);
        _avoidanceOverrides[slot] = option;
        foreach (NavMeshCrowdEntry entry in _crowds.Values)
            entry.Crowd.SetObstacleAvoidanceParams(slot, option);
    }

    /// <summary>Push presets + overrides into a crowd (called on crowd creation/rebind).</summary>
    internal void ApplyAvoidanceParams(DtCrowd crowd)
    {
        for (int slot = 0; slot < _avoidanceOverrides.Length; slot++)
            crowd.SetObstacleAvoidanceParams(slot, _avoidanceOverrides[slot] ?? CreateDefaultAvoidanceParams(slot));
    }

    private static int AvoidanceSlot(ObstacleAvoidanceType quality)
    {
        if (quality == ObstacleAvoidanceType.NoObstacleAvoidance)
            throw new ArgumentOutOfRangeException(nameof(quality), "NoObstacleAvoidance has no avoidance parameters.");
        return (int)quality - 1;
    }

    /// <summary>Built-in presets: slots 0..3 map to Low/Medium/Good/High quality. Values match
    /// the Recast demo's, differing per slot in adaptive sampling density.</summary>
    private static DtObstacleAvoidanceParams CreateDefaultAvoidanceParams(int slot)
    {
        (int divs, int rings, int depth)[] presets = [(5, 2, 1), (5, 2, 2), (7, 2, 3), (7, 3, 3)];
        (int divs, int rings, int depth) preset = presets[Math.Clamp(slot, 0, presets.Length - 1)];
        return new DtObstacleAvoidanceParams
        {
            velBias = 0.4f,
            weightDesVel = 2.0f,
            weightCurVel = 0.75f,
            weightSide = 0.75f,
            weightToi = 2.5f,
            horizTime = 2.5f,
            gridSize = 33,
            adaptiveDivs = preset.divs,
            adaptiveRings = preset.rings,
            adaptiveDepth = preset.depth,
        };
    }

    /// <summary>Raised at the start of each navigation update, before the crowd steps.</summary>
    public event Action<float>? PreUpdate;

    /// <summary>Raised whenever a navmesh is added, removed, or mutated. Agents waiting for a
    /// navmesh subscribe to this instead of polling.</summary>
    public event Action? NavMeshChanged;

    #region Registration

    /// <summary>Obstacle capacity navmeshes are instantiated with. Set BEFORE the surface
    /// registers — applied at instantiation.</summary>
    public int TileCacheMaxObstacles = 256;

    /// <summary>
    /// Instantiate and register a baked navmesh. Returns the instance handle, or null when the
    /// data has no tiles or fails to instantiate.
    /// <para/>
    /// Threading: fires <see cref="NavMeshChanged"/> synchronously on the calling thread, and
    /// subscribers (agents, editor overlays) touch the crowd and Transforms — call from the
    /// main thread, or guarantee nothing is subscribed. (Queries are the thread-safe surface;
    /// registration is not.)
    /// </summary>
    public NavMeshInstance? AddNavMeshData(NavMeshData data)
    {
        if (data == null || !data.HasTiles) return null;

        NavMeshInstance instance;
        try
        {
            Prowl.Recast.Detour.TileCache.DtTileCache cache = data.CreateTileCache(TileCacheMaxObstacles,
                out NavMeshTileBuilder.ProwlTileCacheMeshProcess links);
            instance = new NavMeshInstance(data, cache, links);
        }
        catch (Exception e)
        {
            // Type and stack included deliberately: the throw comes from inside Detour, several
            // frames below anything the message alone would name, and without them an
            // instantiation failure is undiagnosable from the console.
            Debug.LogError($"[Navigation] Failed to instantiate NavMeshData '{data.Name}' ({data.CacheLayers.Count} layers, MaxTiles={data.MaxTiles}, MaxPolys={data.MaxPolys}, tile={data.Settings.EffectiveTileSize} voxels, voxel={data.Settings.EffectiveVoxelSize:0.####}): {e}");
            return null;
        }

        lock (_instancesLock)
            _instances.Add(instance);
        StructureGeneration++;
        NavMeshChanged?.Invoke();
        return instance;
    }

    /// <summary>Unregister a navmesh. Blocks until in-flight queries on it finish.</summary>
    public void RemoveNavMeshData(NavMeshInstance? instance)
    {
        if (instance == null) return;

        bool removed;
        lock (_instancesLock)
            removed = _instances.Remove(instance);
        if (!removed) return;

        // Wait out in-flight queries, then poison the pool.
        instance.Lock.EnterWriteLock();
        instance.QueryPool.Clear();
        instance.Lock.ExitWriteLock();

        // A crowd steers against its instance's DtNavMesh; it must not survive the mesh.
        // Its agents notice their crowd is gone via NavMeshChanged and rejoin the next one
        // (keeping their destinations) when a replacement instance registers. Other agent
        // types' crowds are untouched.
        if (_crowds.TryGetValue(instance.AgentTypeId, out NavMeshCrowdEntry? entry)
            && ReferenceEquals(entry.Instance, instance))
        {
            _crowds.Remove(instance.AgentTypeId);
        }

        StructureGeneration++;
        NavMeshChanged?.Invoke();
    }

    /// <summary>Remove every registered navmesh (scene teardown).</summary>
    public void Clear()
    {
        List<NavMeshInstance> toRemove;
        lock (_instancesLock)
        {
            toRemove = [.. _instances];
            _instances.Clear();
        }
        foreach (NavMeshInstance instance in toRemove)
        {
            instance.Lock.EnterWriteLock();
            instance.QueryPool.Clear();
            instance.Lock.ExitWriteLock();
        }
        _crowds.Clear();
        if (toRemove.Count > 0)
        {
            StructureGeneration++;
            NavMeshChanged?.Invoke();
        }
    }

    /// <summary>The registered navmesh for an agent type, or null. When several are registered
    /// for the same type, the first registered wins (one navmesh per agent type is the
    /// supported setup; merging surfaces arrives with modifier support).</summary>
    public NavMeshInstance? GetInstance(int agentTypeId = 0)
    {
        lock (_instancesLock)
        {
            for (int i = 0; i < _instances.Count; i++)
                if (_instances[i].AgentTypeId == agentTypeId)
                    return _instances[i];
        }
        return null;
    }

    /// <summary>True when a navmesh is registered for the agent type.</summary>
    public bool HasNavMesh(int agentTypeId = 0) => GetInstance(agentTypeId) != null;

    /// <summary>
    /// Run a mutation against an instance's TileCache under the write lock (layer
    /// regeneration, bulk obstacle edits). In-flight queries finish first. Pooled queries
    /// survive the mutation: verified against Prowl.Recast, DtNavMeshQuery holds only the
    /// mesh reference (the same object we mutate) plus node pools and an open list that every
    /// query method clears on entry — there is no cached tile state, so discarding the pool
    /// here would only churn tens-of-KB query objects on every rebuild for nothing.
    /// <para/>
    /// Threading: fires <see cref="NavMeshChanged"/> synchronously on the calling thread (see
    /// <see cref="AddNavMeshData"/> — same main-thread contract).
    /// </summary>
    public void MutateTileCache(NavMeshInstance instance, Action<Prowl.Recast.Detour.TileCache.DtTileCache> mutation)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(mutation);

        instance.Lock.EnterWriteLock();
        try
        {
            mutation(instance.TileCache);
        }
        finally
        {
            instance.Lock.ExitWriteLock();
        }
        // A mutation can leave tiles queued (added tiles rebuild lazily, obstacle edits queue
        // requests), so hand the instance to the pump regardless of what the caller did.
        instance.CachePending = true;
        instance.InvalidateLinkIds();
        NavMeshChanged?.Invoke();
    }

    #endregion

    #region Query lease

    /// <summary>
    /// Rent a thread-safe query over the agent type's navmesh. Dispose the lease promptly —
    /// it holds a read lock that blocks navmesh mutations. Returns false when no navmesh is
    /// registered for the agent type.
    /// </summary>
    public bool TryRentQuery(out NavMeshQueryLease lease, int agentTypeId = 0)
    {
        NavMeshInstance? instance = GetInstance(agentTypeId);
        if (instance == null)
        {
            lease = default;
            return false;
        }

        // Benign race with RemoveNavMeshData: the instance may be unregistered between the
        // lookup and the lock, in which case this queries a just-removed (but still fully
        // alive and internally consistent) mesh one last time. Do not "fix" with a global
        // lock — the stale answer is indistinguishable from having queried a moment earlier.
        instance.Lock.EnterReadLock();
        if (!instance.QueryPool.TryTake(out DtNavMeshQuery? query))
            query = new DtNavMeshQuery(instance.Mesh);
        lease = new NavMeshQueryLease(instance, query);
        return true;
    }

    #endregion

    #region Queries

    private static NavMeshQueryFilter GetScratchFilter(int areaMask)
    {
        NavMeshQueryFilter filter = t_scratchFilter ??= new NavMeshQueryFilter();
        filter.AreaMask = areaMask;
        filter.AgentTypeId = 0;
        return filter;
    }

    private static RcVec3f ToRc(Float3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
    private static Float3 ToFloat3(RcVec3f v) => new(v.X, v.Y, v.Z);

    /// <summary>Calculate a path between two points. Returns true when the resulting path is
    /// complete or partial; <paramref name="path"/> carries the corners and exact status.</summary>
    public bool CalculatePath(Float3 sourcePosition, Float3 targetPosition, int areaMask, NavMeshPath path)
        => CalculatePath(sourcePosition, targetPosition, GetScratchFilter(areaMask), path);

    /// <inheritdoc cref="CalculatePath(Float3, Float3, int, NavMeshPath)"/>
    public bool CalculatePath(Float3 sourcePosition, Float3 targetPosition, NavMeshQueryFilter filter, NavMeshPath path)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(path);
        path.ClearCorners();

        if (!TryRentQuery(out NavMeshQueryLease lease, filter.AgentTypeId))
            return false;

        using (lease)
        {
            DtNavMeshQuery query = lease.Query;
            RcVec3f ext = ToRc(DefaultQueryExtents);

            query.FindNearestPoly(ToRc(sourcePosition), ext, filter, out long startRef, out RcVec3f startPt, out _);
            query.FindNearestPoly(ToRc(targetPosition), ext, filter, out long endRef, out RcVec3f endPt, out _);
            if (startRef == 0 || endRef == 0)
                return false;

            long[] polys = ArrayPool<long>.Shared.Rent(MaxPolyPath);
            DtStraightPath[] straight = ArrayPool<DtStraightPath>.Shared.Rent(MaxStraightPath);
            Float3[] corners = ArrayPool<Float3>.Shared.Rent(MaxStraightPath);
            try
            {
                DtStatus status = query.FindPath(startRef, endRef, startPt, endPt, filter, polys.AsSpan(0, MaxPolyPath), out int polyCount, MaxPolyPath);
                if (status.Failed() || polyCount == 0)
                    return false;

                // A partial path's last poly isn't the target poly; steer to the closest point
                // on it instead of the unreachable target.
                bool partial = polys[polyCount - 1] != endRef;
                RcVec3f steerTarget = endPt;
                if (partial)
                    query.ClosestPointOnPoly(polys[polyCount - 1], endPt, out steerTarget, out _);

                DtStatus straightStatus = query.FindStraightPath(startPt, steerTarget, polys.AsSpan(0, polyCount), polyCount,
                    straight.AsSpan(0, MaxStraightPath), out int cornerCount, MaxStraightPath, 0);
                if (straightStatus.Failed() || cornerCount == 0)
                    return false;

                // A corner buffer filled to capacity means FindStraightPath truncated the
                // path; reporting that as complete would lie to the caller.
                if (cornerCount >= MaxStraightPath)
                    partial = true;

                for (int i = 0; i < cornerCount; i++)
                    corners[i] = ToFloat3(straight[i].pos);

                path.SetCorners(corners.AsSpan(0, cornerCount), partial ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete);
                return true;
            }
            finally
            {
                ArrayPool<long>.Shared.Return(polys);
                ArrayPool<DtStraightPath>.Shared.Return(straight);
                ArrayPool<Float3>.Shared.Return(corners);
            }
        }
    }

    /// <summary>Find the closest point on the navmesh within <paramref name="maxDistance"/> of
    /// <paramref name="sourcePosition"/>.</summary>
    public bool SamplePosition(Float3 sourcePosition, out NavMeshHit hit, float maxDistance, int areaMask)
        => SamplePosition(sourcePosition, out hit, maxDistance, GetScratchFilter(areaMask));

    /// <inheritdoc cref="SamplePosition(Float3, out NavMeshHit, float, int)"/>
    public bool SamplePosition(Float3 sourcePosition, out NavMeshHit hit, float maxDistance, NavMeshQueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        hit = default;

        if (!TryRentQuery(out NavMeshQueryLease lease, filter.AgentTypeId))
            return false;

        using (lease)
        {
            var ext = new RcVec3f(maxDistance, maxDistance, maxDistance);
            lease.Query.FindNearestPoly(ToRc(sourcePosition), ext, filter, out long nearestRef, out RcVec3f nearestPt, out _);
            if (nearestRef == 0)
                return false;

            Float3 position = ToFloat3(nearestPt);
            float distance = (float)Float3.Distance(sourcePosition, position);
            if (distance > maxDistance)
                return false;

            hit.Position = position;
            hit.Normal = Float3.UnitY;
            hit.Distance = distance;
            hit.Mask = GetPolyAreaMaskBit(lease.Query.GetAttachedNavMesh(), nearestRef);
            hit.Hit = true;
            return true;
        }
    }

    /// <summary>Trace a walkability ray along the navmesh surface. Returns true when the ray is
    /// blocked before the target; <paramref name="hit"/> holds the blocking edge either way.</summary>
    public bool Raycast(Float3 sourcePosition, Float3 targetPosition, out NavMeshHit hit, int areaMask)
        => Raycast(sourcePosition, targetPosition, out hit, GetScratchFilter(areaMask));

    /// <inheritdoc cref="Raycast(Float3, Float3, out NavMeshHit, int)"/>
    public bool Raycast(Float3 sourcePosition, Float3 targetPosition, out NavMeshHit hit, NavMeshQueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        hit = default;

        if (!TryRentQuery(out NavMeshQueryLease lease, filter.AgentTypeId))
            return false;

        using (lease)
        {
            DtNavMeshQuery query = lease.Query;
            RcVec3f start = ToRc(sourcePosition);
            RcVec3f end = ToRc(targetPosition);

            query.FindNearestPoly(start, ToRc(DefaultQueryExtents), filter, out long startRef, out RcVec3f startPt, out _);
            if (startRef == 0)
                return false;

            long[] polys = ArrayPool<long>.Shared.Rent(MaxPolyPath);
            try
            {
                DtStatus status = query.Raycast(startRef, startPt, end, filter, out float t, out RcVec3f normal,
                    polys.AsSpan(0, MaxPolyPath), out int _, MaxPolyPath);
                if (status.Failed())
                    return false;

                bool blocked = t < float.MaxValue;
                Float3 position = blocked
                    ? ToFloat3(RcVec3f.Lerp(startPt, end, Math.Clamp(t, 0f, 1f)))
                    : ToFloat3(end);

                hit.Position = position;
                hit.Normal = blocked ? ToFloat3(normal) : Float3.UnitY;
                hit.Distance = (float)Float3.Distance(sourcePosition, position);
                hit.Hit = blocked;
                return blocked;
            }
            finally
            {
                ArrayPool<long>.Shared.Return(polys);
            }
        }
    }

    /// <summary>Locate the closest navmesh border edge from a point.</summary>
    public bool FindClosestEdge(Float3 sourcePosition, out NavMeshHit hit, int areaMask)
        => FindClosestEdge(sourcePosition, out hit, GetScratchFilter(areaMask));

    /// <inheritdoc cref="FindClosestEdge(Float3, out NavMeshHit, int)"/>
    public bool FindClosestEdge(Float3 sourcePosition, out NavMeshHit hit, NavMeshQueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        hit = default;

        if (!TryRentQuery(out NavMeshQueryLease lease, filter.AgentTypeId))
            return false;

        using (lease)
        {
            DtNavMeshQuery query = lease.Query;
            query.FindNearestPoly(ToRc(sourcePosition), ToRc(DefaultQueryExtents), filter, out long startRef, out RcVec3f startPt, out _);
            if (startRef == 0)
                return false;

            // Search radius: generous enough to always find the border of the current region.
            DtStatus status = query.FindDistanceToWall(startRef, startPt, 100f, filter,
                out float distance, out RcVec3f hitPos, out RcVec3f hitNormal);
            if (status.Failed())
                return false;

            hit.Position = ToFloat3(hitPos);
            hit.Normal = ToFloat3(hitNormal);
            hit.Distance = distance;
            hit.Mask = GetPolyAreaMaskBit(query.GetAttachedNavMesh(), startRef);
            hit.Hit = true;
            return true;
        }
    }

    /// <summary>Triangulate the current navmesh for debug drawing or user tooling. Returns an
    /// empty triangulation when no navmesh is registered for the agent type — to visualize a
    /// baked asset that isn't registered, use <see cref="NavMeshData.CalculateTriangulation"/>.</summary>
    public NavMeshTriangulation CalculateTriangulation(int agentTypeId = 0)
    {
        NavMeshInstance? instance = GetInstance(agentTypeId);
        if (instance == null)
            return NavMeshTriangulation.Empty;

        instance.Lock.EnterReadLock();
        try
        {
            return NavMeshTriangulation.FromNavMesh(instance.Mesh);
        }
        finally
        {
            instance.Lock.ExitReadLock();
        }
    }

    private static int GetPolyAreaMaskBit(DtNavMesh mesh, long polyRef)
    {
        if (mesh.GetTileAndPolyByRef(polyRef, out _, out DtPoly poly).Failed())
            return 0;
        return 1 << NavMeshAreas.FromDetourArea(poly.GetArea());
    }

    #endregion

    #region Update

    // Reused each frame for the tile-cache pump (instances can't be iterated under their own
    // write locks while holding the registration lock).
    private readonly List<NavMeshInstance> _cachePumpScratch = [];

    /// <summary>
    /// Advance the navigation world one frame: fires <see cref="PreUpdate"/>, steps every
    /// agent type's crowd, and pumps each TileCache's incremental update (obstacle carving
    /// processes a bounded slice of tile rebuilds per frame, amortizing carve cost off the
    /// critical path). Called by the scene's variable update.
    /// </summary>
    public void Update(float deltaTime)
    {
        // Steering is gameplay and stops with it.
        if (Application.ShouldRunGameplay)
        {
            PreUpdate?.Invoke(deltaTime);
            foreach (NavMeshCrowdEntry entry in _crowds.Values)
                entry.Crowd.Update(deltaTime, null);
        }

        // Carving is not: an obstacle queues its carve from OnEnable, which runs in the editor
        // too, and without a pump that request would sit unprocessed forever — the mesh looking
        // untouched while the component looks configured. Pumping outside play is also what
        // makes the scene view's overlay show a carve as you position a building.
        // Only the live navmesh changes; obstacles never touch the baked asset.
        //
        // Only instances with queued work are pumped. An idle cache would report up-to-date
        // immediately anyway, but skipping it entirely means a navmesh nothing ever carves
        // costs nothing at all per frame — its tiles are finished Detour tiles and stay that way.
        _cachePumpScratch.Clear();
        lock (_instancesLock)
        {
            foreach (NavMeshInstance instance in _instances)
                if (instance.CachePending)
                    _cachePumpScratch.Add(instance);
        }

        foreach (NavMeshInstance instance in _cachePumpScratch)
        {
            bool upToDate;
            instance.Lock.EnterWriteLock();
            try
            {
                upToDate = instance.TileCache.Update();
            }
            finally
            {
                instance.Lock.ExitWriteLock();
            }

            // Reaching here means work was queued, so report unconditionally — idle instances
            // never enter the scratch list. Do NOT gate this on the converged edge: a carve
            // small enough to finish inside one Update() reports up-to-date on its first call,
            // which swallowed the only notification it would ever send and left agents and the
            // scene-view overlay on stale geometry. Pooled queries deliberately survive the tile
            // swaps — same verified invariant as MutateTileCache; only instance death poisons.
            instance.InvalidateLinkIds();
            NavMeshChanged?.Invoke();
            if (upToDate) instance.CachePending = false;
        }
    }

    #endregion
}
