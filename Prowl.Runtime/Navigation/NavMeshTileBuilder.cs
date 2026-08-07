// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;
using Prowl.Recast.Detour.TileCache;
using Prowl.Recast.Detour.TileCache.Io.Compress;
using Prowl.Recast;
using Prowl.Recast.Geom;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Turns one Recast build result into a serialized Detour tile. Poly areas are preserved
/// as-is (they already carry the Prowl area mapping from
/// <see cref="ProwlInputGeomProvider.DetourAreaFor"/>) and every polygon gets the walkable
/// flag, since inclusion/exclusion is the query filter's job.
/// </summary>
internal static class NavMeshTileBuilder
{
    /// <summary>The single poly flag Prowl sets on every built polygon. Detour ignores polys
    /// with zero flags, so something must be set; area-based filtering happens in
    /// <see cref="NavMeshQueryFilter"/> against the poly's area, not its flags.</summary>
    public const int PolyFlagWalkable = 1;

    /// <summary>Vertices per navmesh polygon. Fixed, not a setting: the TileCache builds its
    /// polygons at Detour's maximum and the navmesh must be initialized to match.</summary>
    public const int VertsPerPoly = 6;

    /// <summary>
    /// Per-thread reusable bake state. Tile building allocates a fixed working set per tile
    /// (heightfield spans, context bookkeeping) regardless of geometry; recycling it across
    /// tiles removes that churn from bake-heavy games (destructible maps rebuild tiles at
    /// gameplay frequency). Thread-static because full bakes build tiles in Parallel.For.
    /// </summary>
    private sealed class TileBuildScratch
    {
        public readonly RcContext Context = new();
        /// <summary>Recycled span pool pages, transplanted into each tile's heightfield. Grows
        /// to the largest tile's span count, then no span is ever allocated again.</summary>
        public RcSpanPool? SpanPools;
    }

    [ThreadStatic] private static TileBuildScratch? t_scratch;

    /// <summary>Chunk lists per area mesh overlapping this tile (parallel to
    /// <see cref="ProwlInputGeomProvider.AreaMeshes"/>), or null when nothing overlaps —
    /// collected once and shared by the empty-tile check and the rasterization pass.</summary>
    private static List<RcChunkyTriMeshNode>[]? CollectOverlappingChunks(ProwlInputGeomProvider geom, RcBuilderConfig builderCfg)
    {
        var tileMin = new RcVec2f(builderCfg.bmin.X, builderCfg.bmin.Z);
        var tileMax = new RcVec2f(builderCfg.bmax.X, builderCfg.bmax.Z);

        // Cheap pre-pass: on bounded bakes most tiles miss every area's XZ extent entirely,
        // and this rejects them with zero allocations (GetChunksOverlappingRect allocates its
        // return list even when empty).
        bool anyPossible = false;
        for (int i = 0; i < geom.AreaMeshes.Count; i++)
        {
            if (geom.AreaMeshes[i].OverlapsXZ(tileMin.X, tileMin.Y, tileMax.X, tileMax.Y))
            {
                anyPossible = true;
                break;
            }
        }
        if (!anyPossible)
            return null;

        var chunks = new List<RcChunkyTriMeshNode>[geom.AreaMeshes.Count];
        bool any = false;
        for (int i = 0; i < geom.AreaMeshes.Count; i++)
        {
            chunks[i] = geom.AreaMeshes[i].Mesh.GetChunksOverlappingRect(tileMin, tileMax);
            any |= chunks[i].Count > 0;
        }
        return any ? chunks : null;
    }

    private static RcHeightfield BuildHeightfieldPooled(ProwlInputGeomProvider geom, RcBuilderConfig builderCfg,
        List<RcChunkyTriMeshNode>[]? overlappingChunks, TileBuildScratch? scratch)
    {
        RcConfig cfg = builderCfg.cfg;
        var solid = new RcHeightfield(builderCfg.width, builderCfg.height, builderCfg.bmin, builderCfg.bmax, cfg.Cs, cfg.Ch, cfg.BorderSize);

        // Attach recycled span pool pages: every span in them is free (the previous tile's
        // heightfield was discarded), so the freelist is simply all of them.
        if (scratch?.SpanPools != null)
        {
            solid.pools = scratch.SpanPools;
            solid.freelist = NavMeshRasterizer.BuildFreeList(scratch.SpanPools);
        }

        if (overlappingChunks == null)
            return solid;

        float walkableSlopeCos = MathF.Cos(cfg.WalkableSlopeAngle / 180.0f * MathF.PI);
        for (int i = 0; i < geom.AreaMeshes.Count; i++)
        {
            ProwlInputGeomProvider.AreaMesh areaMesh = geom.AreaMeshes[i];
            float[] verts = areaMesh.Mesh.GetVerts();
            // Chunky-mesh culling: only triangles overlapping this tile (plus border) rasterize.
            foreach (RcChunkyTriMeshNode node in overlappingChunks[i])
            {
                NavMeshRasterizer.RasterizeTriangles(solid, verts, node.tris, node.tris.Length / 3,
                    walkableSlopeCos, areaMesh.DetourArea, cfg.WalkableClimb);
            }
        }

        return solid;
    }

    /// <summary>
    /// Build one tile's compressed layers: area-aware pooled rasterization, the standard
    /// filter + compact + erode + volume-marking steps, then heightfield layers compressed into
    /// self-describing blobs. Returns an empty list for tiles no geometry overlaps — the common
    /// case on bounded bakes of mostly-sealed worlds — without paying for a heightfield.
    /// Contours/polymeshes are NOT built here — the TileCache builds them per tile at runtime,
    /// which is what lets obstacles re-carve without re-voxelizing.
    /// </summary>
    public static List<byte[]> BuildTileLayers(ProwlInputGeomProvider geom, RcConfig cfg, RcVec3f bmin, RcVec3f bmax, int tileX, int tileZ)
    {
        var builderCfg = new RcBuilderConfig(cfg, bmin, bmax, tileX, tileZ);

        List<RcChunkyTriMeshNode>[]? overlappingChunks = CollectOverlappingChunks(geom, builderCfg);
        if (overlappingChunks == null)
            return [];

        TileBuildScratch scratch = t_scratch ??= new TileBuildScratch();
        RcHeightfield solid = BuildHeightfieldPooled(geom, builderCfg, overlappingChunks, scratch);
        RcContext ctx = scratch.Context;

        // Filter + compact + erode + convex volumes: the same steps RcBuilder runs before
        // region building, applied here because the layer path bypasses RcBuilder.Build.
        if (cfg.FilterLowHangingObstacles)
            RcFilters.FilterLowHangingWalkableObstacles(ctx, cfg.WalkableClimb, solid);
        if (cfg.FilterLedgeSpans)
            RcFilters.FilterLedgeSpans(ctx, cfg.WalkableHeight, cfg.WalkableClimb, solid);
        if (cfg.FilterWalkableLowHeightSpans)
            RcFilters.FilterWalkableLowHeightSpans(ctx, cfg.WalkableHeight, solid);

        RcCompactHeightfield chf = RcCompacts.BuildCompactHeightfield(ctx, cfg.WalkableHeight, cfg.WalkableClimb, solid);
        RcAreas.ErodeWalkableArea(ctx, cfg.WalkableRadius, chf);
        foreach (RcConvexVolume vol in geom.ConvexVolumes())
            RcAreas.MarkConvexPolyArea(ctx, vol.verts, vol.hmin, vol.hmax, vol.areaMod, chf);

        // Cull islands too small to stand on (Unity's Min Region Area). The layers themselves
        // are partitioned at runtime by the TileCache, so regions are built here only to find
        // the spans to erase: BuildRegions zeroes the region id of anything it culled, and
        // erasing those spans' areas keeps them out of the compressed layer for good. Regions
        // reaching a tile border are exempt, so an island spanning two tiles survives in both.
        if (cfg.MinRegionArea > 0)
        {
            // BuildLayerRegions, not the watershed pair: it is the partitioning Recast intends
            // for layer builds — which is what these tiles become — needs no distance field
            // (the expensive half of watershed, and this runs on every tile of every bake), and
            // zeroes culled region ids the same way, which is all the sweep below reads.
            RcRegions.BuildLayerRegions(ctx, chf, cfg.MinRegionArea);
            for (int i = 0; i < chf.spanCount; i++)
                if (chf.spans[i].reg == 0)
                    chf.areas[i] = RcRecast.RC_NULL_AREA;
        }

        RcLayers.BuildHeightfieldLayers(ctx, chf, cfg.BorderSize, cfg.WalkableHeight, out RcHeightfieldLayerSet lset);

        // Keep the (possibly grown) pool pages for the next tile on this thread.
        scratch.SpanPools = solid.pools;

        var blobs = new List<byte[]>();
        if (lset == null) return blobs;

        // Compatibility index 0 = the built-in FastLZ compressor; other indices return null
        // unless a custom compressor was registered. Must stay paired with the
        // cCompatibility: true storage layout below and in CreateTileCache.
        IRcCompressor compressor = DtTileCacheCompressorFactory.Shared.Create(0);
        for (int i = 0; i < lset.layers.Length; i++)
        {
            RcHeightfieldLayer layer = lset.layers[i];
            var header = new DtTileCacheLayerHeader
            {
                magic = DtTileCacheLayerHeader.DT_TILECACHE_MAGIC,
                version = DtTileCacheLayerHeader.DT_TILECACHE_VERSION,
                tx = tileX,
                ty = tileZ,
                tlayer = i,
                bmin = layer.bmin,
                bmax = layer.bmax,
                width = layer.width,
                height = layer.height,
                minx = layer.minx,
                maxx = layer.maxx,
                miny = layer.miny,
                maxy = layer.maxy,
                hmin = layer.hmin,
                hmax = layer.hmax,
            };
            blobs.Add(DtTileCacheBuilder.CompressTileCacheLayer(header, layer.heights, layer.areas, layer.cons,
                RcByteOrder.LITTLE_ENDIAN, cCompatibility: true, compressor));
        }
        return blobs;
    }

    /// <summary>
    /// Poly finishing for cache-built tiles: every polygon gets the walkable flag —
    /// inclusion/exclusion is the query filter's job, and areas already carry the Prowl mapping
    /// from the baked layers.
    /// <para/>
    /// This is also where the navmesh gets its off-mesh links. The cache re-contours a whole
    /// tile whenever an obstacle carves or a region regenerates, discarding anything previously
    /// built into it, so connections cannot be baked in once — they are re-supplied here on
    /// every tile build, and Detour keeps only those whose start point lands in the tile.
    /// </summary>
    public sealed class ProwlTileCacheMeshProcess : IDtTileCacheMeshProcess
    {
        private readonly List<(Float3 Start, Float3 End, float Radius, bool Bidirectional, int Area, int UserId)> _connections = [];

        /// <summary>
        /// Unbudgeted links a single tile's pool can absorb. Detour sizes that pool when the tile
        /// is built, from the connections whose endpoints are inside it — and it under-counts in
        /// BOTH directions. A connection that leaves the tile costs its source one extra link
        /// (the bidirectional back-link), and costs the tile it LANDS in one more, which that
        /// tile budgeted nothing for because the connection is not stored there. Past a handful
        /// the pool overflows and AddTile throws IndexOutOfRange — at instantiation, on an asset
        /// that baked and saved cleanly.
        /// <para/>
        /// DO NOT raise this without measuring. A tile's real spare capacity is whatever is left
        /// of <c>edgeCount + portalCount*2</c> after its own polygons are linked, so a sparse
        /// tile — two flat planes, one polygon each — has the least of it and sets the limit for
        /// everyone. Raising this to 5, 6 or 8 was tried: all three crash that geometry, while a
        /// denser 40x40 bake survives 8 arrivals happily. The bound cannot be derived either;
        /// the binding constraint is arrivals from up to eight neighbours, which are only
        /// visible from the global pass in <see cref="SetLinks"/>, before any tile exists to
        /// measure.
        /// </summary>
        private const int MaxTileCrossingConnections = 4;

        // Ration bookkeeping, reused across calls: connections charged to each tile, by grid
        // coordinate. Rationing runs once per link-set change, never per tile build.
        private readonly Dictionary<(int X, int Z), int> _tileBudget = [];
        private int _severedLinks;

        /// <summary>
        /// Replace the link set future tile builds inject. Call under the instance's write lock,
        /// then rebuild the tiles that should carry the change.
        /// <para/>
        /// Rationing happens HERE, once, rather than per tile build: a tile's pool is spent by
        /// connections arriving from any of its eight neighbours as well as by its own, and a
        /// tile build sees only its own. Every connection is charged to the tile it starts in
        /// and the tile it ends in, so a destination cannot be swamped by sources that each stay
        /// under the limit on their own. Lanes are handed out breadth first — every link keeps
        /// one before any link gets a second — so a wide link never crowds out another link.
        /// </summary>
        /// <param name="origin">Tile grid origin, from the asset.</param>
        /// <param name="tileWorldSize">Tile side length in world units, from the asset.</param>
        public void SetLinks(IReadOnlyList<NavMeshLinkSource>? links, float agentRadius, Float3 origin, float tileWorldSize)
        {
            _connections.Clear();
            _tileBudget.Clear();
            _severedLinks = 0;

            if (links == null || links.Count == 0) return;

            float radius = Math.Max(0.01f, agentRadius);
            float ts = tileWorldSize > 0 ? tileWorldSize : float.MaxValue; // ungridded: one tile
            List<(Float3 Start, Float3 End)> crossings = [];
            var lanes = new List<(Float3 Start, Float3 End, int Link)>();
            var linkFirstLane = new int[links.Count];

            for (int l = 0; l < links.Count; l++)
            {
                crossings.Clear();
                links[l].ExpandCrossings(radius, crossings);
                linkFirstLane[l] = lanes.Count;
                foreach ((Float3 start, Float3 end) in crossings)
                    lanes.Add((start, end, l));
            }

            (int, int) Tile(Float3 p) => ((int)MathF.Floor((float)(p.X - origin.X) / ts),
                                          (int)MathF.Floor((float)(p.Z - origin.Z) / ts));

            // A connection wholly inside one tile costs that tile nothing extra, so it is never
            // rationed; only the two ends of a crossing are charged.
            bool TryCharge((Float3 Start, Float3 End, int Link) lane, bool commit)
            {
                (int, int) from = Tile(lane.Start), to = Tile(lane.End);
                if (from == to) return true;
                _tileBudget.TryGetValue(from, out int a);
                _tileBudget.TryGetValue(to, out int b);
                if (a >= MaxTileCrossingConnections || b >= MaxTileCrossingConnections) return false;
                if (commit) { _tileBudget[from] = a + 1; _tileBudget[to] = b + 1; }
                return true;
            }

            Span<bool> taken = lanes.Count <= 256 ? stackalloc bool[lanes.Count] : new bool[lanes.Count];
            for (int l = 0; l < links.Count; l++)
            {
                int end = l + 1 < links.Count ? linkFirstLane[l + 1] : lanes.Count;
                bool got = false;
                for (int i = linkFirstLane[l]; i < end && !got; i++)
                {
                    if (!TryCharge(lanes[i], commit: true)) continue;
                    taken[i] = true;
                    got = true;
                }
                if (!got && end > linkFirstLane[l]) _severedLinks++;
            }

            // Spend what is left widening the links that got through.
            for (int l = 0; l < links.Count; l++)
            {
                int start = linkFirstLane[l], end = l + 1 < links.Count ? linkFirstLane[l + 1] : lanes.Count;
                bool linkIsIn = false;
                for (int i = start; i < end; i++) if (taken[i]) { linkIsIn = true; break; }
                if (!linkIsIn) continue;
                for (int i = start; i < end; i++)
                {
                    if (taken[i] || !TryCharge(lanes[i], commit: true)) continue;
                    taken[i] = true;
                }
            }

            for (int i = 0; i < lanes.Count; i++)
            {
                if (!taken[i]) continue;
                NavMeshLinkSource link = links[lanes[i].Link];
                _connections.Add((lanes[i].Start, lanes[i].End, radius, link.Bidirectional,
                    ProwlInputGeomProvider.DetourAreaFor(link.Area), link.UserId));
            }

            if (_severedLinks > 0)
                Debug.LogWarning($"[Navigation] {_severedLinks} NavMeshLink(s) cross a navmesh tile boundary already carrying the {MaxTileCrossingConnections} connections its link pool holds, and were dropped — agents cannot use them. Spread the links out, move them off the tile edge, or bake with a larger tile size.");
        }

        public void Process(DtNavMeshCreateParams option)
        {
            for (int i = 0; i < option.polyCount; i++)
                option.polyFlags[i] = PolyFlagWalkable;

            if (_connections.Count == 0) return;

            // Detour keeps only the connections whose START point lies in the tile (an XZ test),
            // so do that first: handing over every link on the map would allocate six arrays
            // sized by the whole set for a tile that usually contains none of them. The tile box
            // is widened by each connection's radius so this can never be stricter than the
            // classification it front-runs. Rationing already happened in SetLinks — everything
            // still here is approved.
            int count = 0;
            for (int i = 0; i < _connections.Count; i++)
                if (StartsInTile(_connections[i], option)) count++;
            if (count == 0) return;

            option.offMeshConCount = count;
            option.offMeshConVerts = new float[count * 6];
            option.offMeshConRad = new float[count];
            option.offMeshConDir = new int[count];
            option.offMeshConAreas = new int[count];
            option.offMeshConFlags = new int[count];
            option.offMeshConUserID = new int[count];
            int w = 0;
            for (int i = 0; i < _connections.Count; i++)
            {
                if (!StartsInTile(_connections[i], option)) continue;
                (Float3 start, Float3 end, float radius, bool bidir, int area, int userId) = _connections[i];
                option.offMeshConVerts[6 * w + 0] = (float)start.X;
                option.offMeshConVerts[6 * w + 1] = (float)start.Y;
                option.offMeshConVerts[6 * w + 2] = (float)start.Z;
                option.offMeshConVerts[6 * w + 3] = (float)end.X;
                option.offMeshConVerts[6 * w + 4] = (float)end.Y;
                option.offMeshConVerts[6 * w + 5] = (float)end.Z;
                option.offMeshConRad[w] = radius;
                option.offMeshConDir[w] = bidir ? 1 : 0;
                option.offMeshConAreas[w] = area;
                option.offMeshConFlags[w] = PolyFlagWalkable;
                option.offMeshConUserID[w] = userId;
                w++;
            }
        }

        private static bool StartsInTile(
            (Float3 Start, Float3 End, float Radius, bool Bidirectional, int Area, int UserId) connection,
            DtNavMeshCreateParams option)
        {
            float margin = connection.Radius;
            return connection.Start.X >= option.bmin.X - margin && connection.Start.X <= option.bmax.X + margin
                && connection.Start.Z >= option.bmin.Z - margin && connection.Start.Z <= option.bmax.Z + margin;
        }
    }

    /// <summary>Create the DtTileCache wrapping a navmesh (unseeded — the caller adds the layer
    /// blobs), with the asset's links loaded into the mesh process so every tile it builds
    /// carries them.</summary>
    public static DtTileCache CreateTileCache(NavMeshData data, DtNavMesh navMesh, int maxObstacles)
        => CreateTileCache(data, navMesh, maxObstacles, out _);

    /// <inheritdoc cref="CreateTileCache(NavMeshData, DtNavMesh, int)"/>
    /// <param name="meshProcess">The cache's link registry, for keeping the navmesh in step
    /// with live <see cref="NavMeshLink"/>s after instantiation.</param>
    public static DtTileCache CreateTileCache(NavMeshData data, DtNavMesh navMesh, int maxObstacles,
        out ProwlTileCacheMeshProcess meshProcess)
    {
        NavMeshBuildSettings settings = data.Settings;
        var option = new DtTileCacheParams
        {
            orig = new RcVec3f((float)data.Origin.X, (float)data.Origin.Y, (float)data.Origin.Z),
            cs = settings.EffectiveVoxelSize,
            ch = settings.EffectiveVoxelHeight,
            width = settings.EffectiveTileSize,
            height = settings.EffectiveTileSize,
            walkableHeight = settings.AgentHeight,
            walkableRadius = settings.AgentRadius,
            walkableClimb = settings.AgentMaxClimb,
            maxSimplificationError = settings.EdgeMaxError,
            // Layer capacity: tiles can stack several vertical layers each, and the baked
            // layer count is a hard floor.
            maxTiles = Math.Max(Math.Max(1, data.MaxTiles) * DtTileCacheLayer.EXPECTED_LAYERS_PER_TILE, data.CacheLayers.Count),
            maxObstacles = Math.Max(1, maxObstacles),
        };

        meshProcess = new ProwlTileCacheMeshProcess();
        var links = new List<NavMeshLinkSource>(data.Links.Count);
        foreach (NavMeshData.NavMeshLinkEntry entry in data.Links)
            links.Add(entry.ToSource());
        meshProcess.SetLinks(links, data.Settings.AgentRadius, data.Origin, data.TileWorldSize);

        // FastLZ + cCompatibility layout, matching how BuildTileLayers compressed the blobs.
        return new DtTileCache(option, new DtTileCacheStorageParams(RcByteOrder.LITTLE_ENDIAN, true),
            navMesh, DtTileCacheCompressorFactory.Shared.Create(0), meshProcess);
    }

    /// <summary>
    /// Recompute every obstacle's touched-tile list from the cache's CURRENT tiles. Replacing
    /// a compressed tile bumps its salt, so refs captured when the obstacle was added go
    /// stale — without this, a regenerated tile would rebuild WITHOUT its carves (the rebuild
    /// checks membership in each obstacle's touched list). Call with the cache quiescent
    /// (Update() reporting up-to-date) so every obstacle is in its settled state.
    /// Replicates the private QueryTiles/CalcTightTileBounds pair from public API.
    /// </summary>
    public static void RefreshObstacleTouchedTiles(DtTileCache cache, NavMeshData data)
    {
        float cs = data.Settings.EffectiveVoxelSize;
        float tw = data.TileWorldSize;
        if (tw <= 0) return;

        for (int i = 0; i < cache.GetObstacleCount(); i++)
        {
            DtTileCacheObstacle ob = cache.GetObstacle(i);
            if (ob.state != DtObstacleState.DT_OBSTACLE_PROCESSED) continue;

            RcVec3f bmin = default, bmax = default;
            cache.GetObstacleBounds(ob, ref bmin, ref bmax);
            ob.touched.Clear();

            int tx0 = (int)MathF.Floor((bmin.X - (float)data.Origin.X) / tw);
            int tx1 = (int)MathF.Floor((bmax.X - (float)data.Origin.X) / tw);
            int tz0 = (int)MathF.Floor((bmin.Z - (float)data.Origin.Z) / tw);
            int tz1 = (int)MathF.Floor((bmax.Z - (float)data.Origin.Z) / tw);
            for (int tz = tz0; tz <= tz1; tz++)
            {
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    foreach (long tileRef in cache.GetTilesAt(tx, tz))
                    {
                        DtTileCacheLayerHeader? header = cache.GetTileByRef(tileRef)?.header;
                        if (header == null) continue;
                        // Tight tile bounds (CalcTightTileBounds is internal upstream).
                        var tbmin = new RcVec3f(header.bmin.X + header.minx * cs, header.bmin.Y, header.bmin.Z + header.miny * cs);
                        var tbmax = new RcVec3f(header.bmin.X + (header.maxx + 1) * cs, header.bmax.Y, header.bmin.Z + (header.maxy + 1) * cs);
                        if (DtUtils.OverlapBounds(bmin, bmax, tbmin, tbmax))
                            ob.touched.Add(tileRef);
                    }
                }
            }
        }
    }

}
