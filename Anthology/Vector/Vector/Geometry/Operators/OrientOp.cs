// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

using static Prowl.Vector.Geometry.Operators.EditingCommon;

namespace Prowl.Vector.Geometry.Operators;

internal static class OrientOp
{
    /// <summary>Reverses the direction faces point. Faces joined by hidden edges flip together.</summary>
    internal static void FlipFaces(GeometryData mesh, IEnumerable<Face> faces)
    {
        foreach (Face f in faces.Where(mesh.Faces.Contains).SelectMany(f => f.LogicalFace()).Distinct().ToList())
        {
            List<Vertex> verts = f.NeighborVertices();
            verts.Reverse();
            mesh.ReplaceFace(f, verts);
        }
        mesh.MarkChanged();
    }

    /// <summary>
    /// Turns faces round where neighbours disagree about which way is out, so the surface is consistent, and
    /// makes closed shells face outwards. Returns how many faces were flipped.
    /// </summary>
    internal static int OrientFaces(GeometryData mesh)
    {
        var flipped = new Dictionary<Face, bool>();
        int turned = 0;
        foreach (Face start in mesh.Faces.ToList())
        {
            if (flipped.ContainsKey(start)) continue;
            var component = new List<Face> { start };
            flipped[start] = false;
            for (int i = 0; i < component.Count; i++)
            {
                Face f = component[i];
                var loop = f.Loop!;
                do
                {
                    Edge e = loop.Edge;
                    List<Face> faces = e.NeighborFaces();
                    if (faces.Count == 2)
                    {
                        Face g = faces[0] == f ? faces[1] : faces[0];
                        if (g != f && !flipped.ContainsKey(g))
                        {
                            bool forwardF = (loop.Vert == e.Vert1) != flipped[f];
                            bool forwardG = g.GetLoop(e.Vert1)!.Edge == e;
                            flipped[g] = forwardF == forwardG;
                            component.Add(g);
                        }
                    }
                    loop = loop.Next!;
                } while (loop != f.Loop);
            }

            bool closed = component.All(f => f.NeighborEdges().All(e => e.FaceCount == 2));
            if (closed)
            {
                double volume = 0.0;
                foreach (Face f in component)
                {
                    List<Float3> p = f.NeighborVertices().Select(v => v.Point).ToList();
                    if (flipped[f]) p.Reverse();
                    var frame = new PlaneFrame(Float3.Normalize(Float3.Cross(p[1] - p[0], p[^1] - p[0])), p[0]);
                    List<int> tris = Polygon2D.Triangulate(p.Select(frame.To2D).ToList());
                    for (int t = 0; t < tris.Count; t += 3)
                        volume += Float3.Dot(p[tris[t]], Float3.Cross(p[tris[t + 1]], p[tris[t + 2]])) / 6.0;
                }
                if (volume < 0.0)
                    foreach (Face f in component) flipped[f] = !flipped[f];
            }
        }

        foreach ((Face f, bool flip) in flipped.ToList())
        {
            if (!flip || !mesh.Faces.Contains(f)) continue;
            List<Vertex> verts = f.NeighborVertices();
            verts.Reverse();
            mesh.ReplaceFace(f, verts);
            turned++;
        }
        mesh.MarkChanged();
        return turned;
    }
}
