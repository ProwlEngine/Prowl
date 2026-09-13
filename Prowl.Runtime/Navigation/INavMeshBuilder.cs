// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Turns a triangle soup into baked <see cref="NavMeshTileCacheData"/>. The extension point for the
/// whole generation subsystem: swap in a different implementation to change how baking works without
/// touching anything downstream, since everything downstream (<see cref="NavMesh"/>,
/// <see cref="NavMeshQuery"/>, editor baking) only ever sees the engine-owned result types.
/// </summary>
public interface INavMeshBuilder
{
    /// <summary>Bakes <paramref name="input"/> into a navmesh using <paramref name="settings"/>.</summary>
    NavMeshBuildResult Build(NavMeshBuildInput input, NavMeshBakeSettings settings);
}
