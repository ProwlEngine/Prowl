// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Navigation;

/// <summary>Which <c>DtTileCache</c> obstacle primitive a <see cref="NavMeshObstacle"/> carves with.</summary>
public enum NavMeshObstacleShape
{
    /// <summary>An oriented box, reduced to its world AABB - see <see cref="NavMeshObstacle"/>'s own
    /// doc comment for why <c>DtTileCache</c> can't carve the oriented box exactly.</summary>
    Box,

    /// <summary>An upright cylinder (<see cref="NavMeshObstacle.Radius"/>/<see cref="NavMeshObstacle.Height"/>),
    /// carved via <c>DtTileCache</c>'s own cylinder obstacle - the shape Unity calls "Capsule" for this
    /// component, though what actually gets carved is a cylinder, not a capsule with rounded caps.</summary>
    Capsule,
}
