// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Navigation;

/// <summary>One named slot in <see cref="NavMeshAreas"/>'s 32-entry table. The slot's position is its
/// identity - see <see cref="NavMeshAreas"/> for why that differs from <see cref="NavMeshAgentTypeInfo"/>'s
/// separately stored id.</summary>
public sealed class NavMeshAreaInfo
{
    /// <summary>Display name. The reserved slots (<see cref="NavMeshAreas.Walkable"/>,
    /// <see cref="NavMeshAreas.NotWalkable"/>, <see cref="NavMeshAreas.Jump"/>) ignore attempts to
    /// rename them; every other slot is free to rename.</summary>
    public string Name = "";

    /// <summary>Traversal cost for this area. Clamped to at least 1 wherever it's set - see
    /// <see cref="NavMeshAreas.SetAreaCost"/> for why.</summary>
    public float Cost = 1f;
}
