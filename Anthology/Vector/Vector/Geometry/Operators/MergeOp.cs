// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class MergeOp
    {
        internal static void Merge(GeometryData mesh, GeometryData other)
        {
            var newVerts = new GeometryData.Vertex[other.Vertices.Count];
            int i = 0;

            // Copy all vertices and their attributes
            foreach (var v in other.Vertices)
            {
                newVerts[i] = mesh.AddVertex(v.Point);
                GeometryOperators.AttributeLerp(mesh, newVerts[i], v, v, 1); // Copy all attributes
                v.Id = i;
                ++i;
            }

            // Copy all edges
            foreach (var e in other.Edges)
            {
                mesh.AddEdge(newVerts[e.Vert1.Id], newVerts[e.Vert2.Id]);
            }

            // Copy all faces
            foreach (var f in other.Faces)
            {
                var neighbors = f.NeighborVertices();
                var newNeighbors = new GeometryData.Vertex[neighbors.Count];
                int j = 0;
                foreach (var v in neighbors)
                {
                    newNeighbors[j] = newVerts[v.Id];
                    ++j;
                }
                mesh.AddFace(newNeighbors);
            }
        }
    }
}
