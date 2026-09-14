// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

using Prowl.Recast.Core;
using Prowl.Recast.Detour;
using Prowl.Recast.Detour.TileCache;
using Prowl.Recast.Detour.TileCache.Io.Compress;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A baked navmesh as a standalone <c>.navmesh</c> asset: the compressed layers plus everything
/// needed to reinstantiate a <see cref="DtNavMesh"/> at load time. Built by
/// <see cref="NavMeshBuilder"/> and registered through <see cref="NavMeshWorld.AddNavMeshData"/>.
/// Independent of any scene, so a procedural world can build one at runtime.
/// </summary>
public sealed class NavMeshData : EngineObject
{
    /// <summary>One serialized Detour tile.</summary>
    public sealed class NavMeshTile
    {
        public int X;
        public int Z;
        public byte[] Data = [];
    }

    /// <summary>Current asset format version. Bump when the tile byte format or the shape of the
    /// serialized settings changes, so stale assets fail with a clear message instead of loading
    /// wrong. Version 2 split <see cref="Settings"/> into an agent envelope and overrides.</summary>
    public const int CurrentFormatVersion = 2;

    /// <summary>Oldest format version this engine still reads. Anything older must be rebaked.</summary>
    public const int MinReadableFormatVersion = 2;

    /// <summary>The format version this asset was serialized with.</summary>
    public int FormatVersion = CurrentFormatVersion;

    /// <summary>The settings this navmesh was built with (a snapshot — later inspector edits
    /// to a surface do not retroactively change it). Rebuilds reuse these for consistency.</summary>
    public NavMeshBuildSettings Settings = new();

    /// <summary>World-space bounds of the baked geometry.</summary>
    public Float3 BoundsMin;

    /// <summary>World-space bounds of the baked geometry.</summary>
    public Float3 BoundsMax;

    /// <summary>Origin of the tile grid (world space). Tile (x, z) starts at
    /// Origin + (x * TileWorldSize, 0, z * TileWorldSize).</summary>
    public Float3 Origin => BoundsMin;

    /// <summary>Side length of one tile in world units.</summary>
    public float TileWorldSize;

    /// <summary>Navmesh tile slots this asset was baked for. A slot holds one VERTICAL LAYER,
    /// not one grid tile, so this is the grid scaled by the layers a tile is expected to stack.
    /// <see cref="ResolveCapacity"/> is what instantiation actually uses.</summary>
    public int MaxTiles;

    /// <summary>Total id bits a Detour polygon reference splits between tile and polygon.</summary>
    internal const int TileAndPolyIdBits = 22;

    /// <summary>Ceiling on the tile half of that split (the Recast demos' arithmetic).</summary>
    internal const int MaxTileBits = 14;

    /// <summary>
    /// Compressed voxelization layers, one or more per tile. Each blob is self-describing (tile
    /// coordinates and layer index live in its header); a tile contributes several vertical
    /// layers where floors overlap. The TileCache contours these into Detour tiles, which is
    /// what lets an obstacle re-carve a tile without re-voxelizing the world.
    /// </summary>
    public List<NavMeshTile> CacheLayers = [];

    /// <summary>
    /// A copy for one consumer's private use, so runtime tile and link rewrites do not land on
    /// an asset every other consumer of the same <c>.navmesh</c> is reading. The blobs are
    /// shared rather than duplicated: a rebuild replaces entries in these lists and never edits
    /// one in place, so the copy costs two lists rather than the megabytes they point at.
    /// </summary>
    public NavMeshData Clone() => new()
    {
        Name = Name,
        FormatVersion = FormatVersion,
        Settings = Settings.Clone(),
        BoundsMin = BoundsMin,
        BoundsMax = BoundsMax,
        TileWorldSize = TileWorldSize,
        MaxTiles = MaxTiles,
        CacheLayers = [.. CacheLayers],
        Links = [.. Links],
    };

    /// <summary>
    /// Baked tile coordinates overlapping <paramref name="worldBounds"/>, inclusive; false when
    /// none do. Intersecting with the tiles that were actually baked keeps the range bounded by
    /// the navmesh — a range taken straight from a caller's rect spans every coordinate in it,
    /// however few tiles exist.
    /// </summary>
    internal bool TryGetTileRange(AABB worldBounds, out int minTx, out int maxTx, out int minTz, out int maxTz)
    {
        minTx = maxTx = minTz = maxTz = 0;
        if (TileWorldSize <= 0) return false;

        int rx0 = (int)Math.Floor((worldBounds.Min.X - Origin.X) / TileWorldSize);
        int rx1 = (int)Math.Floor((worldBounds.Max.X - Origin.X) / TileWorldSize);
        int rz0 = (int)Math.Floor((worldBounds.Min.Z - Origin.Z) / TileWorldSize);
        int rz1 = (int)Math.Floor((worldBounds.Max.Z - Origin.Z) / TileWorldSize);

        bool found = false;
        foreach (NavMeshTile tile in CacheLayers)
        {
            if (tile.X < rx0 || tile.X > rx1 || tile.Z < rz0 || tile.Z > rz1) continue;
            if (!found)
            {
                minTx = maxTx = tile.X;
                minTz = maxTz = tile.Z;
                found = true;
                continue;
            }

            minTx = Math.Min(minTx, tile.X);
            maxTx = Math.Max(maxTx, tile.X);
            minTz = Math.Min(minTz, tile.Z);
            maxTz = Math.Max(maxTz, tile.Z);
        }

        return found;
    }

    /// <summary>
    /// Off-mesh links. Tiles are rebuilt from geometry-only layers whenever an obstacle carves
    /// or a region regenerates — anything baked into them is regenerated away — so links live
    /// here and are re-injected on every tile build. Kept in step with the live
    /// <see cref="NavMeshLink"/>s by <see cref="NavMeshSurface.RebuildLinkTiles"/>.
    /// </summary>
    public List<NavMeshLinkSource> Links = [];

    /// <summary>True when there is at least one layer to instantiate.</summary>
    public bool HasTiles => CacheLayers != null && CacheLayers.Count > 0;

    private void ValidateVersion()
    {
        if (FormatVersion < MinReadableFormatVersion || FormatVersion > CurrentFormatVersion)
            throw new InvalidOperationException($"NavMeshData '{Name}' has tile format version {FormatVersion}; this engine reads versions {MinReadableFormatVersion}..{CurrentFormatVersion}. Rebake the navmesh.");
    }

    /// <summary>
    /// The tile and polygon capacities this asset instantiates with. Every vertical layer occupies
    /// its own navmesh tile slot, and multi-layer tiles (overlapping floors, bridges) are the point
    /// of the layer set, so the budget must exceed the baked layer count rather than merely reach
    /// it: a rebuild that stacks a new layer has to land somewhere. An asset whose layers already
    /// fill its baked budget (one baked before the budget carried the layer factor, or a scene that
    /// stacks deeper than the bake allowed for) is re-sized from its actual layer count, splitting
    /// the shared id bits with the same arithmetic the bake used.
    /// <para/>
    /// The tile cache is created with the same tile count, so a layer the cache accepts always has
    /// somewhere to land. Detour drops tiles past capacity through a status the cache discards, so
    /// the two must never disagree.
    /// </summary>
    internal (int MaxTiles, int MaxPolys) ResolveCapacity()
    {
        int slots = Math.Max(1, MaxTiles);
        if (CacheLayers.Count >= slots)
            slots = CacheLayers.Count * DtTileCacheLayer.EXPECTED_LAYERS_PER_TILE;

        int tileBits = TileBitsFor(slots);
        return (1 << tileBits, 1 << (TileAndPolyIdBits - tileBits));
    }

    /// <summary>How many of the shared reference bits a navmesh with this many tile slots gives to tiles.</summary>
    internal static int TileBitsFor(int slots) => Math.Min(DtUtils.Ilog2(DtUtils.NextPow2(slots)), MaxTileBits);

    private DtNavMesh CreateEmptyNavMesh()
    {
        (int maxTiles, int maxPolys) = ResolveCapacity();

        var navMesh = new DtNavMesh();
        var navParams = new DtNavMeshParams
        {
            orig = Origin.ToRc(),
            tileWidth = TileWorldSize,
            tileHeight = TileWorldSize,
            maxTiles = maxTiles,
            maxPolys = maxPolys,
        };

        DtStatus status = navMesh.Init(navParams, NavMeshTileBuilder.VertsPerPoly);
        if (status.Failed())
            throw new InvalidOperationException($"Failed to initialize DtNavMesh from NavMeshData '{Name}': {status}");
        return navMesh;
    }

    /// <summary>
    /// Triangulate this baked navmesh without registering it — for editor gizmos and tooling
    /// that need to visualize an asset the scene isn't running. Instantiates a throwaway
    /// navmesh, so cache the result rather than calling it per frame.
    /// </summary>
    public NavMeshTriangulation CalculateTriangulation()
    {
        if (!HasTiles) return NavMeshTriangulation.Empty;
        try
        {
            // The layers only become polygons once a cache contours them, so this instantiates
            // one that carves nothing and is discarded with the navmesh it built.
            return NavMeshTriangulation.FromNavMesh(CreateTileCache(1).GetNavMesh());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Navigation] Could not triangulate NavMeshData '{Name}': {e.Message}");
            return NavMeshTriangulation.Empty;
        }
    }

    /// <summary>
    /// Instantiate a TileCache (and its owned navmesh) from the compressed layers, seeded
    /// synchronously so the mesh is queryable immediately. Obstacles added later rebuild
    /// affected tiles incrementally via <c>DtTileCache.Update</c>.
    /// </summary>
    /// <param name="maxObstacles">Obstacle capacity the cache is created with.</param>
    public DtTileCache CreateTileCache(int maxObstacles) => CreateTileCache(maxObstacles, out _);

    /// <inheritdoc cref="CreateTileCache(int)"/>
    /// <param name="maxObstacles">Obstacle capacity the cache is created with.</param>
    /// <param name="meshProcess">The cache's link registry, so live <see cref="NavMeshLink"/>s
    /// can update the connections that later tile builds inject.</param>
    internal DtTileCache CreateTileCache(int maxObstacles, out NavMeshTileBuilder.ProwlTileCacheMeshProcess meshProcess)
    {
        ValidateVersion();
        (int maxTiles, _) = ResolveCapacity();
        var option = new DtTileCacheParams
        {
            orig = Origin.ToRc(),
            cs = Settings.EffectiveVoxelSize,
            ch = Settings.EffectiveVoxelHeight,
            width = Settings.EffectiveTileSize,
            height = Settings.EffectiveTileSize,
            walkableHeight = Settings.Agent.Height,
            walkableRadius = Settings.Agent.Radius,
            walkableClimb = Settings.Agent.MaxClimb,
            maxSimplificationError = Settings.Overrides.EdgeMaxError,
            // Height detail, so polygons follow the surface instead of spanning flat between their
            // corners. Recast recommends sampling every six voxels, given here in world units as
            // the cache expects; zero is how it is told to skip detail.
            detailSampleDist = Settings.Overrides.BuildHeightDetail ? Settings.EffectiveVoxelSize * 6 : 0,
            detailSampleMaxError = Settings.EffectiveVoxelHeight,
            // Standard watershed contouring instead of the cache's monotone sweep: avoids slivers on
            // slopes. Thresholds are cell counts converted from world units, so voxel size does not
            // change the mesh between rebakes; the edge cap is loose on purpose, since over-splitting
            // floods flat floors with polygons and the crowd with portal corners.
            watershedPartition = true,
            minRegionArea = (int)(Settings.Overrides.MinRegionArea / (Settings.EffectiveVoxelSize * Settings.EffectiveVoxelSize)),
            mergeRegionArea = (int)(20f / (Settings.EffectiveVoxelSize * Settings.EffectiveVoxelSize)),
            maxEdgeLen = 24,
            // Exactly the navmesh's own layer capacity. A cache sized above it would accept blobs
            // the navmesh then drops on commit, reported only through a status the cache discards.
            maxTiles = maxTiles,
            maxObstacles = Math.Max(1, maxObstacles),
        };

        meshProcess = new NavMeshTileBuilder.ProwlTileCacheMeshProcess();
        meshProcess.SetLinks(Links, Settings.Agent.Radius);

        // FastLZ + cCompatibility layout, matching how NavMeshTileBuilder.BuildTileLayers compressed the blobs.
        var cache = new DtTileCache(option, new DtTileCacheStorageParams(RcByteOrder.LITTLE_ENDIAN, true),
            CreateEmptyNavMesh(), DtTileCacheCompressorFactory.Shared.Create(0), meshProcess);

        // Add every layer before meshing any: a seam is built from both sides' cells, so a tile
        // meshed while its neighbours are missing describes that seam differently than they will,
        // and the two surfaces end up a fraction of a voxel apart along an edge they share.
        var tileRefs = new List<long>(CacheLayers.Count);
        int dropped = 0;
        foreach (NavMeshTile layer in CacheLayers)
        {
            if (layer?.Data == null || layer.Data.Length == 0) continue;
            if (!cache.TryAddTile(layer.Data, 0, out long tileRef))
            {
                dropped++; // the cache is full; the layers that fit still load
                continue;
            }
            if (tileRef == 0)
            {
                Debug.LogWarning($"[Navigation] NavMeshData '{Name}': failed to add cache layer for tile ({layer.X}, {layer.Z}).");
                continue;
            }
            tileRefs.Add(tileRef);
        }
        if (dropped > 0)
            Debug.LogWarning($"[Navigation] NavMeshData '{Name}' has {CacheLayers.Count} cache layers but a navmesh can address {maxTiles}; {dropped} were dropped. Increase TileSize or shrink the bake bounds.");

        MeshTiles(cache, tileRefs);
        return cache;
    }

    /// <summary>
    /// Mesh every layer. This is the whole cost of registering a navmesh — 0.65–1.5 ms per tile, on
    /// the main thread inside OnEnable — so it fans out: the build half of a tile touches no navmesh
    /// state, and only the commit does. Safe here and nowhere else, because the mesh is not
    /// published until this returns, so no query can be running and nothing can carve.
    /// <para/>
    /// Commits run serially in ref order, which is what makes the result identical to a serial
    /// bake: tile linking follows the order tiles are added. Measured on a 256-tile bake: 85 ms
    /// against 500 ms, for 9% more allocation (a neighbour-layer cache per worker instead of one).
    /// </summary>
    private static void MeshTiles(DtTileCache cache, List<long> tileRefs)
    {
        var built = new DtMeshData?[tileRefs.Count];
        try
        {
            // Loop-local scratch rather than thread-static, as NavMeshBuilder.Build does: a
            // thread-static one would outlive the registration by the life of the pool thread,
            // pinning a tile's worth of decompressed layers per worker. Unlike that one this takes
            // the whole pool rather than leaving a core free — the thread that would use it is the
            // one blocked here.
            Parallel.For(0, tileRefs.Count,
                () => new DtTileCacheBuildScratch(),
                (int i, ParallelLoopState _, DtTileCacheBuildScratch scratch) =>
                {
                    built[i] = cache.BuildTileMeshData(tileRefs[i], scratch);
                    return scratch;
                },
                _ => { });
        }
        catch (AggregateException e) when (e.InnerException != null)
        {
            // Callers see the exception the tile build threw, not the parallel wrapper.
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }

        for (int i = 0; i < tileRefs.Count; i++)
            cache.CommitTile(tileRefs[i], built[i]);
    }
}
