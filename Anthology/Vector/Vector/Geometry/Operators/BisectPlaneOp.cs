// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class BisectPlaneOp
    {
        internal static void BisectPlane(GeometryData mesh, Plane plane, double epsilon = 0.0001, bool snapToPlane = true)
        {
            // Store which side of the plane each vertex is on
            var vertexSide = new Dictionary<GeometryData.Vertex, int>(); // -1 = negative side, 0 = on plane, 1 = positive side
            var vertexDistance = new Dictionary<GeometryData.Vertex, double>();

            // Classify all vertices
            foreach (var v in mesh.Vertices.ToArray())
            {
                double distance = plane.GetSignedDistanceToPoint(v.Point);
                vertexDistance[v] = distance;

                if (distance <= -epsilon)
                {
                    vertexSide[v] = -1;
                }
                else if (distance >= epsilon)
                {
                    vertexSide[v] = 1;
                }
                else
                {
                    vertexSide[v] = 0;
                    if (snapToPlane)
                    {
                        // Snap vertex to plane
                        v.Point = plane.ClosestPointOnPlane(v.Point);
                    }
                }
            }

            // Track vertices that are on the plane (from splitting or already there)
            var verticesOnPlane = new HashSet<GeometryData.Vertex>();
            foreach (var v in mesh.Vertices)
            {
                if (vertexSide.ContainsKey(v) && vertexSide[v] == 0)
                    verticesOnPlane.Add(v);
            }

            // Split edges that cross the plane
            var edgesToSplit = new List<(GeometryData.Edge edge, double factor)>();
            foreach (var edge in mesh.Edges.ToArray())
            {
                if (!vertexSide.ContainsKey(edge.Vert1) || !vertexSide.ContainsKey(edge.Vert2))
                    continue;

                int side1 = vertexSide[edge.Vert1];
                int side2 = vertexSide[edge.Vert2];

                // Edge crosses the plane if vertices are on opposite sides
                if (side1 != 0 && side2 != 0 && side1 != side2)
                {
                    double dist1 = vertexDistance[edge.Vert1];
                    double dist2 = vertexDistance[edge.Vert2];

                    // Calculate interpolation factor for the intersection point
                    double factor = dist1 / (dist1 - dist2);
                    edgesToSplit.Add((edge, factor));
                }
            }

            // Split all crossing edges
            foreach (var (edge, factor) in edgesToSplit)
            {
                var newVertex = SplitEdgeOp.SplitEdge(mesh, edge, edge.Vert1, factor, out var newEdge);
                verticesOnPlane.Add(newVertex);
                vertexSide[newVertex] = 0;
                vertexDistance[newVertex] = 0.0;
            }

            // Now split faces that have vertices on both sides of the plane
            var facesToProcess = new List<GeometryData.Face>(mesh.Faces);
            foreach (var face in facesToProcess)
            {
                // Skip if face was already removed by a previous split
                if (!mesh.Faces.Contains(face))
                    continue;

                var faceVerts = face.NeighborVertices();

                // Check if this face needs splitting
                bool hasPositive = false;
                bool hasNegative = false;
                var vertsOnPlaneInFace = new List<GeometryData.Vertex>();
                int totalVerts = 0;

                foreach (var v in faceVerts)
                {
                    if (!vertexSide.ContainsKey(v))
                        continue;

                    totalVerts++;
                    int side = vertexSide[v];
                    if (side == -1) hasNegative = true;
                    else if (side == 1) hasPositive = true;
                    else if (side == 0) vertsOnPlaneInFace.Add(v);
                }

                // Only split if the face spans both sides and has exactly 2 non-adjacent vertices on the plane
                // If all vertices are on the plane, skip (face lies on plane)
                // If only has positive or negative vertices, skip (face is entirely on one side)
                if (hasPositive && hasNegative && vertsOnPlaneInFace.Count >= 2 && vertsOnPlaneInFace.Count < totalVerts)
                {
                    // Sort vertices on plane by their order in the face to find split pairs
                    SplitFaceAlongPlane(mesh, face, vertsOnPlaneInFace, vertexSide);
                }
            }
        }

        /// <summary>
        /// Helper method to split a face along vertices that lie on the bisecting plane.
        /// </summary>
        private static void SplitFaceAlongPlane(GeometryData mesh, GeometryData.Face face,
            List<GeometryData.Vertex> vertsOnPlane, Dictionary<GeometryData.Vertex, int> vertexSide)
        {
            if (vertsOnPlane.Count < 2)
                return;

            // Get all vertices in the face in order
            var allVerts = face.NeighborVertices();

            // Find indices of vertices on the plane
            var planeVertIndices = new List<int>();
            for (int i = 0; i < allVerts.Count; i++)
            {
                if (vertsOnPlane.Contains(allVerts[i]))
                    planeVertIndices.Add(i);
            }

            if (planeVertIndices.Count < 2)
                return;

            // For simple cases with exactly 2 vertices on the plane, split directly
            if (planeVertIndices.Count == 2)
            {
                var vert1 = allVerts[planeVertIndices[0]];
                var vert2 = allVerts[planeVertIndices[1]];

                // Make sure they're not adjacent (would just be an edge on the plane)
                int dist = Math.Abs(planeVertIndices[1] - planeVertIndices[0]);
                if (dist == 1 || dist == allVerts.Count - 1)
                    return;

                // Verify the split would separate positive and negative vertices
                if (!WouldSplitSeparateSides(allVerts, planeVertIndices[0], planeVertIndices[1], vertexSide))
                    return;

                // Split the face
                try
                {
                    SplitFaceOp.SplitFace(mesh, face, vert1, vert2, out var newEdge);
                }
                catch
                {
                    // Face might already be removed or invalid, skip
                }
            }
            else if (planeVertIndices.Count > 2)
            {
                // More complex case: multiple vertices on the plane
                // We need to find pairs that properly separate positive from negative vertices

                // Try pairs in order of separation distance
                var pairs = new List<(int idx1, int idx2, int separation)>();
                for (int i = 0; i < planeVertIndices.Count - 1; i++)
                {
                    for (int j = i + 1; j < planeVertIndices.Count; j++)
                    {
                        int idx1 = planeVertIndices[i];
                        int idx2 = planeVertIndices[j];

                        int dist = Math.Abs(idx2 - idx1);
                        int wrapDist = allVerts.Count - dist;
                        int minDist = Math.Min(dist, wrapDist);

                        if (minDist > 1) // Not adjacent
                        {
                            pairs.Add((idx1, idx2, minDist));
                        }
                    }
                }

                // Sort by separation distance (larger first)
                pairs.Sort((a, b) => b.separation.CompareTo(a.separation));

                foreach (var (idx1, idx2, _) in pairs)
                {
                    var vert1 = allVerts[idx1];
                    var vert2 = allVerts[idx2];

                    // Verify the face still exists
                    if (!mesh.Faces.Contains(face))
                        return;

                    // Check that both vertices still belong to this face
                    var currentFaceVerts = face.NeighborVertices();
                    if (!currentFaceVerts.Contains(vert1) || !currentFaceVerts.Contains(vert2))
                        continue;

                    // Verify this split would separate positive and negative vertices
                    if (!WouldSplitSeparateSides(allVerts, idx1, idx2, vertexSide))
                        continue;

                    try
                    {
                        SplitFaceOp.SplitFace(mesh, face, vert1, vert2, out var newEdge);
                        return; // Successfully split, done with this face
                    }
                    catch
                    {
                        // Try next pair
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Check if splitting a face between two vertex indices would separate positive and negative sides.
        /// </summary>
        private static bool WouldSplitSeparateSides(List<GeometryData.Vertex> allVerts, int idx1, int idx2,
            Dictionary<GeometryData.Vertex, int> vertexSide)
        {
            // Walk from idx1 to idx2 in one direction
            bool hasPositive1 = false, hasNegative1 = false;
            int current = (idx1 + 1) % allVerts.Count;
            while (current != idx2)
            {
                if (vertexSide.ContainsKey(allVerts[current]))
                {
                    int side = vertexSide[allVerts[current]];
                    if (side > 0) hasPositive1 = true;
                    if (side < 0) hasNegative1 = true;
                }
                current = (current + 1) % allVerts.Count;
            }

            // Walk from idx2 to idx1 in the other direction
            bool hasPositive2 = false, hasNegative2 = false;
            current = (idx2 + 1) % allVerts.Count;
            while (current != idx1)
            {
                if (vertexSide.ContainsKey(allVerts[current]))
                {
                    int side = vertexSide[allVerts[current]];
                    if (side > 0) hasPositive2 = true;
                    if (side < 0) hasNegative2 = true;
                }
                current = (current + 1) % allVerts.Count;
            }

            // Good split if one side has only positive/plane and other has only negative/plane
            bool side1Clean = (hasPositive1 && !hasNegative1) || (!hasPositive1 && hasNegative1);
            bool side2Clean = (hasPositive2 && !hasNegative2) || (!hasPositive2 && hasNegative2);
            bool sidesOpposite = (hasPositive1 && hasNegative2) || (hasNegative1 && hasPositive2);

            return side1Clean && side2Clean && sidesOpposite;
        }
    }
}
