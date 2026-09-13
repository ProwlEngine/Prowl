// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>Raw geometry for a navmesh bake: a triangle soup in world space, plus the area-painting
/// overlay a tiled bake applies on top of it. No connectivity is assumed between triangles -
/// <see cref="INavMeshBuilder"/> implementations rasterize each one independently, so shared vertices
/// are never required.
/// <para/>
/// Off-mesh connections (<see cref="NavMeshLink"/>) are deliberately not part of this type: unlike an
/// area volume, a connection doesn't change the baked heightfield at all, so it stays a
/// <see cref="NavMeshQuery"/>-construction-time concern (see that class's own doc comment) rather than
/// something a bake needs to know about - exactly as before this bake became tiled.</summary>
public sealed class NavMeshBuildInput
{
    /// <summary>Triangle vertices, three consecutive entries per triangle, in world space. Every
    /// triangle here is walkable-eligible ground (or a real obstacle already excluded by
    /// <see cref="NavMeshModifier.IgnoreFromBuild"/>) - which area each one actually ends up baked with
    /// is decided by <see cref="AreaVolumes"/>, not per-triangle, matching how Recast's own tiled
    /// pipeline paints areas.</summary>
    public Float3[] Triangles = [];

    /// <summary>Number of triangles in <see cref="Triangles"/> (a third of its length, since each
    /// triangle contributes three consecutive vertex entries).</summary>
    public int TriangleCount => Triangles.Length / 3;

    /// <summary>World-space regions that re-mark whatever baked geometry falls inside them - from
    /// <see cref="NavMeshModifierVolume"/> only. A dynamic <see cref="NavMeshObstacle"/> is deliberately
    /// not collected here: it never touches a bake at all, living entirely in <c>DtTileCache</c>'s own
    /// runtime obstacle system instead (see that class's own doc comment). Later entries win over
    /// earlier ones where they overlap, the same as before.</summary>
    public List<NavMeshAreaVolumeInput> AreaVolumes = [];
}

/// <summary>A world-space prism (a convex XZ footprint extruded between two heights) that stamps
/// <see cref="Area"/> onto any baked geometry inside it, converted to world space once per bake rather
/// than re-evaluated per triangle centroid.</summary>
public readonly struct NavMeshAreaVolumeInput
{
    /// <summary>The footprint's corners on the XZ plane, in world space, wound consistently (winding
    /// order does not matter to Recast's own convex-volume marking, only planarity and convexity do).</summary>
    public readonly Float3[] FootprintXZ;

    /// <summary>Lower world-space Y bound the prism extends from.</summary>
    public readonly float MinY;

    /// <summary>Upper world-space Y bound the prism extends to.</summary>
    public readonly float MaxY;

    /// <summary>The area (see <see cref="NavMeshAreas"/>) this volume stamps.</summary>
    public readonly int Area;

    public NavMeshAreaVolumeInput(Float3[] footprintXZ, float minY, float maxY, int area)
    {
        FootprintXZ = footprintXZ;
        MinY = minY;
        MaxY = maxY;
        Area = area;
    }
}
