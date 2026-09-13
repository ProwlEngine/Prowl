// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// A baked navmesh's tile data: everything <see cref="NavMeshQuery"/> needs to reconstruct a
/// persistent, tiled Detour navmesh backed by a <c>DtTileCache</c> - the real thing this engine's
/// dynamic obstacle carving and large-world support rides on, not a hand-rolled substitute. Each
/// <see cref="NavMeshTileLayer"/> is a compressed heightfield layer for one tile coordinate, produced
/// once at bake time; reconstructing the live navmesh from them (<c>AddTile</c> + <c>BuildNavMeshTile</c>
/// per layer) is a cheap, repeatable operation - see <see cref="NavMeshQuery"/>'s own doc comment.
/// </summary>
public sealed class NavMeshTileCacheData
{
    /// <summary>World-space origin every tile coordinate is measured from - the min corner of the
    /// baked geometry's bounds.</summary>
    public Float3 Origin;

    /// <summary>World-space width/depth of one square tile (<see cref="TileSizeInCells"/> times the
    /// bake's cell size).</summary>
    public float TileWorldSize;

    /// <summary>Width/depth of one tile, in voxel cells.</summary>
    public int TileSizeInCells;

    /// <summary>Horizontal voxel size this was baked with.</summary>
    public float CellSize;

    /// <summary>Vertical voxel size this was baked with.</summary>
    public float CellHeight;

    /// <summary>The baking agent's height, radius and max climb - <c>DtTileCache</c> needs these again
    /// itself (not just at initial bake) to rebuild a tile's polygon mesh whenever an obstacle changes it.</summary>
    public float WalkableHeight;
    public float WalkableRadius;
    public float WalkableClimb;

    /// <summary>Maximum contour simplification error, needed again for the same reason as
    /// <see cref="WalkableHeight"/> above.</summary>
    public float MaxSimplificationError;

    /// <summary>Upper bound on live tiles/polygons/obstacles <c>DtNavMesh</c>/<c>DtTileCache</c> size
    /// their internal storage for - generous headroom over what was actually baked, so a later
    /// <see cref="NavMeshObstacle"/> always has room to register.</summary>
    public int MaxTiles;
    public int MaxPolys;
    public int MaxObstacles;

    /// <summary>Every compressed tile layer this bake produced. More than one entry can share the same
    /// (X, Y) tile coordinate for tall, multi-story geometry - each becomes its own navmesh tile.</summary>
    public List<NavMeshTileLayer> Layers = [];

    /// <summary>Jump/drop links <see cref="NavMeshLinkGenerator"/> produced for this bake, if the agent
    /// type baked with had <see cref="NavMeshBakeSettings.JumpDistance"/> or <see cref="NavMeshBakeSettings.DropHeight"/>
    /// set. Replaced wholesale on every bake that regenerates them; a <see cref="NavMeshLink"/> component
    /// placed in the scene is never touched by this and is woven in separately, alongside these, by
    /// <see cref="NavMeshQuery"/>.</summary>
    public List<NavMeshLinkData> GeneratedLinks = [];
}

/// <summary>One compressed <c>DtTileCache</c> heightfield layer for a single tile coordinate, ready to
/// hand to <c>DtTileCache.AddTile</c> as-is.</summary>
public sealed class NavMeshTileLayer
{
    public int TileX;
    public int TileY;
    public byte[] CompressedData = [];
}
