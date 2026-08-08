// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Recast.Detour;
using Prowl.Recast.Detour.TileCache;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Which objects a <see cref="NavMeshSurface"/> bake considers.</summary>
public enum NavMeshCollectObjects
{
    /// <summary>Every active object in the scene.</summary>
    All,
    /// <summary>Every active object whose geometry intersects the surface's volume
    /// (<see cref="NavMeshSurface.Center"/> / <see cref="NavMeshSurface.Size"/>).</summary>
    Volume,
    /// <summary>Only this GameObject and its children.</summary>
    Children,
}

/// <summary>
/// Bakes and registers a navmesh for one agent type. The baked result is a standalone
/// <see cref="NavMeshData"/> asset; at runtime the surface registers it with the scene's
/// <see cref="NavMeshWorld"/> on enable. Rebuilds can run synchronously, in the background
/// (<see cref="BuildNavMeshAsync"/>), or per-tile for localized geometry changes
/// (<see cref="RebuildTiles"/> — destructible worlds rebuild only what changed).
/// </summary>
// Registration is the whole of this component's lifecycle — there is no per-frame work — and the
// editor needs it as much as play does: an obstacle can only carve a live navmesh, and the scene
// view's overlay draws one. Without this, opening a scene leaves its baked navmesh unregistered
// until something bakes again, so carve previews only work in the session you pressed Bake in.
[ExecuteAlways]
[AddComponentMenu("Navigation/NavMesh Surface")]
[ComponentIcon("")] // map icon
public class NavMeshSurface : MonoBehaviour
{
    [Header("Bake")]
    [Tooltip("The agent type this navmesh is built for (radius, height, slope, climb come from the project's agent table). Agents only use navmeshes of their own type.")]
    [NavMeshAgentType]
    public int AgentTypeId = NavMeshAgentTypes.Humanoid;

    [Tooltip("Surface-level rasterization settings (voxel/tile sizes and Recast detail). Most bakes never need to change these.")]
    [HideInInspector] // drawn inside the editor's Advanced foldout
    public NavMeshBuildOverrides BuildOverrides = new();

    /// <summary>The resolved bake input: the agent type's envelope composed with this
    /// surface's <see cref="BuildOverrides"/>. What gets handed to
    /// <see cref="NavMeshBuilder.Build"/>; a fresh snapshot each call.</summary>
    public NavMeshBuildSettings ResolveBuildSettings()
        => NavMeshAgentTypes.GetBuildSettings(AgentTypeId, BuildOverrides);

    [Tooltip("Which objects contribute bake geometry. NavMeshAgents and their children never contribute — agents walk the mesh rather than forming it.")]
    public NavMeshCollectObjects CollectObjects = NavMeshCollectObjects.All;

    [Tooltip("Volume center (local to this GameObject) when CollectObjects is Volume.")]
    [ShowIf(nameof(IsVolumeMode))]
    public Float3 Center;

    [Tooltip("Volume size when CollectObjects is Volume.")]
    [ShowIf(nameof(IsVolumeMode))]
    public Float3 Size = new(10, 10, 10);

    [Tooltip("Only objects on these layers contribute bake geometry.")]
    public LayerMask Layers = LayerMask.Everything;

    [Tooltip("Voxelize render meshes or physics colliders.")]
    public NavMeshCollectGeometry UseGeometry = NavMeshCollectGeometry.RenderMeshes;

    [Tooltip("Area applied to all walkable geometry in this bake.")]
    [NavMeshArea]
    [HideInInspector] // drawn inside the editor's Advanced foldout (Unity keeps it there too)
    public int DefaultArea = NavMeshAreas.Walkable;

    [Tooltip("The baked navmesh. Assigned by baking, or point it at an existing .navmesh asset.")]
    public AssetRef<NavMeshData> NavMeshData;

    [Tooltip("Draw the walkable surface in the scene view even when this object is not selected — the only way to watch obstacles carve while playing, since entering play mode clears the selection. Debug aid: it re-triangulates the whole mesh on every frame a carve or rebuild is converging, so leave it off in scenes you are profiling.")]
    public bool AlwaysShowNavMesh;

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

    /// <summary>The scene's navigation world, or null when not in a scene.</summary>
    private NavMeshWorld? World
    {
        get
        {
            var scene = GameObject.IsValid() ? GameObject.Scene : null;
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
        Unregister();

        // Release the debug-overlay subscription; without this every surface ever selected
        // stays referenced by the scene's NavMeshWorld until scene teardown.
        if (_debugWorld != null)
        {
            _debugWorld.NavMeshChanged -= InvalidateDebugTriangulation;
            _debugWorld = null;
        }
        _debugTriangulation = null;
    }

    private void Register()
    {
        if (_instance != null) return;
        NavMeshWorld? world = World;
        if (world == null) return;

        // The navmesh has to be present now: registration happens once, on enable, and nothing
        // retries it — a transient null from async streaming would leave the scene permanently
        // without a navmesh, which reads as obstacles that never carve and agents that never
        // move. Block-load it, as the mesh and terrain colliders do for the same reason.
        NavMeshData.EnsureLoaded();

        Runtime.NavMeshData? data = NavMeshData.Res;
        if (data.IsNotValid() || !data!.HasTiles) return;

        // Copy only what the asset database owns. An asset is shared with every other surface
        // pointing at that .navmesh and with the next scene that loads it, so runtime tile and
        // link rewrites must not land on it. One built at runtime and handed over through
        // ApplyNavMeshData has no other owner: copying it would cost a list per registration
        // and, worse, mean re-registering rebuilt from the original bake and threw away every
        // rebuild since — which is the whole navmesh, for a game that generates its map.
        // (Handing one runtime navmesh to two surfaces shares it, as it always has.)
        _runtimeData = NavMeshData.AssetID == Guid.Empty ? data : data.Clone();
        _instance = world.AddNavMeshData(_runtimeData);
        if (_instance == null) _runtimeData = null;
    }

    private void Unregister()
    {
        if (_instance == null) return;
        World?.RemoveNavMeshData(_instance);
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
        NavMeshBuildSettings settings = ResolveBuildSettings(); // one resolve per bake: collection and build must agree
        List<NavMeshGeometrySource> sources = CollectSources(null, settings.EffectiveVoxelSize);
        Runtime.NavMeshData? data = NavMeshBuilder.Build(settings, sources, DefaultArea,
            threads: Math.Max(1, Environment.ProcessorCount - 1), worldBounds: ExplicitWorldBounds(),
            volumes: CollectVolumes(null), links: CollectLinks(null));
        if (data == null)
        {
            Debug.LogWarning($"[Navigation] Bake of '{GameObject.Name}' produced no walkable geometry.");
            return false;
        }

        ApplyNavMeshData(data);
        return true;
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
        NavMeshBuildSettings settings = ResolveBuildSettings(); // resolved on the main thread, once per bake
        List<NavMeshGeometrySource> sources = CollectSources(null, settings.EffectiveVoxelSize);
        List<NavMeshAreaVolume> volumes = CollectVolumes(null); // main thread: touches Transforms
        List<NavMeshLinkSource> links = CollectLinks(null);
        int defaultArea = DefaultArea;
        AABB? worldBounds = ExplicitWorldBounds();
        return Task.Run(() => NavMeshBuilder.Build(settings, sources, defaultArea,
            threads: Math.Max(1, Environment.ProcessorCount - 1), cancellation, worldBounds, volumes, links), cancellation);
    }

    /// <summary>
    /// Swap in a freshly built navmesh: replaces this surface's data (as a runtime resource)
    /// and its registration in the scene. Main thread only.
    /// </summary>
    public void ApplyNavMeshData(NavMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Unregister();
        NavMeshData = data;
        Register();
    }

    /// <summary>Re-register the currently assigned <see cref="NavMeshData"/> (e.g. after the
    /// asset reference was swapped by an editor bake).</summary>
    public void RefreshRegistration()
    {
        Unregister();
        Register();
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
        return RebuildTiles(worldBounds,
            CollectSources(collectionBounds, data!.Settings.EffectiveVoxelSize), out _,
            CollectVolumes(collectionBounds));
    }

    /// <summary>
    /// Re-collect this surface's <see cref="NavMeshLink"/>s into the live navmesh and rebuild
    /// the tiles overlapping <paramref name="worldBounds"/> so the change takes effect. Cheap
    /// next to a geometry rebuild: the compressed layers are untouched and only the affected
    /// tiles are re-contoured from them, with the new link set injected — where
    /// <see cref="RebuildTiles(AABB)"/> has to re-voxelize. Returns false when this surface has
    /// no live navmesh.
    /// </summary>
    /// <param name="worldBounds">Region whose tiles pick up the change — normally the link's
    /// endpoints. The registry is replaced wholesale; only these tiles re-contour.</param>
    /// <param name="links">The surface's complete link set. Null (the default) collects the
    /// scene's <see cref="NavMeshLink"/>s; games driving navigation from explicit sources pass
    /// their own list and skip the scene scan.</param>
    public bool RebuildLinkTiles(AABB worldBounds, IReadOnlyList<NavMeshLinkSource>? links = null)
        => RebuildLinkTiles([worldBounds], links);

    /// <inheritdoc cref="RebuildLinkTiles(AABB, IReadOnlyList{NavMeshLinkSource})"/>
    /// <param name="worldBounds">Regions whose tiles pick up the change. A tile several of them
    /// cover re-contours once — which is the whole point of handing a frame's link edits over
    /// together rather than applying them one at a time.</param>
    /// <param name="links">The surface's complete link set. Null (the default) collects the
    /// scene's <see cref="NavMeshLink"/>s; games driving navigation from explicit sources pass
    /// their own list and skip the collection.</param>
    public bool RebuildLinkTiles(ReadOnlySpan<AABB> worldBounds, IReadOnlyList<NavMeshLinkSource>? links = null)
    {
        NavMeshInstance? instance = Instance;
        Runtime.NavMeshData? data = _runtimeData;
        if (instance == null || data.IsNotValid()) return false;
        if (data!.TileWorldSize <= 0) return false;

        links ??= CollectLinks(null);
        NavMeshWorld? world = World;
        if (world == null) return false;

        // Resolved before the write lock is taken, so worker-thread queries do not block on it.
        HashSet<(int X, int Z)> tiles = [];
        foreach (AABB bounds in worldBounds)
            if (data.TryGetTileRange(bounds, out int tx0, out int tx1, out int tz0, out int tz1))
                for (int tz = tz0; tz <= tz1; tz++)
                    for (int tx = tx0; tx <= tx1; tx++)
                        tiles.Add((tx, tz));

        world.MutateTileCache(instance, cache =>
        {
            // Always replace the link set, even with no tiles in range: it is what tiles rebuilt
            // later — by a carve, or by a rebuild of a neighbouring region — will be built from.
            instance.TileCacheLinks.SetLinks(links, data.Settings.AgentRadius);

            foreach ((int tx, int tz) in tiles)
                foreach (long tileRef in cache.GetTilesAt(tx, tz))
                    cache.BuildNavMeshTile(tileRef);
        });

        // Mirror onto the runtime copy, so a rebuild that re-instantiates it starts from the
        // link set the live mesh is using. The .navmesh asset is left alone: a link moving is a
        // scene edit, and the baked artifact answers for it at the next bake.
        data.Links.Clear();
        foreach (NavMeshLinkSource link in links)
            data.Links.Add(Runtime.NavMeshData.NavMeshLinkEntry.From(link));
        return true;
    }

    /// <summary>
    /// World rect the collectors must cover for a rebuild of <paramref name="worldBounds"/>:
    /// the affected TILES (rebuilds rasterize whole tiles, so sources clipped to just the
    /// changed AABB would erase the rest of a partially-covered tile) plus the erosion border.
    /// </summary>
    private AABB? RebuildCollectionBounds(AABB worldBounds)
    {
        Runtime.NavMeshData? data = NavMeshData.Res;
        if (data.IsNotValid() || data!.TileWorldSize <= 0) return null; // no grid: collect everything

        float ts = data.TileWorldSize;
        // Conservative world-space erosion border (CalcBorder cells = ceil(radius/cs) + 3).
        // Derived from the ASSET's snapshot settings — the grid being rebuilt is the one the
        // asset was baked with, not whatever the surface's current configuration says.
        float border = data.Settings.AgentRadius + 4f * data.Settings.EffectiveVoxelSize;

        double minTx = Math.Floor((worldBounds.Min.X - border - data.Origin.X) / ts);
        double maxTx = Math.Floor((worldBounds.Max.X + border - data.Origin.X) / ts);
        double minTz = Math.Floor((worldBounds.Min.Z - border - data.Origin.Z) / ts);
        double maxTz = Math.Floor((worldBounds.Max.Z + border - data.Origin.Z) / ts);

        return new AABB(
            new Float3((float)(data.Origin.X + minTx * ts - border), (float)data.BoundsMin.Y - 1, (float)(data.Origin.Z + minTz * ts - border)),
            new Float3((float)(data.Origin.X + (maxTx + 1) * ts + border), (float)data.BoundsMax.Y + 1, (float)(data.Origin.Z + (maxTz + 1) * ts + border)));
    }

    /// <summary>
    /// Rebuild the tiles intersecting <paramref name="worldBounds"/> from caller-supplied
    /// geometry, for games whose world isn't visible to the collectors (custom renderers,
    /// custom collision). Pass the bounds of the CHANGED geometry — the affected tile set is
    /// derived from them, expanded by the erosion border. Sources need only cover those tiles
    /// plus the border (derive from <see cref="NavMeshBuildSettings.EffectiveTileSize"/> and
    /// <see cref="NavMeshBuildSettings.EffectiveVoxelSize"/>) — never the whole bake.
    /// An empty source list is valid and empties the affected tiles.
    /// </summary>
    public bool RebuildTiles(AABB worldBounds, IReadOnlyList<NavMeshGeometrySource> sources)
        => RebuildTiles(worldBounds, sources, out _);

    /// <inheritdoc cref="RebuildTiles(AABB, IReadOnlyList{NavMeshGeometrySource})"/>
    /// <param name="rebuiltTiles">Number of tiles rebuilt or emptied, for cost profiling.</param>
    /// <param name="volumes">Area volumes applied to the rebuilt tiles. Null (the default)
    /// collects the scene's <see cref="NavMeshModifierVolume"/>s over the affected region —
    /// note that collection walks the scene's active objects, so callers who chose explicit
    /// sources to avoid scene scans should pass an empty list (no volumes, no scan) or their
    /// own list.</param>
    public bool RebuildTiles(AABB worldBounds, IReadOnlyList<NavMeshGeometrySource> sources, out int rebuiltTiles,
        IReadOnlyList<NavMeshAreaVolume>? volumes = null)
    {
        rebuiltTiles = 0;
        Runtime.NavMeshData? data = _runtimeData;
        if (World == null || _instance == null || data.IsNotValid())
            return false;

        volumes ??= CollectVolumes(RebuildCollectionBounds(worldBounds));
        List<(int X, int Z, List<byte[]> Layers)> rebuilt = NavMeshBuilder.BuildTilesInBounds(
            data!, sources, worldBounds.Min, worldBounds.Max, DefaultArea, volumes: volumes);
        return ApplyRebuiltTiles(rebuilt, out rebuiltTiles);
    }

    /// <summary>
    /// Voxelize the affected tiles on the thread pool, off the frame. The returned tiles are
    /// NOT yet live — apply them with <see cref="ApplyRebuiltTiles"/> from the main thread.
    /// Sequencing rules: do not run two rebuilds of overlapping regions concurrently, and do
    /// not interleave with a full rebake — the build reads the asset's grid anchoring and the
    /// apply assumes it is unchanged since dispatch.
    /// </summary>
    public Task<List<(int X, int Z, List<byte[]> Layers)>> RebuildTilesAsync(
        AABB worldBounds, IReadOnlyList<NavMeshGeometrySource> sources, CancellationToken cancellation = default,
        IReadOnlyList<NavMeshAreaVolume>? volumes = null)
    {
        // The surface's own copy, not the asset: the background build reads the tile grid off
        // it, and it must be the grid the live mesh is on.
        Runtime.NavMeshData? data = _runtimeData;
        if (data.IsNotValid())
            return Task.FromResult(new List<(int, int, List<byte[]>)>());

        // Volumes are collected on the calling (main) thread — they touch Transforms; the
        // resulting payload is self-contained for the background build. The null default scans
        // the scene's active objects — explicit-sources callers who avoid scene scans on
        // purpose should pass [] (or their own list) instead.
        volumes ??= CollectVolumes(RebuildCollectionBounds(worldBounds));
        int defaultArea = DefaultArea;
        return Task.Run(() => NavMeshBuilder.BuildTilesInBounds(
            data!, sources, worldBounds.Min, worldBounds.Max, defaultArea, cancellation, volumes), cancellation);
    }

    /// <summary>
    /// Swap rebuilt tiles (from <see cref="RebuildTilesAsync"/> or
    /// <see cref="NavMeshBuilder.BuildTilesInBounds"/>) into the live TileCache and mirror them
    /// into the asset. Main thread only. The swap quiesces pending obstacle work, replaces each
    /// affected tile's layers (removing the paired navmesh tiles — the cache's RemoveTile does
    /// not), refreshes every obstacle's touched-tile list (tile replacement bumps salts, so
    /// captured refs go stale — without the refresh, regenerated tiles would rebuild WITHOUT
    /// their carves), then rebuilds the new tiles with carves re-applied.
    /// </summary>
    public bool ApplyRebuiltTiles(List<(int X, int Z, List<byte[]> Layers)> rebuilt, out int rebuiltTiles)
    {
        ArgumentNullException.ThrowIfNull(rebuilt);
        rebuiltTiles = 0;
        NavMeshWorld? world = World;
        Runtime.NavMeshData? data = _runtimeData;
        if (world == null || _instance == null || data.IsNotValid() || rebuilt.Count == 0)
            return false;
        rebuiltTiles = rebuilt.Count;

        world.MutateTileCache(_instance, cache =>
        {
            // Quiesce: every obstacle settles and no pending rebuild references the tiles
            // being replaced. Bounded — a healthy cache converges in a handful of slices. On
            // exhaustion the refresh below skips unsettled obstacles, which is exactly the
            // stale-carve failure this mechanism prevents, so it must not fail silent.
            bool converged = false;
            for (int i = 0; i < 1024 && !converged; i++)
                converged = cache.Update();
            if (!converged)
                Debug.LogWarning("[Navigation] ApplyRebuiltTiles: the tile cache did not converge within 1024 update slices; obstacle carves may not re-apply to the regenerated tiles.");

            DtNavMesh navMesh = cache.GetNavMesh();
            var addedRefs = new List<long>();
            foreach ((int x, int z, List<byte[]> blobs) in rebuilt)
            {
                foreach (long tileRef in cache.GetTilesAt(x, z))
                {
                    // The cache's RemoveTile frees only the compressed tile; the built
                    // navmesh tile must be removed explicitly (while the header still exists).
                    DtTileCacheLayerHeader? header = cache.GetTileByRef(tileRef)?.header;
                    if (header != null)
                    {
                        long navRef = navMesh.GetTileRefAt(header.tx, header.ty, header.tlayer);
                        if (navRef != 0) navMesh.RemoveTile(navRef);
                    }
                    cache.RemoveTile(tileRef);
                }

                foreach (byte[] blob in blobs)
                {
                    try
                    {
                        long added = cache.AddTile(blob, 0);
                        if (added != 0) addedRefs.Add(added);
                        else Debug.LogWarning($"[Navigation] ApplyRebuiltTiles: layer for tile ({x}, {z}) collided with an existing layer slot and was skipped.");
                    }
                    catch (Exception e)
                    {
                        // AddTile throws on cache tile-capacity exhaustion (regeneration can
                        // legitimately grow the layer count past the bake's).
                        Debug.LogWarning($"[Navigation] ApplyRebuiltTiles: failed to add a layer for tile ({x}, {z}): {e.Message}");
                    }
                }
            }

            NavMeshTileBuilder.RefreshObstacleTouchedTiles(cache, data!);

            foreach (long added in addedRefs)
                cache.BuildNavMeshTile(added); // re-contours with carves applied via the refreshed lists
        });

        // Mirror the swap into this surface's runtime copy so a later re-instantiation agrees
        // with the live mesh; the .navmesh asset on disk is not touched. Obstacles are runtime
        // state and never serialize, so the copy holds clean regenerated layers. Single pass
        // over the tile list: RemoveAll-per-tile would be O(total x rebuilt).
        var replaced = new HashSet<(int, int)>(rebuilt.Count);
        foreach ((int x, int z, _) in rebuilt)
            replaced.Add((x, z));
        data!.CacheLayers.RemoveAll(t => replaced.Contains((t.X, t.Z)));
        foreach ((int x, int z, List<byte[]> blobs) in rebuilt)
            foreach (byte[] blob in blobs)
                data.CacheLayers.Add(new Runtime.NavMeshData.NavMeshTile { X = x, Z = z, Data = blob });

        return true;
    }

    /// <summary>Collect this surface's bake geometry from the scene (main thread).</summary>
    public List<NavMeshGeometrySource> CollectSources() => CollectSources(null);

    /// <inheritdoc cref="CollectSources(AABB?, float)"/>
    public List<NavMeshGeometrySource> CollectSources(AABB? filterBounds)
        => CollectSources(filterBounds, ResolveBuildSettings().EffectiveVoxelSize);

    /// <summary>
    /// Collect this surface's bake geometry, restricted to objects whose bounds intersect
    /// <paramref name="filterBounds"/> (conservative test against transformed local bounds) —
    /// partial rebuilds pass their affected-tile rect so collection cost scales with the
    /// changed region like the rest of the rebuild path. Volume mode composes: the volume is
    /// intersected with the filter. <paramref name="terrainVoxelSize"/> sets terrain
    /// decimation granularity — pass the voxel size of the settings the geometry will be
    /// voxelized with (bakes: the freshly resolved settings; partial rebuilds: the asset's
    /// snapshot settings).
    /// </summary>
    public List<NavMeshGeometrySource> CollectSources(AABB? filterBounds, float terrainVoxelSize)
    {
        List<NavMeshGeometrySource> sources = [];
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsNotValid()) return sources;
        if (!TryResolveCollectionBounds(filterBounds, out AABB? bounds))
            return sources; // filter rect entirely outside the volume

        IEnumerable<GameObject> objects = CollectObjects == NavMeshCollectObjects.Children
            ? EnumerateSelfAndChildren(GameObject)
            : scene!.ActiveObjects;

        NavMeshGeometryCollector.Collect(objects, UseGeometry, Layers, terrainVoxelSize, DefaultArea, sources, bounds, AgentTypeId);
        return sources;
    }

    /// <summary>
    /// Compose an optional world-space filter with the Volume-mode extent (the one bounds
    /// rule shared by geometry and volume collection). False when the intersection is empty —
    /// nothing can be collected.
    /// </summary>
    private bool TryResolveCollectionBounds(AABB? filterBounds, out AABB? bounds)
    {
        bounds = filterBounds;
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
    public List<NavMeshAreaVolume> CollectVolumes(AABB? filterBounds)
    {
        List<NavMeshAreaVolume> volumes = [];
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsNotValid()) return volumes;
        if (!TryResolveCollectionBounds(filterBounds, out AABB? bounds))
            return volumes;

        IEnumerable<GameObject> objects = CollectObjects == NavMeshCollectObjects.Children
            ? EnumerateSelfAndChildren(GameObject)
            : scene!.ActiveObjects;

        NavMeshGeometryCollector.CollectModifierVolumes(objects, Layers, AgentTypeId, volumes, bounds);
        return volumes;
    }

    /// <summary>
    /// Collect the scene's <see cref="NavMeshLink"/>s that apply to this surface's agent type
    /// (main thread), optionally restricted to links overlapping
    /// <paramref name="filterBounds"/>. Same object scoping (CollectObjects/Layers) as
    /// geometry collection.
    /// </summary>
    public List<NavMeshLinkSource> CollectLinks(AABB? filterBounds)
    {
        List<NavMeshLinkSource> links = [];
        var scene = GameObject.IsValid() ? GameObject.Scene : null;
        if (scene.IsNotValid()) return links;
        if (!TryResolveCollectionBounds(filterBounds, out AABB? bounds))
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

    private NavMeshTriangulation? _debugTriangulation;
    private NavMeshWorld? _debugWorld;
    // What the cached triangulation was built from, so it rebuilds when the asset is swapped
    // or the surface gains/loses a live registration (entering or leaving play mode).
    private Runtime.NavMeshData? _debugSource;
    private bool _debugFromLive;

    private void InvalidateDebugTriangulation() => _debugTriangulation = null;

    /// <summary>Unselected drawing: only the walkable overlay, and only when asked for. Watching
    /// obstacles carve needs it while something else is selected — and entering play mode clears
    /// the selection outright, so selection-only drawing cannot show a runtime carve at all.
    /// </summary>
    public override void DrawGizmos()
    {
        if (AlwaysShowNavMesh) DrawWalkableOverlay();
    }

    public override void DrawGizmosSelected()
    {
        if (CollectObjects == NavMeshCollectObjects.Volume)
            Debug.DrawWireCube(Transform.TransformPoint(Center), Size * 0.5f, Color.Cyan);

        Runtime.NavMeshData? data = NavMeshData.Res;
        if (data.IsNotValid() || !data!.HasTiles)
            return;

        Debug.DrawWireCube((data.BoundsMin + data.BoundsMax) * 0.5f, (data.BoundsMax - data.BoundsMin) * 0.5f, Color.Blue);

        // Already drawn unselected — drawing it twice would double the blend.
        if (!AlwaysShowNavMesh) DrawWalkableOverlay();
    }

    /// <summary>The walkable surface, colored per area. Cached; invalidated on navmesh change.</summary>
    private void DrawWalkableOverlay()
    {
        Runtime.NavMeshData? data = NavMeshData.Res;
        if (data.IsNotValid() || !data!.HasTiles)
            return;

        NavMeshWorld? world = World;
        if (world != null && _debugWorld != world)
        {
            if (_debugWorld != null) _debugWorld.NavMeshChanged -= InvalidateDebugTriangulation;
            world.NavMeshChanged += InvalidateDebugTriangulation;
            _debugWorld = world;
            _debugTriangulation = null;
        }

        // Prefer the live navmesh: it is the one carving and rebuilds change. Fall back to the
        // baked asset when this surface has no live instance to read — its data failed to
        // instantiate, or another surface of the same agent type holds the registration.
        bool live = world != null && world.GetInstance(AgentTypeId) != null;
        if (_debugTriangulation == null || _debugFromLive != live || !ReferenceEquals(_debugSource, data))
        {
            _debugTriangulation = live ? world!.CalculateTriangulation(AgentTypeId) : data.CalculateTriangulation();
            _debugFromLive = live;
            _debugSource = data;
        }

        NavMeshTriangulation tri = _debugTriangulation.Value;
        // Lift slightly off the surface so the overlay doesn't z-fight the floor.
        var lift = new Float3(0, 0.03f, 0);
        for (int t = 0; t < tri.Areas.Length; t++)
        {
            Color color = AreaColor(tri.Areas[t]);
            Debug.DrawTriangle(
                tri.Vertices[tri.Indices[t * 3 + 0]] + lift,
                tri.Vertices[tri.Indices[t * 3 + 1]] + lift,
                tri.Vertices[tri.Indices[t * 3 + 2]] + lift,
                color);
        }

        foreach (NavMeshConnection con in tri.Connections)
            DrawConnection(con, lift);
    }

    /// <summary>Endpoint marker size, shared so a link's own gizmo matches the overlay.</summary>
    internal const float EndpointGizmoRadius = 0.15f;

    /// <summary>
    /// One off-mesh connection, drawn where the navmesh put it rather than where the component
    /// asked: endpoints that snapped elsewhere show it, and a link that never attached is
    /// visibly absent. Opaque, because the translucent surface fill is a poor read for a line.
    /// </summary>
    internal static void DrawConnection(NavMeshConnection con, Float3 lift)
    {
        Color area = AreaColor(con.Area);
        var color = new Color(area.R, area.G, area.B, 1f);
        Float3 start = con.Start + lift, end = con.End + lift;

        Debug.DrawLine(start, end, color);
        Debug.DrawWireSphere(start, EndpointGizmoRadius, color);
        Debug.DrawWireSphere(end, EndpointGizmoRadius, color);

        // Measured flat: the markings read as ground plan, and a steep link would otherwise
        // splay them out of the surface.
        var flat = new Float3(end.X - start.X, 0, end.Z - start.Z);
        double length = Math.Sqrt(flat.X * flat.X + flat.Z * flat.Z);
        if (length <= 1e-4) return;
        var forward = new Float3((float)(flat.X / length), 0, (float)(flat.Z / length));
        var perp = new Float3(-forward.Z, 0, forward.X);

        // The width the connection actually covers, as a bar across each end.
        if (con.Radius > 0f)
        {
            Float3 half = perp * con.Radius;
            Debug.DrawLine(start - half, start + half, color);
            Debug.DrawLine(end - half, end + half, color);
        }

        // One-way connections get an arrowhead; a bidirectional one is just the line.
        if (con.Bidirectional) return;
        Float3 back = end - forward * (EndpointGizmoRadius * 3f);
        Float3 barb = perp * (EndpointGizmoRadius * 1.5f);
        Debug.DrawLine(end, back + barb, color);
        Debug.DrawLine(end, back - barb, color);
    }

    /// <summary>Stable debug color for an area index (Walkable is the familiar navmesh blue).</summary>
    public static Color AreaColor(int areaIndex)
    {
        if (areaIndex == NavMeshAreas.Walkable) return new Color(0f, 0.75f, 1f, 0.35f);
        // Deterministic hue per area index.
        float hue = (areaIndex * 137.5f) % 360f / 360f;
        float r = Math.Abs(hue * 6f - 3f) - 1f;
        float g = 2f - Math.Abs(hue * 6f - 2f);
        float b = 2f - Math.Abs(hue * 6f - 4f);
        return new Color(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f), 0.35f);
    }

    #endregion
}
