// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// One chunk of triangle geometry contributed to a navmesh bake: source-local vertices and
/// indices plus the transform into world space. Collected from renderers, colliders, terrain,
/// or supplied directly by user code for procedural geometry.
/// </summary>
public struct NavMeshGeometrySource
{
    /// <summary>Vertices in source-local space.</summary>
    public Float3[] Vertices;

    /// <summary>Triangle indices into <see cref="Vertices"/> (three per triangle).</summary>
    public int[] Indices;

    /// <summary>Transforms <see cref="Vertices"/> into world space.</summary>
    public Float4x4 Transform;

    /// <summary>Sentinel for <see cref="Area"/>: the source takes the bake's default area.</summary>
    public const int UnspecifiedArea = -1;

    /// <summary>The navigation area for this geometry (index into <see cref="NavMeshAreas"/>).
    /// Walkable polygons rasterized from this source carry this area, so per-source costs and
    /// masks apply. <see cref="UnspecifiedArea"/> (the constructor default) falls back to the
    /// bake's default area. Where surfaces of different areas overlap vertically within the
    /// climb threshold, the HIGHER area index wins the merged span (Recast's convention) — not
    /// the higher cost — so order user areas accordingly when stacking geometry.</summary>
    public int Area;

    public NavMeshGeometrySource(Float3[] vertices, int[] indices, Float4x4 transform, int area = UnspecifiedArea)
    {
        Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        Indices = indices ?? throw new ArgumentNullException(nameof(indices));
        Transform = transform;
        Area = area;
    }

    /// <summary>Number of whole triangles described by <see cref="Indices"/>.</summary>
    public readonly int TriangleCount => (Indices?.Length ?? 0) / 3;
}
