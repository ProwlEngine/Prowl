// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Threading.Tasks;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Marks a scene as having a navmesh to walk on, for one agent type, and holds the settings it was
/// (or should be) baked with. A scene can hold several surfaces, one per agent type it needs to
/// support - see <see cref="NavMeshSystem"/> for how a query or an agent finds the right one. Baking
/// itself is an editor-only operation (see the surface's custom inspector); at runtime this component
/// only loads the already-baked <see cref="NavMesh"/> asset and builds a <see cref="NavMeshQuery"/>
/// from it in <see cref="OnEnable"/>. Every bake is tiled (see <see cref="NavMeshBakeSettings.TileSize"/>),
/// so one surface already scales to a large scene on its own - there is no separate "region" concept to
/// opt into for that anymore.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Surface")]
public sealed class NavMeshSurface : MonoBehaviour
{
    /// <summary>The query built from <see cref="NavMeshAsset"/>, backing the public <see cref="Query"/>
    /// property. Null until the first successful <see cref="RebuildQuery"/>.</summary>
    private NavMeshQuery? _query;

    /// <summary>Which agent type (see <see cref="NavMeshAgentTypes"/>) this surface bakes for. The
    /// radius, height, max slope and max step height a bake uses all come from this type, not from a
    /// value stored on the surface itself, so every surface for a type stays in lockstep with it.
    /// Hidden from the default inspector - <c>NavMeshSurfaceEditor</c> draws it as a named dropdown
    /// instead of a raw id, since the id itself means nothing to whoever is editing a scene.</summary>
    [HideInInspector]
    public int AgentTypeId = NavMeshAgentTypes.HumanoidId;

    /// <summary>Voxel size on the horizontal plane. Per-surface rather than part of the agent type,
    /// since how finely a space needs to be resolved is a property of that space (a tight room, a
    /// steep ramp), not of who is walking it.</summary>
    public float CellSize = NavMeshBakeSettings.Default.CellSize;

    /// <summary>Voxel size along the vertical axis. See <see cref="CellSize"/>.</summary>
    public float CellHeight = NavMeshBakeSettings.Default.CellHeight;

    /// <summary>World-space width/depth of one baked navmesh tile. See
    /// <see cref="NavMeshBakeSettings.TileSize"/>.</summary>
    public float TileSize = NavMeshBakeSettings.Default.TileSize;

    /// <summary>Which GameObjects contribute geometry to a bake.</summary>
    public LayerMask LayerMask = LayerMask.Everything;

    /// <summary>Which GameObjects are eligible to contribute geometry, beyond <see cref="LayerMask"/>.
    /// <see cref="NavMeshCollectObjects.Volume"/> and <see cref="NavMeshCollectObjects.Children"/> read
    /// <see cref="Center"/>/<see cref="Size"/> and this surface's own GameObject, respectively.</summary>
    public NavMeshCollectObjects CollectObjects = NavMeshCollectObjects.All;

    /// <summary>The declared bake volume's center, local to this surface's transform. Only used when
    /// <see cref="CollectObjects"/> is <see cref="NavMeshCollectObjects.Volume"/>.</summary>
    public Float3 Center;

    /// <summary>The declared bake volume's size, local to this surface's transform (rotation and scale
    /// honoured). Only used when <see cref="CollectObjects"/> is <see cref="NavMeshCollectObjects.Volume"/>.</summary>
    public Float3 Size = new(10f, 10f, 10f);

    /// <summary>Which of a contributing object's own geometry sources a bake voxelizes.</summary>
    public NavMeshGeometrySource UseGeometry = NavMeshGeometrySource.RenderMeshes;

    /// <summary>The baked navmesh this surface serves. Assigned by the editor's Bake button; a build
    /// only ever reads it.</summary>
    public AssetRef<NavMesh> NavMeshAsset;

    /// <summary>Draws the baked walkable polygons in the scene view.</summary>
    public bool DrawGizmo = true;

    /// <summary>The query built from <see cref="NavMeshAsset"/>, or null if the asset is unassigned
    /// or has never been baked. Rebuilt whenever this component (re)enables.</summary>
    public NavMeshQuery? Query => _query;

    /// <summary>The bake settings a fresh bake of this surface would actually use: <see cref="AgentTypeId"/>'s
    /// registered radius/height/slope/step, combined with this surface's own <see cref="CellSize"/>/
    /// <see cref="CellHeight"/>/<see cref="TileSize"/>. Falls back to the built-in Humanoid type if
    /// <see cref="AgentTypeId"/> no longer names a registered type (it was removed after this surface
    /// was set up).</summary>
    public NavMeshBakeSettings EffectiveBakeSettings
    {
        get
        {
            NavMeshAgentTypeInfo type = NavMeshAgentTypes.GetById(AgentTypeId) ?? NavMeshAgentTypeInfo.CreateHumanoid();
            NavMeshBakeSettings settings = type.ToBakeSettings(CellSize, CellHeight);
            settings.TileSize = TileSize;
            return settings;
        }
    }

    /// <summary>True when <see cref="NavMeshAsset"/> holds a bake, but that bake was for a different
    /// agent type or used different settings than baking again right now would produce. False for an
    /// asset that has never been baked at all - there is nothing to compare against yet, so "stale"
    /// doesn't apply.</summary>
    public bool IsStale
    {
        get
        {
            NavMesh? mesh = NavMeshAsset.Res;
            if (mesh.IsNotValid() || !mesh.IsBuilt) return false;
            return mesh.AgentTypeId != AgentTypeId || mesh.BakeSettings != EffectiveBakeSettings;
        }
    }

    /// <summary>Builds <see cref="Query"/> from whatever <see cref="NavMeshAsset"/> currently holds and
    /// registers this surface with the scene's <see cref="NavMeshSystem"/>.</summary>
    public override void OnEnable()
    {
        RebuildQuery();

        if (Scene.IsValid())
            NavMeshSystem.GetOrCreate(Scene).RegisterSurface(this);
    }

    /// <summary>Unregisters this surface from the scene's <see cref="NavMeshSystem"/> and drops <see cref="Query"/>.</summary>
    public override void OnDisable()
    {
        if (Scene.IsValid())
            NavMeshSystem.GetOrCreate(Scene).UnregisterSurface(this);

        _query = null;
    }

    /// <summary>Re-resolves <see cref="NavMeshAsset"/> and rebuilds <see cref="Query"/> from it.
    /// Called automatically on enable; call again after reassigning <see cref="NavMeshAsset"/> at
    /// runtime (the editor's Bake button does this for you).</summary>
    public void RebuildQuery()
    {
        NavMesh? mesh = NavMeshAsset.Res;
        if (mesh.IsNotValid() || !mesh.IsBuilt)
        {
            _query = null;
            return;
        }

        List<NavMeshLinkData>? links = null;
        List<NavMeshLink>? linkSources = null;
        NavMeshCostLayer? costLayer = null;
        if (Scene.IsValid())
        {
            NavMeshSystem system = NavMeshSystem.GetOrCreate(Scene);

            links = [];
            linkSources = [];
            foreach (NavMeshLink link in system.GetLinks(AgentTypeId))
            {
                foreach (NavMeshLinkData data in link.ToLinkData())
                {
                    links.Add(data);
                    linkSources.Add(link);
                }
            }

            // Read, never created here: a query rebuild must never reset a cost layer's own entries just
            // because a rebake or a link change happened to trigger one, only ever pick up the same
            // instance NavMeshSystem already owns for this type - see NavMeshCostLayer's own doc comment.
            costLayer = system.GetOrCreateCostLayer(AgentTypeId);
        }

        _query = new NavMeshQuery(mesh, links, linkSources, costLayer);
    }

    /// <summary>Rebuilds only the baked tiles overlapping the box [<paramref name="center"/> &#177;
    /// <paramref name="size"/>/2] against current scene geometry, in place on the live query - no other
    /// tile is touched, and no crowd agent anywhere on this surface is disrupted, unlike <see cref="RebuildQuery"/>
    /// (which reconstructs the entire persistent navmesh from its stored layers, dropping every crowd
    /// built against the old one along with it). For a world that changes at gameplay time: a destroyed
    /// wall, a newly-built structure, a piece of streamed-in terrain.
    /// <para/>
    /// Recollects and rebakes this surface's entire scope, the same as a full bake - there is no cheaper
    /// way to voxelize only a region without a bounded, declared-bake-extent geometry provider this
    /// engine does not have (a real, open limitation, not hidden here). What this method actually saves
    /// is applying and disrupting only the tiles that changed, not the raster/voxelize cost of a full
    /// bake - so it is worth reaching for when a change is small and localized but the surface's crowds
    /// must not be interrupted, not as a general performance win over baking.
    /// <para/>
    /// Returns false, doing nothing, if this surface has never been baked, or if the freshly-collected
    /// geometry's own bounds no longer align with the existing bake's tile grid - typically because new
    /// geometry now extends past the original bake's bounds. Blindly applying tiles from a shifted grid
    /// would silently misplace them, so this refuses rather than guess; call <see cref="RebuildQuery"/>
    /// (or re-bake) instead when that happens.</summary>
    public bool RebuildTiles(Float3 center, Float3 size)
    {
        RebuildTilesWork? work = PrepareRebuildTiles(center, size);
        return work != null && ApplyRebuildTiles(work.Value);
    }

    /// <summary>Overload of <see cref="RebuildTiles(Float3, Float3)"/> taking a world-space box directly.</summary>
    public bool RebuildTiles(AABB bounds) => RebuildTiles(bounds.Center, bounds.Size);

    /// <summary>Async counterpart to <see cref="RebuildTiles(Float3, Float3)"/>: collects scene geometry
    /// synchronously on the calling thread first (reading the live scene and the mutable agent-type/area
    /// registries is not safe to do anywhere else - the same rule <see cref="NavMeshBakeJob"/> documents
    /// for a full bake), then runs the expensive Recast build itself, and applies the result, on a
    /// thread-pool thread. False (with nothing applied) under the same conditions <see cref="RebuildTiles(Float3, Float3)"/>
    /// itself returns false for.</summary>
    public Task<bool> RebuildTilesAsync(Float3 center, Float3 size)
    {
        RebuildTilesWork? work = PrepareRebuildTiles(center, size);
        return work == null ? Task.FromResult(false) : Task.Run(() => ApplyRebuildTiles(work.Value));
    }

    /// <summary>Overload of <see cref="RebuildTilesAsync(Float3, Float3)"/> taking a world-space box directly.</summary>
    public Task<bool> RebuildTilesAsync(AABB bounds) => RebuildTilesAsync(bounds.Center, bounds.Size);

    /// <summary>Everything <see cref="ApplyRebuildTiles"/> needs that has to be read on the calling
    /// thread first - see <see cref="RebuildTilesAsync(Float3, Float3)"/>'s own doc comment for why.</summary>
    private readonly struct RebuildTilesWork(NavMeshBuildInput input, NavMeshBakeSettings settings, NavMeshTileCacheData existingData, Float3 regionMin, Float3 regionMax)
    {
        public readonly NavMeshBuildInput Input = input;
        public readonly NavMeshBakeSettings Settings = settings;
        public readonly NavMeshTileCacheData ExistingData = existingData;
        public readonly Float3 RegionMin = regionMin;
        public readonly Float3 RegionMax = regionMax;
    }

    /// <summary>The main-thread-only half of a tile rebuild: resolving <see cref="EffectiveBakeSettings"/>
    /// and collecting scene geometry. Null under the same conditions <see cref="RebuildTiles(Float3, Float3)"/>
    /// itself returns false for.</summary>
    private RebuildTilesWork? PrepareRebuildTiles(Float3 center, Float3 size)
    {
        NavMesh? mesh = NavMeshAsset.Res;
        if (Scene.IsNotValid() || _query == null || mesh.IsNotValid() || mesh.TileCacheData == null)
            return null;

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(Scene, this);
        Float3 half = size * 0.5f;
        return new RebuildTilesWork(input, EffectiveBakeSettings, mesh.TileCacheData, center - half, center + half);
    }

    /// <summary>The rest of a tile rebuild: the actual Recast build (the expensive part, safe to run off
    /// the main thread - it only ever touches the immutable snapshot <see cref="PrepareRebuildTiles"/>
    /// already captured) and applying the result to the live query. False, applying nothing, if the build
    /// itself failed, or if the freshly-baked geometry's own bounds no longer align with the existing
    /// bake's tile grid - typically because new geometry now extends past the original bake's bounds.
    /// Blindly applying tiles from a shifted grid would silently misplace them, so this refuses rather
    /// than guess; call <see cref="RebuildQuery"/> (or re-bake) instead when that happens.</summary>
    private bool ApplyRebuildTiles(RebuildTilesWork work)
    {
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(work.Input, work.Settings);
        if (!result.Success || result.TileCacheData == null) return false;

        NavMeshTileCacheData data = work.ExistingData;
        const float originEpsilon = 0.01f;
        if (Float3.Distance(result.TileCacheData.Origin, data.Origin) > originEpsilon
            || result.TileCacheData.TileWorldSize != data.TileWorldSize)
            return false;

        var byCoord = new Dictionary<(int X, int Y), List<byte[]>>();
        foreach (NavMeshTileLayer layer in result.TileCacheData.Layers)
        {
            float tileMinX = data.Origin.X + layer.TileX * data.TileWorldSize;
            float tileMinZ = data.Origin.Z + layer.TileY * data.TileWorldSize;
            float tileMaxX = tileMinX + data.TileWorldSize;
            float tileMaxZ = tileMinZ + data.TileWorldSize;

            bool overlaps = tileMinX < work.RegionMax.X && tileMaxX > work.RegionMin.X && tileMinZ < work.RegionMax.Z && tileMaxZ > work.RegionMin.Z;
            if (!overlaps) continue;

            (int TileX, int TileY) key = (layer.TileX, layer.TileY);
            if (!byCoord.TryGetValue(key, out List<byte[]>? list)) byCoord[key] = list = [];
            list.Add(layer.CompressedData);
        }

        foreach (KeyValuePair<(int X, int Y), List<byte[]>> entry in byCoord)
        {
            _query!.ReplaceTile(entry.Key.X, entry.Key.Y, entry.Value);

            data.Layers.RemoveAll(l => l.TileX == entry.Key.X && l.TileY == entry.Key.Y);
            foreach (byte[] bytes in entry.Value)
                data.Layers.Add(new NavMeshTileLayer { TileX = entry.Key.X, TileY = entry.Key.Y, CompressedData = bytes });
        }

        return true;
    }

    /// <summary>Draws the baked polygon outlines, colored per <see cref="AgentTypeId"/> (or amber if
    /// <see cref="IsStale"/>), when <see cref="DrawGizmo"/> is enabled.</summary>
    public override void DrawGizmos()
    {
        if (!DrawGizmo || _query == null) return;

        Color typeColor = NavMeshGizmoColors.ForAgentType(AgentTypeId);
        Color color = IsStale ? new Color(1f, 0.6f, 0.1f, 0.5f) : new Color(typeColor.R, typeColor.G, typeColor.B, 0.5f);

        _query.DrawWalkablePolygons(color);
    }
}
