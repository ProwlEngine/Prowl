// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Static navigation query API over the current scene's <see cref="NavMeshWorld"/> — the
/// Unity-style entry point (<c>NavMesh.CalculatePath</c>, <c>NavMesh.SamplePosition</c>, ...).
/// Multi-scene setups and background threads that outlive scene changes should use
/// <see cref="Scene.Navigation"/> on a specific scene instead; this facade always targets
/// <see cref="Scene.Current"/>. Every method degrades to "no result" when no scene or navmesh
/// is loaded.
/// </summary>
public static class NavMesh
{
    /// <summary>Area mask that includes every area.</summary>
    public const int AllAreas = NavMeshAreas.AllAreas;

    /// <summary>The current scene's navigation world, or null when no scene is loaded.</summary>
    public static NavMeshWorld? World
    {
        get
        {
            Scene? scene = Scene.Current;
            return scene.IsValid() ? scene.Navigation : null;
        }
    }

    /// <summary>Calculate a path between two points. Returns true when a complete or partial
    /// path was found; <paramref name="path"/> carries the corners and exact status.</summary>
    public static bool CalculatePath(Float3 sourcePosition, Float3 targetPosition, int areaMask, NavMeshPath path)
        => World?.CalculatePath(sourcePosition, targetPosition, areaMask, path) ?? MarkInvalid(path);

    /// <inheritdoc cref="CalculatePath(Float3, Float3, int, NavMeshPath)"/>
    public static bool CalculatePath(Float3 sourcePosition, Float3 targetPosition, NavMeshQueryFilter filter, NavMeshPath path)
        => World?.CalculatePath(sourcePosition, targetPosition, filter, path) ?? MarkInvalid(path);

    private static bool MarkInvalid(NavMeshPath path)
    {
        path?.ClearCorners();
        return false;
    }

    /// <summary>Find the closest navmesh point within <paramref name="maxDistance"/> of a position.</summary>
    public static bool SamplePosition(Float3 sourcePosition, out NavMeshHit hit, float maxDistance, int areaMask)
    {
        NavMeshWorld? world = World;
        if (world != null) return world.SamplePosition(sourcePosition, out hit, maxDistance, areaMask);
        hit = default;
        return false;
    }

    /// <inheritdoc cref="SamplePosition(Float3, out NavMeshHit, float, int)"/>
    public static bool SamplePosition(Float3 sourcePosition, out NavMeshHit hit, float maxDistance, NavMeshQueryFilter filter)
    {
        NavMeshWorld? world = World;
        if (world != null) return world.SamplePosition(sourcePosition, out hit, maxDistance, filter);
        hit = default;
        return false;
    }

    /// <summary>Trace a walkability ray along the navmesh. Returns true when blocked before the target.</summary>
    public static bool Raycast(Float3 sourcePosition, Float3 targetPosition, out NavMeshHit hit, int areaMask)
    {
        NavMeshWorld? world = World;
        if (world != null) return world.Raycast(sourcePosition, targetPosition, out hit, areaMask);
        hit = default;
        return false;
    }

    /// <summary>Locate the closest navmesh border edge from a point.</summary>
    public static bool FindClosestEdge(Float3 sourcePosition, out NavMeshHit hit, int areaMask)
    {
        NavMeshWorld? world = World;
        if (world != null) return world.FindClosestEdge(sourcePosition, out hit, areaMask);
        hit = default;
        return false;
    }

    /// <summary>Triangulate the current navmesh for debug drawing or user tooling.</summary>
    public static NavMeshTriangulation CalculateTriangulation()
        => World?.CalculateTriangulation() ?? new NavMeshTriangulation { Vertices = [], Indices = [], Areas = [] };

    /// <summary>Register a baked navmesh with the current scene. Returns its handle, or null.</summary>
    public static NavMeshInstance? AddNavMeshData(NavMeshData navMeshData)
        => World?.AddNavMeshData(navMeshData);

    /// <summary>Unregister a navmesh from the current scene.</summary>
    public static void RemoveNavMeshData(NavMeshInstance? instance)
        => World?.RemoveNavMeshData(instance);

    /// <summary>Find an area index by name, or -1.</summary>
    public static int GetAreaFromName(string areaName) => NavMeshAreas.GetAreaFromName(areaName);

    /// <summary>The project-default path cost multiplier for an area.</summary>
    public static float GetAreaCost(int areaIndex) => NavMeshAreas.GetAreaCost(areaIndex);

    /// <summary>Set the project-default path cost multiplier for an area.</summary>
    public static void SetAreaCost(int areaIndex, float cost) => NavMeshAreas.SetAreaCost(areaIndex, cost);
}
