// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class SubdivideOp
    {
        internal static void Subdivide(GeometryData mesh)
        {
            int i = 0;
            var edgeCenters = new GeometryData.Vertex[mesh.Edges.Count];
            var originalEdges = new GeometryData.Edge[mesh.Edges.Count];

            // Create vertex at each edge center
            foreach (var e in mesh.Edges)
            {
                edgeCenters[i] = mesh.AddVertex(e.Center());
                GeometryOperators.AttributeLerp(mesh, edgeCenters[i], e.Vert1, e.Vert2, 0.5);
                originalEdges[i] = e;
                e.Id = i++;
            }

            var originalFaces = new List<GeometryData.Face>(mesh.Faces);
            foreach (var f in originalFaces)
            {
                var faceCenter = mesh.AddVertex(f.Center());
                double w = 0;

                // Create one quad per loop in the original face
                var it = f.Loop;
                if (it == null) continue;

                do
                {
                    w += 1;
                    GeometryOperators.AttributeLerp(mesh, faceCenter, faceCenter, it.Vert, 1.0 / w);

                    var quad = new GeometryData.Vertex[]
                    {
                        it.Vert,
                        edgeCenters[it.Edge.Id],
                        faceCenter,
                        edgeCenters[it.Prev!.Edge.Id]
                    };
                    mesh.AddFace(quad);
                    it = it.Next;
                } while (it != f.Loop);

                // Remove the original face
                mesh.RemoveFace(f);
            }

            // Remove old edges
            foreach (var e in originalEdges)
            {
                mesh.RemoveEdge(e);
            }
        }
    }
}
