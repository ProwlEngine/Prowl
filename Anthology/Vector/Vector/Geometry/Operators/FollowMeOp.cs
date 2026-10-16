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

internal static class FollowMeOp
{
    private static Quaternion ShortestArc(Float3 from, Float3 to)
    {
        float d = Math.Clamp(Float3.Dot(from, to), -1f, 1f);
        if (d > 1f - 1e-7f) return Quaternion.Identity;
        Float3 axis = Float3.Cross(from, to);
        if (Float3.LengthSquared(axis) < 1e-12f)
        {
            Float3 any = MathF.Abs(from.X) < 0.9f ? Float3.UnitX : Float3.UnitY;
            return Quaternion.AxisAngle(Float3.Normalize(Float3.Cross(from, any)), MathF.PI);
        }
        return Quaternion.AxisAngle(Float3.Normalize(axis), MathF.Acos(d));
    }

    /// <summary>
    /// Sweeps a flat region along a path of edges, like SketchUp's Follow Me. Corners are mitred so the walls
    /// stay flat, and the ends are capped unless the path loops. Returns how many faces were made.
    /// </summary>
    // Swept faces lie in other planes than the profile, so its texture mapping does not carry over
    private static Face? Plain(Face? face)
    {
        if (face != null) face.Mapping = null;
        return face;
    }

    internal static int FollowMe(GeometryData mesh, IReadOnlyCollection<Face> profile, IReadOnlyCollection<Edge> path)
    {
        if (profile.Count == 0) throw new InvalidOperationException("Select a face to sweep");
        List<Vertex>? chain = OrderChain(path, out bool closed);
        if (chain == null || chain.Count < 2) throw new InvalidOperationException("The path must be one connected line of edges that does not branch");

        List<Face> region = profile.SelectMany(f => f.LogicalFace()).Distinct().ToList();
        Float3 normal = Float3.Normalize(region[0].Normal());
        if (!region.All(f => region[0].IsCoplanarWith(f))) throw new InvalidOperationException("The profile must be flat");

        Float3 centroid = GeometryData.Centroid(region.SelectMany(f => f.NeighborVertices()).Distinct());
        if (!closed && Float3.Length(chain[^1].Point - centroid) < Float3.Length(chain[0].Point - centroid)) chain.Reverse();

        List<List<Vertex>> loops = GeometryData.Outlines(region);
        if (loops.Count == 0) throw new InvalidOperationException("The profile has no outline");

        int count = chain.Count;
        int segments = closed ? count : count - 1;
        var points = chain.Select(v => v.Point).ToList();
        var tangents = new List<Float3>(segments);
        for (int k = 0; k < segments; k++)
        {
            Float3 t = points[(k + 1) % count] - points[k];
            if (Float3.Length(t) < GeometryData.Epsilon) throw new InvalidOperationException("The path has a zero length edge");
            tangents.Add(Float3.Normalize(t));
        }

        var frames = new List<Quaternion> { Quaternion.Identity };
        for (int k = 1; k < segments; k++) frames.Add(ShortestArc(tangents[k - 1], tangents[k]) * frames[k - 1]);

        Face look = region[0];
        int ringCount = closed ? segments : segments + 1;
        Float3 origin = points[0];

        // Rings of vertices, one per path vertex
        var rings = new List<List<List<Vertex>>>();
        for (int i = 0; i < ringCount; i++) rings.Add([]);

        foreach (List<Vertex> loop in loops)
        {
            for (int i = 0; i < ringCount; i++) rings[i].Add(new List<Vertex>(loop.Count));
        }

        for (int li = 0; li < loops.Count; li++)
        {
            List<Vertex> loop = loops[li];
            foreach (Vertex v in loop)
            {
                Float3 offset = v.Point - origin;
                for (int i = 0; i < ringCount; i++)
                {
                    if (i == 0 && !closed)
                    {
                        rings[0][li].Add(v);
                        continue;
                    }

                    int incoming = closed ? (i - 1 + segments) % segments : i - 1;
                    Float3 tIn = tangents[incoming];
                    Float3 plane = closed || i < segments ? Float3.Normalize(tIn + tangents[closed ? i % segments : i]) : tIn;
                    Float3 b = points[incoming] + frames[incoming] * offset;
                    float denom = Float3.Dot(tIn, plane);
                    if (denom < 0.05f) throw new InvalidOperationException("The path turns back on itself too sharply to sweep along");
                    float lambda = Float3.Dot(points[i % count] - b, plane) / denom;
                    rings[i][li].Add(mesh.AddVertex(b + tIn * lambda));
                }
            }
        }

        int made = 0;
        bool forward = Float3.Dot(normal, tangents[0]) > 0f;

        var pieces = region.Select(f => f.NeighborVertices()).ToList();
        var hiddenPairs = new List<(Vertex, Vertex)>();
        foreach (Face f in region)
            foreach (Edge e in f.NeighborEdges())
                if (e.IsHidden) hiddenPairs.Add((e.Vert1, e.Vert2));

        void Strip(int from, int to)
        {
            for (int li = 0; li < loops.Count; li++)
            {
                List<Vertex> a = rings[from][li], b = rings[to][li];
                for (int j = 0; j < a.Count; j++)
                {
                    int n = (j + 1) % a.Count;
                    List<Vertex> quad = forward ? [a[j], a[n], b[n], b[j]] : [a[j], b[j], b[n], a[n]];
                    if (Plain(mesh.AddFace(quad, look)) != null) made++;
                }
            }
        }

        for (int i = 0; i + 1 < ringCount; i++) Strip(i, i + 1);
        if (closed) Strip(ringCount - 1, 0);

        // Soft corners of the profile give soft lengthways edges
        for (int li = 0; li < loops.Count; li++)
        {
            List<Vertex> loop = loops[li];
            for (int j = 0; j < loop.Count; j++)
            {
                Edge? before = mesh.FindEdge(loop[(j + loop.Count - 1) % loop.Count], loop[j]), after = mesh.FindEdge(loop[j], loop[(j + 1) % loop.Count]);
                if (before == null || after == null || !before.IsSoft || !after.IsSoft) continue;
                for (int i = 0; i < ringCount; i++)
                {
                    Vertex a = rings[i][li][j], b = rings[(i + 1) % ringCount][li][j];
                    if ((i + 1 < ringCount || closed) && a != b && mesh.FindEdge(a, b) is Edge along) along.IsSoft = true;
                }
            }
        }

        if (closed)
        {
            foreach (Face f in region) mesh.RemoveFace(f);
            foreach (List<Vertex> loop in loops)
                for (int j = 0; j < loop.Count; j++)
                    if (mesh.FindEdge(loop[j], loop[(j + 1) % loop.Count]) is Edge leftover && leftover.Loop == null) mesh.RemoveEdge(leftover);
        }
        else
        {
            // The profile closes the start of the solid, turned to face away from it, and a copy closes the far end
            for (int i = 0; i < region.Count; i++)
            {
                List<Vertex> verts = pieces[i];
                if (forward) verts = [.. Enumerable.Reverse(verts)];
                mesh.ReplaceFace(region[i], verts);
            }

            var end = new Dictionary<Vertex, Vertex>();
            for (int li = 0; li < loops.Count; li++)
                for (int j = 0; j < loops[li].Count; j++)
                    end[loops[li][j]] = rings[ringCount - 1][li][j];

            Float3 endDirection = tangents[^1];
            foreach (List<Vertex> piece in pieces)
            {
                List<Vertex> mapped = piece.Select(v => end[v]).ToList();
                Float3 newell = Float3.Zero;
                for (int j = 0; j < mapped.Count; j++) newell += Float3.Cross(mapped[j].Point, mapped[(j + 1) % mapped.Count].Point);
                if (Float3.Dot(newell, endDirection) < 0f) mapped.Reverse();
                if (Plain(mesh.AddFace(mapped, look)) != null) made++;
            }
            foreach ((Vertex a, Vertex b) in hiddenPairs)
                if (end.TryGetValue(a, out Vertex? ea) && end.TryGetValue(b, out Vertex? eb) && mesh.FindEdge(ea, eb) is Edge twin)
                    twin.IsHidden = true;
        }

        mesh.MergeCoincident(rings.SelectMany(r => r).SelectMany(l => l).Distinct().ToList());
        foreach (Vertex v in loops.SelectMany(l => l).Distinct().ToList())
            if (mesh.Vertices.Contains(v) && v.Edge == null) mesh.RemoveVertex(v);        mesh.RemoveUnused();
        mesh.MarkChanged();
        return made;
    }
}
