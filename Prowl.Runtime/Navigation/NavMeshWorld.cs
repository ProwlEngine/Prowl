// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

    /// <summary>Tell the pump this cache has work waiting. Needed after queuing on
    /// <see cref="TileCache"/> directly, which is what <see cref="NavMeshObstacle"/> does;
    /// <see cref="NavMeshWorld.MutateTileCache"/> calls it for you. Main thread only.</summary>
    public void MarkCachePending() => CachePending = true;

    /// <summary>The agent type this navmesh was built for.</summary>
    public int AgentTypeId => Data.Settings.AgentTypeId;

    /// <summary>The asset this instance was created from.</summary>
    public NavMeshData NavMeshData => Data;

    /// <summary>The underlying Detour navmesh, owned by <see cref="TileCache"/>. Advanced use;
    /// mutating it directly bypasses the query locking and desyncs it from the cache that built
    /// it — prefer <see cref="NavMeshWorld.MutateTileCache"/> for tile changes.</summary>
    public DtNavMesh NativeNavMesh => Mesh;

    // The mesh's traversable off-mesh connections by link id, built lazily and invalidated on
    // mutation — turns per-link lookups (every NavMeshLink at scene load, and again per frame
    // while one is selected) into O(1) after a single O(tiles) pass. A link Detour could not
    // attach is absent, so "contains" means usable rather than merely present, and a catch-up
    // retries one that failed instead of taking the stub for success. Main thread only.
    private Dictionary<int, NavMeshConnection>? _connections;

    internal void InvalidateLinkIds() => _connections = null;

    // ReaderWriterLockSlim owns kernel wait handles that only Dispose releases, and disposing
    // one while a thread is inside it throws on that thread rather than this one. Since a worker
    // can ask for a query at any moment — including the instant this instance is unregistered —
    // users are counted: one for the registration, one per outstanding lease or mutation, and
    // whichever is last out disposes. A count that reached zero cannot be revived, so that
    // happens exactly once and with nobody inside.
    private int _users = 1;

    private volatile bool _retired;

    internal bool TryAcquire()
    {
        if (_retired) return false;
        int users = Volatile.Read(ref _users);
        while (users > 0)
        {
            int seen = Interlocked.CompareExchange(ref _users, users + 1, users);
            if (seen == users) return true;
            users = seen;
        }
        return false;
    }

    internal void Release()
    {
        if (Interlocked.Decrement(ref _users) == 0)
            Lock.Dispose();
    }

    /// <summary>Unregistration, from the lock's point of view: stop admitting queries, wait out
    /// the ones already inside, poison the pool, and drop the registration's own hold.</summary>
    internal void Retire()
    {
        _retired = true;

        Lock.EnterWriteLock();
        QueryPool.Clear();
        Lock.ExitWriteLock();

        Release();
    }

    /// <summary>Whether the mesh holds a traversable connection stamped with the given link id
    /// (see <see cref="NavMeshLink.LinkId"/>). Main thread.</summary>
    public bool ContainsLinkId(int linkId) => Connections.ContainsKey(linkId);

    /// <summary>The connection the mesh holds for a link id — where its endpoints actually
    /// snapped to, which is not necessarily where the component put them. False when the link
    /// never attached. Main thread.</summary>
    public bool TryGetConnection(int linkId, out NavMeshConnection connection)
        => Connections.TryGetValue(linkId, out connection);

    private Dictionary<int, NavMeshConnection> Connections
    {
        get
        {
            if (_connections != null) return _connections;

            _connections = [];
            for (int t = 0; t < Mesh.GetMaxTiles(); t++)
            {
                DtMeshTile? tile = Mesh.GetTile(t);
                if (tile?.data?.offMeshCons == null) continue;
                foreach (DtOffMeshConnection con in tile.data.offMeshCons)
                    if (con.userId != 0 && NavMeshConnection.TryFrom(tile, con, out NavMeshConnection connection))
                        _connections[con.userId] = connection;
            }
            return _connections;
        }
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
        _instance.Release();
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
    private int _maxPolyPath = 1024;
    private int _maxStraightPath = 256;

    /// <summary>
    /// How many navmesh polygons one path may cross. Detour needs an explicit ceiling; the
    /// default matches what the Recast demos use for long paths. A route that would exceed it
    /// comes back <see cref="NavMeshPathStatus.PathPartial"/> rather than failing, so the symptom
    /// of setting it too low is agents that stop short on long journeys for no visible reason.
    /// Buffers are rented per query, so the cost is per query in flight, not per world.
    /// </summary>
    public int MaxPolyPath
    {
        get => _maxPolyPath;
        set => _maxPolyPath = Math.Max(2, value);
    }

    /// <summary>
    /// How many corners one path may have. The same trade as <see cref="MaxPolyPath"/>: a path
    /// that fills the buffer is reported partial. Corners are the turns of the string-pulled
    /// route, so this can be far smaller than the polygon count.
    /// </summary>
    public int MaxStraightPath
    {
        get => _maxStraightPath;
        set => _maxStraightPath = Math.Max(2, value);
    }

    private readonly List<NavMeshInstance> _instances = [];
    private readonly Lock _instancesLock = new();

    [ThreadStatic] private static NavMeshQueryFilter? t_scratchFilter;

    // Boxed because queries read the extents from worker threads and a Float3 is three separate
    // floats: a plain field could be read mid-assignment and snap against a mix of the old and
    // new value. Storing it behind a reference makes publication a single atomic write, so a
    // reader sees one whole value or the other. Only the setter allocates.
    private volatile object _defaultQueryExtents = new Float3(1f, 2f, 1f);

    /// <summary>Default half-extents used to snap query positions onto the navmesh, in world
    /// units. Larger values tolerate more vertical mismatch but can snap to the wrong floor.</summary>
    public Float3 DefaultQueryExtents
    {
        get => (Float3)_defaultQueryExtents;
        set => _defaultQueryExtents = value;
    }

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

    /// <summary>Raised whenever a navmesh is added, removed, or mutated — including on every
    /// frame a carve is still converging, so a listener that only wants the finished result
    /// should use <see cref="NavMeshSettled"/>.</summary>
    public event Action? NavMeshChanged;

    /// <summary>
    /// Raised when queued work finishes and the navmesh is stable again. A carve spans several
    /// frames, and anything expensive — re-pathing a crowd, rebuilding a cached triangulation —
    /// wants to run once at the end rather than on each of them.
    /// </summary>
    public event Action? NavMeshSettled;

    #region Registration

    /// <summary>Obstacle capacity navmeshes are instantiated with. Set BEFORE the surface
    /// registers — applied at instantiation.</summary>
    public int TileCacheMaxObstacles = 256;

    /// <summary>Tiles each instance may rebuild per frame while draining queued carves. Higher
    /// spends more frame time to put a carve on the navmesh sooner: a batch takes
    /// <c>tiles / MaxTileUpdatesPerFrame</c> frames to land. Values below 1 are treated as 1.</summary>
    public int MaxTileUpdatesPerFrame = 4;

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

        instance.Retire();

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
            instance.Retire();
        _pendingLinkTiles.Clear();
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
        // Unregistered: its cache is no longer anyone's navmesh, and its lock may already be
        // gone. An async rebuild finishing after its surface was torn down lands here.
        if (!instance.TryAcquire()) return;

        instance.Lock.EnterWriteLock();
        try
        {
            mutation(instance.TileCache);
        }
        finally
        {
            instance.Lock.ExitWriteLock();
            instance.Release();
        }
        // A mutation can leave tiles queued (added tiles rebuild lazily, obstacle edits queue
        // requests), so hand the instance to the pump regardless of what the caller did.
        instance.CachePending = true;
        instance.InvalidateLinkIds();
        NavMeshChanged?.Invoke();
    }

    // Link id -> the enabled component that owns it, for resolving a crowd agent's off-mesh
    // connection back to the link it came from. Per world rather than per process: ids come from
    // the component's scene identifier, so a global table would let one additively loaded scene
    // answer for another's links. Main thread only, like the callbacks that fill it.
    private readonly Dictionary<int, NavMeshLink> _links = [];

    internal void RegisterLink(NavMeshLink link) => _links[link.LinkId] = link;

    /// <summary>Only if this link is the registered owner: on an id collision the loser must not
    /// evict the winner when it is disabled.</summary>
    internal void UnregisterLink(NavMeshLink link)
    {
        if (_links.TryGetValue(link.LinkId, out NavMeshLink? owner) && ReferenceEquals(owner, link))
            _links.Remove(link.LinkId);
    }

    /// <summary>The enabled link with the given id in this scene, or null (see
    /// <see cref="NavMeshLink.LinkId"/>).</summary>
    public NavMeshLink? FindLink(int linkId)
        => _links.TryGetValue(linkId, out NavMeshLink? link) && link.IsValid() ? link : null;

    /// <summary>Every enabled link in this scene. What a bake gathers its off-mesh connections
    /// from, so it never has to walk the scene to find them.</summary>
    public IReadOnlyCollection<NavMeshLink> Links => _links.Values;

    // The scene's enabled surfaces. A link needs the ones it applies to on every edit, so
    // finding them by walking every GameObject would cost a scene scan per link moved — with
    // the link collection each rebuild does nested inside it.
    private readonly List<NavMeshSurface> _surfaces = [];

    internal void RegisterSurface(NavMeshSurface surface)
    {
        if (!_surfaces.Contains(surface)) _surfaces.Add(surface);
    }

    internal void UnregisterSurface(NavMeshSurface surface)
    {
        _surfaces.Remove(surface);
        _pendingLinkTiles.Remove(surface);
    }

    // Link tiles waiting to re-contour, per surface. A link edit dirties the tiles around both
    // its endpoints, and one event edits many links at once — a building coming down takes its
    // ladders with it. Applied as they arrive, each edit would re-collect the scene's links,
    // replace the whole link set again, and re-contour tiles the edit before it had just done.
    // Held until the frame's edits are all in, then applied as one pass per surface.
    private Dictionary<NavMeshSurface, List<AABB>> _pendingLinkTiles = [];

    // The batch being drained, swapped with the one above rather than enumerated in place. The
    // region lists are pooled between frames: a link following a Transform marks every frame, and
    // this is otherwise a list per surface per frame for the life of the movement.
    private Dictionary<NavMeshSurface, List<AABB>> _drainingLinkTiles = [];
    private readonly Stack<List<AABB>> _regionPool = [];

    internal void MarkLinkTilesDirty(NavMeshSurface surface, AABB region)
    {
        if (!_pendingLinkTiles.TryGetValue(surface, out List<AABB>? regions))
            _pendingLinkTiles[surface] = regions = _regionPool.Count > 0 ? _regionPool.Pop() : [];
        regions.Add(region);
    }

    private void DrainLinkTiles()
    {
        if (_pendingLinkTiles.Count == 0) return;

        // Swapped out before draining, because a rebuild raises NavMeshChanged synchronously and a
        // handler is free to disable a link or a surface from it. Marking or unregistering during
        // the drain would then mutate the collection being enumerated, and anything marked would
        // be lost to the clear afterwards. Against the swapped-in batch it simply joins the next
        // frame's, which is where a change made mid-drain belongs anyway.
        (_pendingLinkTiles, _drainingLinkTiles) = (_drainingLinkTiles, _pendingLinkTiles);

        foreach ((NavMeshSurface surface, List<AABB> regions) in _drainingLinkTiles)
        {
            if (surface.IsValid())
                surface.RebuildLinkTiles(CollectionsMarshal.AsSpan(regions));
            regions.Clear();
            _regionPool.Push(regions);
        }
        _drainingLinkTiles.Clear();
    }

    /// <summary>Every enabled surface in this scene, whether or not it has a navmesh loaded.</summary>
    public IReadOnlyList<NavMeshSurface> Surfaces => _surfaces;

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
        // Acquiring is what keeps the instance's lock alive for the life of the lease — it may
        // be unregistered a moment from now, and the last user out is what disposes. A retired
        // one refuses, so a query never begins against a navmesh the world has already dropped.
        if (instance == null || !instance.TryAcquire())
        {
            lease = default;
            return false;
        }

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

            // Read once: the ceilings are settable, and a change between the rent and the span
            // would size a buffer to one value and index it by another.
            int maxPolys = MaxPolyPath, maxCorners = MaxStraightPath;

            long[] polys = ArrayPool<long>.Shared.Rent(maxPolys);
            DtStraightPath[] straight = ArrayPool<DtStraightPath>.Shared.Rent(maxCorners);
            Float3[] corners = ArrayPool<Float3>.Shared.Rent(maxCorners);
            try
            {
                DtStatus status = query.FindPath(startRef, endRef, startPt, endPt, filter, polys.AsSpan(0, maxPolys), out int polyCount, maxPolys);
                if (status.Failed() || polyCount == 0)
                    return false;

                // A partial path's last poly isn't the target poly; steer to the closest point
                // on it instead of the unreachable target.
                bool partial = polys[polyCount - 1] != endRef;
                RcVec3f steerTarget = endPt;
                if (partial)
                    query.ClosestPointOnPoly(polys[polyCount - 1], endPt, out steerTarget, out _);

                DtStatus straightStatus = query.FindStraightPath(startPt, steerTarget, polys.AsSpan(0, polyCount), polyCount,
                    straight.AsSpan(0, maxCorners), out int cornerCount, maxCorners, 0);
                if (straightStatus.Failed() || cornerCount == 0)
                    return false;

                // A corner buffer filled to capacity means FindStraightPath truncated the
                // path; reporting that as complete would lie to the caller.
                if (cornerCount >= maxCorners)
                    partial = true;

                for (int i = 0; i < cornerCount; i++)
                    corners[i] = ToFloat3(straight[i].pos);

                path.SetCorners(corners.AsSpan(0, cornerCount), partial ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete);
                path.SetPolys(polys.AsSpan(0, polyCount));
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

            int maxPolys = MaxPolyPath;
            long[] polys = ArrayPool<long>.Shared.Rent(maxPolys);
            try
            {
                DtStatus status = query.Raycast(startRef, startPt, end, filter, out float t, out RcVec3f normal,
                    polys.AsSpan(0, maxPolys), out int _, maxPolys);
                if (status.Failed())
                    return false;

                bool blocked = t < float.MaxValue;
                Float3 position = blocked
                    ? ToFloat3(RcVec3f.Lerp(startPt, end, Math.Clamp(t, 0f, 1f)))
                    : ToFloat3(end);

                hit.Position = position;
                hit.Normal = blocked ? ToFloat3(normal) : Float3.UnitY;
                hit.Distance = (float)Float3.Distance(sourcePosition, position);
                // The area walked out of, which is the one the wall belongs to.
                hit.Mask = GetPolyAreaMaskBit(query.GetAttachedNavMesh(), startRef);
                hit.Hit = blocked;
                return blocked;
            }
            finally
            {
                ArrayPool<long>.Shared.Return(polys);
            }
        }
    }

    /// <summary>Default <paramref name="maxDistance"/> for
    /// <see cref="FindClosestEdge(Float3, out NavMeshHit, int, float)"/>: wide enough for a
    /// typical level, not derived from the mesh.</summary>
    public const float DefaultEdgeSearchDistance = 100f;

    /// <summary>Locate the closest navmesh border edge from a point.</summary>
    /// <param name="maxDistance">How far to search. Cost grows with it and an edge beyond it is
    /// not found, so pass the widest gap that matters rather than a blanket maximum.</param>
    public bool FindClosestEdge(Float3 sourcePosition, out NavMeshHit hit, int areaMask,
        float maxDistance = DefaultEdgeSearchDistance)
        => FindClosestEdge(sourcePosition, out hit, GetScratchFilter(areaMask), maxDistance);

    /// <inheritdoc cref="FindClosestEdge(Float3, out NavMeshHit, int, float)"/>
    public bool FindClosestEdge(Float3 sourcePosition, out NavMeshHit hit, NavMeshQueryFilter filter,
        float maxDistance = DefaultEdgeSearchDistance)
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

            DtStatus status = query.FindDistanceToWall(startRef, startPt, maxDistance, filter,
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
        if (instance == null || !instance.TryAcquire())
            return NavMeshTriangulation.Empty;

        instance.Lock.EnterReadLock();
        try
        {
            return NavMeshTriangulation.FromNavMesh(instance.Mesh);
        }
        finally
        {
            instance.Lock.ExitReadLock();
            instance.Release();
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

        // Links mark their tiles from LateUpdate, which runs after this, so what drains here is
        // the previous frame's edits — one frame of latency in exchange for a frame's worth of
        // them costing one pass. Immediately before the pump, so the tile work a rebuild queues
        // is drained this frame rather than waiting for the next.
        DrainLinkTiles();

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
            if (!instance.TryAcquire()) continue;

            bool upToDate;
            instance.Lock.EnterWriteLock();
            try
            {
                upToDate = instance.TileCache.Update(MaxTileUpdatesPerFrame);
            }
            finally
            {
                instance.Lock.ExitWriteLock();
                instance.Release();
            }

            // Reaching here means work was queued, so report unconditionally — idle instances
            // never enter the scratch list. Changed must NOT be gated on the converged edge: a
            // carve small enough to finish inside one Update reports up-to-date on its first
            // call, so gating would leave it with no notification at all. Settled is the gated
            // one, for listeners that want the finished mesh rather than each step toward it.
            // Pooled queries deliberately survive the tile swaps — same verified invariant as
            // MutateTileCache; only instance death poisons.
            instance.InvalidateLinkIds();
            NavMeshChanged?.Invoke();
            if (upToDate)
            {
                instance.CachePending = false;
                NavMeshSettled?.Invoke();
            }
        }
    }

    #endregion
}
