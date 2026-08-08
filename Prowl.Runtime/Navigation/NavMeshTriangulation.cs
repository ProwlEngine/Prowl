// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Recast.Detour;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// An off-mesh connection an agent can actually traverse, at the endpoints Detour snapped onto
/// walkable polygons — which is not necessarily where the <see cref="NavMeshLink"/> that produced
/// it asked for. A link that reached nothing walkable is reported here not at all, which is the
/// only way to tell it failed short of watching an agent refuse to cross.
/// </summary>
public readonly struct NavMeshConnection(Float3 start, Float3 end, float radius, int area, bool bidirectional, int linkId)
{
    public readonly Float3 Start = start;
    public readonly Float3 End = end;

    /// <summary>Endpoint radius: half the width the link was built with.</summary>
    public readonly float Radius = radius;

    /// <summary>Area index (see <see cref="NavMeshAreas"/>), which is also what it costs.</summary>
    public readonly int Area = area;

    public readonly bool Bidirectional = bidirectional;

    /// <summary>The <see cref="NavMeshLink.LinkId"/> stamped at bake, or 0 for a connection that
    /// came from somewhere else.</summary>
    public readonly int LinkId = linkId;

    /// <summary>
    /// Read one of a tile's connections, unless it is not traversable end to end. Detour keeps
    /// the stub in the tile whichever end failed, so the test is on the links themselves.
    /// Everything worth reporting hangs off the connection's polygon rather than the connection
    /// — the area, and the endpoints, since <c>con.pos</c> holds what was asked for while the
    /// polygon's vertices hold where the ends snapped to.
    /// </summary>
    internal static bool TryFrom(DtMeshTile tile, DtOffMeshConnection con, out NavMeshConnection connection)
    {
        connection = default;
        DtPoly poly = tile.data.polys[con.poly];
        if (!IsAttachedAtBothEnds(tile, poly)) return false;

        connection = new NavMeshConnection(
            VertexAt(tile, poly.verts[0]), VertexAt(tile, poly.verts[1]), con.rad,
            NavMeshAreas.FromDetourArea(poly.GetArea()),
            (con.flags & DtDetour.DT_OFFMESH_CON_BIDIR) != 0,
            con.userId);
        return true;
    }

    /// <summary>
    /// A connection's two ends are attached independently — the start when its own tile is
    /// built, the far end when the tile it lands in is — and each leaves a link on the
    /// connection's polygon tagged with which end it is. An end that found nothing walkable
    /// within the connection's radius leaves none, and an agent arriving at a connection with
    /// no far end has nowhere to come out: the path across is partial, not complete.
    /// </summary>
    private static bool IsAttachedAtBothEnds(DtMeshTile tile, DtPoly poly)
    {
        bool start = false, end = false;
        for (int i = poly.firstLink; i != DtDetour.DT_NULL_LINK; i = tile.links[i].next)
        {
            if (tile.links[i].edge == 0) start = true;
            else if (tile.links[i].edge == 1) end = true;
        }
        return start && end;
    }

    /// <summary>One of a tile's vertices, which are stored as loose floats.</summary>
    internal static Float3 VertexAt(DtMeshTile tile, int vertexIndex)
    {
        int i = vertexIndex * 3;
        return new Float3(tile.data.verts[i], tile.data.verts[i + 1], tile.data.verts[i + 2]);
    }
}

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

    /// <summary>The mesh's off-mesh connections. Kept apart from the triangles because a
    /// connection is somewhere an agent may travel, not surface it travels on.</summary>
    public NavMeshConnection[] Connections;

    /// <summary>An empty triangulation (no navmesh to walk).</summary>
    public static NavMeshTriangulation Empty => new() { Vertices = [], Indices = [], Areas = [], Connections = [] };

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
        List<NavMeshConnection> connections = [];

        for (int t = 0; t < mesh.GetMaxTiles(); t++)
        {
            DtMeshTile tile = mesh.GetTile(t);
            if (tile?.data?.header == null) continue;

            foreach (DtOffMeshConnection con in tile.data.offMeshCons ?? [])
                if (NavMeshConnection.TryFrom(tile, con, out NavMeshConnection connection))
                    connections.Add(connection);

            for (int p = 0; p < tile.data.header.polyCount; p++)
            {
                DtPoly poly = tile.data.polys[p];
                // Its two vertices are endpoints, not a surface; it is reported in Connections.
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION) continue;

                int baseVert = vertices.Count;
                for (int v = 0; v < poly.vertCount; v++)
                    vertices.Add(NavMeshConnection.VertexAt(tile, poly.verts[v]));

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

        return new NavMeshTriangulation
        {
            Vertices = [.. vertices],
            Indices = [.. indices],
            Areas = [.. areas],
            Connections = [.. connections],
        };
    }
}
