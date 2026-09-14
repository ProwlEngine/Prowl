// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Bakes and registers a navmesh for one agent type. The baked result is a standalone
/// <see cref="NavMeshData"/> asset, which the surface registers with the scene's
/// <see cref="NavMeshWorld"/> on enable. Rebuilds run synchronously, in the background
/// (<see cref="BuildNavMeshAsync"/>), or per-tile for localized geometry changes
/// (<see cref="RebuildTiles"/>).
/// <para/>
/// Registration is the whole of this component's lifecycle — there is no per-frame work — and it
/// runs in the editor as well as in play: obstacles can only carve a live navmesh and the overlay
/// draws one, so without it a scene's baked navmesh stays unregistered until something bakes again.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Navigation/NavMesh Surface")]
[ComponentIcon("\uf279")] // map icon
public class NavMeshSurface : MonoBehaviour
{
    [Header("Bake")]
    [Tooltip("The agent type this navmesh is built for (radius, height, slope, climb come from the project's agent table). Agents only use navmeshes of their own type. One surface per agent type per scene.")]
    [InspectorName("Agent Type")]
    [SerializeField] private NavMeshAgentTypeId agentTypeId = NavMeshAgentTypes.Humanoid;

    [Tooltip("Surface-level rasterization settings (voxel/tile sizes and Recast detail). Most bakes never need to change these.")]
    [HideInInspector] // drawn inside the editor's Advanced foldout
    [SerializeField] private NavMeshBuildOverrides buildOverrides = new();

    /// <summary>The resolved bake input: the agent type's envelope composed with this
    /// surface's <see cref="BuildOverrides"/>. What gets handed to
    /// <see cref="NavMeshBuilder.Build"/>; a fresh snapshot each call.</summary>
    public NavMeshBuildSettings ResolveBuildSettings()
        => NavMeshAgentTypes.GetBuildSettings(AgentTypeId, BuildOverrides);

    [Tooltip("Which objects contribute bake geometry. NavMeshAgents and their children never contribute — agents walk the mesh rather than forming it.")]
    [SerializeField] private NavMeshCollectObjects collectObjects = NavMeshCollectObjects.All;

    [Tooltip("Volume center (local to this GameObject) when CollectObjects is Volume.")]
    [ShowIf(nameof(IsVolumeMode))]
    [SerializeField] private Float3 center;

    [Tooltip("Volume size when CollectObjects is Volume.")]
    [ShowIf(nameof(IsVolumeMode))]
    [SerializeField] private Float3 size = new(10, 10, 10);

    [Tooltip("Only objects on these layers contribute bake geometry.")]
    [SerializeField] private LayerMask layers = LayerMask.Everything;

    [Tooltip("Voxelize render meshes or physics colliders.")]
    [SerializeField] private NavMeshCollectGeometry useGeometry = NavMeshCollectGeometry.RenderMeshes;

    [Tooltip("Area applied to all walkable geometry in this bake.")]
    [HideInInspector] // drawn inside the editor's Advanced foldout
    [SerializeField] private NavMeshArea defaultArea = NavMeshAreas.Walkable;

    [Tooltip("The baked navmesh. Assigned by baking, or point it at an existing .navmesh asset.")]
    // A field, not a property, like MeshRenderer.Mesh: AssetRef<T> caches its resolved instance as
    // a side effect of .Res, and a property hands out a copy — so every read would resolve from the
    // database again and the async-load dedup the cache drives would never engage.
    public AssetRef<NavMeshData> NavMeshData;

    private NavMeshInstance? _instance;
    private Runtime.NavMeshData? _runtimeData;
    private bool IsVolumeMode => CollectObjects == NavMeshCollectObjects.Volume;

    /// <summary>The live navmesh registration, while enabled and a navmesh is loaded.</summary>
    public NavMeshInstance? Instance => _instance;

    /// <summary>
    /// What the live navmesh was built from, and what rebuilds rewrite. Null while unregistered.
    /// For a <c>.navmesh</c> asset this is a private copy made at registration, because the object
    /// the database hands out is shared by every surface pointing at it and by the next scene that
    /// loads it. For a navmesh built at runtime and handed over through
    /// <see cref="ApplyNavMeshData"/> it is that object itself — nothing else owns it.
    /// </summary>
    public Runtime.NavMeshData? RuntimeData => _runtimeData;

    public NavMeshAgentTypeId AgentTypeId { get => agentTypeId; set => agentTypeId = value; }
    public NavMeshBuildOverrides BuildOverrides { get => buildOverrides; set => buildOverrides = value; }
    public NavMeshCollectObjects CollectObjects { get => collectObjects; set => collectObjects = value; }
    public Float3 Center { get => center; set => center = value; }
    public Float3 Size { get => size; set => size = value; }
    public LayerMask Layers { get => layers; set => layers = value; }
    public NavMeshCollectGeometry UseGeometry { get => useGeometry; set => useGeometry = value; }
    public NavMeshArea DefaultArea { get => defaultArea; set => defaultArea = value; }

    /// <summary>The scene's navigation world, or null when not in a scene.</summary>
    private NavMeshWorld? World
    {
        get
        {
            Scene? scene = Scene;
            return scene.IsValid() ? scene.Navigation : null;
        }
    }

    public override void OnEnable()
    {
        World?.RegisterSurface(this);
        Register();
    }

    public override void OnDisable()
    {
        World?.UnregisterSurface(this);
        Unregister(handOver: true);
        _overlay?.Release();
    }

    private void Register()
    {
        if (_instance != null) return;
        NavMeshWorld? world = World;
        if (world == null) return;

        // The navmesh has to be present now: registration happens once on enable and nothing
        // retries it — a transient null from async streaming would leave the scene permanently
        // without one. Block-load it, as the mesh and terrain colliders do for the same reason.
        NavMeshData.EnsureLoaded();

        Runtime.NavMeshData? data = NavMeshData.Res;
        if (data.IsNotValid() || !data!.HasTiles) return;

        // Copy only what the asset database owns. A .navmesh asset is shared with every other
        // surface pointing at it and with the next scene that loads it, so runtime tile and link
        // rewrites must not land on it. One built at runtime and handed over through
        // ApplyNavMeshData has no other owner — copying it would just cost a list per
        // registration and throw away every rebuild since the original bake on re-registering.
        // (Two surfaces of DIFFERENT types handed the same runtime data still share it.)
        _runtimeData = NavMeshData.AssetID == Guid.Empty ? data : data.Clone();
        _instance = world.AddNavMeshData(_runtimeData);
        if (_instance == null)
        {
            _runtimeData = null;
            return;
        }

        world.QueueLinkReconcile(this);
    }

    /// <param name="handOver">Let a spare surface of the type take the navmesh over. Off while this
    /// surface re-registers, or the spare claims the type before this one can take it back.</param>
    private void Unregister(bool handOver)
    {
        if (_instance == null) return;
        World?.RemoveNavMeshData(_instance, handOver);
        _instance = null;
        _runtimeData = null;
    }

    #region Building

    /// <summary>
    /// Collect geometry and bake the navmesh synchronously, then (re)register it with the
    /// scene. Blocks the calling thread for the duration of the bake — prefer
    /// <see cref="BuildNavMeshAsync"/> during gameplay.
    /// </summary>
    public bool BuildNavMesh()
    {
        Runtime.NavMeshData? data = BuildNavMeshData();
        if (data == null) return false;

        ApplyNavMeshData(data);
        return true;
    }

    /// <summary>
    /// Collect geometry and bake synchronously, returning the result without registering it.
    /// For callers that persist the bake first and register the saved asset instead, so the
    /// tiles are meshed once rather than once per registration.
    /// </summary>
    public Runtime.NavMeshData? BuildNavMeshData()
    {
        Runtime.NavMeshData? data = PrepareBake()(default);
        if (data == null)
            Debug.LogWarning($"[Navigation] Bake of '{GameObject.Name}' produced no walkable geometry.");

        return data;
    }

    /// <summary>In Volume mode the volume is an explicit statement of the bake's extent, so
    /// the tile grid spans it even where no geometry exists yet (rooms opening up later can be
    /// added via <see cref="RebuildTiles(AABB, IReadOnlyList{NavMeshGeometrySource})"/>).</summary>
    private AABB? ExplicitWorldBounds()
        => CollectObjects == NavMeshCollectObjects.Volume ? VolumeBounds : null;

    /// <summary>World-space extent of the Volume-mode box.</summary>
    private AABB VolumeBounds => AABB.FromCenterAndSize(Transform.TransformPoint(Center), Size);

    /// <summary>
    /// Bake in the background: geometry is collected on the calling (main) thread, the
    /// voxelization runs on the thread pool. Apply the result with
    /// <see cref="ApplyNavMeshData"/> from the main thread when the task completes.
    /// </summary>
    public Task<NavMeshData?> BuildNavMeshAsync(CancellationToken cancellation = default)
    {
        Func<CancellationToken, NavMeshData?> bake = PrepareBake();
        return Task.Run(() => bake(cancellation), cancellation);
    }

    /// <summary>Collect everything a bake needs on the calling (main) thread, since collection touches
    /// Transforms, and return the self-contained build.</summary>
    private Func<CancellationToken, NavMeshData?> PrepareBake()
    {
        NavMeshBuildSettings settings = ResolveBuildSettings(); // one resolve per bake: collection and build must agree
        List<NavMeshGeometrySource> sources = CollectSources(null, settings.EffectiveVoxelSize);
        List<NavMeshAreaVolume> volumes = CollectVolumes();
        List<NavMeshLinkSource> links = CollectLinks();
        int defaultArea = DefaultArea;
        AABB? worldBounds = ExplicitWorldBounds();
        int threads = Math.Max(1, Environment.ProcessorCount - 1);
        return cancellation => NavMeshBuilder.Build(settings, sources, defaultArea, threads, cancellation, worldBounds, volumes, links);
    }

    /// <summary>
    /// Swap in a freshly built navmesh: replaces this surface's data (as a runtime resource)
    /// and its registration in the scene. Main thread only.
    /// </summary>
    public void ApplyNavMeshData(NavMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        NavMeshData = data;
        RefreshRegistration();
    }

    /// <summary>Re-register the currently assigned <see cref="NavMeshData"/> (e.g. after the
    /// asset reference was swapped by an editor bake).</summary>
    public void RefreshRegistration()
    {
        NavMeshAgentTypeId? previousType = _instance?.AgentTypeId;
        Unregister(handOver: false);
        Register();

        // This surface no longer provides the type it held (its data was cleared, has no tiles, or
        // was baked for another type), so a spare takes it over as it would on a disable.
        if (previousType is NavMeshAgentTypeId type) World?.RegisterSpareSurface(type);
    }

    /// <summary>
    /// Rebuild only the tiles intersecting <paramref name="worldBounds"/> against the current
    /// scene geometry (via this surface's collectors) and swap them into the live navmesh —
    /// cost scales with the changed volume, not the map size. The tile grid stays anchored to
    /// the original bake, so geometry outside the original bounds needs a full
    /// <see cref="BuildNavMesh"/>. Requires an enabled surface with a registered navmesh.
    /// </summary>
    public bool RebuildTiles(AABB worldBounds)
    {
        Runtime.NavMeshData? data = _runtimeData;
        if (data.IsNotValid()) return false;
        // Collection (terrain decimation) uses the BAKED voxel size, same as the tiles being
        // rebuilt — the current agent table may disagree with the bake this grid came from.
        AABB? collectionBounds = RebuildCollectionBounds(worldBounds);
        return RebuildTiles(worldBounds, CollectSources(collectionBounds, data!.Settings.EffectiveVoxelSize));
    }

    /// <summary>
    /// Replace the live link set and re-contour the tiles overlapping <paramref name="worldBounds"/>.
    /// No re-voxelization: the compressed layers are untouched, which is what makes this cheap next
    /// to <see cref="RebuildTiles(AABB)"/>. False when this surface has no live navmesh.
    /// </summary>
    /// <param name="worldBounds">Regions whose tiles pick up the change, normally around link endpoints. A tile several of them cover
    /// re-contours once, which is the point of handing a frame's link edits over together.</param>
    /// <param name="links">The surface's complete link set. Null collects the scene's
    /// <see cref="NavMeshLink"/>s.</param>
    public bool RebuildLinkTiles(ReadOnlySpan<AABB> worldBounds, IReadOnlyList<NavMeshLinkSource>? links = null)
    {
        NavMeshInstance? instance = Instance;
        Runtime.NavMeshData? data = _runtimeData;
        if (instance == null || data.IsNotValid()) return false;
        if (data!.TileWorldSize <= 0) return false;

        links ??= CollectLinks();
        NavMeshWorld? world = World;
        if (world == null) return false;

        // Resolved before the write lock is taken, so worker-thread queries do not block on it.
        HashSet<(int X, int Z)> tiles = [];
        foreach (AABB bounds in worldBounds)
            if (data.TryGetTileRange(bounds, out int tx0, out int tx1, out int tz0, out int tz1))
                for (int tz = tz0; tz <= tz1; tz++)
                    for (int tx = tx0; tx <= tx1; tx++)
                        tiles.Add((tx, tz));

        bool applied = world.MutateTileCache(instance, cache =>
        {
            // Always replace the link set, even with no tiles in range: it is what tiles rebuilt
            // later — by a carve, or by a rebuild of a neighbouring region — will be built from.
            instance.TileCacheLinks.SetLinks(links, data.Settings.Agent.Radius);

            foreach ((int tx, int tz) in tiles)
                foreach (long tileRef in cache.GetTilesAt(tx, tz))
                    cache.BuildNavMeshTile(tileRef);
        });

        // Unregistered between the check above and here, so nothing ran: the runtime copy has to
        // keep the link set the live mesh was actually built from.
        if (!applied) return false;

        // Mirror onto the runtime copy, so a rebuild that re-instantiates it starts from the
        // link set the live mesh is using. The .navmesh asset is left alone: a link moving is a
        // scene edit, and the baked artifact answers for it at the next bake.
        data.Links = [.. links];
        return true;
    }

    /// <summary>
    /// Dirty the tiles of every link the registered navmesh disagrees with the scene about. A link
    /// catches itself up when it is missing, but one that was disabled, deactivated, deleted or
    /// rescoped since the bake never enables to take itself back out, and one edited while this
    /// surface was unregistered is already present under the same id. Both are found here by
    /// comparing definitions, and the drain rebuilds their tiles from the live set.
    /// </summary>
    internal void MarkStaleBakedLinks()
    {
        NavMeshWorld? world = World;
        Runtime.NavMeshData? data = _runtimeData;
        if (world == null || _instance == null || data.IsNotValid()) return;

        List<NavMeshLinkSource> live = CollectLinks();

        foreach (NavMeshLinkSource baked in data!.Links)
            if (!live.Exists(link => SameLink(link, baked)))
                world.MarkLinkEndpointsDirty(this, baked.Start, baked.End, baked.Width);

        foreach (NavMeshLinkSource link in live)
            if (!data.Links.Exists(baked => SameLink(link, baked)))
                world.MarkLinkEndpointsDirty(this, link.Start, link.End, link.Width);
    }

    private static bool SameLink(NavMeshLinkSource live, NavMeshLinkSource baked)
    {
        const float Tolerance = 1e-4f;
        return live.UserId == baked.UserId
            && live.Bidirectional == baked.Bidirectional
            && live.Area == baked.Area
            && MathF.Abs(live.Width - baked.Width) < Tolerance
            && Float3.Distance(live.Start, baked.Start) < Tolerance
            && Float3.Distance(live.End, baked.End) < Tolerance;
    }

    /// <summary>
    /// World rect the collectors must cover for a rebuild of <paramref name="worldBounds"/>: the
    /// affected TILES plus the erosion border, because rebuilds rasterize whole tiles. Null when
    /// there is no grid to derive it from, meaning collect everything.
    /// <para/>
    /// Public because the explicit-sources overload makes covering this rect the caller's job, and
    /// getting it wrong leaves holes rather than failing. Collectors clip terrain to the filter they
    /// are given, so the changed AABB alone is not conservative.
    /// </summary>
    public AABB? RebuildCollectionBounds(AABB worldBounds)
    {
        Runtime.NavMeshData? data = _runtimeData;
        if (data.IsNotValid() || data!.TileWorldSize <= 0) return null; // no grid: collect everything

        float ts = data.TileWorldSize;
        // No vertical limit: geometry added above or below the original bake still has to be collected,
        // and the rebuild grows the heightfield to fit it. Finite, because terrain clipping transforms this box.
        const float UnboundedHeight = 100_000f;

        // Conservative world-space erosion border (CalcBorder cells = ceil(radius/cs) + 3).
        // Derived from the live copy's snapshot settings — the grid being rebuilt is the one the
        // navmesh was baked with, not whatever the surface's current configuration says.
        float border = data.Settings.Agent.Radius + 4f * data.Settings.EffectiveVoxelSize;

        double minTx = Math.Floor((worldBounds.Min.X - border - data.Origin.X) / ts);
        double maxTx = Math.Floor((worldBounds.Max.X + border - data.Origin.X) / ts);
        double minTz = Math.Floor((worldBounds.Min.Z - border - data.Origin.Z) / ts);
        double maxTz = Math.Floor((worldBounds.Max.Z + border - data.Origin.Z) / ts);

        return new AABB(
            new Float3((float)(data.Origin.X + minTx * ts - border), -UnboundedHeight, (float)(data.Origin.Z + minTz * ts - border)),
            new Float3((float)(data.Origin.X + (maxTx + 1) * ts + border), UnboundedHeight, (float)(data.Origin.Z + (maxTz + 1) * ts + border)));
    }

    /// <summary>
    /// Rebuild the tiles intersecting <paramref name="worldBounds"/> from caller-supplied
    /// geometry, for games whose world the collectors cannot see. Pass the bounds of the CHANGED
    /// geometry; the affected tile set derives from them plus the erosion border, and
    /// <see cref="RebuildCollectionBounds"/> returns exactly the rect to collect against. Covering
    /// less is not a cheaper rebuild — partly covered tiles come back with holes. An empty source
    /// list is valid and empties the affected tiles.
    /// </summary>
    /// <param name="volumes">Area volumes applied to the rebuilt tiles. Null (the default)
    /// collects the scene's <see cref="NavMeshModifierVolume"/>s over the affected region —
    /// note that collection walks the scene's active objects, so callers who chose explicit
    /// sources to avoid scene scans should pass an empty list (no volumes, no scan) or their
    /// own list.</param>
    public bool RebuildTiles(AABB worldBounds, IReadOnlyList<NavMeshGeometrySource> sources,
        IReadOnlyList<NavMeshAreaVolume>? volumes = null)
    {
        Func<CancellationToken, List<NavMeshTileRebuild>>? rebuild = _instance != null ? PrepareRebuild(worldBounds, sources, volumes) : null;
        return rebuild != null && ApplyRebuiltTilesImmediately(rebuild(default));
    }

    /// <summary>
    /// Voxelize the affected tiles on the thread pool, off the frame. The returned tiles are
    /// NOT yet live — apply them with <see cref="ApplyRebuiltTiles"/> from the main thread.
    /// Sequencing rules: do not run two rebuilds of overlapping regions concurrently, and do
    /// not interleave with a full rebake — the build reads the asset's grid anchoring and the
    /// apply assumes it is unchanged since dispatch.
    /// </summary>
    public Task<List<NavMeshTileRebuild>> RebuildTilesAsync(
        AABB worldBounds, IReadOnlyList<NavMeshGeometrySource> sources, CancellationToken cancellation = default,
        IReadOnlyList<NavMeshAreaVolume>? volumes = null)
    {
        Func<CancellationToken, List<NavMeshTileRebuild>>? rebuild = PrepareRebuild(worldBounds, sources, volumes);
        if (rebuild == null) return Task.FromResult(new List<NavMeshTileRebuild>());
        return Task.Run(() => rebuild(cancellation), cancellation);
    }

    /// <summary>Resolve a partial rebuild on the calling (main) thread, since volume collection touches
    /// Transforms, and return the self-contained build. Null while unregistered. It reads the surface's
    /// own copy rather than the asset, because that is the grid the live mesh is on.</summary>
    private Func<CancellationToken, List<NavMeshTileRebuild>>? PrepareRebuild(AABB worldBounds,
        IReadOnlyList<NavMeshGeometrySource> sources, IReadOnlyList<NavMeshAreaVolume>? volumes)
    {
        Runtime.NavMeshData? data = _runtimeData;
        if (data.IsNotValid()) return null;

        volumes ??= CollectVolumes(RebuildCollectionBounds(worldBounds));
        int defaultArea = DefaultArea;
        return cancellation => NavMeshBuilder.BuildTilesInBounds(data!, sources, worldBounds.Min, worldBounds.Max, defaultArea, cancellation, volumes);
    }

    /// <summary>
    /// Swap rebuilt tiles (from <see cref="RebuildTilesAsync"/> or
    /// <see cref="NavMeshBuilder.BuildTilesInBounds"/>) into the live TileCache and mirror them
    /// into the asset. Main thread only.
    /// <para/>
    /// A cache with carve work in flight cannot take a swap, and draining it inline costs milliseconds
    /// per queued tile under the write lock — so the swap is held until the frame the pump reports the
    /// cache settled, usually the next one. Anything queuing tile work sets that flag, a link rebuild
    /// included. A cache that never settles (an obstacle moving every frame re-queues as fast as the
    /// pump drains) gives up after a few passes and pays the drain. Use
    /// <see cref="ApplyRebuiltTilesImmediately"/> when the tiles must be live before the call returns.
    /// <para/>
    /// False only when there is nothing to apply to; true means applied OR held. A held swap keeps
    /// the layer blobs handed to it, so do not recycle them until it lands.
    /// </summary>
    public bool ApplyRebuiltTiles(IReadOnlyList<NavMeshTileRebuild> rebuilt)
    {
        ArgumentNullException.ThrowIfNull(rebuilt);
        NavMeshWorld? world = World;
        NavMeshInstance? instance = _instance;
        if (world == null || instance == null || _runtimeData.IsNotValid() || rebuilt.Count == 0)
            return false;

        if (!instance.CachePending)
            return ApplyRebuiltTilesImmediately(rebuilt);

        DeferSwap(world, instance, rebuilt);
        return true;
    }

    private void DeferSwap(NavMeshWorld world, NavMeshInstance instance, IReadOnlyList<NavMeshTileRebuild> rebuilt)
    {
        // The caller's list outlives the call now, and a destructible world is exactly the sort of
        // caller that reuses one. Copying the entries is enough; the blobs inside are read-only.
        List<NavMeshTileRebuild> held = [.. rebuilt];
        world.DeferTileSwap(instance, () =>
        {
            // Re-registered since (a rebake, a disable/enable): these layers were voxelized
            // against the grid of a navmesh that is no longer the live one.
            if (ReferenceEquals(_instance, instance))
                SwapTiles(held);
        });
    }

    /// <inheritdoc cref="ApplyRebuiltTiles"/>
    /// <remarks>Applies within the call instead of waiting for a settled frame: the swap quiesces
    /// pending obstacle work, replaces each tile's layers, refreshes every obstacle's touched-tile
    /// list (stale after a tile replacement bumps its salt), then rebuilds the new tiles with
    /// carves re-applied. Returns false, leaving the tiles as they were, when the cache cannot be
    /// quiesced. Called from a handler while a deferred swap is applying, it queues behind the swaps
    /// still waiting instead, since applying first would let them revert it.</remarks>
    public bool ApplyRebuiltTilesImmediately(IReadOnlyList<NavMeshTileRebuild> rebuilt)
    {
        ArgumentNullException.ThrowIfNull(rebuilt);
        NavMeshWorld? world = World;
        if (world == null || _instance == null || _runtimeData.IsNotValid() || rebuilt.Count == 0)
            return false;

        if (world.IsApplyingDeferredSwap)
        {
            DeferSwap(world, _instance, rebuilt);
            return true;
        }

        // Anything already held for this navmesh has to land first: it was issued earlier, and
        // applying it afterwards would revert the tiles this call is about to write.
        world.FlushDeferredTileSwaps(_instance);
        return SwapTiles(rebuilt);
    }

    private bool SwapTiles(IReadOnlyList<NavMeshTileRebuild> rebuilt)
    {
        NavMeshWorld? world = World;
        Runtime.NavMeshData? data = _runtimeData;
        if (world == null || _instance == null || data.IsNotValid())
            return false;

        bool applied = false;
        world.MutateTileCache(_instance, cache =>
        {
            // Quiesce: every obstacle settles and no pending rebuild references the tiles being
            // replaced. Unbounded slices, unlike the per-frame pump's budget — this runs inline
            // on the main thread and cannot proceed unquiesced, so paying the whole queue here
            // beats abandoning the caller's rebuild. A request enqueues its tiles only once the
            // previous batch drains, so a few passes always suffice.
            bool converged = false;
            for (int i = 0; i < 8 && !converged; i++)
                converged = cache.Update(int.MaxValue);
            if (!converged)
            {
                // Replacing tiles an obstacle is still mid-carve on desyncs its pending list.
                Debug.LogWarning("[Navigation] ApplyRebuiltTiles: the tile cache would not settle; the tile swap was skipped and the navmesh keeps its current tiles. The pump keeps draining, so a later rebuild can succeed.");
                return;
            }

            var addedRefs = new List<long>();
            foreach ((int x, int z, IReadOnlyList<byte[]> blobs) in rebuilt)
            {
                foreach (long tileRef in cache.GetTilesAt(x, z))
                    cache.RemoveTile(tileRef);

                foreach (byte[] blob in blobs)
                {
                    if (!cache.TryAddTile(blob, 0, out long added))
                        Debug.LogWarning($"[Navigation] ApplyRebuiltTiles: the tile pool is full; a layer for tile ({x}, {z}) was dropped. Raise the agent type's tile capacity.");
                    else if (added == 0)
                        Debug.LogWarning($"[Navigation] ApplyRebuiltTiles: layer for tile ({x}, {z}) collided with an existing layer slot and was skipped.");
                    else
                        addedRefs.Add(added);
                }
            }

            cache.RefreshObstacleTouchedTiles();

            foreach (long added in addedRefs)
                cache.BuildNavMeshTile(added); // re-contours with carves applied via the refreshed lists

            applied = true;
        });

        if (!applied) return false;

        // Mirror the swap into this surface's runtime copy so a later re-instantiation agrees
        // with the live mesh; the .navmesh asset on disk is not touched. Obstacles are runtime
        // state and never serialize, so the copy holds clean regenerated layers. Single pass
        // over the tile list: RemoveAll-per-tile would be O(total x rebuilt).
        var replaced = new HashSet<(int, int)>(rebuilt.Count);
        foreach ((int x, int z, _) in rebuilt)
            replaced.Add((x, z));
        data!.CacheLayers.RemoveAll(t => replaced.Contains((t.X, t.Z)));
        foreach ((int x, int z, IReadOnlyList<byte[]> blobs) in rebuilt)
            foreach (byte[] blob in blobs)
                data.CacheLayers.Add(new Runtime.NavMeshData.NavMeshTile { X = x, Z = z, Data = blob });

        return true;
    }

    /// <summary>
    /// Collect this surface's bake geometry from the scene (main thread), optionally restricted to
    /// objects whose bounds intersect <paramref name="filterBounds"/>, a conservative test against
    /// transformed local bounds, so a partial rebuild's collection cost scales with the changed
    /// region. Volume mode composes: the volume intersects the filter.
    /// </summary>
    public List<NavMeshGeometrySource> CollectSources(AABB? filterBounds = null)
        => CollectSources(filterBounds, ResolveBuildSettings().EffectiveVoxelSize);

    // terrainVoxelSize must match the settings the geometry will be voxelized with, or terrain decimates at a different phase.
    internal List<NavMeshGeometrySource> CollectSources(AABB? filterBounds, float terrainVoxelSize)
    {
        List<NavMeshGeometrySource> sources = [];
        if (TryGetCollectionScope(filterBounds, out Scene? scene, out AABB? bounds))
            NavMeshGeometryCollector.Collect(CollectionObjects(scene!), UseGeometry, Layers, terrainVoxelSize, DefaultArea, sources, bounds, AgentTypeId);
        return sources;
    }

    private IEnumerable<GameObject> CollectionObjects(Scene scene)
        => CollectObjects == NavMeshCollectObjects.Children ? EnumerateSelfAndChildren(GameObject) : scene.ActiveObjects;

    /// <summary>
    /// The scene to collect from and the world-space filter, composed with the Volume-mode extent
    /// (the one bounds rule every collector shares). False when not in a scene or when the
    /// intersection is empty, so nothing can be collected.
    /// </summary>
    private bool TryGetCollectionScope(AABB? filterBounds, out Scene? scene, out AABB? bounds)
    {
        scene = Scene;
        bounds = filterBounds;
        if (scene.IsNotValid()) return false;
        if (CollectObjects != NavMeshCollectObjects.Volume) return true;

        AABB volume = VolumeBounds;
        if (bounds is AABB b)
        {
            if (!b.Intersects(volume)) return false;
            bounds = b.ClippedBy(volume);
        }
        else
        {
            bounds = volume;
        }
        return true;
    }

    /// <summary>
    /// Collect the scene's <see cref="NavMeshModifierVolume"/>s that apply to this surface's
    /// agent type (main thread), optionally restricted to volumes overlapping
    /// <paramref name="filterBounds"/>. Same object scoping (CollectObjects/Layers) as
    /// geometry collection.
    /// </summary>
    public List<NavMeshAreaVolume> CollectVolumes(AABB? filterBounds = null)
    {
        List<NavMeshAreaVolume> volumes = [];
        if (TryGetCollectionScope(filterBounds, out Scene? scene, out AABB? bounds))
            NavMeshGeometryCollector.CollectModifierVolumes(CollectionObjects(scene!), Layers, AgentTypeId, volumes, bounds);
        return volumes;
    }

    /// <summary>
    /// Collect the scene's <see cref="NavMeshLink"/>s that apply to this surface's agent type
    /// (main thread), optionally restricted to links overlapping
    /// <paramref name="filterBounds"/>. Same object scoping (CollectObjects/Layers) as
    /// geometry collection.
    /// </summary>
    public List<NavMeshLinkSource> CollectLinks(AABB? filterBounds = null)
    {
        List<NavMeshLinkSource> links = [];
        if (!TryGetCollectionScope(filterBounds, out Scene? scene, out AABB? bounds))
            return links;

        // Scene-wide collection reads the world's registry rather than every GameObject: a link
        // edit re-collects on the spot, so this runs at gameplay rate. Children mode still walks,
        // because what it scopes to is the hierarchy.
        if (CollectObjects == NavMeshCollectObjects.Children)
            NavMeshGeometryCollector.CollectLinks(EnumerateSelfAndChildren(GameObject), Layers, AgentTypeId, links, bounds);
        else
            NavMeshGeometryCollector.CollectLinks(scene!.Navigation.Links, Layers, AgentTypeId, links, bounds);
        return links;
    }

    private static IEnumerable<GameObject> EnumerateSelfAndChildren(GameObject root)
    {
        yield return root;
        foreach (GameObject child in root.Children)
        {
            if (child.IsNotValid()) continue;
            foreach (GameObject go in EnumerateSelfAndChildren(child))
                yield return go;
        }
    }

    #endregion

    #region Gizmos

    private NavMeshSurfaceOverlay? _overlay;

    public override void DrawGizmos()
    {
        if (NavMeshDebugDisplay.AlwaysShow) DrawWalkableOverlay();
    }

    public override void DrawGizmosSelected()
    {
        if (CollectObjects == NavMeshCollectObjects.Volume)
            Debug.DrawWireCube(Transform.TransformPoint(Center), Size * 0.5f, Color.Cyan);

        Runtime.NavMeshData? data = NavMeshData.Res;
        if (data.IsNotValid() || !data!.HasTiles)
            return;

        Debug.DrawWireCube((data.BoundsMin + data.BoundsMax) * 0.5f, (data.BoundsMax - data.BoundsMin) * 0.5f, Color.Blue);

        // Already drawn unselected, and drawing it twice would double the blend.
        if (!NavMeshDebugDisplay.AlwaysShow) DrawWalkableOverlay();
    }

    private void DrawWalkableOverlay() => (_overlay ??= new NavMeshSurfaceOverlay()).Draw(this, World);

    #endregion
}
