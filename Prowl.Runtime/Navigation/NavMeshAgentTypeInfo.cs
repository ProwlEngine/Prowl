// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Navigation;

/// <summary>
/// One named agent size in the project's navigation settings: the physical envelope a bake voxelizes
/// for and an agent walking that bake is assumed to have. <see cref="Id"/> is what a
/// <see cref="NavMeshSurface"/> or <see cref="NavMeshAgent"/> actually stores - never the index into
/// <see cref="NavMeshAgentTypes"/>'s list, which shifts as entries are added or removed. A rename only
/// ever touches <see cref="Name"/>.
/// </summary>
public sealed class NavMeshAgentTypeInfo
{
    /// <summary>Stable identifier, assigned once at creation and never reused or renumbered.</summary>
    public int Id;

    /// <summary>Display name. Free to rename; nothing else keys off it.</summary>
    public string Name = "Agent Type";

    /// <summary>Radius of the agent this type bakes for, in world units.</summary>
    public float AgentRadius = 0.5f;

    /// <summary>Height of the agent, in world units.</summary>
    public float AgentHeight = 2.0f;

    /// <summary>Steepest slope, in degrees, still considered walkable for this agent type.</summary>
    public float MaxSlopeAngle = 45.0f;

    /// <summary>Tallest step this agent type can climb, in world units.</summary>
    public float MaxStepHeight = 0.4f;

    /// <summary>Furthest horizontal gap this agent type can cross with a jump, in world units. 0 (the
    /// default) means this type never generates jump or drop links at bake time - see
    /// <see cref="NavMeshLinkGenerator"/>.</summary>
    public float JumpDistance;

    /// <summary>Tallest ledge this agent type can drop from without a link being generated as an
    /// ordinary jump instead, in world units. 0 (the default) means this type never generates drop
    /// links at bake time - see <see cref="NavMeshLinkGenerator"/>.</summary>
    public float DropHeight;

    /// <summary>The settings a fresh bake for this agent type should start from.</summary>
    public NavMeshBakeSettings ToBakeSettings(float cellSize, float cellHeight) => new()
    {
        AgentRadius = AgentRadius,
        AgentHeight = AgentHeight,
        MaxSlopeAngle = MaxSlopeAngle,
        MaxStepHeight = MaxStepHeight,
        JumpDistance = JumpDistance,
        DropHeight = DropHeight,
        CellSize = cellSize,
        CellHeight = cellHeight,
    };

    /// <summary>The built-in agent type every project starts with. Id 0, never removable.</summary>
    public static NavMeshAgentTypeInfo CreateHumanoid() => new()
    {
        Id = NavMeshAgentTypes.HumanoidId,
        Name = "Humanoid",
        AgentRadius = 0.5f,
        AgentHeight = 2.0f,
        MaxSlopeAngle = 45.0f,
        MaxStepHeight = 0.4f,
    };
}
