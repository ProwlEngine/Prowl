// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.Runtime;

/// <summary>
/// The project-wide navigation area table: up to 32 named areas with per-area path costs,
/// addressed by index and combined into 32-bit area masks (matching Unity's area model).
/// Area 0 is Walkable, area 1 is Not Walkable, area 2 is Jump; 3..31 are user-defined.
/// <para/>
/// Internally Detour stores a 6-bit area id per polygon where 0 (<c>RC_NULL_AREA</c>) is
/// reserved for "not part of the navmesh", so Prowl area index <c>i</c> is stored as Detour
/// area <c>i + 1</c>. <see cref="ToDetourArea"/> / <see cref="FromDetourArea"/> are the only
/// places that offset is applied — never hand-roll it. BAKE-side conversions go through
/// <c>ProwlInputGeomProvider.DetourAreaFor</c> instead, which additionally maps
/// <see cref="NotWalkable"/> to the null area (calling <see cref="ToDetourArea"/> directly on
/// a source/volume area silently resurrects traversable "Not Walkable" polys).
/// </summary>
public static class NavMeshAreas
{
    /// <summary>Maximum number of areas (indices 0..31), the width of an area mask.</summary>
    public const int MaxAreas = 32;

    /// <summary>The built-in default walkable area index.</summary>
    public const int Walkable = 0;

    /// <summary>The built-in not-walkable area index. Geometry marked with this area is
    /// excluded from the navmesh entirely.</summary>
    public const int NotWalkable = 1;

    /// <summary>The built-in area index used for jump/off-mesh connections.</summary>
    public const int Jump = 2;

    /// <summary>Area mask that includes every area.</summary>
    public const int AllAreas = -1;

    // Replaced wholesale rather than edited in place: query filters read the cost table from
    // worker threads while the settings UI rewrites it on every keystroke, and publishing a new
    // array is one atomic store, so a reader gets a whole revision of the table rather than one
    // caught mid-edit. Writers take a lock so two of them cannot clobber each other's copy.
    private static volatile string[] s_names = CreateDefaultNames();
    private static volatile float[] s_costs = CreateDefaultCosts();
    private static readonly Lock s_writeLock = new();

    private static string[] CreateDefaultNames()
    {
        string[] names = new string[MaxAreas];
        for (int i = 0; i < names.Length; i++) names[i] = string.Empty;
        names[Walkable] = "Walkable";
        names[NotWalkable] = "Not Walkable";
        names[Jump] = "Jump";
        return names;
    }

    private static float[] CreateDefaultCosts()
    {
        float[] costs = new float[MaxAreas];
        for (int i = 0; i < costs.Length; i++) costs[i] = 1f;
        costs[Jump] = 2f;
        return costs;
    }

    /// <summary>Get the name of an area, or an empty string for unnamed user areas.</summary>
    public static string GetAreaName(int areaIndex)
    {
        if (areaIndex < 0 || areaIndex >= MaxAreas) return string.Empty;
        return s_names[areaIndex];
    }

    /// <summary>Rename an area. Built-in areas (0-2) cannot be renamed.</summary>
    public static void SetAreaName(int areaIndex, string name)
    {
        if (areaIndex <= Jump || areaIndex >= MaxAreas) return;
        lock (s_writeLock)
        {
            string[] names = [.. s_names];
            names[areaIndex] = name ?? string.Empty;
            s_names = names;
        }
    }

    /// <summary>Find an area index by name, or -1 when no area has that name. Null/empty never
    /// matches (unnamed slots store empty strings).</summary>
    public static int GetAreaFromName(string areaName)
    {
        if (string.IsNullOrEmpty(areaName)) return -1;
        string[] names = s_names;
        for (int i = 0; i < MaxAreas; i++)
            if (string.Equals(names[i], areaName, StringComparison.Ordinal))
                return i;
        return -1;
    }

    /// <summary>The default path cost multiplier for an area (used when a query filter does not override it).</summary>
    public static float GetAreaCost(int areaIndex)
    {
        if (areaIndex < 0 || areaIndex >= MaxAreas) return 1f;
        return s_costs[areaIndex];
    }

    /// <summary>Set the default path cost multiplier for an area. Clamped to >= 1: Detour's
    /// A* heuristic (straight-line distance) is only admissible when no traversal is cheaper
    /// than distance itself, so costs below 1 would silently produce suboptimal paths. To
    /// express "agents prefer this area", raise every OTHER area's cost above 1 instead.</summary>
    public static void SetAreaCost(int areaIndex, float cost)
    {
        if (areaIndex < 0 || areaIndex >= MaxAreas) return;
        lock (s_writeLock)
        {
            float[] costs = [.. s_costs];
            costs[areaIndex] = Math.Max(1f, cost);
            s_costs = costs;
        }
    }

    /// <summary>Replace the whole area table (names + costs). Called by project-settings loading.</summary>
    public static void ApplyTable(IReadOnlyList<string> names, IReadOnlyList<float> costs)
    {
        lock (s_writeLock)
        {
            string[] newNames = [.. s_names];
            float[] newCosts = [.. s_costs];
            for (int i = 0; i < MaxAreas; i++)
            {
                if (names != null && i < names.Count && i > Jump) newNames[i] = names[i] ?? string.Empty;
                if (costs != null && i < costs.Count) newCosts[i] = Math.Max(1f, costs[i]);
            }
            s_names = newNames;
            s_costs = newCosts;
        }
    }

    /// <summary>Indices of all defined areas (the built-ins plus every user area with a
    /// non-empty name), in index order. What area dropdowns enumerate.</summary>
    public static List<int> GetDefinedAreas()
    {
        string[] names = s_names;
        var defined = new List<int>(8);
        for (int i = 0; i < MaxAreas; i++)
            if (i <= Jump || !string.IsNullOrEmpty(names[i]))
                defined.Add(i);
        return defined;
    }

    /// <summary>Convert a Prowl area index (0..31) to the Detour polygon area value (1..32).</summary>
    public static int ToDetourArea(int areaIndex) => Math.Clamp(areaIndex, 0, MaxAreas - 1) + 1;

    /// <summary>Convert a Detour polygon area value back to a Prowl area index. Returns
    /// <see cref="NotWalkable"/> for the reserved null area (0), which should not appear on
    /// polygons that made it into a navmesh.</summary>
    public static int FromDetourArea(int detourArea) => detourArea <= 0 ? NotWalkable : Math.Min(detourArea - 1, MaxAreas - 1);
}
