// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Recast;
using Prowl.Recast.Detour.TileCache;
using Prowl.Recast.Detour.TileCache.Io.Compress;
using Prowl.Recast.Geom;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Builds a navmesh with Recast's voxelize / filter / region / contour / polygonize pipeline, via
/// DotRecast (a managed port of Recast) - specifically its tiled <c>DtTileCache</c> path, not the
/// single-mesh "solo" one: every bake is tiled (see <see cref="NavMeshBakeSettings.TileSize"/>), and the
/// output is compressed per-tile heightfield layers (<see cref="NavMeshTileCacheData"/>), not a single
/// static polygon mesh - <see cref="NavMeshQuery"/> reconstructs a persistent, tiled Detour navmesh from
/// them, and a runtime <see cref="NavMeshObstacle"/> carves directly into that live navmesh's own
/// <c>DtTileCache</c> without ever touching this class again. This is the only file in the engine that
/// touches a <c>Prowl.Recast.*</c> type for the BAKE side of the pipeline (<see cref="NavMeshQuery"/> is
/// the other, for reconstructing a live navmesh from what this class produces) - every input and output
/// crossing this class's own boundary is an engine-owned type (<see cref="NavMeshBuildInput"/>,
/// <see cref="NavMeshBuildResult"/>, <see cref="NavMeshTileCacheData"/>), so a DotRecast version bump or
/// a swap to a different library only ever means touching these two files.
/// <para/>
/// <see cref="NavMeshBakeSettings.CellSize"/>/<see cref="NavMeshBakeSettings.CellHeight"/> need to be
/// fine enough to actually resolve a slope, not just small in absolute terms: a steep, long ramp
/// voxelized too coarsely can end up with adjacent columns whose floor heights differ by more than
/// <see cref="NavMeshBakeSettings.MaxStepHeight"/> purely from quantization, which breaks region
/// connectivity across the whole slope rather than just at its edges. If a bake with an otherwise
/// reasonable slope produces no walkable surface at all, try a smaller cell size before assuming the
/// geometry itself is the problem.
/// </summary>
public sealed class RecastNavMeshBuilder : INavMeshBuilder
{
    /// <summary>Widest a single generated polygon can be - Recast's own demo default. Also used by
    /// <see cref="NavMeshQuery"/> when reconstructing the persistent tiled navmesh these tiles get
    /// added to, since a mismatch there would silently misinterpret each tile's polygon data.</summary>
    internal const int MaxVertsPerPoly = 6;

    /// <summary>Smallest region (in voxel cells) Recast keeps rather than discarding as noise.</summary>
    private const int RegionMinSizeCells = 8;

    /// <summary>Regions smaller than this (in voxel cells) get merged into a neighbor instead of
    /// staying their own separate region.</summary>
    private const int RegionMergeSizeCells = 20;

    /// <summary>Longest an edge can be before contour simplification splits it.</summary>
    private const float EdgeMaxLen = 12.0f;

    /// <summary>Maximum distance a simplified edge is allowed to deviate from the original contour.</summary>
    private const float EdgeMaxError = 1.3f;

    /// <summary>Sampling distance for the detail mesh's height data.</summary>
    private const float DetailSampleDist = 6.0f;

    /// <summary>Maximum height deviation the detail mesh is allowed from the sampled surface.</summary>
    private const float DetailSampleMaxError = 1.0f;

    /// <summary>Floor on a tile's size, in voxel cells, regardless of how small
    /// <see cref="NavMeshBakeSettings.TileSize"/> is set - a tile smaller than this produces
    /// disproportionate fixed per-tile overhead for no real benefit.</summary>
    private const int MinTileSizeCells = 16;

    // NavMeshBakeSettings.TileSize is a world-space size, but a fine CellSize on the same setting
    // divides it into far more cells than intended if nothing else caps it - confirmed empirically:
    // an otherwise-reasonable 32-unit tile at a 0.1 cell size produces a 320-cell tile, which both
    // takes minutes to voxelize for one tile alone and overflows a fixed-size buffer inside
    // DtTileCacheBuilder's own contour walking, crashing the bake outright rather than just being
    // slow. Recast's own tile-cache samples never go this high either - a few hundred cells per side
    // is already generous for what a single tile is meant to be.
    /// <summary>Ceiling on a tile's size, in voxel cells, regardless of how fine
    /// <see cref="NavMeshBakeSettings.CellSize"/> makes it relative to <see cref="NavMeshBakeSettings.TileSize"/>.</summary>
    private const int MaxTileSizeCells = 128;

    /// <summary>Extra cells of padding a tile rasterizes beyond its own bounds, on top of the walkable
    /// radius, so neighboring tiles' borders line up and Detour actually links them - a tile baked with
    /// zero border size produces a navmesh where every tile is an island, unreachable from its
    /// neighbors (confirmed empirically: this is not a theoretical concern, it silently breaks
    /// cross-tile pathing outright rather than degrading it).</summary>
    private const int BorderPaddingCells = 3;

    // DtTileCache needs a compressor registered under whichever "compatibility" key its own storage
    // params request (see DtTileCacheStorageParams below) before it can compress or decompress a
    // single layer - shared and registered once, since every bake and every NavMeshQuery reconstruction
    // in the process uses the exact same compressor.
    private static readonly DtTileCacheCompressorFactory s_compressorFactory = CreateCompressorFactory();

    private static DtTileCacheCompressorFactory CreateCompressorFactory()
    {
        DtTileCacheCompressorFactory factory = DtTileCacheCompressorFactory.Shared;
        factory.TryAdd(0, DtTileCacheFastLzCompressor.Shared);
        factory.TryAdd(1, DtTileCacheFastLzCompressor.Shared);
        return factory;
    }

    /// <summary>The byte order / C-struct-compatibility every compressed tile layer in this engine is
    /// stored with - shared between baking (here) and reconstruction (<see cref="NavMeshQuery"/>), since
    /// a layer compressed one way cannot be decompressed the other.</summary>
    internal static DtTileCacheStorageParams StorageParams => new(Prowl.Recast.Core.RcByteOrder.LITTLE_ENDIAN, false);

    /// <summary>The same compressor every bake here used, for <see cref="NavMeshQuery"/> to decompress
    /// (and, for a runtime <see cref="NavMeshObstacle"/> carve, re-compress) tile layers with.</summary>
    internal static Prowl.Recast.Core.IRcCompressor Compressor => s_compressorFactory.Create(0);

    /// <summary>Bakes <paramref name="input"/> into tiled navmesh data using <paramref name="settings"/>,
    /// via Recast's voxelize / filter / region / contour / polygonize pipeline, tile by tile.</summary>
    public NavMeshBuildResult Build(NavMeshBuildInput input, NavMeshBakeSettings settings)
    {
        if (input.Triangles == null || input.Triangles.Length < 3)
            return NavMeshBuildResult.Failed("No input geometry to bake.");

        int triCount = input.TriangleCount;
        float[] verts = new float[input.Triangles.Length * 3];
        int[] tris = new int[triCount * 3];

        Float3 min = input.Triangles[0];
        Float3 max = input.Triangles[0];

        for (int i = 0; i < input.Triangles.Length; i++)
        {
            Float3 v = input.Triangles[i];
            verts[i * 3 + 0] = v.X;
            verts[i * 3 + 1] = v.Y;
            verts[i * 3 + 2] = v.Z;
            tris[i] = i;

            min = new Float3(Maths.Min(min.X, v.X), Maths.Min(min.Y, v.Y), Maths.Min(min.Z, v.Z));
            max = new Float3(Maths.Max(max.X, v.X), Maths.Max(max.Y, v.Y), Maths.Max(max.Z, v.Z));
        }

        var geom = new RcSampleInputGeomProvider(verts, tris);

        foreach (NavMeshAreaVolumeInput volume in input.AreaVolumes)
        {
            float[] flatVerts = new float[volume.FootprintXZ.Length * 3];
            for (int i = 0; i < volume.FootprintXZ.Length; i++)
            {
                flatVerts[i * 3 + 0] = volume.FootprintXZ[i].X;
                flatVerts[i * 3 + 1] = volume.MinY;
                flatVerts[i * 3 + 2] = volume.FootprintXZ[i].Z;
            }
            geom.AddConvexVolume(flatVerts, volume.MinY, volume.MaxY, new RcAreaModification(NavMeshAreas.ToRecastArea(volume.Area)));
        }

        int tileSizeCells = Maths.Clamp((int)MathF.Round(settings.TileSize / settings.CellSize), MinTileSizeCells, MaxTileSizeCells);
        int walkableRadiusCells = (int)MathF.Ceiling(settings.AgentRadius / settings.CellSize);
        int borderSizeCells = walkableRadiusCells + BorderPaddingCells;

        RcConfig cfg = new(
            useTiles: true, tileSizeX: tileSizeCells, tileSizeZ: tileSizeCells, borderSize: borderSizeCells,
            partition: RcPartition.WATERSHED,
            cellSize: settings.CellSize, cellHeight: settings.CellHeight,
            agentMaxSlope: settings.MaxSlopeAngle, agentHeight: settings.AgentHeight,
            agentRadius: settings.AgentRadius, agentMaxClimb: settings.MaxStepHeight,
            minRegionArea: RegionMinSizeCells, mergeRegionArea: RegionMergeSizeCells,
            edgeMaxLen: EdgeMaxLen, edgeMaxError: EdgeMaxError, vertsPerPoly: MaxVertsPerPoly,
            detailSampleDist: DetailSampleDist, detailSampleMaxError: DetailSampleMaxError,
            filterLowHangingObstacles: true, filterLedgeSpans: true, filterWalkableLowHeightSpans: true,
            walkableAreaMod: new RcAreaModification(NavMeshAreas.ToRecastArea(NavMeshAreas.Walkable)),
            buildMeshDetail: true);

        var layerBuilder = new DtTileCacheLayerBuilder(s_compressorFactory);

        List<DtTileCacheLayerBuildResult> results;
        try
        {
            results = layerBuilder.Build(geom, cfg, StorageParams, threads: 1, tw: tileSizeCells, th: tileSizeCells);
        }
        catch (Exception ex)
        {
            return NavMeshBuildResult.Failed($"Tile layer build failed: {ex.Message}");
        }

        var layers = new List<NavMeshTileLayer>();
        foreach (DtTileCacheLayerBuildResult result in results)
            foreach (byte[] compressed in result.layers)
                layers.Add(new NavMeshTileLayer { TileX = result.tx, TileY = result.ty, CompressedData = compressed });

        if (layers.Count == 0)
            return NavMeshBuildResult.Failed("No walkable surface survived filtering for the given bake settings.");

        var tileCacheData = new NavMeshTileCacheData
        {
            Origin = min,
            TileWorldSize = tileSizeCells * settings.CellSize,
            TileSizeInCells = tileSizeCells,
            CellSize = settings.CellSize,
            CellHeight = settings.CellHeight,
            WalkableHeight = settings.AgentHeight,
            WalkableRadius = settings.AgentRadius,
            WalkableClimb = settings.MaxStepHeight,
            MaxSimplificationError = EdgeMaxError,
            MaxTiles = NextPowerOfTwo(layers.Count * 4 + 64),
            MaxPolys = 1 << 16,
            MaxObstacles = 128,
            Layers = layers,
        };

        // Gated purely by the settings themselves - a project that never sets JumpDistance/DropHeight
        // pays nothing extra here and sees no behavior change at all.
        if (settings.JumpDistance > 0f || settings.DropHeight > 0f)
            tileCacheData.GeneratedLinks = NavMeshLinkGenerator.Generate(input, tileCacheData, settings);

        return new NavMeshBuildResult { Success = true, TileCacheData = tileCacheData, Bounds = new AABB(min, max) };
    }

    /// <summary>The smallest power of two at or above <paramref name="value"/> - Detour's own tiled
    /// navmesh sizes its internal tile-ref bit allocation from <c>DtNavMeshParams.maxTiles</c>, which it
    /// silently rounds up to a power of two anyway; rounding up here first keeps the value this class
    /// reports consistent with what actually gets allocated.</summary>
    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value) power <<= 1;
        return power;
    }
}
