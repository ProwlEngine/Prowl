// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A baked navmesh as a standalone, serializable asset (stored as a <c>.navmesh</c> file):
/// the Detour tiles as raw bytes plus everything needed to reinstantiate a
/// <see cref="DtNavMesh"/> at load time. Produced by <see cref="NavMeshBuilder"/> in the
/// editor or at runtime, consumed by <see cref="NavMeshWorld.AddNavMeshData"/>. Like
/// <see cref="Resources.TerrainData"/>, the asset is independent of any scene — procedural
/// worlds can build one at runtime and register it without an editor bake.
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

    /// <summary>One off-mesh link the cache re-injects when it rebuilds a tile.
    /// The serializable mirror of <see cref="NavMeshLinkSource"/>.</summary>
    public sealed class NavMeshLinkEntry
    {
        public Float3 Start;
        public Float3 End;
        public float Width;
        public bool Bidirectional;
        public int Area = NavMeshAreas.Jump;
        public int UserId;

        public NavMeshLinkSource ToSource() => new(Start, End, Width, Bidirectional, Area, UserId);

        public static NavMeshLinkEntry From(NavMeshLinkSource source) => new()
        {
            Start = source.Start,
            End = source.End,
            Width = source.Width,
            Bidirectional = source.Bidirectional,
            Area = source.Area,
            UserId = source.UserId,
        };
    }

    /// <summary>Current serialized-tile format version. Bump when the tile byte format changes
    /// (e.g. a Prowl.Recast upgrade changing Detour's tile layout), so stale assets fail with a
    /// clear message instead of a deserialize throw.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Oldest format version this engine still reads. Anything older must be rebaked.</summary>
    public const int MinReadableFormatVersion = 1;

    /// <summary>The format version this asset's tiles were serialized with.</summary>
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
    public Float3 Origin;

    /// <summary>Side length of one tile in world units.</summary>
    public float TileWorldSize;

    /// <summary>Capacity the Detour navmesh is initialized with.</summary>
    public int MaxTiles;

    /// <summary>Per-tile polygon capacity the Detour navmesh is initialized with.</summary>
    public int MaxPolys;

    /// <summary>
    /// Compressed voxelization layers, one or more per tile. Each blob is self-describing (tile
    /// coordinates and layer index live in its header); a tile contributes several vertical
    /// layers where floors overlap. The TileCache contours these into Detour tiles, which is
    /// what lets an obstacle re-carve a tile without re-voxelizing the world.
    /// </summary>
    public List<NavMeshTile> CacheLayers = [];

    /// <summary>
    /// Off-mesh links. Tiles are rebuilt from geometry-only layers whenever an obstacle carves
    /// or a region regenerates — anything baked into them is regenerated away — so links live
    /// here and are re-injected on every tile build. Kept in step with the live
    /// <see cref="NavMeshLink"/>s by <see cref="NavMeshSurface.RebuildLinkTiles"/>.
    /// </summary>
    public List<NavMeshLinkEntry> Links = [];

    /// <summary>True when there is at least one layer to instantiate.</summary>
    public bool HasTiles => CacheLayers != null && CacheLayers.Count > 0;

    private void ValidateVersion()
    {
        if (FormatVersion < MinReadableFormatVersion || FormatVersion > CurrentFormatVersion)
            throw new InvalidOperationException($"NavMeshData '{Name}' has tile format version {FormatVersion}; this engine reads versions {MinReadableFormatVersion}..{CurrentFormatVersion}. Rebake the navmesh.");
    }

    private DtNavMesh CreateEmptyNavMesh()
    {
        int maxTiles = Math.Max(1, MaxTiles);
        int maxPolys = Math.Max(1, MaxPolys);
        if (CacheLayers.Count > maxTiles)
        {
            // Every vertical layer occupies its own navmesh tile slot, and multi-layer tiles
            // (overlapping floors, bridges) are the point of the layer set — size honestly
            // from the actual layer count, re-splitting the shared 22 id bits with the same
            // arithmetic the bake used (tile bits capped at 14).
            int tileBits = Math.Min(DtUtils.Ilog2(DtUtils.NextPow2(CacheLayers.Count)), 14);
            maxTiles = 1 << tileBits;
            maxPolys = 1 << (22 - tileBits);
        }

        var navMesh = new DtNavMesh();
        var navParams = new DtNavMeshParams
        {
            orig = new RcVec3f((float)Origin.X, (float)Origin.Y, (float)Origin.Z),
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
    public Prowl.Recast.Detour.TileCache.DtTileCache CreateTileCache(int maxObstacles)
        => CreateTileCache(maxObstacles, out _);

    /// <inheritdoc cref="CreateTileCache(int)"/>
    /// <param name="maxObstacles">Obstacle capacity the cache is created with.</param>
    /// <param name="meshProcess">The cache's link registry, so live <see cref="NavMeshLink"/>s
    /// can update the connections that later tile builds inject.</param>
    internal Prowl.Recast.Detour.TileCache.DtTileCache CreateTileCache(int maxObstacles,
        out NavMeshTileBuilder.ProwlTileCacheMeshProcess meshProcess)
    {
        ValidateVersion();
        DtNavMesh navMesh = CreateEmptyNavMesh();
        Prowl.Recast.Detour.TileCache.DtTileCache cache = NavMeshTileBuilder.CreateTileCache(this, navMesh, maxObstacles, out meshProcess);

        foreach (NavMeshTile layer in CacheLayers)
        {
            if (layer?.Data == null || layer.Data.Length == 0) continue;
            long tileRef = cache.AddTile(layer.Data, 0);
            if (tileRef == 0)
            {
                Debug.LogWarning($"[Navigation] NavMeshData '{Name}': failed to add cache layer for tile ({layer.X}, {layer.Z}).");
                continue;
            }
            cache.BuildNavMeshTile(tileRef);
        }

        return cache;
    }
}
