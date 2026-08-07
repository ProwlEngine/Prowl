// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Recast.Detour;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A triangulated snapshot of a navmesh, for debug drawing and user tooling
/// (matches Unity's NavMeshTriangulation). Triangles are fan-triangulated from the navmesh
/// polygons (not the height-detail mesh), which is exact in XZ and approximate in Y on slopes.
/// </summary>
public struct NavMeshTriangulation
{
    /// <summary>World-space vertices.</summary>
    public Float3[] Vertices;

    /// <summary>Triangle indices into <see cref="Vertices"/> (three per triangle).</summary>
    public int[] Indices;

    /// <summary>Per-triangle area index (see <see cref="NavMeshAreas"/>), parallel to
    /// <see cref="Indices"/> / 3.</summary>
    public int[] Areas;

    /// <summary>An empty triangulation (no navmesh to walk).</summary>
    public static NavMeshTriangulation Empty => new() { Vertices = [], Indices = [], Areas = [] };

    /// <summary>
    /// Fan-triangulate every walkable polygon of a Detour navmesh. Callers holding a live
    /// instance must take its read lock around this; callers triangulating a mesh they built
    /// themselves (an unregistered asset, e.g. for editor gizmos) own it exclusively already.
    /// </summary>
    public static NavMeshTriangulation FromNavMesh(DtNavMesh mesh)
    {
        if (mesh == null) return Empty;

        List<Float3> vertices = [];
        List<int> indices = [];
        List<int> areas = [];

        for (int t = 0; t < mesh.GetMaxTiles(); t++)
        {
            DtMeshTile tile = mesh.GetTile(t);
            if (tile?.data?.header == null) continue;

            for (int p = 0; p < tile.data.header.polyCount; p++)
            {
                DtPoly poly = tile.data.polys[p];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION) continue;

                int baseVert = vertices.Count;
                for (int v = 0; v < poly.vertCount; v++)
                {
                    int vi = poly.verts[v] * 3;
                    vertices.Add(new Float3(tile.data.verts[vi], tile.data.verts[vi + 1], tile.data.verts[vi + 2]));
                }

                int area = NavMeshAreas.FromDetourArea(poly.GetArea());
                for (int v = 2; v < poly.vertCount; v++)
                {
                    indices.Add(baseVert);
                    indices.Add(baseVert + v - 1);
                    indices.Add(baseVert + v);
                    areas.Add(area);
                }
            }
        }

        return new NavMeshTriangulation { Vertices = [.. vertices], Indices = [.. indices], Areas = [.. areas] };
    }
}
