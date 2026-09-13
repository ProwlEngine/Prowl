// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Navigation;

/// <summary>Which GameObjects a <see cref="NavMeshSurface"/> bake draws geometry from.</summary>
public enum NavMeshCollectObjects
{
    /// <summary>Every matching GameObject in the scene, filtered only by <see cref="NavMeshSurface.LayerMask"/>.</summary>
    All,

    /// <summary>Only GameObjects whose collected geometry falls at least partly inside
    /// <see cref="NavMeshSurface.Center"/>/<see cref="NavMeshSurface.Size"/> - a declared bake volume,
    /// local to the surface's own transform.</summary>
    Volume,

    /// <summary>Only the surface's own GameObject and its descendants.</summary>
    Children,
}
