// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Builds a <see cref="NavMeshData"/> from collected geometry. Pure CPU work over an
/// already-flattened triangle soup — no Transform or GameObject access — so it is safe to run
/// on a background thread once the sources have been collected on the main thread.
/// Navmeshes are always built tiled so they can be partially rebuilt later
/// (see <c>NavMeshSurface.RebuildTiles</c>).
/// </summary>
public static class NavMeshBuilder
{
    /// <summary>
    /// Build a complete navmesh from geometry sources. Returns null when nothing walkable was
    /// produced (no geometry, all down-facing, cancelled) — never an empty NavMeshData.
    /// </summary>
    /// <param name="settings">Agent envelope + voxelization parameters. Snapshotted into the result.</param>
    /// <param name="sources">Collected geometry. Vertices are transformed by each source's matrix during flattening.</param>
    /// <param name="defaultArea">Area for sources that don't specify one (see <see cref="NavMeshAreas"/>).</param>
    /// <param name="threads">Worker threads for tile building. 0 or 1 builds single-threaded (deterministic tile order).</param>
    /// <param name="cancellation">Cancels between tiles; a cancelled build returns null.</param>
    /// <param name="worldBounds">Explicit XZ extent for the tile grid. Supply this when the
    /// walkable world will GROW after baking (destructible/streamed maps): the grid, tile
    /// capacity, and the bounds later rebuilds anchor to are sized from it instead of from the
    /// initial geometry, so <c>RebuildTiles</c> can add tiles anywhere inside it. The vertical
    /// range still unions with the geometry — callers know their footprint, not their height,
    /// and Recast clips spans to the heightfield's vertical range.</param>
    /// <param name="volumes">Convex area volumes stamped over the rasterized geometry (from
    /// <see cref="NavMeshModifierVolume"/>s, or built directly). Volumes never create
    /// walkable surface; a Not Walkable volume erases it.</param>
    /// <param name="links">Off-mesh connections placed in the tiles containing their start
    /// points (from <see cref="NavMeshLink"/>s, or built directly). Stored on the asset and
    /// re-injected as each tile is contoured, since tiles are rebuilt from geometry-only layers
    /// at runtime.</param>
    public static NavMeshData? Build(NavMeshBuildSettings settings, IReadOnlyList<NavMeshGeometrySource> sources,
        int defaultArea = NavMeshAreas.Walkable, int threads = 0, CancellationToken cancellation = default,
        AABB? worldBounds = null, IReadOnlyList<NavMeshAreaVolume>? volumes = null,
        IReadOnlyList<NavMeshLinkSource>? links = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sources);

        int inputTriangles = 0;
        for (int i = 0; i < sources.Count; i++)
            inputTriangles += sources[i].TriangleCount;
        if (inputTriangles == 0)
            return null;

        var geom = new ProwlInputGeomProvider(sources, defaultArea);
        if (geom.TriangleCount == 0)
            return null;
        AddVolumes(geom, volumes);

        settings = settings.Clone();
        ResolveTileSize(settings);

        float cs = settings.EffectiveVoxelSize;
        int tileVoxels = settings.EffectiveTileSize;
        RcConfig cfg = CreateConfig(settings, defaultArea);

        RcVec3f bmin = geom.GetMeshBoundsMin();
        RcVec3f bmax = geom.GetMeshBoundsMax();
        if (worldBounds is AABB wb)
        {
            // XZ extent from the caller; Y is the union of both so no geometry falls outside
            // the heightfield's vertical range.
            bmin = new RcVec3f((float)wb.Min.X, Math.Min(bmin.Y, (float)wb.Min.Y), (float)wb.Min.Z);
            bmax = new RcVec3f((float)wb.Max.X, Math.Max(bmax.Y, (float)wb.Max.Y), (float)wb.Max.Z);
        }

        RcRecast.CalcGridSize(bmin, bmax, cs, out int gridX, out int gridZ);
        int tilesX = (gridX + tileVoxels - 1) / tileVoxels;
        int tilesZ = (gridZ + tileVoxels - 1) / tileVoxels;

        var data = new NavMeshData
        {
            Settings = settings,
            BoundsMin = new Float3(bmin.X, bmin.Y, bmin.Z),
            BoundsMax = new Float3(bmax.X, bmax.Y, bmax.Z),
            Origin = new Float3(bmin.X, bmin.Y, bmin.Z),
            TileWorldSize = tileVoxels * cs,
            MaxTiles = GetMaxTiles(bmin, bmax, cs, tileVoxels),
            MaxPolys = GetMaxPolysPerTile(bmin, bmax, cs, tileVoxels),
        };

        // Detour packs tile + poly ids into shared reference bits (tile bits cap at 14), so a
        // large enough grid overflows MaxTiles — AddTile then drops tiles at instantiation.
        // Surface it at bake time, where the fix (larger tiles / tighter bounds) is actionable.
        if (tilesX * tilesZ > data.MaxTiles)
            Debug.LogWarning($"[Navigation] Bake grid is {tilesX}x{tilesZ} = {tilesX * tilesZ} tiles but the navmesh can only address {data.MaxTiles}; tiles beyond capacity will fail to add. Increase TileSize or shrink the bake bounds.");

        // Compressed voxelization blobs per tile, contoured on demand by the TileCache. The
        // results array is indexed by tile, keeping output order deterministic regardless of
        // thread scheduling.
        var layerResults = new List<byte[]>?[tilesX * tilesZ];
        if (threads > 1)
        {
            Parallel.For(0, tilesX * tilesZ, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = CancellationToken.None }, i =>
            {
                if (cancellation.IsCancellationRequested) return;
                layerResults[i] = NavMeshTileBuilder.BuildTileLayers(geom, cfg, bmin, bmax, i % tilesX, i / tilesX);
            });
        }
        else
        {
            for (int i = 0; i < layerResults.Length; i++)
            {
                if (cancellation.IsCancellationRequested) return null;
                layerResults[i] = NavMeshTileBuilder.BuildTileLayers(geom, cfg, bmin, bmax, i % tilesX, i / tilesX);
            }
        }

        if (cancellation.IsCancellationRequested)
            return null;

        for (int i = 0; i < layerResults.Length; i++)
        {
            List<byte[]>? blobs = layerResults[i];
            if (blobs == null) continue;
            foreach (byte[] blob in blobs)
                data.CacheLayers.Add(new NavMeshData.NavMeshTile { X = i % tilesX, Z = i / tilesX, Data = blob });
        }

        // A bake that rasterized nothing walkable returns null, not an empty NavMeshData —
        // every consumer rejects tile-less data anyway, and null keeps the "produced no
        // walkable geometry" diagnostics accurate downstream.
        if (data.CacheLayers.Count == 0)
            return null;

        if (links != null)
            foreach (NavMeshLinkSource link in links)
                data.Links.Add(NavMeshData.NavMeshLinkEntry.From(link));

        Debug.Log($"[Navigation] Baked {data.CacheLayers.Count} cache layers ({tilesX}x{tilesZ} grid, {geom.TriangleCount} input triangles, {data.Links.Count} links).");
        return data;
    }

    /// <summary>
    /// Rebuild the compressed layers of the tiles intersecting
    /// <paramref name="worldMin"/>..<paramref name="worldMax"/> against fresh geometry, keeping
    /// the original bake's tile grid (XZ anchored to the bake, Y unioned with current geometry)
    /// and expanding by the erosion border. A region entirely outside the baked bounds is a
    /// no-op — growing the bounds needs a full rebuild. Returns one entry per affected tile; an
    /// empty layer list means the tile is now empty. Apply with
    /// <c>NavMeshSurface.ApplyRebuiltTiles</c> — the swap refreshes obstacle state so existing
    /// carves re-apply to the regenerated tiles.
    /// </summary>
    public static List<(int X, int Z, List<byte[]> Layers)> BuildTilesInBounds(NavMeshData data,
        IReadOnlyList<NavMeshGeometrySource> sources, Float3 worldMin, Float3 worldMax,
        int defaultArea = NavMeshAreas.Walkable, CancellationToken cancellation = default,
        IReadOnlyList<NavMeshAreaVolume>? volumes = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(sources);

        var results = new List<(int, int, List<byte[]>)>();
        RcConfig cfg = CreateConfig(data.Settings, defaultArea);

        if (!TryPrepareRebuild(data, sources, defaultArea, volumes, worldMin, worldMax, cfg,
            out ProwlInputGeomProvider? geom, out RcVec3f bmin, out RcVec3f bmax,
            out int minTx, out int maxTx, out int minTz, out int maxTz))
            return results;

        for (int tz = minTz; tz <= maxTz; tz++)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                if (cancellation.IsCancellationRequested) return results;
                List<byte[]> layers = geom == null ? [] : NavMeshTileBuilder.BuildTileLayers(geom, cfg, bmin, bmax, tx, tz);
                results.Add((tx, tz, layers));
            }
        }

        return results;
    }

    /// <summary>
    /// Prologue of the partial-rebuild path: builds the geometry provider, applies volumes, and
    /// derives the affected tile range. The grid-anchoring invariant lives HERE and only here:
    /// <para/>
    /// Sources may legitimately be empty (a region walled in completely) — the provider is
    /// then null and the affected tiles are EMPTIED; "no geometry" must not be conflated with
    /// "no change". The tile grid is anchored in XZ to the ORIGINAL bake bounds (fresh
    /// geometry bounds would shift tile (0,0) and misalign every tile against the live
    /// navmesh), while the Y range follows the CURRENT geometry — Recast clips rasterized
    /// spans to the heightfield's vertical range, so new geometry above the original bounds
    /// (a wall dropped on a flat floor) would silently vanish from the rebuild. (With no
    /// geometry the Y union is skipped: an empty provider reports (0,0,0) bounds, which would
    /// spuriously widen bakes that don't straddle Y=0.) The affected range expands by the
    /// erosion border, and a changed region entirely OUTSIDE the baked bounds returns false —
    /// clamping it would drag the tile range onto the nearest edge column and rebuild healthy
    /// edge tiles against sources that don't cover them. Easy to hit from destructible-world
    /// events near the map border.
    /// </summary>
    private static bool TryPrepareRebuild(NavMeshData data, IReadOnlyList<NavMeshGeometrySource> sources,
        int defaultArea, IReadOnlyList<NavMeshAreaVolume>? volumes, Float3 worldMin, Float3 worldMax,
        RcConfig cfg, out ProwlInputGeomProvider? geom, out RcVec3f bmin, out RcVec3f bmax,
        out int minTx, out int maxTx, out int minTz, out int maxTz)
    {
        minTx = maxTx = minTz = maxTz = 0;
        float cs = data.Settings.EffectiveVoxelSize;

        int inputTriangles = 0;
        for (int i = 0; i < sources.Count; i++)
            inputTriangles += sources[i].TriangleCount;
        geom = inputTriangles > 0 ? new ProwlInputGeomProvider(sources, defaultArea) : null;
        if (geom != null && geom.TriangleCount == 0) geom = null; // all triangles were degenerate/dropped
        if (geom != null) AddVolumes(geom, volumes); // volumes only re-mark rasterized geometry

        bmin = new RcVec3f((float)data.BoundsMin.X, (float)data.BoundsMin.Y, (float)data.BoundsMin.Z);
        bmax = new RcVec3f((float)data.BoundsMax.X, (float)data.BoundsMax.Y, (float)data.BoundsMax.Z);
        if (geom != null)
        {
            bmin.Y = Math.Min(bmin.Y, geom.GetMeshBoundsMin().Y);
            bmax.Y = Math.Max(bmax.Y, geom.GetMeshBoundsMax().Y);
        }

        float ts = data.TileWorldSize;
        if (ts <= 0) return false;
        RcRecast.CalcGridSize(bmin, bmax, cs, out int gridX, out int gridZ);
        int tilesX = (gridX + cfg.TileSizeX - 1) / cfg.TileSizeX;
        int tilesZ = (gridZ + cfg.TileSizeZ - 1) / cfg.TileSizeZ;

        float border = cfg.BorderSize * cs;
        if ((float)worldMax.X + border < bmin.X || (float)worldMin.X - border > bmax.X
            || (float)worldMax.Z + border < bmin.Z || (float)worldMin.Z - border > bmax.Z)
            return false;

        minTx = Math.Clamp((int)MathF.Floor(((float)worldMin.X - border - bmin.X) / ts), 0, tilesX - 1);
        maxTx = Math.Clamp((int)MathF.Floor(((float)worldMax.X + border - bmin.X) / ts), 0, tilesX - 1);
        minTz = Math.Clamp((int)MathF.Floor(((float)worldMin.Z - border - bmin.Z) / ts), 0, tilesZ - 1);
        maxTz = Math.Clamp((int)MathF.Floor(((float)worldMax.Z + border - bmin.Z) / ts), 0, tilesZ - 1);
        return true;
    }

    /// <summary>Hand area volumes to the provider as Recast convex volumes; the stock pipeline
    /// applies them to the compact heightfield after rasterization (RcBuilder.Build →
    /// MarkConvexPolyArea), which only re-marks spans geometry produced — Not Walkable maps to
    /// the null area and erases them.</summary>
    private static void AddVolumes(ProwlInputGeomProvider geom, IReadOnlyList<NavMeshAreaVolume>? volumes)
    {
        if (volumes == null) return;
        foreach (NavMeshAreaVolume volume in volumes)
        {
            if (volume.Footprint == null || volume.Footprint.Length < 3) continue;
            float[] verts = new float[volume.Footprint.Length * 3];
            for (int i = 0; i < volume.Footprint.Length; i++)
            {
                verts[i * 3 + 0] = (float)volume.Footprint[i].X;
                verts[i * 3 + 1] = volume.MinY;
                verts[i * 3 + 2] = (float)volume.Footprint[i].Z;
            }
            geom.AddConvexVolume(new RcConvexVolume
            {
                verts = verts,
                hmin = volume.MinY,
                hmax = volume.MaxY,
                areaMod = new RcAreaModification(ProwlInputGeomProvider.DetourAreaFor(volume.Area)),
            });
        }
    }

    private static RcConfig CreateConfig(NavMeshBuildSettings settings, int defaultArea)
    {
        float cs = settings.EffectiveVoxelSize;
        int tileVoxels = settings.EffectiveTileSize;

        // Contouring, polygonization and detail sampling all happen in the TileCache at runtime,
        // which uses its own fixed parameters — the values RcConfig needs for those stages are
        // never read on this path. Only rasterization, filtering, erosion and region culling are.
        return new RcConfig(
            useTiles: true,
            tileSizeX: tileVoxels,
            tileSizeZ: tileVoxels,
            borderSize: RcConfig.CalcBorder(settings.AgentRadius, cs),
            partition: RcPartition.WATERSHED,
            cellSize: cs,
            cellHeight: settings.EffectiveVoxelHeight,
            agentMaxSlope: settings.AgentMaxSlope,
            agentHeight: settings.AgentHeight,
            agentRadius: settings.AgentRadius,
            agentMaxClimb: settings.AgentMaxClimb,
            minRegionArea: settings.MinRegionArea,
            mergeRegionArea: 0,
            edgeMaxLen: 0,
            edgeMaxError: settings.EdgeMaxError,
            vertsPerPoly: NavMeshTileBuilder.VertsPerPoly,
            detailSampleDist: 0,
            detailSampleMaxError: 0,
            filterLowHangingObstacles: settings.FilterLowHangingObstacles,
            filterLedgeSpans: settings.FilterLedgeSpans,
            filterWalkableLowHeightSpans: settings.FilterWalkableLowHeightSpans,
            walkableAreaMod: new RcAreaModification(ProwlInputGeomProvider.DetourAreaFor(defaultArea)),
            buildMeshDetail: false);
    }

    /// <summary>
    /// Pin the tile size the bake will actually use into <paramref name="settings"/>, so the
    /// bake, the serialized asset, the TileCache instantiated from it, and later tile rebuilds
    /// all read one agreed value.
    /// <para/>
    /// A compressed layer header stores the layer's grid dimensions as BYTES, so a tile wider
    /// than <see cref="NavMeshBuildSettings.MaxTileSize"/> voxels wraps and decompresses as an
    /// empty layer: the bake reports success and the navmesh comes out with no polygons at all.
    /// <see cref="NavMeshBuildSettings.EffectiveTileSize"/> clamps to keep that unreachable;
    /// this reports it when the clamp actually moved a value the user asked for.
    /// </summary>
    private static void ResolveTileSize(NavMeshBuildSettings settings)
    {
        int resolved = settings.EffectiveTileSize;

        if (settings.OverrideTileSize && settings.TileSize != resolved)
            Debug.LogWarning($"[Navigation] Tile size must be 16..{NavMeshBuildSettings.MaxTileSize} voxels (a layer header stores tile dimensions in a byte, and tiles below 16 are all border); {settings.TileSize} was clamped to {resolved}. Carving cost scales with tile size, so smaller is usually better within that range.");

        settings.OverrideTileSize = true;
        settings.TileSize = resolved;
    }

    // Tile/poly capacity split: Detour packs tile id + poly id into one reference, so bits
    // given to tiles are taken from polys. 22 total id bits, tile bits capped at 14
    // (the Recast demos' arithmetic).

    private static int GetMaxTiles(RcVec3f bmin, RcVec3f bmax, float cellSize, int tileSize)
        => 1 << GetTileBits(bmin, bmax, cellSize, tileSize);

    private static int GetMaxPolysPerTile(RcVec3f bmin, RcVec3f bmax, float cellSize, int tileSize)
        => 1 << (22 - GetTileBits(bmin, bmax, cellSize, tileSize));

    private static int GetTileBits(RcVec3f bmin, RcVec3f bmax, float cellSize, int tileSize)
    {
        RcRecast.CalcGridSize(bmin, bmax, cellSize, out int sizeX, out int sizeZ);
        int tilesX = (sizeX + tileSize - 1) / tileSize;
        int tilesZ = (sizeZ + tileSize - 1) / tileSize;
        return Math.Min(DtUtils.Ilog2(DtUtils.NextPow2(tilesX * tilesZ)), 14);
    }
}
