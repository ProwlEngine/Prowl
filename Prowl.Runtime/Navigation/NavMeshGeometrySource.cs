// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Navigation;

/// <summary>Which geometry a <see cref="NavMeshSurface"/> bake voxelizes.</summary>
public enum NavMeshGeometrySource
{
    /// <summary><see cref="MeshRenderer"/> meshes only.</summary>
    RenderMeshes,

    /// <summary><see cref="BoxCollider"/>/<see cref="MeshCollider"/> shapes only - useful when render
    /// meshes carry detail (foliage, greebles) a walkable surface shouldn't be shaped by, but their
    /// collision proxies should still stand in for them.</summary>
    PhysicsColliders,
}
