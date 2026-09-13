// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// A baked navigation mesh asset. Round-trips through Echo like any other Prowl asset: every field
/// is a plain, engine-owned type, so there is nothing here that needs a hand-written
/// <c>ISerializable</c> implementation.
/// </summary>
[CreateAssetMenu("NavMesh", Extension = ".navmesh")]
public sealed class NavMesh : EngineObject
{
    /// <summary>The baked tile data <see cref="NavMeshQuery"/> reconstructs a live, tiled Detour
    /// navmesh from. Null for a never-baked asset.</summary>
    public NavMeshTileCacheData? TileCacheData;

    /// <summary>World-space bounds of the geometry this mesh was baked from.</summary>
    public AABB Bounds;

    /// <summary>The settings this mesh was actually baked with. A <see cref="NavMeshSurface"/> compares
    /// its own current settings against this to decide whether it has gone stale.</summary>
    public NavMeshBakeSettings BakeSettings;

    /// <summary>The agent type (see <see cref="NavMeshAgentTypes"/>) this mesh was baked for. Compared
    /// against a surface's own <see cref="NavMeshSurface.AgentTypeId"/> for the same staleness check
    /// <see cref="BakeSettings"/> is used for - a surface repointed at a different agent type is just
    /// as stale as one with changed numeric settings.</summary>
    public int AgentTypeId;

    /// <summary>Creates an empty, never-baked navmesh asset.</summary>
    public NavMesh() : base("NavMesh") { }

    /// <summary>Whether this asset holds a usable bake. False for a freshly created, never-baked asset.</summary>
    public bool IsBuilt => TileCacheData != null && TileCacheData.Layers.Count > 0;

    /// <summary>Replaces this asset's baked data in place, so an existing <see cref="AssetRef{NavMesh}"/>
    /// pointed at it picks up a rebake without needing to be reassigned.</summary>
    public void Apply(NavMeshBuildResult result, NavMeshBakeSettings settings, int agentTypeId)
    {
        TileCacheData = result.TileCacheData;
        Bounds = result.Bounds;
        BakeSettings = settings;
        AgentTypeId = agentTypeId;
    }
}
