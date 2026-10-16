// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators;

internal static class ExtrudeOp
{
    /// <summary>
    /// Pushes faces out along their normals and joins them to what they were part of with walls. Faces joined
    /// by hidden edges are one face and move together. In per face mode every face moves on its own, otherwise
    /// the selection moves as one region. Faces and walls carry the attributes of the face they came from, and
    /// edges and vertices left inside the region that nothing uses any more are removed.
    /// </summary>
    internal static void ExtrudeFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToExtrude, float distance, ExtrudeMode mode = ExtrudeMode.AlongNormals)
    {
        var faces = facesToExtrude.Where(mesh.Faces.Contains).SelectMany(f => f.LogicalFace()).Distinct().ToList();
        if (faces.Count == 0) return;

        if (mode != ExtrudeMode.PerFace)
        {
            ExtrudeRegion(mesh, faces, distance, mode);
            return;
        }

        var done = new HashSet<GeometryData.Face>();
        foreach (var face in faces)
        {
            if (done.Contains(face)) continue;
            List<GeometryData.Face> group = face.LogicalFace();
            done.UnionWith(group);
            ExtrudeRegion(mesh, group, distance, ExtrudeMode.AverageNormal);
        }
    }

    private static void ExtrudeRegion(GeometryData mesh, List<GeometryData.Face> faces, float distance, ExtrudeMode mode)
    {
        var snapshots = faces.Select(f => FaceSnapshot.Capture(mesh, f)).ToList();
        var normals = faces.Select(f => f.Normal()).ToList();
        var oldEdges = faces.SelectMany(f => f.NeighborEdges()).Distinct().ToList();
        var edgeAttributes = oldEdges.ToDictionary(e => GeometryOperators.GetCanonicalEdge(e.Vert1, e.Vert2), e => AttributeTransfer.Copy(e.Attributes));
        var oldVertices = snapshots.SelectMany(s => s.Vertices).Distinct().ToList();

        Float3 averageNormal = Float3.Zero;
        foreach (var normal in normals) averageNormal += normal;
        averageNormal = Float3.LengthSquared(averageNormal) > 1e-12f ? Float3.Normalize(averageNormal) : normals[0];

        var vertexNormals = new Dictionary<GeometryData.Vertex, Float3>();
        for (int i = 0; i < faces.Count; i++)
            foreach (var v in snapshots[i].Vertices)
                vertexNormals[v] = (vertexNormals.TryGetValue(v, out var sum) ? sum : Float3.Zero) + normals[i];

        var usage = new Dictionary<(GeometryData.Vertex, GeometryData.Vertex), int>();
        foreach (var snapshot in snapshots)
        {
            var verts = snapshot.Vertices;
            for (int k = 0; k < verts.Count; k++)
            {
                var key = GeometryOperators.GetCanonicalEdge(verts[k], verts[(k + 1) % verts.Count]);
                usage[key] = usage.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }

        var moved = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
        foreach (var v in oldVertices)
        {
            Float3 direction = averageNormal;
            if (mode == ExtrudeMode.AlongNormals && Float3.LengthSquared(vertexNormals[v]) > 1e-12f) direction = Float3.Normalize(vertexNormals[v]);
            var made = mesh.AddVertex(v.Point + direction * distance);
            GeometryOperators.AttributeLerp(mesh, made, v, v, 1f);
            moved[v] = made;
        }

        foreach (var face in faces) mesh.RemoveFace(face);

        foreach (FaceSnapshot snapshot in snapshots)
        {
            var top = mesh.AddFace(snapshot.Vertices.Select(v => moved[v]).ToArray());
            if (top == null) continue;

            var origin = snapshot.Vertices.ToDictionary(v => moved[v]);
            snapshot.ApplyFaceAttributes(top);
            FaceSnapshot.ApplyLoops(top, v => snapshot.LoopOf(origin[v]));
        }

        // The moved outline keeps what the old one carried, hidden joins between the pieces included
        foreach (var pair in edgeAttributes)
            if (mesh.FindEdge(moved[pair.Key.Item1], moved[pair.Key.Item2]) is { } copy)
                AttributeTransfer.Apply(pair.Value, copy.Attributes);

        foreach (FaceSnapshot snapshot in snapshots)
        {
            var verts = snapshot.Vertices;
            for (int k = 0; k < verts.Count; k++)
            {
                var v1 = verts[k];
                var v2 = verts[(k + 1) % verts.Count];
                if (usage[GeometryOperators.GetCanonicalEdge(v1, v2)] != 1) continue;

                var e1 = moved[v1];
                var e2 = moved[v2];
                var wall = mesh.AddFace(v1, v2, e2, e1);
                if (wall == null) continue;

                // A wall stands across the face it grew from, so the face's texture mapping does not fit it
                snapshot.ApplyFaceAttributes(wall);
                wall.Mapping = null;
                FaceSnapshot.ApplyLoops(wall, v => snapshot.LoopOf(v == e1 ? v1 : v == e2 ? v2 : v));
            }
        }

        foreach (var edge in oldEdges)
            if (edge.Loop == null && mesh.Edges.Contains(edge)) mesh.RemoveEdge(edge);
        foreach (var vertex in oldVertices)
            if (vertex.Edge == null && mesh.Vertices.Contains(vertex)) mesh.RemoveVertex(vertex);
    }
}
