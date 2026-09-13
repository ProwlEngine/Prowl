// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// The project-wide list of named agent types, mirroring <see cref="TagLayerManager"/>'s role for
/// tags: a plain, process-wide static the editor's Agents settings page writes and everything
/// else (surfaces, agents, the bake pipeline) reads by id.
/// <para/>
/// Safe to read (<see cref="Types"/>, <see cref="GetById"/>) from any thread while the main thread
/// writes: every write below publishes a whole new array rather than mutating the live one in place
/// (the same copy-on-write <see cref="NavMeshAreas"/> uses), so a reader never observes a half-applied
/// write or a torn enumeration the way it could reading a plain <see cref="List{T}"/> concurrently
/// mutated by <see cref="Add"/>/<see cref="Remove"/>. A background bake should still snapshot the
/// specific entry it needs on the main thread before handing work to a worker rather than calling back
/// into this class repeatedly from there, the same reasoning <see cref="NavMeshAreas"/> documents.
/// </summary>
public static class NavMeshAgentTypes
{
    /// <summary>Id of the built-in "Humanoid" type. Every project has this id; it cannot be removed.</summary>
    public const int HumanoidId = 0;

    /// <summary>The project-wide agent type table, in registration order. Volatile: every write below
    /// publishes a freshly built array rather than mutating this one's elements or structure, so a read
    /// of the field itself is the only synchronization a reader needs - see this class's own doc comment.</summary>
    private static volatile NavMeshAgentTypeInfo[] s_types = CreateDefault();

    /// <summary>The id the next <see cref="Add"/> call will assign. Only ever touched on the main
    /// thread, alongside every writer above.</summary>
    private static int s_nextId = HumanoidId + 1;

    /// <summary>The table every project (and <see cref="ResetDefault"/>) starts from: just the built-in
    /// Humanoid type.</summary>
    private static NavMeshAgentTypeInfo[] CreateDefault() => [NavMeshAgentTypeInfo.CreateHumanoid()];

    /// <summary>Every registered agent type, in registration order.</summary>
    public static IReadOnlyList<NavMeshAgentTypeInfo> Types => s_types;

    /// <summary>Looks up a type by its stable id. Null if no such id is registered (it was removed, or
    /// never existed - a surface referencing a removed type should treat this the same way).</summary>
    public static NavMeshAgentTypeInfo? GetById(int id)
    {
        NavMeshAgentTypeInfo[] types = s_types;
        foreach (NavMeshAgentTypeInfo type in types)
            if (type.Id == id) return type;
        return null;
    }

    /// <summary>Registers a new agent type and returns its freshly assigned, stable id.</summary>
    public static int Add(string name, float agentRadius, float agentHeight, float maxSlopeAngle, float maxStepHeight)
    {
        int id = s_nextId++;
        var type = new NavMeshAgentTypeInfo
        {
            Id = id,
            Name = name,
            AgentRadius = agentRadius,
            AgentHeight = agentHeight,
            MaxSlopeAngle = maxSlopeAngle,
            MaxStepHeight = maxStepHeight,
        };

        NavMeshAgentTypeInfo[] current = s_types;
        var next = new NavMeshAgentTypeInfo[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = type;
        s_types = next;
        return id;
    }

    /// <summary>Removes an agent type by id. Always fails for <see cref="HumanoidId"/>.</summary>
    public static bool Remove(int id)
    {
        if (id == HumanoidId) return false;

        NavMeshAgentTypeInfo[] current = s_types;
        if (Array.FindIndex(current, t => t.Id == id) < 0) return false;

        s_types = Array.FindAll(current, t => t.Id != id);
        return true;
    }

    /// <summary>Replaces the whole table (used when loading project or player settings). Guarantees
    /// the built-in Humanoid entry still exists afterward, and re-derives the next-id counter from the
    /// highest id present so a freshly added type never collides with one loaded from disk.</summary>
    public static void ReplaceAll(List<NavMeshAgentTypeInfo> types)
    {
        types ??= [];
        bool hasHumanoid = false;
        foreach (NavMeshAgentTypeInfo type in types)
            if (type.Id == HumanoidId) { hasHumanoid = true; break; }
        if (!hasHumanoid)
            types.Insert(0, NavMeshAgentTypeInfo.CreateHumanoid());

        NavMeshAgentTypeInfo[] next = [.. types];
        s_types = next;

        int nextId = HumanoidId + 1;
        foreach (NavMeshAgentTypeInfo type in next)
            if (type.Id >= nextId) nextId = type.Id + 1;
        s_nextId = nextId;
    }

    /// <summary>Resets to just the built-in Humanoid type.</summary>
    public static void ResetDefault()
    {
        s_types = CreateDefault();
        s_nextId = HumanoidId + 1;
    }
}
