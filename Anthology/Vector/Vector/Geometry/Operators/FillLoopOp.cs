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

internal static class FillLoopOp
{
    /// <summary>
    /// Closes a loop of edges with a face, flat or not: a bent loop becomes triangles joined by hidden edges.
    /// Returns how many faces were made.
    /// </summary>
    internal static int FillLoop(GeometryData mesh, IEnumerable<Edge> edges, int material)
    {
        List<Vertex>? chain = OrderChain(edges, out bool closed);
        if (chain == null || !closed || chain.Count < 3) throw new InvalidOperationException("Select edges that form one closed loop");

        Float3 normal = Float3.Zero;
        for (int i = 0; i < chain.Count; i++) normal += Float3.Cross(chain[i].Point, chain[(i + 1) % chain.Count].Point);
        if (Float3.LengthSquared(normal) < 1e-12f) throw new InvalidOperationException("The loop encloses no area");
        normal = Float3.Normalize(normal);

        // Face the way the neighbouring faces run along the shared edges, when there are any
        for (int i = 0; i < chain.Count; i++)
        {
            Edge? shared = mesh.FindEdge(chain[i], chain[(i + 1) % chain.Count]);
            if (shared?.Loop == null) continue;
            if (shared.Loop.Vert == chain[i]) chain.Reverse();
            break;
        }

        var frame = new PlaneFrame(Float3.Normalize(Float3.Cross(chain[1].Point - chain[0].Point, chain[^1].Point - chain[0].Point) + normal * 1e-6f), chain[0].Point);
        var flat = chain.Select(v => frame.To2D(v.Point)).ToList();
        if (Polygon2D.SignedArea(flat) < 0f)
        {
            chain.Reverse();
            flat.Reverse();
        }

        bool planar = chain.All(v => MathF.Abs(frame.Distance(v.Point)) < GeometryData.Epsilon * 2f);
        if (planar)
        {
            int before = mesh.Faces.Count;
            mesh.AddFace(chain, material);
            mesh.MarkChanged();
            return mesh.Faces.Count - before;
        }

        List<int> tris = Polygon2D.Triangulate(flat);
        var outline = new HashSet<(Vertex, Vertex)>();
        for (int i = 0; i < chain.Count; i++)
        {
            outline.Add((chain[i], chain[(i + 1) % chain.Count]));
            outline.Add((chain[(i + 1) % chain.Count], chain[i]));
        }

        int made = 0;
        for (int t = 0; t < tris.Count; t += 3)
        {
            Vertex a = chain[tris[t]], b = chain[tris[t + 1]], c = chain[tris[t + 2]];
            if (mesh.AddFace([a, b, c], material) == null) continue;
            made++;
            foreach ((Vertex x, Vertex y) in new[] { (a, b), (b, c), (c, a) })
                if (!outline.Contains((x, y)) && mesh.FindEdge(x, y) is Edge diagonal) diagonal.IsHidden = true;
        }
        mesh.MarkChanged();
        return made;
    }
}
