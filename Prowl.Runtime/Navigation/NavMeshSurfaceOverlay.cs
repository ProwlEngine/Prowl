// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// The walkable surface overlay for one <see cref="NavMeshSurface"/>, colored per area. The
/// triangulation is cached and rebuilt when the navmesh it was built from changes.
/// </summary>
internal sealed class NavMeshSurfaceOverlay
{
    // ~4 Hz at 60 fps: fast enough to watch a carve, slow enough that the cost stops mattering.
    private const int StaleDraws = 15;

    private NavMeshTriangulation? _triangulation;
    private NavMeshWorld? _world;
    // What the cached triangulation was built from, so it rebuilds when the asset is swapped or the
    // surface's live registration changes (a rebake, or registering and unregistering).
    private NavMeshData? _source;
    private NavMeshInstance? _builtFrom;
    private bool _stale;
    private int _drawsSinceTriangulation;
    private List<(Float3 Position, bool Corner)>? _vertexMarkers;
    private List<(Float3 A, Float3 B)>? _detailEdges;

    private void Invalidate() => _triangulation = null;

    private void MarkStale() => _stale = true;

    /// <summary>Drop the cache and the world subscriptions, so a disabled surface is not kept alive by its world.</summary>
    public void Release()
    {
        if (_world != null)
        {
            _world.NavMeshSettled -= Invalidate;
            _world.NavMeshChanged -= MarkStale;
            _world = null;
        }
        _triangulation = null;
    }

    public void Draw(NavMeshSurface surface, NavMeshWorld? world)
    {
        NavMeshData? data = surface.NavMeshData.Res;
        if (data.IsNotValid() || !data!.HasTiles)
            return;

        if (world != null && _world != world)
        {
            Release();
            world.NavMeshSettled += Invalidate;
            world.NavMeshChanged += MarkStale;
            _world = world;
        }

        // Settled invalidates at once; a mesh that merely changed waits. A re-triangulation costs
        // milliseconds and megabytes, and Changed fires on every frame a carve is converging. A
        // cache is not guaranteed to settle either: an obstacle moving every frame re-queues as
        // fast as the pump drains, and watching one carve is what this overlay is for.
        _drawsSinceTriangulation++;
        if (_stale && _drawsSinceTriangulation >= StaleDraws)
            _triangulation = null;

        // Prefer the surface's own live navmesh: it is the one carving and rebuilds change.
        // Asking the world for the agent type instead would draw a rival surface's mesh here.
        NavMeshInstance? live = world != null ? surface.Instance : null;
        if (_triangulation == null || !ReferenceEquals(_builtFrom, live) || !ReferenceEquals(_source, data))
        {
            _stale = false;
            _drawsSinceTriangulation = 0;
            _triangulation = live != null ? world!.CalculateTriangulation(surface.AgentTypeId) : data.CalculateTriangulation();
            _builtFrom = live;
            _source = data;
            _vertexMarkers = null;
            _detailEdges = null;
        }

        NavMeshTriangulation tri = _triangulation.Value;
        // Lifted past the mesh's own error band: quantized heights interpolated between samples can
        // dip a few centimetres below finely tessellated ground and take the occluded gizmo styling.
        var lift = new Float3(0, 0.08f, 0);
        for (int t = 0; t < tri.Areas.Length; t++)
        {
            Debug.DrawTriangle(
                tri.Vertices[tri.Indices[t * 3 + 0]] + lift,
                tri.Vertices[tri.Indices[t * 3 + 1]] + lift,
                tri.Vertices[tri.Indices[t * 3 + 2]] + lift,
                NavMeshDebugDisplay.AreaColor(tri.Areas[t]));
        }

        // Polygon outlines over the fill: the walkable border dark, inner edges light, tile seams warm.
        foreach (NavMeshEdge edge in tri.Edges)
        {
            Color c = edge.Kind switch
            {
                NavMeshEdgeKind.Border => new Color(0.05f, 0.12f, 0.35f, 1f),
                NavMeshEdgeKind.TilePortal => new Color(0.9f, 0.55f, 0.15f, 1f),
                _ => new Color(0.65f, 0.85f, 1f, 0.9f),
            };
            Debug.DrawLine(edge.A + lift, edge.B + lift, c);
        }

        if (NavMeshDebugDisplay.ShowDetail)
            DrawDetailWireframe(_detailEdges ??= BuildDetailEdges(tri), lift);
        if (NavMeshDebugDisplay.ShowVertices)
            DrawVertexMarkers(_vertexMarkers ??= BuildVertexMarkers(tri), lift);

        foreach (NavMeshConnection con in tri.Connections)
            NavMeshDebugDisplay.DrawConnection(con, lift);
    }

    /// <summary>Each height detail edge once. Neighbouring triangles share edges, and drawing three
    /// per triangle would double the alpha where they overlap.</summary>
    private static List<(Float3 A, Float3 B)> BuildDetailEdges(NavMeshTriangulation tri)
    {
        var seen = new HashSet<(int, int)>(tri.Areas.Length * 2);
        var edges = new List<(Float3, Float3)>(tri.Areas.Length * 2);
        for (int t = 0; t < tri.Areas.Length; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                int i = tri.Indices[t * 3 + e];
                int j = tri.Indices[t * 3 + (e + 1) % 3];
                if (seen.Add((Math.Min(i, j), Math.Max(i, j))))
                    edges.Add((tri.Vertices[i], tri.Vertices[j]));
            }
        }

        return edges;
    }

    private static void DrawDetailWireframe(List<(Float3 A, Float3 B)> edges, Float3 lift)
    {
        var c = new Color(1f, 1f, 1f, 0.18f);
        foreach ((Float3 a, Float3 b) in edges)
            Debug.DrawLine(a + lift, b + lift, c);
    }

    /// <summary>
    /// One dot per distinct vertex position: white for polygon corners, orange for vertices the
    /// height detail added. The triangulation repeats shared corners per polygon, so markers dedupe
    /// by position and a corner wins a tie.
    /// </summary>
    private static List<(Float3 Position, bool Corner)> BuildVertexMarkers(NavMeshTriangulation tri)
    {
        var seen = new Dictionary<(int, int, int), bool>(tri.Vertices.Length);
        for (int v = 0; v < tri.Vertices.Length; v++)
        {
            Float3 p = tri.Vertices[v];
            var key = ((int)Math.Round(p.X * 128), (int)Math.Round(p.Y * 128), (int)Math.Round(p.Z * 128));
            bool corner = tri.IsPolygonCorner[v];
            if (seen.TryGetValue(key, out bool wasCorner) && (wasCorner || !corner))
                continue;
            seen[key] = corner;
        }

        var markers = new List<(Float3, bool)>(seen.Count);
        foreach (KeyValuePair<(int, int, int), bool> m in seen)
            markers.Add((new Float3(m.Key.Item1 / 128f, m.Key.Item2 / 128f, m.Key.Item3 / 128f), m.Value));
        return markers;
    }

    private static void DrawVertexMarkers(List<(Float3 Position, bool Corner)> markers, Float3 lift)
    {
        var cornerColor = new Color(1f, 1f, 1f, 1f);
        var detailColor = new Color(1f, 0.6f, 0.1f, 1f);
        foreach ((Float3 position, bool corner) in markers)
            DrawSolidDot(position + lift, 0.03f, corner ? cornerColor : detailColor);
    }

    /// <summary>A tiny solid octahedron: reads as a dot from any angle and costs eight triangles.</summary>
    private static void DrawSolidDot(Float3 p, float r, Color color)
    {
        var xp = new Float3(r, 0, 0); var yp = new Float3(0, r, 0); var zp = new Float3(0, 0, r);
        Debug.DrawTriangle(p + yp, p + xp, p + zp, color);
        Debug.DrawTriangle(p + yp, p + zp, p - xp, color);
        Debug.DrawTriangle(p + yp, p - xp, p - zp, color);
        Debug.DrawTriangle(p + yp, p - zp, p + xp, color);
        Debug.DrawTriangle(p - yp, p + zp, p + xp, color);
        Debug.DrawTriangle(p - yp, p - xp, p + zp, color);
        Debug.DrawTriangle(p - yp, p - zp, p - xp, color);
        Debug.DrawTriangle(p - yp, p + xp, p - zp, color);
    }
}
