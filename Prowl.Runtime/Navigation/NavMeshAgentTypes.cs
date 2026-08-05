// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// One project-level agent type: the physical envelope navmeshes are voxelized for. Surfaces
/// and agents reference an entry by <see cref="Id"/>; the inspector shows the name
/// (via <see cref="NavMeshAgentTypeAttribute"/>).
/// </summary>
public sealed class NavMeshAgentType
{
    /// <summary>Persistent identifier. Stable across renames and removals of other types —
    /// never an index into the table.</summary>
    public int Id;

    public string Name = string.Empty;

    /// <summary>Agent radius in world units. Walkable surfaces are eroded by this distance from walls.</summary>
    public float Radius = 0.5f;

    /// <summary>Agent height in world units. Spaces lower than this are not walkable.</summary>
    public float Height = 2.0f;

    /// <summary>Maximum walkable slope angle in degrees.</summary>
    public float MaxSlope = 45f;

    /// <summary>Maximum ledge height the agent can step up, in world units.</summary>
    public float MaxClimb = 0.4f;

    public NavMeshAgentType Clone() => (NavMeshAgentType)MemberwiseClone();
}

/// <summary>
/// The project-wide agent type table (mirrors <see cref="NavMeshAreas"/>): defined in the
/// editor's navigation settings, restored in players from Navigation.yaml, with a code-side
/// default (the built-in Humanoid, id 0) so headless and procedural use needs no settings
/// file. Unity's Agents tab equivalent.
/// </summary>
public static class NavMeshAgentTypes
{
    /// <summary>The built-in default agent type id. Always present; cannot be removed.</summary>
    public const int Humanoid = 0;

    private static readonly List<NavMeshAgentType> s_types = [CreateHumanoid()];

    private static NavMeshAgentType CreateHumanoid() => new()
    {
        Id = Humanoid,
        Name = "Humanoid",
        Radius = 0.5f,
        Height = 2.0f,
        MaxSlope = 45f,
        MaxClimb = 0.4f,
    };

    /// <summary>All defined agent types, in table order. Do not mutate the entries — use
    /// <see cref="ApplyTable"/> (settings) to change the table.</summary>
    public static IReadOnlyList<NavMeshAgentType> All => s_types;

    /// <summary>The agent type with the given id, or null when undefined.</summary>
    public static NavMeshAgentType? Get(int agentTypeId)
    {
        for (int i = 0; i < s_types.Count; i++)
            if (s_types[i].Id == agentTypeId)
                return s_types[i];
        return null;
    }

    /// <summary>Display name for an agent type id ("Agent Type N" for undefined ids, so stale
    /// references stay visible rather than blank).</summary>
    public static string GetName(int agentTypeId)
        => Get(agentTypeId)?.Name ?? $"Agent Type {agentTypeId}";

    /// <summary>Find an agent type id by name, or -1. Null/empty never matches.</summary>
    public static int GetIdFromName(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        for (int i = 0; i < s_types.Count; i++)
            if (string.Equals(s_types[i].Name, name, StringComparison.Ordinal))
                return s_types[i].Id;
        return -1;
    }

    /// <summary>
    /// Replace the table (called by settings loading). The built-in Humanoid entry is
    /// enforced: id 0 always exists and keeps its name, though its envelope values are
    /// editable like Unity's.
    /// </summary>
    public static void ApplyTable(IEnumerable<NavMeshAgentType> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        s_types.Clear();
        foreach (NavMeshAgentType type in types)
        {
            if (type == null) continue;
            if (Get(type.Id) != null)
            {
                // Unreachable from the editor UI; a hand-edited Navigation.yaml can do it.
                // Get returns the first match, so a silent duplicate would shadow the second.
                Debug.LogWarning($"[Navigation] Duplicate agent type id {type.Id} ('{type.Name}') ignored; '{Get(type.Id)!.Name}' keeps the id.");
                continue;
            }
            NavMeshAgentType copy = type.Clone();
            if (copy.Id == Humanoid) copy.Name = "Humanoid";
            s_types.Add(copy);
        }

        if (Get(Humanoid) == null)
            s_types.Insert(0, CreateHumanoid());
    }

    /// <summary>
    /// Compose the resolved bake input for an agent type: envelope from the table, everything
    /// else from <paramref name="overrides"/> (or defaults). This is what surfaces hand to
    /// <see cref="NavMeshBuilder.Build"/>; the builder API itself only ever sees the resolved
    /// <see cref="NavMeshBuildSettings"/>. An undefined id falls back to the Humanoid envelope
    /// with a warning — a bake with wrong-but-sane dimensions beats no bake.
    /// </summary>
    public static NavMeshBuildSettings GetBuildSettings(int agentTypeId, NavMeshBuildOverrides? overrides = null)
    {
        NavMeshAgentType? type = Get(agentTypeId);
        if (type == null)
        {
            Debug.LogWarning($"[Navigation] Agent type {agentTypeId} is not defined in the navigation settings; baking with the Humanoid envelope. Define it in Project Settings > Navigation > Agents.");
            type = Get(Humanoid) ?? CreateHumanoid();
        }

        overrides ??= s_defaultOverrides;
        return new NavMeshBuildSettings
        {
            AgentTypeId = agentTypeId,
            AgentRadius = type.Radius,
            AgentHeight = type.Height,
            AgentMaxSlope = type.MaxSlope,
            AgentMaxClimb = type.MaxClimb,

            OverrideVoxelSize = overrides.OverrideVoxelSize,
            VoxelSize = overrides.VoxelSize,
            OverrideTileSize = overrides.OverrideTileSize,
            TileSize = overrides.TileSize,
            MinRegionArea = overrides.MinRegionArea,
            EdgeMaxError = overrides.EdgeMaxError,
            FilterLowHangingObstacles = overrides.FilterLowHangingObstacles,
            FilterLedgeSpans = overrides.FilterLedgeSpans,
            FilterWalkableLowHeightSpans = overrides.FilterWalkableLowHeightSpans,
        };
    }

    private static readonly NavMeshBuildOverrides s_defaultOverrides = new();
}

/// <summary>
/// Draws an int field as a dropdown of the agent types defined in project settings, instead
/// of a raw id (the agent-type analogue of <see cref="NavMeshAreaAttribute"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class NavMeshAgentTypeAttribute : Attribute { }
