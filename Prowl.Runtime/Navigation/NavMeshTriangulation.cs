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

/// <summary>What a navmesh polygon edge borders, for debug drawing: the walkable boundary,
/// another polygon, or the seam to a neighbouring tile.</summary>
public enum NavMeshEdgeKind : byte
{
    /// <summary>Nothing walkable on the far side — the edge of the mesh.</summary>
    Border,

    /// <summary>Another polygon in the same tile.</summary>
    Inner,

    /// <summary>A tile seam; the far side lives in the neighbouring tile.</summary>
    TilePortal,
}

/// <summary>One segment of a navmesh polygon edge, classified. These are the polygon outlines —
/// the mesh's structure — as opposed to the height-detail triangles that carpet their interiors.
/// An outline is reported as the chain of segments the height detail actually renders, since the
/// detail bends the surface between corners and a single chord corner to corner would leave the
/// surface it is meant to outline.</summary>
public readonly struct NavMeshEdge(Float3 a, Float3 b, NavMeshEdgeKind kind)
{
    public readonly Float3 A = a;
    public readonly Float3 B = b;
    public readonly NavMeshEdgeKind Kind = kind;
}

/// <summary>
/// A triangulated snapshot of a navmesh, for debug drawing and user tooling
/// (matches Unity's NavMeshTriangulation). Triangles come from each polygon's height detail,
/// which is the surface an agent is actually placed on — a polygon's own corners describe only
/// its outline, and reading heights from those alone flattens whatever the polygon spans.
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

    /// <summary>Parallel to <see cref="Vertices"/>: true for a polygon corner, false for a
    /// vertex the height detail added between corners. Corners are the navmesh's structure —
    /// welded, shared across polygons and stitched across tiles — while detail vertices belong
    /// to one polygon's surface only, which is the distinction debug drawing wants to show.</summary>
    public bool[] IsPolygonCorner;

    /// <summary>Polygon outline edges, classified (border / inner / tile seam). Inner edges are
    /// reported once per pair.</summary>
    public NavMeshEdge[] Edges;

    /// <summary>The mesh's off-mesh connections. Kept apart from the triangles because a
    /// connection is somewhere an agent may travel, not surface it travels on.</summary>
    public NavMeshConnection[] Connections;

    /// <summary>An empty triangulation (no navmesh to walk).</summary>
    public static NavMeshTriangulation Empty => new() { Vertices = [], Indices = [], Areas = [], IsPolygonCorner = [], Edges = [], Connections = [] };

    /// <summary>
    /// Fan-triangulate every walkable polygon of a Detour navmesh. Callers holding a live
    /// instance must take its read lock around this; callers triangulating a mesh they built
    /// themselves (an unregistered asset, e.g. for editor gizmos) own it exclusively already.
    /// </summary>
    public static NavMeshTriangulation FromNavMesh(DtNavMesh mesh)
    {
        if (mesh == null) return Empty;

        List<Float3> vertices = [];
        List<bool> isCorner = [];
        List<int> indices = [];
        List<int> areas = [];
        List<NavMeshEdge> edges = [];
        List<NavMeshConnection> connections = [];
        List<(float T, int Index)> bends = [];

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

                int area = NavMeshAreas.FromDetourArea(poly.GetArea());
                int baseVert = vertices.Count;
                for (int v = 0; v < poly.vertCount; v++)
                {
                    vertices.Add(NavMeshConnection.VertexAt(tile, poly.verts[v]));
                    isCorner.Add(true);
                }

                if (tile.data.detailMeshes == null)
                {
                    // No detail to read: the polygon is its own flat fan (as Detour also assumes),
                    // and its outlines are the corner-to-corner chords.
                    for (int v = 0; v < poly.vertCount; v++)
                        if (TryClassifyEdge(poly, v, p, out NavMeshEdgeKind flatKind))
                            edges.Add(new NavMeshEdge(vertices[baseVert + v], vertices[baseVert + (v + 1) % poly.vertCount], flatKind));

                    for (int v = 2; v < poly.vertCount; v++)
                    {
                        indices.Add(baseVert);
                        indices.Add(baseVert + v - 1);
                        indices.Add(baseVert + v);
                        areas.Add(area);
                    }
                    continue;
                }

                // A detail sub-mesh reuses the polygon's corners as its first vertices and stores
                // only the ones it added, so appending those keeps every detail index — corner or
                // added — at baseVert + index.
                DtPolyDetail detail = tile.data.detailMeshes[p];
                for (int v = 0; v < detail.vertCount; v++)
                {
                    int i = (detail.vertBase + v) * 3;
                    vertices.Add(new Float3(tile.data.detailVerts[i], tile.data.detailVerts[i + 1], tile.data.detailVerts[i + 2]));
                    isCorner.Add(false);
                }

                for (int d = 0; d < detail.triCount; d++)
                {
                    int i = (detail.triBase + d) * 4;
                    indices.Add(baseVert + tile.data.detailTris[i]);
                    indices.Add(baseVert + tile.data.detailTris[i + 1]);
                    indices.Add(baseVert + tile.data.detailTris[i + 2]);
                    areas.Add(area);
                }

                // Outlines follow the detail: every detail vertex the builder placed along an
                // edge is a bend in the rendered surface, so the edge is reported as the chain
                // through them. Which vertices those are is read off the geometry rather than
                // the detail triangles' boundary flags, since a vertex shared by two edges (a
                // corner's own copy) carries no flag of its own.
                for (int v = 0; v < poly.vertCount; v++)
                {
                    if (!TryClassifyEdge(poly, v, p, out NavMeshEdgeKind kind)) continue;

                    Float3 a = vertices[baseVert + v];
                    Float3 b = vertices[baseVert + (v + 1) % poly.vertCount];
                    bends.Clear();
                    for (int d = 0; d < detail.vertCount; d++)
                    {
                        Float3 q = vertices[baseVert + poly.vertCount + d];
                        if (TryEdgeParameter(a, b, q, out float along))
                            bends.Add((along, baseVert + poly.vertCount + d));
                    }

                    bends.Sort(static (x, y) => x.T.CompareTo(y.T));
                    Float3 from = a;
                    foreach ((float _, int index) in bends)
                    {
                        edges.Add(new NavMeshEdge(from, vertices[index], kind));
                        from = vertices[index];
                    }

                    edges.Add(new NavMeshEdge(from, b, kind));
                }
            }
        }

        return new NavMeshTriangulation
        {
            Vertices = [.. vertices],
            Indices = [.. indices],
            Areas = [.. areas],
            IsPolygonCorner = [.. isCorner],
            Edges = [.. edges],
            Connections = [.. connections],
        };
    }

    /// <summary>What the polygon's edge starting at <paramref name="v"/> borders, or false when
    /// another polygon already reported it — an interior edge belongs to the lower-indexed of the
    /// pair, so it is drawn once.</summary>
    private static bool TryClassifyEdge(DtPoly poly, int v, int p, out NavMeshEdgeKind kind)
    {
        int nei = poly.neis[v];
        kind = nei == 0 ? NavMeshEdgeKind.Border
            : (nei & DtDetour.DT_EXT_LINK) != 0 ? NavMeshEdgeKind.TilePortal
            : NavMeshEdgeKind.Inner;
        return nei == 0 || (nei & DtDetour.DT_EXT_LINK) != 0 || nei - 1 >= p;
    }

    /// <summary>Where <paramref name="q"/> falls along the edge a→b, if it lies on it. Detail
    /// vertices are placed on the edge line in XZ and carry the surface's height, so the test is
    /// horizontal; the tolerance is the one the detail builder itself uses to decide a vertex is
    /// on a polygon boundary.</summary>
    private static bool TryEdgeParameter(Float3 a, Float3 b, Float3 q, out float t)
    {
        const float onEdgeSq = 0.001f * 0.001f;
        t = 0;
        float dx = b.X - a.X, dz = b.Z - a.Z;
        float lenSq = dx * dx + dz * dz;
        if (lenSq < 1e-12f) return false;

        t = ((q.X - a.X) * dx + (q.Z - a.Z) * dz) / lenSq;
        if (t <= 0 || t >= 1) return false;

        float ex = a.X + dx * t - q.X, ez = a.Z + dz * t - q.Z;
        return ex * ex + ez * ez <= onEdgeSq;
    }
}
