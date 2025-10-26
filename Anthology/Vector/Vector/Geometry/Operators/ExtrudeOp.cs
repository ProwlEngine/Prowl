// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class ExtrudeOp
    {
        internal static void ExtrudeFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToExtrude, double distance, ExtrudeMode mode = ExtrudeMode.AlongNormals)
        {
            var facesToExtrudeList = facesToExtrude.ToList();
            if (facesToExtrudeList.Count == 0) return;

            // Calculate face normals for all faces
            var faceNormals = new Dictionary<GeometryData.Face, Double3>();
            foreach (var face in facesToExtrudeList)
            {
                var verts = face.NeighborVertices();
                if (verts.Count >= 3)
                {
                    Double3 v0 = verts[0].Point;
                    Double3 v1 = verts[1].Point;
                    Double3 v2 = verts[2].Point;
                    faceNormals[face] = Double3.Normalize(Double3.Cross(v1 - v0, v2 - v0));
                }
                else
                {
                    faceNormals[face] = Double3.Zero;
                }
            }

            // Calculate average normal for AverageNormal mode
            Double3 averageNormal = Double3.Zero;
            if (mode == ExtrudeMode.AverageNormal)
            {
                foreach (var normal in faceNormals.Values)
                {
                    averageNormal += normal;
                }
                if (facesToExtrudeList.Count > 0)
                {
                    averageNormal = Double3.Normalize(averageNormal);
                }
            }

            // For AlongNormals mode, calculate averaged vertex normals
            var vertexNormals = new Dictionary<GeometryData.Vertex, Double3>();
            if (mode == ExtrudeMode.AlongNormals)
            {
                // For each vertex, average the normals of all selected faces using it
                foreach (var face in facesToExtrudeList)
                {
                    var verts = face.NeighborVertices();
                    var faceNormal = faceNormals[face];

                    foreach (var vert in verts)
                    {
                        if (!vertexNormals.ContainsKey(vert))
                        {
                            vertexNormals[vert] = Double3.Zero;
                        }
                        vertexNormals[vert] += faceNormal;
                    }
                }

                // Normalize the accumulated normals
                foreach (var vert in vertexNormals.Keys.ToList())
                {
                    vertexNormals[vert] = Double3.Normalize(vertexNormals[vert]);
                }
            }

            // Track which vertices we've created for the extruded surface
            // For PerFace mode, we'll have separate mappings per face
            var vertexMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

            // Track all edges that are part of the extruded faces
            var edgeUsageCount = new Dictionary<(GeometryData.Vertex, GeometryData.Vertex), int>();

            // First pass: count edge usage to identify boundary edges (only for shared vertex modes)
            if (mode != ExtrudeMode.PerFace)
            {
                foreach (var face in facesToExtrudeList)
                {
                    var verts = face.NeighborVertices();
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var v1 = verts[i];
                        var v2 = verts[(i + 1) % verts.Count];

                        // Create a canonical edge key (smaller vertex first for consistency)
                        var edgeKey = GeometryOperators.GetCanonicalEdge(v1, v2);

                        if (!edgeUsageCount.ContainsKey(edgeKey))
                            edgeUsageCount[edgeKey] = 0;
                        edgeUsageCount[edgeKey]++;
                    }
                }
            }

            // Store face data for recreation
            var faceData = new List<(
                GeometryData.Face originalFace,
                List<GeometryData.Vertex> verts,
                Dictionary<string, GeometryData.AttributeValue> faceAttrs,
                List<Dictionary<string, GeometryData.AttributeValue>> loopAttrs,
                Dictionary<GeometryData.Vertex, GeometryData.Vertex> perFaceMapping)>();

            foreach (var face in facesToExtrudeList)
            {
                var verts = face.NeighborVertices();
                var perFaceMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

                // Create extruded vertices based on mode
                foreach (var vert in verts)
                {
                    GeometryData.Vertex newVert;

                    if (mode == ExtrudeMode.PerFace)
                    {
                        // PerFace: Always create new vertices for this face
                        Double3 normal = faceNormals[face];
                        Double3 newPos = vert.Point + normal * distance;
                        newVert = mesh.AddVertex(newPos);
                        GeometryOperators.AttributeLerp(mesh, newVert, vert, vert, 1.0);
                        perFaceMapping[vert] = newVert;
                    }
                    else if (!vertexMapping.ContainsKey(vert))
                    {
                        // AlongNormals or AverageNormal: Create shared vertices
                        Double3 normal;
                        if (mode == ExtrudeMode.AlongNormals)
                        {
                            normal = vertexNormals[vert];
                        }
                        else // AverageNormal
                        {
                            normal = averageNormal;
                        }

                        Double3 newPos = vert.Point + normal * distance;
                        newVert = mesh.AddVertex(newPos);
                        GeometryOperators.AttributeLerp(mesh, newVert, vert, vert, 1.0);
                        vertexMapping[vert] = newVert;
                    }
                }

                // Store face attributes
                var faceAttrs = new Dictionary<string, GeometryData.AttributeValue>();
                foreach (var attr in mesh.FaceAttributes)
                {
                    if (face.Attributes.ContainsKey(attr.Name))
                        faceAttrs[attr.Name] = GeometryData.AttributeValue.Copy(face.Attributes[attr.Name]);
                }

                // Store loop attributes
                var loopAttrs = new List<Dictionary<string, GeometryData.AttributeValue>>();
                if (face.Loop != null)
                {
                    var it = face.Loop;
                    do
                    {
                        var attrs = new Dictionary<string, GeometryData.AttributeValue>();
                        foreach (var attr in mesh.LoopAttributes)
                        {
                            if (it.Attributes.ContainsKey(attr.Name))
                                attrs[attr.Name] = GeometryData.AttributeValue.Copy(it.Attributes[attr.Name]);
                        }
                        loopAttrs.Add(attrs);
                        it = it.Next;
                    } while (it != face.Loop);
                }

                faceData.Add((face, verts, faceAttrs, loopAttrs, perFaceMapping));
            }

            // Remove original faces
            foreach (var face in facesToExtrudeList)
            {
                mesh.RemoveFace(face);
            }

            // Create extruded top faces
            for (int faceIdx = 0; faceIdx < faceData.Count; faceIdx++)
            {
                var (originalFace, verts, faceAttrs, loopAttrs, perFaceMapping) = faceData[faceIdx];

                // Map to new vertices based on mode
                GeometryData.Vertex[] newVerts;
                if (mode == ExtrudeMode.PerFace)
                {
                    newVerts = verts.Select(v => perFaceMapping[v]).ToArray();
                }
                else
                {
                    newVerts = verts.Select(v => vertexMapping[v]).ToArray();
                }

                var newFace = mesh.AddFace(newVerts);
                if (newFace != null)
                {
                    // Restore face attributes
                    foreach (var kvp in faceAttrs)
                    {
                        newFace.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                    }

                    // Restore loop attributes
                    if (newFace.Loop != null && loopAttrs.Count > 0)
                    {
                        var it = newFace.Loop;
                        int idx = 0;

                        do
                        {
                            if (idx < loopAttrs.Count)
                            {
                                foreach (var kvp in loopAttrs[idx])
                                {
                                    it.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                                }
                            }
                            it = it.Next;
                            idx++;
                        } while (it != newFace.Loop);
                    }
                }
            }

            // Create wall faces for boundary edges
            if (mode == ExtrudeMode.PerFace)
            {
                // PerFace: Each face creates its own walls
                foreach (var (originalFace, verts, _, _, perFaceMapping) in faceData)
                {
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var v1 = verts[i];
                        var v2 = verts[(i + 1) % verts.Count];

                        var extV1 = perFaceMapping[v1];
                        var extV2 = perFaceMapping[v2];

                        // Create quad wall face
                        mesh.AddFace(v1, v2, extV2, extV1);
                    }
                }
            }
            else
            {
                // AlongNormals or AverageNormal: Only create walls for boundary edges
                foreach (var (originalFace, verts, _, _, _) in faceData)
                {
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var v1 = verts[i];
                        var v2 = verts[(i + 1) % verts.Count];

                        // Create canonical edge key
                        var edgeKey = GeometryOperators.GetCanonicalEdge(v1, v2);

                        // Only create a wall if this edge was on the boundary (used by only one face)
                        if (edgeUsageCount[edgeKey] == 1)
                        {
                            var extV1 = vertexMapping[v1];
                            var extV2 = vertexMapping[v2];

                            // Create quad wall face
                            mesh.AddFace(v1, v2, extV2, extV1);
                        }
                    }
                }
            }
        }
    }
}
