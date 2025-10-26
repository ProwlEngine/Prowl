// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Vector.Geometry.Operators
{
    internal class InsetOp
    {
        internal class EdgeInfo
        {
            public GeometryData.Vertex V1 { get; set; }
            public GeometryData.Vertex V2 { get; set; }
            public int UsageCount { get; set; }
            public bool IsBoundary => UsageCount == 1;

            public override int GetHashCode()
            {
                // Canonical edge representation
                var h1 = V1.GetHashCode();
                var h2 = V2.GetHashCode();
                return h1 < h2 ? (h1, h2).GetHashCode() : (h2, h1).GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is EdgeInfo other)
                {
                    return (V1 == other.V1 && V2 == other.V2) ||
                           (V1 == other.V2 && V2 == other.V1);
                }
                return false;
            }
        }

        internal class VertexInfo
        {
            public GeometryData.Vertex Vertex { get; set; }
            public List<GeometryData.Face> Faces { get; set; } = new List<GeometryData.Face>();
            public HashSet<GeometryData.Vertex> SharedEdgeNeighbors { get; set; } = new HashSet<GeometryData.Vertex>();
            public Double3 TargetPosition { get; set; }
            public bool IsShared => Faces.Count > 1;
        }

        internal class FaceData
        {
            public GeometryData.Face OriginalFace { get; set; }
            public List<GeometryData.Vertex> Vertices { get; set; }
            public Double3 FaceCenter { get; set; }
            public Dictionary<string, GeometryData.AttributeValue> FaceAttributes { get; set; }
            public List<Dictionary<string, GeometryData.AttributeValue>> LoopAttributes { get; set; }
            public Dictionary<GeometryData.Vertex, GeometryData.Vertex> PerFaceMapping { get; set; }
        }


        internal static void InsetFaces(GeometryData mesh, IEnumerable<GeometryData.Face> facesToInset, double thickness, InsetMode mode = InsetMode.Shared)
        {
            var facesToInsetList = facesToInset.ToList();
            if (facesToInsetList.Count == 0) return;

            // Clamp amount to valid range
            thickness = Math.Max(0.0, Math.Min(1.0, thickness));

            // Early exit for zero inset
            if (Math.Abs(thickness) < 1e-10) return;

            // Calculate face centers and normals
            var faceCenters = new Dictionary<GeometryData.Face, Double3>();
            foreach (var face in facesToInsetList)
            {
                faceCenters[face] = face.Center();
            }

            // Build comprehensive edge information
            var edgeInfoMap = BuildEdgeInfo(facesToInsetList);

            // Build vertex information for Shared mode
            var vertexInfoMap = BuildVertexInfo(facesToInsetList, edgeInfoMap, mode);

            // Calculate target positions
            if (mode == InsetMode.Shared)
            {
                CalculateSharedTargetPositions(vertexInfoMap, faceCenters, thickness);
            }

            // Store face data for recreation
            var faceDataList = new List<FaceData>();
            foreach (var face in facesToInsetList)
            {
                var faceData = new FaceData
                {
                    OriginalFace = face,
                    Vertices = face.NeighborVertices(),
                    FaceCenter = faceCenters[face]
                };

                // Store face attributes
                faceData.FaceAttributes = new Dictionary<string, GeometryData.AttributeValue>();
                foreach (var attr in mesh.FaceAttributes)
                {
                    if (face.Attributes.ContainsKey(attr.Name))
                        faceData.FaceAttributes[attr.Name] = GeometryData.AttributeValue.Copy(face.Attributes[attr.Name]);
                }

                // Store loop attributes
                faceData.LoopAttributes = new List<Dictionary<string, GeometryData.AttributeValue>>();
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
                        faceData.LoopAttributes.Add(attrs);
                        it = it.Next;
                    } while (it != face.Loop);
                }

                faceDataList.Add(faceData);
            }

            // Create new vertices for inset
            var vertexMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

            foreach (var faceData in faceDataList)
            {
                faceData.PerFaceMapping = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

                foreach (var vert in faceData.Vertices)
                {
                    GeometryData.Vertex newVert;

                    if (mode == InsetMode.PerFace)
                    {
                        // PerFace: Always create new vertices
                        Double3 newPos = Maths.Lerp(vert.Point, faceData.FaceCenter, thickness);
                        newVert = mesh.AddVertex(newPos);
                        GeometryOperators.AttributeLerp(mesh, newVert, vert, vert, 1.0);
                        faceData.PerFaceMapping[vert] = newVert;
                    }
                    else if (!vertexMapping.ContainsKey(vert))
                    {
                        // Shared: Use pre-calculated target position
                        if (vertexInfoMap.TryGetValue(vert, out var vertInfo))
                        {
                            newVert = mesh.AddVertex(vertInfo.TargetPosition);
                            GeometryOperators.AttributeLerp(mesh, newVert, vert, vert, 1.0);
                            vertexMapping[vert] = newVert;
                        }
                    }
                }
            }

            // Remove original faces
            foreach (var face in facesToInsetList)
            {
                mesh.RemoveFace(face);
            }

            // Create inset faces
            foreach (var faceData in faceDataList)
            {
                GeometryData.Vertex[] newVerts;

                if (mode == InsetMode.PerFace)
                {
                    newVerts = faceData.Vertices.Select(v => faceData.PerFaceMapping[v]).ToArray();
                }
                else
                {
                    newVerts = faceData.Vertices.Select(v => vertexMapping[v]).ToArray();
                }

                var newFace = mesh.AddFace(newVerts);
                if (newFace != null)
                {
                    // Restore face attributes
                    foreach (var kvp in faceData.FaceAttributes)
                    {
                        newFace.Attributes[kvp.Key] = GeometryData.AttributeValue.Copy(kvp.Value);
                    }

                    // Restore loop attributes
                    if (newFace.Loop != null && faceData.LoopAttributes.Count > 0)
                    {
                        var it = newFace.Loop;
                        int idx = 0;
                        do
                        {
                            if (idx < faceData.LoopAttributes.Count)
                            {
                                foreach (var kvp in faceData.LoopAttributes[idx])
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

            // Create wall faces
            CreateWallFaces(mesh, faceDataList, edgeInfoMap, vertexMapping, mode);
        }

        private static Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> BuildEdgeInfo(
            List<GeometryData.Face> facesToInsetList)
        {
            var edgeInfoMap = new Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo>();

            foreach (var face in facesToInsetList)
            {
                var verts = face.NeighborVertices();
                for (int i = 0; i < verts.Count; i++)
                {
                    var v1 = verts[i];
                    var v2 = verts[(i + 1) % verts.Count];

                    // Create canonical edge key
                    var edgeKey = GeometryOperators.GetCanonicalEdge(v1, v2);

                    if (!edgeInfoMap.ContainsKey(edgeKey))
                    {
                        edgeInfoMap[edgeKey] = new EdgeInfo { V1 = edgeKey.Item1, V2 = edgeKey.Item2, UsageCount = 0 };
                    }
                    edgeInfoMap[edgeKey].UsageCount++;
                }
            }

            return edgeInfoMap;
        }

        private static Dictionary<GeometryData.Vertex, VertexInfo> BuildVertexInfo(
            List<GeometryData.Face> facesToInsetList,
            Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> edgeInfoMap,
            InsetMode mode)
        {
            var vertexInfoMap = new Dictionary<GeometryData.Vertex, VertexInfo>();

            // Build vertex-to-faces mapping
            foreach (var face in facesToInsetList)
            {
                var verts = face.NeighborVertices();
                foreach (var vert in verts)
                {
                    if (!vertexInfoMap.ContainsKey(vert))
                    {
                        vertexInfoMap[vert] = new VertexInfo { Vertex = vert };
                    }
                    vertexInfoMap[vert].Faces.Add(face);
                }
            }

            if (mode == InsetMode.Shared)
            {
                // Identify shared edges (edges used by more than one face)
                var sharedEdges = new HashSet<(GeometryData.Vertex, GeometryData.Vertex)>();
                foreach (var kvp in edgeInfoMap)
                {
                    if (kvp.Value.UsageCount > 1)
                    {
                        sharedEdges.Add(kvp.Key);
                    }
                }

                // For each vertex, find its shared edge neighbors
                foreach (var face in facesToInsetList)
                {
                    var verts = face.NeighborVertices();
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var v1 = verts[i];
                        var v2 = verts[(i + 1) % verts.Count];

                        var edgeKey = GeometryOperators.GetCanonicalEdge(v1, v2);

                        if (sharedEdges.Contains(edgeKey) && vertexInfoMap[v1].IsShared && vertexInfoMap[v2].IsShared)
                        {
                            vertexInfoMap[v1].SharedEdgeNeighbors.Add(v2);
                            vertexInfoMap[v2].SharedEdgeNeighbors.Add(v1);
                        }
                    }
                }
            }

            return vertexInfoMap;
        }

        private static void CalculateSharedTargetPositions(
            Dictionary<GeometryData.Vertex, VertexInfo> vertexInfoMap,
            Dictionary<GeometryData.Face, Double3> faceCenters,
            double amount)
        {
            foreach (var kvp in vertexInfoMap)
            {
                var vert = kvp.Key;
                var vertInfo = kvp.Value;

                if (!vertInfo.IsShared)
                {
                    // Non-shared vertex: move towards face center
                    var faceCenter = faceCenters[vertInfo.Faces[0]];
                    vertInfo.TargetPosition = Maths.Lerp(vert.Point, faceCenter, amount);
                }
                else
                {
                    // Shared vertex: handle based on connectivity
                    var connectedSharedVerts = vertInfo.SharedEdgeNeighbors.ToList();

                    if (connectedSharedVerts.Count == 0)
                    {
                        // Isolated - shouldn't happen but fallback to stay in place
                        vertInfo.TargetPosition = vert.Point;
                    }
                    else if (connectedSharedVerts.Count == 1)
                    {
                        // End of chain: move along the single shared edge
                        var edgePartner = connectedSharedVerts[0];
                        var edgeMidpoint = (vert.Point + edgePartner.Point) * 0.5;
                        vertInfo.TargetPosition = Maths.Lerp(vert.Point, edgeMidpoint, amount);
                    }
                    else if (connectedSharedVerts.Count == 2)
                    {
                        // Middle of chain: move towards average of neighbors
                        // This preserves the edge flow
                        var avgPos = Double3.Zero;
                        foreach (var neighbor in connectedSharedVerts)
                        {
                            avgPos += neighbor.Point;
                        }
                        avgPos /= connectedSharedVerts.Count;
                        vertInfo.TargetPosition = Maths.Lerp(vert.Point, avgPos, amount);
                    }
                    else
                    {
                        // Junction vertex (3+ shared edges): don't move to preserve topology
                        // This prevents mesh collapse at complex junctions
                        vertInfo.TargetPosition = vert.Point;
                    }
                }
            }
        }

        private static void CreateWallFaces(
            GeometryData mesh,
            List<FaceData> faceDataList,
            Dictionary<(GeometryData.Vertex, GeometryData.Vertex), EdgeInfo> edgeInfoMap,
            Dictionary<GeometryData.Vertex, GeometryData.Vertex> vertexMapping,
            InsetMode mode)
        {
            if (mode == InsetMode.PerFace)
            {
                // PerFace: Each face creates its own walls
                foreach (var faceData in faceDataList)
                {
                    for (int i = 0; i < faceData.Vertices.Count; i++)
                    {
                        var v1 = faceData.Vertices[i];
                        var v2 = faceData.Vertices[(i + 1) % faceData.Vertices.Count];

                        var insetV1 = faceData.PerFaceMapping[v1];
                        var insetV2 = faceData.PerFaceMapping[v2];

                        // Create quad wall face (winding order matters!)
                        mesh.AddFace(insetV1, v1, v2, insetV2);
                    }
                }
            }
            else
            {
                // Shared: Only create walls for boundary edges
                foreach (var faceData in faceDataList)
                {
                    for (int i = 0; i < faceData.Vertices.Count; i++)
                    {
                        var v1 = faceData.Vertices[i];
                        var v2 = faceData.Vertices[(i + 1) % faceData.Vertices.Count];

                        var edgeKey = GeometryOperators.GetCanonicalEdge(v1, v2);

                        // Only create wall if this edge is on the boundary
                        if (edgeInfoMap.TryGetValue(edgeKey, out var edgeInfo) && edgeInfo.IsBoundary)
                        {
                            if (vertexMapping.TryGetValue(v1, out var insetV1) &&
                                vertexMapping.TryGetValue(v2, out var insetV2))
                            {
                                // Create quad wall face
                                mesh.AddFace(insetV1, v1, v2, insetV2);
                            }
                        }
                    }
                }
            }
        }
    }
}
