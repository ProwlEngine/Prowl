// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// The project-wide table of 32 named navmesh areas, unlike <see cref="NavMeshAgentTypeInfo"/> a
/// slot's position in the table (0-31) is its identity, not a separately stored id - a baked polygon
/// stores its area as that index directly, so removing an area clears the slot rather than shifting
/// every later one, the same way a shift would silently repoint every polygon and area mask baked
/// after it. Slots 0-2 are reserved: <see cref="Walkable"/>, <see cref="NotWalkable"/>,
/// <see cref="Jump"/> always exist with those names.
/// <para/>
/// Safe to read (<see cref="GetAreaName"/>, <see cref="GetAreaCost"/>) from any thread while the main
/// thread writes: every write publishes a whole new table rather than mutating a slot in place (the
/// same copy-on-write <see cref="NavMeshAgentTypes"/> uses), so a reader either sees the table from
/// before a write or the one from after it, in full, never a half-applied one. What this does not
/// give a reader is a guarantee that two separate calls observe the same generation of the table - a
/// write racing between them can make a name and a cost read back from different moments - which is
/// why <see cref="NavMeshQuery"/> still snapshots everything it needs in one pass rather than calling
/// back into this class repeatedly over the lifetime of a search.
/// </summary>
public static class NavMeshAreas
{
    /// <summary>Number of area slots.</summary>
    public const int MaxAreas = 32;

    /// <summary>Ordinary walkable ground.</summary>
    public const int Walkable = 0;

    /// <summary>Geometry that should become a genuine hole in the bake. See
    /// <see cref="ToRecastArea"/> for how this maps to Recast's own null area.</summary>
    public const int NotWalkable = 1;

    /// <summary>Default area for a <see cref="NavMeshLink"/>.</summary>
    public const int Jump = 2;

    /// <summary>The project-wide area table, indexed by slot. Volatile: every write below publishes a
    /// freshly built array rather than mutating this one's elements, so a read of the field itself is
    /// the only synchronization a reader needs - see this class's own doc comment.</summary>
    private static volatile NavMeshAreaInfo[] s_areas = CreateDefault();

    /// <summary>The table every project (and <see cref="ResetDefault"/>) starts from: 32 empty slots
    /// with the three reserved ones already named.</summary>
    private static NavMeshAreaInfo[] CreateDefault()
    {
        var areas = new NavMeshAreaInfo[MaxAreas];
        for (int i = 0; i < MaxAreas; i++)
            areas[i] = new NavMeshAreaInfo();

        areas[Walkable].Name = "Walkable";
        areas[NotWalkable].Name = "Not Walkable";
        areas[Jump].Name = "Jump";
        areas[Jump].Cost = 2f;
        return areas;
    }

    /// <summary>Display name for a slot. Empty for a slot nothing has named yet.</summary>
    public static string GetAreaName(int index) => (uint)index < MaxAreas ? s_areas[index].Name : "";

    /// <summary>Traversal cost for a slot. 1 for an out-of-range index.</summary>
    public static float GetAreaCost(int index) => (uint)index < MaxAreas ? s_areas[index].Cost : 1f;

    /// <summary>Sets a slot's cost, clamped to at least 1. Detour's pathfinding heuristic is straight-line
    /// distance, which only stays admissible (never overestimates the true cheapest cost) when nothing
    /// costs less than distance itself; a sub-1 cost can make it return a route that only looks shortest.
    /// To make agents prefer one area, raise every other area's cost instead of lowering this one's.</summary>
    public static void SetAreaCost(int index, float cost)
    {
        if ((uint)index >= MaxAreas) return;
        PublishSlot(index, new NavMeshAreaInfo { Name = s_areas[index].Name, Cost = cost < 1f ? 1f : cost });
    }

    /// <summary>Renames a slot. No-op for a reserved slot (<see cref="Walkable"/>,
    /// <see cref="NotWalkable"/>, <see cref="Jump"/>), whose names are fixed.</summary>
    public static void SetAreaName(int index, string name)
    {
        if ((uint)index >= MaxAreas) return;
        if (index == Walkable || index == NotWalkable || index == Jump) return;
        PublishSlot(index, new NavMeshAreaInfo { Name = name ?? "", Cost = s_areas[index].Cost });
    }

    /// <summary>Clears a slot back to an unnamed, cost-1 area. No-op for a reserved slot.</summary>
    public static void Clear(int index)
    {
        if ((uint)index >= MaxAreas) return;
        if (index == Walkable || index == NotWalkable || index == Jump) return;
        PublishSlot(index, new NavMeshAreaInfo());
    }

    /// <summary>Publishes a whole new table with <paramref name="index"/> replaced by <paramref name="slot"/>
    /// and every other slot carried over unchanged (never mutated - see this class's own doc comment) -
    /// the single choke point every single-slot writer above goes through.</summary>
    private static void PublishSlot(int index, NavMeshAreaInfo slot)
    {
        NavMeshAreaInfo[] current = s_areas;
        var next = new NavMeshAreaInfo[MaxAreas];
        Array.Copy(current, next, MaxAreas);
        next[index] = slot;
        s_areas = next;
    }

    /// <summary>Replaces the whole table (used when loading project or player settings). Reserved
    /// slots keep their fixed names regardless of what <paramref name="areas"/> supplies for them,
    /// but their cost is still taken from it.</summary>
    public static void ReplaceAll(NavMeshAreaInfo[] areas)
    {
        NavMeshAreaInfo[] next = CreateDefault();
        int count = areas == null ? 0 : Maths.Min(areas.Length, MaxAreas);
        for (int i = 0; i < count; i++)
        {
            if (areas![i] == null) continue;
            if (i != Walkable && i != NotWalkable && i != Jump)
                next[i].Name = areas[i].Name ?? "";
            next[i].Cost = areas[i].Cost < 1f ? 1f : areas[i].Cost;
        }
        s_areas = next;
    }

    /// <summary>Resets every slot to the built-in defaults.</summary>
    public static void ResetDefault() => s_areas = CreateDefault();

    /// <summary>Converts a Prowl area index (0-31) to the Recast/Detour area id actually baked into a
    /// polygon (0-63, where 0 means "not part of the navmesh"). <see cref="NotWalkable"/> maps to that
    /// null area specifically so geometry tagged with it becomes a real hole rather than a traversable
    /// "area 1" polygon; everything else shifts up by one to make room for that reservation.</summary>
    public static int ToRecastArea(int prowlAreaIndex) => prowlAreaIndex == NotWalkable ? 0 : prowlAreaIndex + 1;

    /// <summary>Reverses <see cref="ToRecastArea"/>.</summary>
    public static int FromRecastArea(int recastAreaId) => recastAreaId == 0 ? NotWalkable : recastAreaId - 1;
}
