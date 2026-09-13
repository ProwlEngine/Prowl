// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// The tuning knobs a navmesh bake needs from a contributor: how big the agent that will walk this
/// mesh is, and how coarse the voxelization backing the bake is allowed to be. Everything else Recast
/// wants (region size, edge simplification, detail sampling) is a fixed, sane default inside
/// <see cref="RecastNavMeshBuilder"/> for v1 rather than another knob to get wrong here.
/// </summary>
public struct NavMeshBakeSettings
{
    [SerializeField] private float agentRadius;
    [SerializeField] private float agentHeight;
    [SerializeField] private float maxSlopeAngle;
    [SerializeField] private float maxStepHeight;
    [SerializeField] private float cellSize;
    [SerializeField] private float cellHeight;
    [SerializeField] private float tileSize;
    [SerializeField] private float jumpDistance;
    [SerializeField] private float dropHeight;

    /// <summary>Radius of the agent this mesh is baked for, in world units. Walkable ground is
    /// eroded inward by this amount so an agent's center never touches a mesh edge closer than its
    /// own radius.</summary>
    public float AgentRadius { get => agentRadius; set => agentRadius = value; }

    /// <summary>Height of the agent, in world units. Anything with less headroom than this is
    /// rejected as unwalkable.</summary>
    public float AgentHeight { get => agentHeight; set => agentHeight = value; }

    /// <summary>Steepest slope, in degrees, still considered walkable ground.</summary>
    public float MaxSlopeAngle { get => maxSlopeAngle; set => maxSlopeAngle = value; }

    /// <summary>Tallest ledge, in world units, an agent can step up or down without it counting as
    /// a wall. Also known as the agent's max climb.</summary>
    public float MaxStepHeight { get => maxStepHeight; set => maxStepHeight = value; }

    /// <summary>Voxel size on the horizontal (XZ) plane. Smaller values pick up finer detail at
    /// a real cost in bake time and memory; this is the single biggest lever on both.</summary>
    public float CellSize { get => cellSize; set => cellSize = value; }

    /// <summary>Voxel size along the vertical (Y) axis. Usually smaller than <see cref="CellSize"/>
    /// since walkable steps need finer vertical resolution than lateral detail does.</summary>
    public float CellHeight { get => cellHeight; set => cellHeight = value; }

    /// <summary>World-space width/depth of one square navmesh tile. Every bake is tiled (see
    /// <see cref="NavMeshTileCacheData"/>) - this is the one knob controlling the size/count trade-off:
    /// smaller tiles mean cheaper, more localized <see cref="NavMeshObstacle"/> re-bakes and finer
    /// streaming granularity for a large world, at the cost of more tiles (and more fixed per-tile
    /// overhead) for the same total area.</summary>
    public float TileSize { get => tileSize; set => tileSize = value; }

    /// <summary>Furthest horizontal gap a jump or drop link generated at bake time may cross, in world
    /// units. 0 (the default) disables generation entirely - see <see cref="NavMeshLinkGenerator"/>.</summary>
    public float JumpDistance { get => jumpDistance; set => jumpDistance = value; }

    /// <summary>Tallest ledge a generated drop link may descend, in world units. A height difference
    /// within <see cref="NavMeshBakeSettings.MaxStepHeight"/> of level generates a (bidirectional) jump
    /// instead of a (one-directional) drop; anything taller than this is not linked at all. 0 (the
    /// default) disables drop-link generation specifically, independent of <see cref="JumpDistance"/>.</summary>
    public float DropHeight { get => dropHeight; set => dropHeight = value; }

    /// <summary>A reasonable starting point for a human-scale agent.</summary>
    public static NavMeshBakeSettings Default => new()
    {
        AgentRadius = 0.5f,
        AgentHeight = 2.0f,
        MaxSlopeAngle = 45.0f,
        MaxStepHeight = 0.4f,
        CellSize = 0.3f,
        CellHeight = 0.2f,
        TileSize = 32f,
    };

    /// <summary>Whether every field matches <paramref name="obj"/>'s. Used by <see cref="NavMeshSurface.IsStale"/>
    /// to detect a bake made with different settings than baking again right now would use.</summary>
    public override bool Equals(object? obj)
    {
        if (obj is not NavMeshBakeSettings other) return false;
        return agentRadius.Equals(other.agentRadius)
            && agentHeight.Equals(other.agentHeight)
            && maxSlopeAngle.Equals(other.maxSlopeAngle)
            && maxStepHeight.Equals(other.maxStepHeight)
            && cellSize.Equals(other.cellSize)
            && cellHeight.Equals(other.cellHeight)
            && tileSize.Equals(other.tileSize)
            && jumpDistance.Equals(other.jumpDistance)
            && dropHeight.Equals(other.dropHeight);
    }

    /// <summary>Combines every field into one hash code, consistent with <see cref="Equals(object?)"/>.</summary>
    public override int GetHashCode() =>
        System.HashCode.Combine(
            System.HashCode.Combine(agentRadius, agentHeight, maxSlopeAngle, maxStepHeight, cellSize, cellHeight, tileSize),
            jumpDistance, dropHeight);

    /// <summary>Whether every field of <paramref name="left"/> matches <paramref name="right"/>'s.</summary>
    public static bool operator ==(NavMeshBakeSettings left, NavMeshBakeSettings right) => left.Equals(right);

    /// <summary>Whether any field of <paramref name="left"/> differs from <paramref name="right"/>'s.</summary>
    public static bool operator !=(NavMeshBakeSettings left, NavMeshBakeSettings right) => !left.Equals(right);
}
