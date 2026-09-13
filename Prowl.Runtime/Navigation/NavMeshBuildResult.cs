// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>Result of a navmesh bake.</summary>
public sealed class NavMeshBuildResult
{
    /// <summary>Whether the bake produced a usable mesh. False means <see cref="TileCacheData"/> is
    /// null and <see cref="Error"/> explains why.</summary>
    public bool Success;

    /// <summary>Human-readable reason for a failed bake. Null on success.</summary>
    public string? Error;

    /// <summary>The baked tile data. Null unless <see cref="Success"/> is true.</summary>
    public NavMeshTileCacheData? TileCacheData;

    /// <summary>World-space bounds of the input geometry the mesh was baked from.</summary>
    public AABB Bounds;

    /// <summary>Builds a failed result carrying <paramref name="error"/> as the reason.</summary>
    public static NavMeshBuildResult Failed(string error) => new() { Success = false, Error = error };
}
