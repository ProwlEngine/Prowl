// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// The parameters a navmesh is voxelized and built with: the agent's physical envelope plus
/// Recast rasterization detail. One instance describes one agent type; the project-wide agent
/// type table lives in navigation settings and surfaces reference an entry by <see cref="AgentTypeId"/>.
/// Defaults match Unity's Humanoid agent.
/// </summary>
public sealed class NavMeshBuildSettings
{
    /// <summary>Identifies the agent type this navmesh is built for. Agents only use navmeshes
    /// built for their own agent type.</summary>
    public int AgentTypeId = 0;

    /// <summary>Agent radius in world units. Walkable surfaces are eroded by this distance from walls.</summary>
    public float AgentRadius = 0.5f;

    /// <summary>Agent height in world units. Spaces lower than this are not walkable.</summary>
    public float AgentHeight = 2.0f;

    /// <summary>Maximum walkable slope angle in degrees.</summary>
    public float AgentMaxSlope = 45f;

    /// <summary>Maximum ledge height the agent can step up, in world units.</summary>
    public float AgentMaxClimb = 0.4f;

    /// <summary>When false, the voxel size is derived from the agent radius (radius / 3, matching
    /// Unity). Set true to use <see cref="VoxelSize"/> directly.</summary>
    public bool OverrideVoxelSize = false;

    /// <summary>Explicit XZ voxel size in world units, used when <see cref="OverrideVoxelSize"/> is set.</summary>
    [EnableIf(nameof(OverrideVoxelSize))]
    public float VoxelSize = 0.1666667f;

    /// <summary>When false, the tile size defaults to <see cref="DefaultTileSize"/> voxels. Set
    /// true to use <see cref="TileSize"/> directly.</summary>
    public bool OverrideTileSize = false;

    /// <summary>Tile side length in voxels, used when <see cref="OverrideTileSize"/> is set.
    /// Smaller tiles make partial rebuilds and carving cheaper but add per-tile overhead.
    /// Clamped to 16..<see cref="MaxTileSize"/>.</summary>
    [EnableIf(nameof(OverrideTileSize))]
    public int TileSize = DefaultTileSize;

    /// <summary>Regions with a surface area smaller than this (world units²) are culled.</summary>
    public float MinRegionArea = 2f;

    /// <summary>Maximum distance the simplified border may deviate from the raw contour, in voxels.</summary>
    public float EdgeMaxError = 1.3f;

    /// <summary>Remove spans over low hanging walkable obstacles (curbs, steps).</summary>
    public bool FilterLowHangingObstacles = true;

    /// <summary>Remove spans at ledges, preventing paths that overhang drops.</summary>
    public bool FilterLedgeSpans = true;

    /// <summary>Remove walkable spans with too little clearance above them.</summary>
    public bool FilterWalkableLowHeightSpans = true;

    /// <summary>Sample heights across each polygon so agents follow the surface it covers rather
    /// than a plane through its corners. Turn off only where the ground is flat or planar, which
    /// is where the detail costs build time and describes nothing the corners do not.</summary>
    public bool BuildHeightDetail = true;

    /// <summary>The XZ voxel size actually used for the build.</summary>
    public float EffectiveVoxelSize => OverrideVoxelSize ? Math.Max(0.01f, VoxelSize) : Math.Max(0.01f, AgentRadius / 3f);

    /// <summary>The voxel height actually used for the build (half the XZ voxel size).</summary>
    public float EffectiveVoxelHeight => EffectiveVoxelSize * 0.5f;

    /// <summary>The tile side length in voxels actually used for the build. A bake stores the
    /// resolved value back into its settings, so a baked asset always reports what was really
    /// used.</summary>
    public int EffectiveTileSize => OverrideTileSize ? Math.Clamp(TileSize, 16, MaxTileSize) : DefaultTileSize;

    /// <summary>Largest tile size a navmesh can represent: compressed layer headers store the
    /// layer's grid dimensions in a byte, and a wider tile wraps to an empty layer — a navmesh
    /// with no polygons at all, from a bake that reported success.</summary>
    public const int MaxTileSize = 255;

    /// <summary>Tile size used when nothing is overridden. Carving re-contours a whole tile, so
    /// tile size is the per-carve cost and the default stays well under the cap.</summary>
    public const int DefaultTileSize = 64;

    /// <summary>Snapshot copy, so a bake isn't mutated by later inspector edits. Written out
    /// field by field rather than memberwise: a reference field added later would be shared by
    /// every copy, and the first symptom would be one bake's settings changing under another.</summary>
    public NavMeshBuildSettings Clone() => new()
    {
        AgentTypeId = AgentTypeId,
        AgentRadius = AgentRadius,
        AgentHeight = AgentHeight,
        AgentMaxSlope = AgentMaxSlope,
        AgentMaxClimb = AgentMaxClimb,
        OverrideVoxelSize = OverrideVoxelSize,
        VoxelSize = VoxelSize,
        OverrideTileSize = OverrideTileSize,
        TileSize = TileSize,
        MinRegionArea = MinRegionArea,
        EdgeMaxError = EdgeMaxError,
        FilterLowHangingObstacles = FilterLowHangingObstacles,
        FilterLedgeSpans = FilterLedgeSpans,
        FilterWalkableLowHeightSpans = FilterWalkableLowHeightSpans,
        BuildHeightDetail = BuildHeightDetail,
    };
}

/// <summary>
/// The surface-level half of the bake parameters: rasterization detail that belongs to a
/// particular bake rather than to an agent type (whose envelope comes from the project-level
/// <see cref="NavMeshAgentTypes"/> table). Composed into a resolved
/// <see cref="NavMeshBuildSettings"/> by <see cref="NavMeshAgentTypes.GetBuildSettings"/>.
/// Defaults match Unity's; most bakes never need to touch these.
/// </summary>
public sealed class NavMeshBuildOverrides
{
    [Tooltip("Use an explicit voxel size instead of deriving it from the agent radius (radius / 3). Smaller voxels capture finer geometry and cost more bake time and memory.")]
    public bool OverrideVoxelSize = false;

    [Tooltip("Explicit XZ voxel size in world units, used when Override Voxel Size is on. The navmesh cannot represent features smaller than this.")]
    [EnableIf(nameof(OverrideVoxelSize))]
    public float VoxelSize = 0.1666667f;

    [Tooltip("Use an explicit tile size instead of the default (64 voxels). Smaller tiles make partial rebuilds and obstacle carving cheaper (less area re-voxelized per change) but add per-tile overhead.")]
    public bool OverrideTileSize = false;

    [Tooltip("Tile side length in voxels, used when Override Tile Size is on. Capped at 255 (a format limit: layer headers store tile dimensions in a byte). Carving re-contours a whole tile, so keep this small.")]
    [EnableIf(nameof(OverrideTileSize))]
    public int TileSize = NavMeshBuildSettings.DefaultTileSize;

    [Tooltip("Walkable regions with a surface area smaller than this (world units squared) are removed. Raise it to cull small isolated islands like table tops.")]
    public float MinRegionArea = 2f;

    [Tooltip("How far the simplified border may deviate from the raw voxel contour, in voxels. Lower is more faithful and produces more polygons.")]
    public float EdgeMaxError = 1.3f;

    [Tooltip("Treat low obstacles (curbs, steps) the agent can climb as walkable.")]
    public bool FilterLowHangingObstacles = true;

    [Tooltip("Remove walkable voxels at ledges, preventing paths that overhang drops.")]
    public bool FilterLedgeSpans = true;

    [Tooltip("Remove walkable voxels with too little clearance above them for the agent to stand.")]
    public bool FilterWalkableLowHeightSpans = true;

    [Tooltip("Sample heights across each polygon so agents follow the ground it covers. Without it a polygon is flat between its corners, which is exact on floors and ramps but stretches across curved ground like terrain. Costs build time on every tile, including each obstacle carve, so turn it off for scenes built entirely from flat and planar surfaces.")]
    public bool BuildHeightDetail = true;
}
