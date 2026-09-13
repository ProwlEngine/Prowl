// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;
using Prowl.Recast.Detour.TileCache;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Runtime queries (path, raycast, sample) against one baked <see cref="NavMesh"/>. Internally builds a
/// persistent, tiled <c>DtNavMesh</c> backed by a <c>DtTileCache</c> - a Detour type never crosses this
/// class's public boundary, the same "engine types only" rule <see cref="RecastNavMeshBuilder"/> follows
/// for baking. Building one is not free (every stored tile layer gets decompressed and turned into a
/// live navmesh tile), so a <see cref="NavMeshSystem"/> builds one per surface and hands it out rather
/// than every caller building its own.
/// <para/>
/// The <c>DtTileCache</c> this class owns is what makes a <see cref="NavMeshObstacle"/>'s dynamic
/// carving cheap: an obstacle changes only the one or two tiles it actually overlaps, in place, on the
/// same persistent navmesh - <see cref="NavMeshCrowd"/>'s agents keep walking through the whole thing
/// uninterrupted, never needing to be torn down and rejoined the way a full rebuild elsewhere in the
/// engine still does. See <see cref="TileCache"/> and <see cref="TickTileCache"/>.
/// <para/>
/// A query point close to a small mesh's own edge, combined with the search extents' deliberately
/// generous radius, can have its search box extend past the mesh's bounds entirely; when that happens
/// Detour's own nearest-poly search can still report success while its returned position snaps to
/// something degenerate rather than the true closest point (the found polygon reference itself stays
/// correct). <see cref="FindPath"/>'s corridor resolution isn't affected by this - it re-derives
/// positions from the polygon path rather than trusting that raw snapped point - but a caller reading a
/// snapped position directly (as <see cref="NavMeshCrowd"/> briefly does internally) should not assume
/// it lands exactly on the queried point for a query this close to a mesh edge.
/// </summary>
public sealed class NavMeshQuery
{
    /// <summary>Upper bound on how many polygons a single <see cref="FindPath"/> corridor can span.</summary>
    private const int MaxPathPolys = 256;

    /// <summary>Upper bound on how many corners <see cref="FindPath"/>'s string-pulling can produce.</summary>
    private const int MaxStraightPathPoints = 256;

    /// <summary>How many tiles <see cref="TickTileCache"/> is allowed to actually rebuild per call - a
    /// small, fixed budget so a burst of obstacle changes in one frame never turns into an unbounded
    /// stall; the rest just carries over to the next tick, the same as <c>DtTileCache</c>'s own demo
    /// usage budgets it.</summary>
    private const int TileCacheUpdateBudget = 4;

    /// <summary>The persistent, tiled Detour navmesh this query was built from, reconstructed by
    /// <see cref="BuildTiledNavMesh"/>.</summary>
    private readonly DtNavMesh _navMesh;

    /// <summary>Owns <see cref="_navMesh"/>'s tiles - both the ones reconstructed at construction time
    /// and any a <see cref="NavMeshObstacle"/> carves in afterward. See this class's own doc comment.</summary>
    private readonly DtTileCache _tileCache;

    // Detour's own query engine keeps per-instance scratch state (its A* open/closed node pools) that
    // is not safe for two threads to drive concurrently through the same instance - two threads calling
    // FindPath at once through one shared DtNavMeshQuery would corrupt each other's search state. A pool
    // of otherwise-identical instances, each bound to the same read-only _navMesh, sidesteps that: a
    // caller rents one for the duration of a single query and returns it, so concurrent callers simply
    // get different instances rather than contending over one. The pool has no fixed ceiling - it grows
    // to whatever the actual peak concurrent query count turns out to be and reuses from then on, which
    // is the same trade-off a thread pool itself makes for its own worker count.
    /// <summary>Pool of interchangeable Detour query engines, all bound to <see cref="_navMesh"/> - see
    /// <see cref="RentDetourQuery"/>/<see cref="ReturnDetourQuery"/>.</summary>
    private readonly ConcurrentBag<DtNavMeshQuery> _queryPool = [];

    // A structural change to _navMesh's own tiles - TickTileCache() rebuilding a carved tile, or
    // ReplaceTile() swapping one wholesale - is not safe to run while any pooled query above is mid-walk
    // through that same navmesh's polygon/tile data. Every read-only query call takes this lock's read
    // side (many readers run concurrently, none blocking each other); every structural mutation takes
    // its write side (exclusive - waits for every in-flight read to finish first, and blocks new ones
    // until it's done). This is the "queries are thread-safe, mutations take the write lock" contract
    // this class's own doc comment describes.
    /// <summary>Guards <see cref="_navMesh"/>'s structural consistency between concurrent queries (read
    /// side) and the handful of methods that actually rebuild or replace one of its tiles (write side).</summary>
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    /// <summary>The "every area allowed" filter <see cref="FindPath"/>/<see cref="Raycast"/>/
    /// <see cref="SamplePosition"/> use by default, carrying this query's area-cost snapshot. Not
    /// <c>new DtQueryDefaultFilter()</c>'s own default: that leaves <c>includeFlags</c> at Detour's
    /// historical 16-bit-wide default (0xFFFF), which would silently exclude every polygon whose area
    /// index is 16 or higher - see <see cref="NavMeshTileCacheMeshProcess"/>, where a polygon's Detour
    /// flag bit is set directly from its <see cref="NavMeshAreas"/> index so an area mask can select it
    /// later.</summary>
    private readonly IDtQueryFilter _filter;

    /// <summary>How far off-mesh a query point is still allowed to snap to a polygon, sized from the
    /// baked mesh's own agent radius/height so a caller passing a position slightly above/beside the
    /// ground still resolves.</summary>
    private readonly RcVec3f _searchExtents;

    /// <summary>Parallel to the off-mesh connections woven into <see cref="_navMesh"/> - which
    /// <see cref="NavMeshLink"/> produced each one, indexed by the connection's user id. Null if this
    /// query wasn't built with link sources.</summary>
    private readonly IReadOnlyList<NavMeshLink>? _linkSources;

    /// <summary>This query's agent type's transient cost overlay - see <see cref="NavMeshCostAwareFilter"/>,
    /// which every filter <see cref="CreateFilter"/> builds reads from. Owned by <see cref="NavMeshSystem"/>
    /// (see its own <see cref="NavMeshCostLayer"/> doc comment for why); a caller building a query without
    /// one (most of this file's own tests, anything outside a live scene) gets a private, empty layer
    /// nobody else can reach instead of costs silently doing nothing.</summary>
    private readonly NavMeshCostLayer _costLayer;

    /// <summary>Cached flow fields, keyed by which cell of a <see cref="_flowFieldCellSize"/>-sized grid
    /// their goal falls into - see <see cref="GetOrBuildFlowField"/>. Cleared whenever a tile actually
    /// changes (<see cref="TickTileCache"/> applying a carve, <see cref="ReplaceTile"/>), never partially:
    /// a field's own reachable set can span many tiles, so there is no cheap way to know from here alone
    /// which cached fields a single changed tile could have affected without walking every one of them,
    /// and a bake's own tile count is normally small enough that clearing the whole cache and rebuilding
    /// on next use is not a meaningful cost next to the carve itself.</summary>
    private readonly Dictionary<(int X, int Z), NavMeshFlowField> _flowFieldCache = [];

    /// <summary>World-space size of one flow-field cache cell - reuses the bake's own tile size rather
    /// than inventing a separate figure, since it is already a reasonable "how far apart do two goals
    /// need to be to deserve their own field" granularity for this same navmesh.</summary>
    private readonly float _flowFieldCellSize;

    /// <summary>The baked asset's own stored tile layers - kept for the query's whole lifetime (not just
    /// read once at construction) so <see cref="LoadTilesInRegion"/> can bring a tile <see cref="UnloadTilesInRegion"/>
    /// removed back from storage later, and so a newly re-added tile reflects whatever <see cref="NavMeshSurface.RebuildTiles(Float3, Float3)"/>
    /// has since written into it.</summary>
    private readonly NavMeshTileCacheData _tileCacheData;

    /// <summary>Builds a query for <paramref name="navMesh"/>.</summary>
    /// <param name="navMesh">The baked mesh to query against.</param>
    /// <param name="links">Off-mesh connections (see <see cref="NavMeshLink"/>) to weave into this
    /// query's navmesh tiles as they're built. Resolved once here, not re-read live: a link added, moved
    /// or removed after this query is built needs a new query to take effect (rebuilding a surface's
    /// query is cheap relative to a full bake, so this is the intended way to pick up a link change).</param>
    /// <param name="linkSources">Parallel to <paramref name="links"/> - which <see cref="NavMeshLink"/>
    /// produced each entry, so crowd movement can fire <see cref="NavMeshLink.Traversed"/> when an agent
    /// crosses one. Optional: null (or mismatched length) just means traversal events can't be resolved
    /// for this query, not an error - <see cref="NavMeshLinkData"/> itself deliberately stays free of any
    /// reference back to a MonoBehaviour, so this is threaded through separately rather than added there.</param>
    /// <param name="costLayer">This query's agent type's transient cost overlay - see <see cref="NavMeshSystem.GetOrCreateCostLayer"/>.
    /// Optional: null gets a private, empty layer instead (see this field's own doc comment).</param>
    public NavMeshQuery(NavMesh navMesh, IReadOnlyList<NavMeshLinkData>? links = null, IReadOnlyList<NavMeshLink>? linkSources = null, NavMeshCostLayer? costLayer = null)
    {
        if (navMesh.IsNotValid() || !navMesh.IsBuilt)
            throw new ArgumentException("Cannot query a NavMesh with no baked data.", nameof(navMesh));

        // Jump/drop links NavMeshLinkGenerator baked into this asset are woven in alongside whatever
        // NavMeshLink components the caller resolved - a generated link has no such component, so it
        // occupies a null slot in the parallel source list rather than being left out of it entirely.
        List<NavMeshLinkData> generated = navMesh.TileCacheData!.GeneratedLinks;
        IReadOnlyList<NavMeshLinkData> allLinks = generated.Count == 0
            ? links ?? []
            : [.. generated, .. links ?? []];
        IReadOnlyList<NavMeshLink>? allLinkSources = generated.Count == 0
            ? linkSources
            : linkSources != null ? [.. new NavMeshLink[generated.Count], .. linkSources] : null;

        _linkSources = allLinkSources != null && allLinkSources.Count == allLinks.Count ? allLinkSources : null;
        _costLayer = costLayer ?? new NavMeshCostLayer();
        _tileCacheData = navMesh.TileCacheData!;
        _flowFieldCellSize = Maths.Max(navMesh.TileCacheData!.TileWorldSize, 1f);

        (_navMesh, _tileCache) = BuildTiledNavMesh(navMesh.TileCacheData!, allLinks);
        _queryPool.Add(new DtNavMeshQuery(_navMesh)); // seed the pool with one instance up front
        _filter = CreateFilter(uint.MaxValue);

        // How far off-mesh a query point is still allowed to snap to a polygon. Generous relative to
        // the agent so a caller passing a position slightly above/beside the ground still resolves.
        float radius = Maths.Max(navMesh.BakeSettings.AgentRadius, navMesh.BakeSettings.CellSize) * 4f + 1f;
        float height = navMesh.BakeSettings.AgentHeight + 1f;
        _searchExtents = new RcVec3f(radius, height, radius);
    }

    /// <summary>The underlying Detour navmesh, for <see cref="NavMeshCrowd"/> to build its crowd
    /// simulation from. Internal: crowd movement is the only other place a Detour type is allowed to
    /// cross a public boundary, so this never becomes part of this class's own public surface.</summary>
    internal DtNavMesh DetourNavMesh => _navMesh;

    /// <summary>The tile cache backing <see cref="DetourNavMesh"/>, for <see cref="NavMeshObstacle"/> to
    /// carve into directly.</summary>
    internal DtTileCache TileCache => _tileCache;

    /// <summary>The <see cref="NavMeshLink"/> that contributed the off-mesh connection identified by
    /// <paramref name="userId"/> (the index it was given among <see cref="NavMeshLinkData"/> entries when
    /// this query was built). Null if out of range or this query wasn't built with link sources.</summary>
    internal NavMeshLink? GetLinkByOffMeshUserId(int userId) =>
        _linkSources != null && userId >= 0 && userId < _linkSources.Count ? _linkSources[userId] : null;

    /// <summary>Builds a filter restricted to <paramref name="areaMask"/> (one bit per
    /// <see cref="NavMeshAreas"/> index - the same bit a polygon's Detour flag was given in
    /// <see cref="NavMeshTileCacheMeshProcess"/>), carrying this query's area-cost snapshot. Internal:
    /// exists for <see cref="NavMeshCrowd"/> to build its own per-agent-type-id filter slots from, the
    /// only other place a Detour type is allowed to cross a public boundary (see this class's own doc
    /// comment).</summary>
    internal IDtQueryFilter CreateFilter(uint areaMask) =>
        new NavMeshCostAwareFilter(new DtQueryDefaultFilter(unchecked((int)areaMask), 0, BuildAreaCostSnapshot()), _costLayer);

    /// <summary>Detour's own per-area cost multipliers, indexed by its own area id space (0-63, see
    /// <see cref="NavMeshAreas.ToRecastArea"/>) rather than the Prowl area index directly. Snapshotted
    /// once per filter build from the live <see cref="NavMeshAreas"/> registry - the same "read what you
    /// need on the main thread, don't keep re-reading mutable state" rule <see cref="NavMeshAgentTypes"/>
    /// and <see cref="NavMeshAreas"/> themselves call out.</summary>
    private static float[] BuildAreaCostSnapshot()
    {
        var costs = new float[64];
        for (int recastArea = 0; recastArea < costs.Length; recastArea++)
            costs[recastArea] = NavMeshAreas.GetAreaCost(NavMeshAreas.FromRecastArea(recastArea));
        return costs;
    }

    /// <summary>Snaps <paramref name="point"/> onto the nearest polygon and returns both its reference
    /// and position, using this query's own search extents/filter - the same state <see cref="FindPath"/>
    /// and <see cref="SamplePosition"/> already rely on. For <see cref="NavMeshCrowd"/> to resolve a
    /// destination's polygon reference for <c>DtCrowd.RequestMoveTarget</c>, which needs one and
    /// <see cref="SamplePosition"/> alone doesn't expose it.</summary>
    internal bool TryFindNearestPolyRef(Float3 point, out long polyRef, out Float3 nearest) =>
        TryFindNearestPolyRef(point, uint.MaxValue, out polyRef, out nearest);

    /// <summary>Overload of <see cref="TryFindNearestPolyRef(Float3, out long, out Float3)"/> restricted
    /// to <paramref name="areaMask"/> - for <see cref="NavMeshCrowd.SetDestination"/> to snap a
    /// destination onto a polygon the requesting agent's own <see cref="NavMeshAgent.AreaMask"/> allows,
    /// rather than onto whichever polygon happens to be nearest regardless of area.</summary>
    internal bool TryFindNearestPolyRef(Float3 point, uint areaMask, out long polyRef, out Float3 nearest)
    {
        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try
            {
                IDtQueryFilter filter = areaMask == uint.MaxValue ? _filter : CreateFilter(areaMask);
                bool found = TryFindNearestPoly(query, point, filter, out polyRef, out RcVec3f result);
                nearest = ToFloat3(result);
                return found;
            }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Finds a path from <paramref name="start"/> to <paramref name="end"/>, both snapped to
    /// the nearest polygon first - across as many baked tiles as the route actually spans, in the one
    /// call, exactly as it would within a single tile. Returns <see cref="NavMeshPath.None"/> if either
    /// point has no nearby polygon or no corridor connects them. Thread-safe: safe to call concurrently
    /// with any other query method, from any thread, including while <see cref="NavMeshSystem.TickCrowds"/>
    /// is carving an obstacle or another thread is mid-<see cref="FindPath"/> of its own - see this
    /// class's own doc comment.</summary>
    /// <param name="areaMask">Which <see cref="NavMeshAreas"/> the path may cross, one bit per area
    /// index. Defaults to every area. A polygon whose area isn't in the mask is invisible to this
    /// call - the same restriction <see cref="NavMeshAgent.SetDestination"/> applies via its own
    /// <see cref="NavMeshAgent.AreaMask"/>.</param>
    public NavMeshPath FindPath(Float3 start, Float3 end, uint areaMask = uint.MaxValue)
    {
        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try { return FindPathAutoHierarchical(query, start, end, areaMask); }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Same as <see cref="FindPath"/>, but always the plain, unrestricted search - never the
    /// hierarchical fast path, regardless of how far apart <paramref name="start"/>/<paramref name="end"/>
    /// are. The "flat A*" baseline the hierarchical strategy is meant to stay close to, for a caller
    /// that wants to compare the two, or one that has a specific reason not to trust the heuristic for
    /// its own geometry.</summary>
    internal NavMeshPath FindPathFlat(Float3 start, Float3 end, uint areaMask = uint.MaxValue)
    {
        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try { return FindPathCore(query, start, end, areaMask); }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>The number of tiles the coarse tile-level A* between <paramref name="start"/> and
    /// <paramref name="end"/> actually routes through - an upper bound on how much of the navmesh the
    /// hierarchical strategy's own restricted search can ever visit, since it only ever considers
    /// polygons belonging to those tiles (padded with their immediate neighbors). Null if the two points
    /// aren't on tiles at all, or no coarse route between them exists.</summary>
    internal int? GetHierarchicalCorridorTileCount(Float3 start, Float3 end, uint areaMask = uint.MaxValue)
    {
        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try
            {
                IDtQueryFilter filter = areaMask == uint.MaxValue ? _filter : CreateFilter(areaMask);
                if (!TryGetTileCoord(query, start, filter, out (int X, int Y) startTile)) return null;
                if (!TryGetTileCoord(query, end, filter, out (int X, int Y) endTile)) return null;

                HierarchicalGraph graph = EnsureHierarchicalGraph(query, filter);
                return CoarseTileAStar(graph, startTile, endTile)?.Count;
            }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>How many tiles apart (Chebyshev distance) start and end have to be before <see cref="FindPath"/>
    /// tries the hierarchical route first - close enough together, an ordinary unrestricted search is
    /// already cheap and there is nothing for a coarse tile-level pass to usefully narrow down.</summary>
    private const int HierarchicalTileDistanceThreshold = 4;

    /// <summary>Resolves the filter once, then tries the hierarchical strategy when start and end are far
    /// enough apart in tile terms (see <see cref="HierarchicalTileDistanceThreshold"/>), falling back to
    /// the plain, unrestricted search on any failure along the way - a missing tile graph, no coarse
    /// route, or the restricted search itself coming up empty. <see cref="FindPath"/>'s own public
    /// contract (a path or <see cref="NavMeshPath.None"/>, thread-safe, no allocation surprises beyond
    /// what a normal call already makes) is identical either way; which strategy actually ran is an
    /// internal, unobservable detail.</summary>
    private NavMeshPath FindPathAutoHierarchical(DtNavMeshQuery query, Float3 start, Float3 end, uint areaMask)
    {
        IDtQueryFilter filter = areaMask == uint.MaxValue ? _filter : CreateFilter(areaMask);

        if (TryGetTileCoord(query, start, filter, out (int X, int Y) startTile) &&
            TryGetTileCoord(query, end, filter, out (int X, int Y) endTile))
        {
            int tileDistance = Math.Max(Math.Abs(startTile.X - endTile.X), Math.Abs(startTile.Y - endTile.Y));
            if (tileDistance > HierarchicalTileDistanceThreshold)
            {
                NavMeshPath? hierarchical = TryFindPathHierarchical(query, start, end, filter, startTile, endTile);
                if (hierarchical != null) return hierarchical;
            }
        }

        return FindPathCoreWithFilter(query, start, end, filter);
    }

    private NavMeshPath FindPathCore(DtNavMeshQuery query, Float3 start, Float3 end, uint areaMask) =>
        FindPathCoreWithFilter(query, start, end, areaMask == uint.MaxValue ? _filter : CreateFilter(areaMask));

    private NavMeshPath FindPathCoreWithFilter(DtNavMeshQuery query, Float3 start, Float3 end, IDtQueryFilter filter)
    {
        if (!TryFindNearestPoly(query, start, filter, out long startRef, out RcVec3f startPos)) return NavMeshPath.None;
        if (!TryFindNearestPoly(query, end, filter, out long endRef, out RcVec3f endPos)) return NavMeshPath.None;

        Span<long> path = new long[MaxPathPolys];
        DtStatus status = query.FindPath(startRef, endRef, startPos, endPos, filter, path, out int pathCount, path.Length);
        if (!status.Succeeded() || pathCount == 0)
            return NavMeshPath.None;

        Span<DtStraightPath> straight = new DtStraightPath[MaxStraightPathPoints];
        DtStatus straightStatus = query.FindStraightPath(
            startPos, endPos, path[..pathCount], pathCount, straight, out int straightCount, straight.Length, 0);
        if (!straightStatus.Succeeded() || straightCount == 0)
            return NavMeshPath.None;

        var corners = new Float3[straightCount];
        var isOffMeshLink = new bool[straightCount];
        for (int i = 0; i < straightCount; i++)
        {
            corners[i] = ToFloat3(straight[i].pos);
            isOffMeshLink[i] = (straight[i].flags & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0;
        }

        var corridor = new int[pathCount];
        for (int i = 0; i < pathCount; i++)
            corridor[i] = DtDetour.DecodePolyIdPoly(path[i]);

        return new NavMeshPath
        {
            Success = true,
            Partial = status.IsPartial(),
            Corners = corners,
            CornerIsOffMeshLink = isOffMeshLink,
            CorridorPolygons = corridor,
        };
    }

    /// <summary>Casts a straight-line "can an agent walk directly here" ray across the mesh surface,
    /// starting from the polygon nearest <paramref name="start"/>. Ignores height on the way
    /// (a 2D check along whichever floor <paramref name="start"/> sits on) - meant for short,
    /// same-floor checks, not general visibility. Returns false and leaves
    /// <paramref name="hitPoint"/> at <paramref name="end"/> when nothing is hit before reaching it.
    /// Thread-safe - see <see cref="FindPath"/>'s own doc comment.</summary>
    public bool Raycast(Float3 start, Float3 end, out Float3 hitPoint)
    {
        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try { return RaycastCore(query, start, end, out hitPoint); }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    private bool RaycastCore(DtNavMeshQuery query, Float3 start, Float3 end, out Float3 hitPoint)
    {
        hitPoint = end;
        if (!TryFindNearestPoly(query, start, _filter, out long startRef, out RcVec3f startPos))
            return false;

        RcVec3f endPos = ToRc(end);
        Span<long> path = new long[MaxPathPolys];
        DtStatus status = query.Raycast(startRef, startPos, endPos, _filter, out float t, out RcVec3f _, path, out int _, path.Length);
        if (!status.Succeeded() || t >= 1.0f || float.IsNaN(t))
            return false;

        hitPoint = ToFloat3(startPos) + (end - ToFloat3(startPos)) * t;
        return true;
    }

    /// <summary>Snaps <paramref name="point"/> onto the nearest polygon and finds the closest point on
    /// any navmesh edge (a wall, a ledge, a carved obstacle boundary) within <paramref name="maxRadius"/>
    /// of it - Unity's <c>NavMeshHit</c>-returning <c>FindClosestEdge</c>, split into its own out
    /// parameters rather than a hit struct. <paramref name="hitNormal"/> points away from the wall, into
    /// walkable space. False if nothing on the mesh is within the search extents, or no edge is within
    /// <paramref name="maxRadius"/> of the snapped point. Thread-safe - see <see cref="FindPath"/>'s own
    /// doc comment.</summary>
    public bool FindClosestEdge(Float3 point, float maxRadius, out Float3 hitPosition, out Float3 hitNormal, out float distance)
    {
        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try { return FindClosestEdgeCore(query, point, maxRadius, out hitPosition, out hitNormal, out distance); }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    private bool FindClosestEdgeCore(DtNavMeshQuery query, Float3 point, float maxRadius, out Float3 hitPosition, out Float3 hitNormal, out float distance)
    {
        hitPosition = point;
        hitNormal = Float3.Zero;
        distance = 0f;

        if (!TryFindNearestPoly(query, point, _filter, out long polyRef, out RcVec3f nearest))
            return false;

        DtStatus status = query.FindDistanceToWall(polyRef, nearest, maxRadius, _filter, out distance, out RcVec3f hitPos, out RcVec3f hitNorm);
        if (!status.Succeeded())
            return false;

        hitPosition = ToFloat3(hitPos);
        hitNormal = ToFloat3(hitNorm);
        return true;
    }

    /// <summary>Snaps <paramref name="point"/> onto the nearest polygon and returns the closest
    /// on-mesh position, sampled at the detail mesh's actual height rather than the polygon outline's
    /// corner heights. False if nothing on the mesh is within the search extents built from the bake
    /// settings' agent size. Thread-safe - see <see cref="FindPath"/>'s own doc comment.</summary>
    public bool SamplePosition(Float3 point, out Float3 result)
    {
        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try
            {
                result = point;
                if (!TryFindNearestPoly(query, point, _filter, out _, out RcVec3f nearest))
                    return false;

                result = ToFloat3(nearest);
                return true;
            }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Rents a Detour query engine for the duration of <see cref="NavMeshQueryHandle.Dispose"/> -
    /// the manual counterpart to renting-and-returning around every single call the way <see cref="FindPath"/>
    /// and friends already do internally. For a caller doing several queries back to back and wanting to
    /// pay the rental's read-lock/pool overhead once rather than per call. Named to match the "Try"
    /// pattern the rest of this engine reserves for an operation that can genuinely come back empty; here
    /// that would only ever happen if this query were ever invalidated out from under a caller mid-rental,
    /// which nothing in this engine currently does, so today this always succeeds - the signature is kept
    /// this way so adding that possibility later (a query explicitly disposed while rented, say) is not a
    /// breaking API change.</summary>
    public bool TryRentQuery(out NavMeshQueryHandle handle)
    {
        _lock.EnterReadLock();
        handle = new NavMeshQueryHandle(this, RentDetourQuery());
        return true;
    }

    /// <summary>Returns a query engine rented via <see cref="TryRentQuery"/>/an internal call, and
    /// releases the read-lock side that came with renting it. Internal: only <see cref="NavMeshQueryHandle.Dispose"/>
    /// and this class's own methods ever return one.</summary>
    internal void ReturnRentedQuery(DtNavMeshQuery query)
    {
        ReturnDetourQuery(query);
        _lock.ExitReadLock();
    }

    // The four methods below back NavMeshQueryHandle's own public surface: the handle already holds
    // both the read lock and a rented engine for its whole lifetime (acquired by TryRentQuery), so these
    // run the same *Core logic FindPath/Raycast/FindClosestEdge/SamplePosition use per call, without
    // taking the lock or renting an engine a second time.
    internal NavMeshPath FindPathUsing(DtNavMeshQuery query, Float3 start, Float3 end, uint areaMask) =>
        FindPathCore(query, start, end, areaMask);

    internal bool RaycastUsing(DtNavMeshQuery query, Float3 start, Float3 end, out Float3 hitPoint) =>
        RaycastCore(query, start, end, out hitPoint);

    internal bool FindClosestEdgeUsing(DtNavMeshQuery query, Float3 point, float maxRadius, out Float3 hitPosition, out Float3 hitNormal, out float distance) =>
        FindClosestEdgeCore(query, point, maxRadius, out hitPosition, out hitNormal, out distance);

    internal bool SamplePositionUsing(DtNavMeshQuery query, Float3 point, out Float3 result)
    {
        result = point;
        if (!TryFindNearestPoly(query, point, _filter, out _, out RcVec3f nearest))
            return false;

        result = ToFloat3(nearest);
        return true;
    }

    /// <summary>Sets an additive cost over every polygon within <paramref name="radius"/> of
    /// <paramref name="worldPos"/> - <see cref="NavMeshSystem.AddCost"/>'s own primitive. The search
    /// radius is graph distance along the mesh (Detour's own <c>FindPolysAroundCircle</c>), not straight
    /// -line distance, so it naturally stays within one connected region rather than reaching through a
    /// wall to a point that happens to sit nearby in world space on the other side of it. False if
    /// nothing on the mesh is near enough to <paramref name="worldPos"/> to start from at all.</summary>
    internal bool AddCost(Float3 worldPos, float radius, float cost, float decayPerSecond)
    {
        const int maxResults = 256;

        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try
            {
                if (!TryFindNearestPoly(query, worldPos, _filter, out long startRef, out RcVec3f startPos))
                    return false;

                Span<long> resultRefs = new long[maxResults];
                Span<long> resultParents = new long[maxResults];
                Span<float> resultCosts = new float[maxResults];
                DtStatus status = query.FindPolysAroundCircle(
                    startRef, startPos, radius, _filter, resultRefs, resultParents, resultCosts, out int count, maxResults);
                if (!status.Succeeded() || count == 0)
                    return false;

                for (int i = 0; i < count; i++)
                    _costLayer.Set(resultRefs[i], cost, decayPerSecond);
                return true;
            }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>The flow field toward <paramref name="goal"/>, reusing a cached one already built for a
    /// nearby goal (same <see cref="_flowFieldCellSize"/>-sized cell) rather than rebuilding - the whole
    /// point of a shared field for a crowd converging on one spot. Cleared automatically whenever a tile
    /// this field's own reachable set could plausibly include actually changes (see <see cref="TickTileCache"/>/
    /// <see cref="ReplaceTile"/>'s own doc comments); the next call after that rebuilds fresh.</summary>
    internal NavMeshFlowField GetOrBuildFlowField(Float3 goal, float maxRadius)
    {
        (int x, int z) = ((int)MathF.Floor(goal.X / _flowFieldCellSize), (int)MathF.Floor(goal.Z / _flowFieldCellSize));

        _lock.EnterReadLock();
        try
        {
            if (_flowFieldCache.TryGetValue((x, z), out NavMeshFlowField? cached)) return cached;
        }
        finally { _lock.ExitReadLock(); }

        // Not cached - build outside any lock this method itself holds (BuildFlowField takes its own
        // read lock), then insert. Two concurrent callers missing the same cell both building and one
        // overwriting the other's entry is a harmless, rare race - not a correctness concern, since
        // either result is a valid field for the same goal cell.
        NavMeshFlowField built = BuildFlowField(goal, maxRadius);

        _lock.EnterWriteLock();
        try { _flowFieldCache[(x, z)] = built; }
        finally { _lock.ExitWriteLock(); }

        return built;
    }

    /// <summary>Floods outward from <paramref name="goal"/> across the polygon graph, via the same
    /// cost-aware filter every other query on this instance already uses, and turns the result into a
    /// direction (toward whichever neighbor the search actually arrived from) and remaining distance per
    /// reachable polygon - see <see cref="NavMeshFlowField"/>'s own doc comment for why a polygon, not a
    /// voxel cell, is this field's unit of resolution.</summary>
    private NavMeshFlowField BuildFlowField(Float3 goal, float maxRadius)
    {
        const int maxResults = 2048;

        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try
            {
                var directions = new Dictionary<long, Float3>();
                var distances = new Dictionary<long, float>();

                if (!TryFindNearestPoly(query, goal, _filter, out long goalRef, out RcVec3f goalPos))
                    return new NavMeshFlowField(this, directions, distances);

                Span<long> resultRefs = new long[maxResults];
                Span<long> resultParents = new long[maxResults];
                Span<float> resultCosts = new float[maxResults];
                DtStatus status = query.FindPolysAroundCircle(
                    goalRef, goalPos, maxRadius, _filter, resultRefs, resultParents, resultCosts, out int count, maxResults);
                if (!status.Succeeded()) return new NavMeshFlowField(this, directions, distances);

                var centers = new Dictionary<long, Float3>(count);
                for (int i = 0; i < count; i++)
                    centers[resultRefs[i]] = PolygonCenter(resultRefs[i]);

                for (int i = 0; i < count; i++)
                {
                    long polyRef = resultRefs[i];
                    distances[polyRef] = resultCosts[i];

                    long parentRef = resultParents[i];
                    if (parentRef == 0 || !centers.TryGetValue(parentRef, out Float3 parentCenter))
                    {
                        directions[polyRef] = Float3.Zero; // the goal's own polygon - nowhere closer to go
                        continue;
                    }

                    Float3 toward = parentCenter - centers[polyRef];
                    directions[polyRef] = Float3.Length(toward) > 0.0001f ? Float3.Normalize(toward) : Float3.Zero;
                }

                return new NavMeshFlowField(this, directions, distances);
            }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>The average of a polygon's own vertices - a cheap, good-enough stand-in for its true
    /// centroid for the purpose of picking a flow direction between it and a neighbor.</summary>
    private Float3 PolygonCenter(long polyRef)
    {
        DtStatus status = _navMesh.GetTileAndPolyByRef(polyRef, out DtMeshTile tile, out DtPoly poly);
        if (!status.Succeeded()) return Float3.Zero;

        Float3 sum = Float3.Zero;
        for (int i = 0; i < poly.vertCount; i++)
            sum += VertexAt(tile.data.verts, poly.verts[i]);
        return sum / poly.vertCount;
    }

    /// <summary>Ranks candidate positions within <paramref name="radius"/> (graph distance) of
    /// <paramref name="origin"/> - <see cref="NavMeshSystem.QueryTacticalPositions"/>'s own primitive.
    /// Every reachable polygon's own centre and each of its edge midpoints is one candidate; a scorer
    /// returning negative for a candidate drops it from the results entirely, and every other scorer's
    /// return for it sums into <see cref="NavMeshTacticalResult.TotalScore"/>, which the results are
    /// sorted by, highest first.</summary>
    internal List<NavMeshTacticalResult> QueryTacticalPositions(Float3 origin, float radius, IReadOnlyList<ITacticalScorer> scorers)
    {
        const int maxResults = 1024;
        var results = new List<NavMeshTacticalResult>();

        _lock.EnterReadLock();
        try
        {
            DtNavMeshQuery query = RentDetourQuery();
            try
            {
                if (!TryFindNearestPoly(query, origin, _filter, out long startRef, out RcVec3f startPos))
                    return results;

                Span<long> resultRefs = new long[maxResults];
                Span<long> resultParents = new long[maxResults];
                Span<float> resultCosts = new float[maxResults];
                DtStatus status = query.FindPolysAroundCircle(
                    startRef, startPos, radius, _filter, resultRefs, resultParents, resultCosts, out int count, maxResults);
                if (!status.Succeeded()) return results;

                var candidates = new HashSet<Float3>();
                for (int i = 0; i < count; i++)
                    CollectCandidatesFor(resultRefs[i], candidates);

                var breakdown = new float[scorers.Count];
                foreach (Float3 candidate in candidates)
                {
                    bool disqualified = false;
                    float total = 0f;
                    for (int s = 0; s < scorers.Count; s++)
                    {
                        float score = scorers[s].Score(candidate);
                        breakdown[s] = score;
                        if (score < 0f) { disqualified = true; break; }
                        total += score;
                    }
                    if (disqualified) continue;

                    results.Add(new NavMeshTacticalResult(candidate, total, (float[])breakdown.Clone()));
                }

                results.Sort((a, b) => b.TotalScore.CompareTo(a.TotalScore));
                return results;
            }
            finally { ReturnDetourQuery(query); }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Adds a polygon's own centre and each of its edge midpoints to <paramref name="candidates"/> -
    /// <see cref="QueryTacticalPositions"/>'s own sampling. A <see cref="HashSet{Float3}"/> naturally
    /// dedupes the edge midpoint two adjoining polygons in the same reachable set both contribute.</summary>
    private void CollectCandidatesFor(long polyRef, HashSet<Float3> candidates)
    {
        DtStatus status = _navMesh.GetTileAndPolyByRef(polyRef, out DtMeshTile tile, out DtPoly poly);
        if (!status.Succeeded()) return;

        Float3 sum = Float3.Zero;
        for (int i = 0; i < poly.vertCount; i++)
        {
            Float3 a = VertexAt(tile.data.verts, poly.verts[i]);
            Float3 b = VertexAt(tile.data.verts, poly.verts[(i + 1) % poly.vertCount]);
            candidates.Add((a + b) * 0.5f);
            sum += a;
        }
        candidates.Add(sum / poly.vertCount);
    }

    /// <summary>The tile grid coordinate the polygon nearest <paramref name="pos"/> belongs to, or false
    /// if nothing is nearby - <see cref="FindPathAutoHierarchical"/>'s own way of deciding whether start
    /// and end are far enough apart in tile terms to bother with the hierarchical strategy at all.</summary>
    private bool TryGetTileCoord(DtNavMeshQuery query, Float3 pos, IDtQueryFilter filter, out (int X, int Y) tileCoord)
    {
        if (TryFindNearestPoly(query, pos, filter, out long polyRef, out _))
        {
            DtStatus status = _navMesh.GetTileAndPolyByRef(polyRef, out DtMeshTile tile, out DtPoly _);
            if (status.Succeeded()) { tileCoord = (tile.data.header.x, tile.data.header.y); return true; }
        }
        tileCoord = default;
        return false;
    }

    /// <summary>The abstract tile-level graph <see cref="TryFindPathHierarchical"/> runs its coarse A*
    /// over: one node per tile that currently holds at least one polygon, edges between grid-adjacent
    /// tiles Detour can actually path between (checked once, at build time, via a real probe search
    /// between each tile's own representative point), each edge's cost the length of that same probe
    /// path - "a precomputed intra-tile path", not a straight-line guess at it.</summary>
    private sealed class HierarchicalGraph
    {
        public readonly Dictionary<(int X, int Y), Float3> Representative = [];
        public readonly Dictionary<(int X, int Y), List<(int X, int Y)>> Adjacency = [];
        public readonly Dictionary<((int X, int Y) From, (int X, int Y) To), float> EdgeCost = [];
    }

    /// <summary>Cached by <see cref="EnsureHierarchicalGraph"/>; cleared (not incrementally patched)
    /// whenever a tile actually changes - see <see cref="TickTileCache"/>/<see cref="ReplaceTile"/>'s own
    /// doc comments for why a full clear, the same trade-off <see cref="_flowFieldCache"/> already makes.</summary>
    private HierarchicalGraph? _hierarchicalGraph;

    // Includes the four diagonals, not just the four orthogonal neighbors: a coarse search restricted to
    // orthogonal tile adjacency forces a "staircase" route across any genuinely diagonal trip, which can
    // overshoot the true shortest distance by a wide margin - confirmed empirically (a corner-to-corner
    // route on an open bake came out roughly 12% longer with only orthogonal edges available).
    private static readonly (int Dx, int Dy)[] s_tileNeighborOffsets =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>Builds (or returns the already-built) hierarchical tile graph for the navmesh as it
    /// currently stands.</summary>
    private HierarchicalGraph EnsureHierarchicalGraph(DtNavMeshQuery query, IDtQueryFilter filter)
    {
        if (_hierarchicalGraph != null) return _hierarchicalGraph;

        var graph = new HierarchicalGraph();

        for (int i = 0; i < _navMesh.GetMaxTiles(); i++)
        {
            DtMeshTile? tile = _navMesh.GetTile(i);
            DtMeshData? data = tile?.data;
            if (data?.header == null || data.header.polyCount == 0) continue;

            (int X, int Y) coord = (data.header.x, data.header.y);
            if (graph.Representative.ContainsKey(coord)) continue; // a multi-layer tile - first layer wins

            long polyRef = _navMesh.GetPolyRefBase(tile!) | 0u;
            graph.Representative[coord] = PolygonCenter(polyRef);
        }

        foreach (KeyValuePair<(int X, int Y), Float3> entry in graph.Representative)
        {
            foreach ((int dx, int dy) in s_tileNeighborOffsets)
            {
                (int X, int Y) neighborCoord = (entry.Key.X + dx, entry.Key.Y + dy);
                if (!graph.Representative.TryGetValue(neighborCoord, out Float3 neighborPoint)) continue;

                NavMeshPath probe = FindPathCoreWithFilter(query, entry.Value, neighborPoint, filter);
                if (!probe.Success || probe.Partial) continue;

                float cost = 0f;
                for (int c = 1; c < probe.Corners.Length; c++)
                    cost += Float3.Distance(probe.Corners[c - 1], probe.Corners[c]);

                if (!graph.Adjacency.TryGetValue(entry.Key, out List<(int X, int Y)>? list))
                    graph.Adjacency[entry.Key] = list = [];
                list.Add(neighborCoord);
                graph.EdgeCost[(entry.Key, neighborCoord)] = cost;
            }
        }

        _hierarchicalGraph = graph;
        return graph;
    }

    /// <summary>Coarse A* over <paramref name="graph"/>'s own tile nodes, from <paramref name="start"/>
    /// to <paramref name="goal"/> - straight-line distance between representative points as the
    /// heuristic, precomputed probe-path length (see <see cref="EnsureHierarchicalGraph"/>) as edge cost.
    /// Null if no route exists at the tile level at all.</summary>
    private static List<(int X, int Y)>? CoarseTileAStar(HierarchicalGraph graph, (int X, int Y) start, (int X, int Y) goal)
    {
        if (!graph.Representative.TryGetValue(goal, out Float3 goalPoint)) return null;

        var open = new PriorityQueue<(int X, int Y), float>();
        var cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var gScore = new Dictionary<(int X, int Y), float> { [start] = 0f };
        var closed = new HashSet<(int X, int Y)>();

        open.Enqueue(start, Float3.Distance(graph.Representative[start], goalPoint));

        while (open.Count > 0)
        {
            (int X, int Y) current = open.Dequeue();
            if (current == goal)
            {
                var path = new List<(int X, int Y)> { current };
                while (cameFrom.TryGetValue(current, out (int X, int Y) previous))
                {
                    path.Add(previous);
                    current = previous;
                }
                path.Reverse();
                return path;
            }
            if (!closed.Add(current)) continue;

            if (!graph.Adjacency.TryGetValue(current, out List<(int X, int Y)>? neighbors)) continue;
            foreach ((int X, int Y) neighbor in neighbors)
            {
                float tentative = gScore[current] + graph.EdgeCost[(current, neighbor)];
                if (gScore.TryGetValue(neighbor, out float existing) && tentative >= existing) continue;

                gScore[neighbor] = tentative;
                cameFrom[neighbor] = current;
                open.Enqueue(neighbor, tentative + Float3.Distance(graph.Representative[neighbor], goalPoint));
            }
        }
        return null;
    }

    /// <summary>Runs the actual hierarchical strategy: a coarse tile-level route from <paramref name="startTile"/>
    /// to <paramref name="endTile"/>, then one ordinary Detour search from <paramref name="start"/> to
    /// <paramref name="end"/> with its own polygon visitation restricted to that route's tiles (padded
    /// with each one's own immediate neighbors, so a portal polygon just outside the strict corridor
    /// isn't wrongly excluded). Null on any failure - no coarse route, or the restricted search itself
    /// coming up empty - so the caller falls back to an unrestricted search rather than reporting no path
    /// exists when one genuinely does.</summary>
    private NavMeshPath? TryFindPathHierarchical(
        DtNavMeshQuery query, Float3 start, Float3 end, IDtQueryFilter filter, (int X, int Y) startTile, (int X, int Y) endTile)
    {
        HierarchicalGraph graph = EnsureHierarchicalGraph(query, filter);
        if (!graph.Representative.ContainsKey(startTile) || !graph.Representative.ContainsKey(endTile)) return null;

        List<(int X, int Y)>? tilePath = CoarseTileAStar(graph, startTile, endTile);
        if (tilePath == null || tilePath.Count == 0) return null;

        var allowedTiles = new HashSet<(int X, int Y)>(tilePath);
        foreach ((int X, int Y) tile in tilePath)
            if (graph.Adjacency.TryGetValue(tile, out List<(int X, int Y)>? neighbors))
                foreach ((int X, int Y) neighbor in neighbors)
                    allowedTiles.Add(neighbor);

        var restrictedFilter = new NavMeshTileRestrictedFilter(filter, allowedTiles);
        NavMeshPath result = FindPathCoreWithFilter(query, start, end, restrictedFilter);
        return result.Success ? result : null;
    }

    /// <summary>Takes one query engine from <see cref="_queryPool"/>, allocating a fresh one bound to
    /// <see cref="_navMesh"/> if the pool is currently empty (every existing instance is out on loan).</summary>
    private DtNavMeshQuery RentDetourQuery() => _queryPool.TryTake(out DtNavMeshQuery? query) ? query : new DtNavMeshQuery(_navMesh);

    /// <summary>Returns a query engine taken via <see cref="RentDetourQuery"/> so a later call can reuse
    /// it instead of allocating another.</summary>
    private void ReturnDetourQuery(DtNavMeshQuery query) => _queryPool.Add(query);

    /// <summary>Pumps this query's <c>DtTileCache</c> obstacle queue by a small, bounded budget - the
    /// mechanism a runtime <see cref="NavMeshObstacle"/> carve actually gets applied through. Called
    /// once per fixed tick by <see cref="NavMeshSystem.TickCrowds"/>; a no-op when nothing is pending.
    /// Takes this query's write lock for the duration - see this class's own doc comment - since this is
    /// exactly the "rebuild a carved tile" structural mutation concurrent queries need excluded from.</summary>
    internal void TickTileCache()
    {
        _lock.EnterWriteLock();
        try
        {
            bool changed = _tileCache.Update(TileCacheUpdateBudget);
            if (changed)
            {
                _flowFieldCache.Clear();
                _hierarchicalGraph = null;
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Queues a box obstacle for <see cref="NavMeshObstacle"/> - carved into whichever tile(s)
    /// it overlaps the next few times <see cref="TickTileCache"/> runs, not synchronously. The returned
    /// reference identifies it for a later <see cref="RemoveObstacle"/>; 0 if the tile cache has no more
    /// room for another obstacle (<see cref="NavMeshTileCacheData.MaxObstacles"/>).</summary>
    internal long AddBoxObstacle(Float3 min, Float3 max) => _tileCache.AddBoxObstacle(ToRc(min), ToRc(max));

    /// <summary>Queues a cylinder obstacle for <see cref="NavMeshObstacle"/>'s <see cref="NavMeshObstacleShape.Capsule"/>
    /// shape - same queuing/budget behavior as <see cref="AddBoxObstacle"/>, just <c>DtTileCache</c>'s
    /// other carvable primitive. <paramref name="basePos"/> is the cylinder's base (bottom-center), not
    /// its middle - <c>DtTileCache</c> extrudes it upward by <paramref name="height"/> from there.</summary>
    internal long AddCylinderObstacle(Float3 basePos, float radius, float height) =>
        _tileCache.AddObstacle(ToRc(basePos), radius, height);

    /// <summary>Queues removal of a previously added obstacle - see <see cref="AddBoxObstacle"/>.</summary>
    internal void RemoveObstacle(long obstacleRef) => _tileCache.RemoveObstacle(obstacleRef);

    /// <summary>Replaces every tile at grid coordinate (<paramref name="tileX"/>, <paramref name="tileY"/>)
    /// with <paramref name="compressedLayers"/> - <see cref="NavMeshSurface.RebuildTiles(Float3, Float3)"/>'s
    /// own primitive. Unlike <see cref="AddBoxObstacle"/>/<see cref="RemoveObstacle"/>, this applies
    /// synchronously (no <see cref="TickTileCache"/> budget involved): replacing a tile's own baked
    /// content is a structural change to the tile cache, not an obstacle queued against an unchanged
    /// tile, so there is no equivalent "spread the cost over a few frames" concern to budget for here.
    /// Takes this query's write lock for the duration - see this class's own doc comment.</summary>
    internal void ReplaceTile(int tileX, int tileY, IReadOnlyList<byte[]> compressedLayers)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (long tileRef in _tileCache.GetTilesAt(tileX, tileY))
                _tileCache.RemoveTile(tileRef);

            foreach (byte[] data in compressedLayers)
            {
                long tileRef = _tileCache.AddTile(data, 0);
                if (tileRef != 0) _tileCache.BuildNavMeshTile(tileRef);
            }

            _flowFieldCache.Clear();
            _hierarchicalGraph = null;
        }
        finally { _lock.ExitWriteLock(); }
    }

    // Deriving a tile index from a world position by floor-dividing against Origin/TileWorldSize looks
    // equivalent to this, but is not guaranteed to be: Recast's own tile indexing during the bake is
    // assigned in voxel-cell space, and Origin/TileWorldSize are each independently rounded values
    // derived from it, so the two can drift by a tile at a boundary. Iterating the stored layers and
    // testing each one's own already-authoritative (TileX, TileY) against the region - the same way
    // NavMeshSurface.ApplyRebuildTiles already does - has no such risk: a layer's world bounds are exact
    // multiplication against the same index the bake itself assigned it, not a re-derived guess at it.
    /// <summary>Every distinct tile grid coordinate stored in this navmesh's own baked layers whose world
    /// bounds overlap [<paramref name="min"/>, <paramref name="max"/>] - shared by
    /// <see cref="LoadTilesInRegion"/>/<see cref="UnloadTilesInRegion"/>.</summary>
    private IEnumerable<(int X, int Y)> TileCoordsOverlapping(Float3 min, Float3 max)
    {
        var seen = new HashSet<(int X, int Y)>();
        foreach (NavMeshTileLayer layer in _tileCacheData.Layers)
        {
            (int X, int Y) coord = (layer.TileX, layer.TileY);
            if (!seen.Add(coord)) continue;

            float tileMinX = _tileCacheData.Origin.X + layer.TileX * _tileCacheData.TileWorldSize;
            float tileMinZ = _tileCacheData.Origin.Z + layer.TileY * _tileCacheData.TileWorldSize;
            float tileMaxX = tileMinX + _tileCacheData.TileWorldSize;
            float tileMaxZ = tileMinZ + _tileCacheData.TileWorldSize;

            if (tileMinX < max.X && tileMaxX > min.X && tileMinZ < max.Z && tileMaxZ > min.Z)
                yield return coord;
        }
    }

    /// <summary>Removes every currently-live tile overlapping [<paramref name="min"/>, <paramref name="max"/>]
    /// from the navmesh at runtime, without discarding it from the baked asset's own stored layers -
    /// <see cref="LoadTilesInRegion"/> is what brings one back. An agent whose own polygon was on an
    /// unloaded tile simply stops resolving a path across it (<see cref="FindPath"/> reports no route,
    /// same as any other genuinely unreachable destination) rather than anything throwing; its crowd slot
    /// is entirely untouched, so it keeps whatever position it last had and is free to path again the
    /// moment the tile comes back. Streaming a large world's navmesh in and out with the rest of its
    /// content, independent of whether anything has actually changed there (unlike <see cref="ReplaceTile"/>,
    /// which always rebuilds from fresh geometry) is what this is for.</summary>
    internal void UnloadTilesInRegion(Float3 min, Float3 max)
    {
        _lock.EnterWriteLock();
        try
        {
            bool changed = false;
            foreach ((int x, int y) in TileCoordsOverlapping(min, max))
            {
                foreach (long tileRef in _tileCache.GetTilesAt(x, y))
                {
                    DtCompressedTile compressed = _tileCache.GetTileByRef(tileRef);
                    long liveRef = _navMesh.GetTileRefAt(compressed.header.tx, compressed.header.ty, compressed.header.tlayer);
                    if (liveRef != 0) _navMesh.RemoveTile(liveRef);
                    _tileCache.RemoveTile(tileRef);
                    changed = true;
                }
            }

            if (changed)
            {
                _flowFieldCache.Clear();
                _hierarchicalGraph = null;
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Re-adds every tile overlapping [<paramref name="min"/>, <paramref name="max"/>] that
    /// <see cref="UnloadTilesInRegion"/> previously removed, straight from the baked asset's own stored
    /// layers - no rebake, no scene geometry re-collected, since nothing about the tile's own content
    /// needs to change to bring it back. A tile already live in the region is left alone.</summary>
    internal void LoadTilesInRegion(Float3 min, Float3 max)
    {
        _lock.EnterWriteLock();
        try
        {
            bool changed = false;
            foreach ((int x, int y) in TileCoordsOverlapping(min, max))
            {
                if (_tileCache.GetTilesAt(x, y).Count > 0) continue; // already live

                foreach (NavMeshTileLayer layer in _tileCacheData.Layers)
                {
                    if (layer.TileX != x || layer.TileY != y) continue;
                    long tileRef = _tileCache.AddTile(layer.CompressedData, 0);
                    if (tileRef != 0) { _tileCache.BuildNavMeshTile(tileRef); changed = true; }
                }
            }

            if (changed)
            {
                _flowFieldCache.Clear();
                _hierarchicalGraph = null;
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Draws every live tile's polygon edges in <paramref name="color"/> - for
    /// <see cref="NavMeshSurface"/>'s own gizmo, reading the current, possibly obstacle-carved navmesh
    /// directly rather than a static baked mesh, since there is no longer one of those to read instead.</summary>
    internal void DrawWalkablePolygons(Color color)
    {
        _lock.EnterReadLock();
        try
        {
            for (int i = 0; i < _navMesh.GetMaxTiles(); i++)
            {
                DtMeshTile? tile = _navMesh.GetTile(i);
                DtMeshData? data = tile?.data;
                if (data?.header == null) continue;

                for (int p = 0; p < data.header.polyCount; p++)
                {
                    DtPoly poly = data.polys[p];
                    for (int e = 0; e < poly.vertCount; e++)
                    {
                        Float3 a = VertexAt(data.verts, poly.verts[e]);
                        Float3 b = VertexAt(data.verts, poly.verts[(e + 1) % poly.vertCount]);
                        Debug.DrawLine(a, b, color);
                    }
                }
            }
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>The world-space position of vertex <paramref name="index"/> in a tile's flat vertex array.</summary>
    private static Float3 VertexAt(float[] verts, int index) =>
        new(verts[index * 3], verts[index * 3 + 1], verts[index * 3 + 2]);

    /// <summary>Raw nearest-polygon lookup every other query method snaps its input points through -
    /// against <paramref name="query"/> specifically (a rented instance), never a shared one, so two
    /// threads calling this concurrently never touch the same Detour query engine.</summary>
    private bool TryFindNearestPoly(DtNavMeshQuery query, Float3 point, IDtQueryFilter filter, out long polyRef, out RcVec3f result)
    {
        DtStatus status = query.FindNearestPoly(ToRc(point), _searchExtents, filter, out polyRef, out result, out _);
        return status.Succeeded() && polyRef != 0;
    }

    /// <summary>Reconstructs a persistent, tiled Detour navmesh from <paramref name="data"/>'s stored
    /// compressed tile layers - decompressing and turning each one into a live navmesh tile via the same
    /// <c>DtTileCache</c> a runtime <see cref="NavMeshObstacle"/> carve later rebuilds tiles through, so
    /// there is exactly one code path (<see cref="NavMeshTileCacheMeshProcess"/>) that ever turns a tile
    /// layer into actual polygons, whether that happens here or from a later obstacle change. Internal:
    /// also <see cref="NavMeshLinkGenerator"/>'s own entry point for the same reconstruction, needed
    /// purely to scan a freshly-baked mesh's boundary edges before any link (generated or otherwise) can
    /// exist yet.</summary>
    internal static (DtNavMesh NavMesh, DtTileCache TileCache) BuildTiledNavMesh(NavMeshTileCacheData data, IReadOnlyList<NavMeshLinkData> links)
    {
        var navMeshParams = new DtNavMeshParams
        {
            orig = ToRc(data.Origin),
            tileWidth = data.TileWorldSize,
            tileHeight = data.TileWorldSize,
            maxTiles = data.MaxTiles,
            maxPolys = data.MaxPolys,
        };

        var navMesh = new DtNavMesh();
        DtStatus initStatus = navMesh.Init(ref navMeshParams, RecastNavMeshBuilder.MaxVertsPerPoly);
        if (!initStatus.Succeeded())
            throw new InvalidOperationException($"Failed to initialize the tiled Detour navmesh: {initStatus}");

        var tileCacheParams = new DtTileCacheParams
        {
            orig = ToRc(data.Origin),
            cs = data.CellSize,
            ch = data.CellHeight,
            width = data.TileSizeInCells,
            height = data.TileSizeInCells,
            walkableHeight = data.WalkableHeight,
            walkableRadius = data.WalkableRadius,
            walkableClimb = data.WalkableClimb,
            maxSimplificationError = data.MaxSimplificationError,
            maxTiles = data.MaxTiles,
            maxObstacles = data.MaxObstacles,
        };

        var meshProcess = new NavMeshTileCacheMeshProcess(links);
        var tileCache = new DtTileCache(ref tileCacheParams, RecastNavMeshBuilder.StorageParams, navMesh, RecastNavMeshBuilder.Compressor, meshProcess);

        foreach (NavMeshTileLayer layer in data.Layers)
        {
            long tileRef = tileCache.AddTile(layer.CompressedData, 0);
            if (tileRef != 0) tileCache.BuildNavMeshTile(tileRef);
        }

        return (navMesh, tileCache);
    }

    /// <summary>Converts an engine-space vector to Detour's own vector type.</summary>
    private static RcVec3f ToRc(Float3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Converts a Detour vector back to engine space.</summary>
    private static Float3 ToFloat3(RcVec3f v) => new(v.X, v.Y, v.Z);
}
