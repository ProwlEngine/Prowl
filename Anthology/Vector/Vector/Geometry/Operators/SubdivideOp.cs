// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators;

internal static class SubdivideOp
{
    /// <summary>
    /// Splits every edge at its middle and every face into one quad per corner. Faces keep their attributes,
    /// the corners of the new quads get loop attributes interpolated across the old face, and the halves of an
    /// edge keep what the edge carried. Loose edges are split too.
    /// </summary>
    internal static void Subdivide(GeometryData mesh)
    {
        var originalEdges = mesh.Edges.ToArray();
        var edgeCenters = new GeometryData.Vertex[originalEdges.Length];
        var edgeAttributes = new Dictionary<string, GeometryData.AttributeValue>[originalEdges.Length];
        var loose = new bool[originalEdges.Length];

        for (int i = 0; i < originalEdges.Length; i++)
        {
            var e = originalEdges[i];
            edgeCenters[i] = mesh.AddVertex(e.Center());
            GeometryOperators.AttributeLerp(mesh, edgeCenters[i], e.Vert1, e.Vert2, 0.5f);
            edgeAttributes[i] = AttributeTransfer.Copy(e.Attributes);
            loose[i] = e.Loop == null;
            e.Id = i;
        }

        foreach (var f in mesh.Faces.ToList())
        {
            if (f.Loop == null) continue;
            FaceSnapshot snapshot = FaceSnapshot.Capture(mesh, f);
            int count = snapshot.Vertices.Count;

            var faceCenter = mesh.AddVertex(f.Center());
            for (int i = 0; i < count; i++)
                GeometryOperators.AttributeLerp(mesh, faceCenter, i == 0 ? snapshot.Vertices[0] : faceCenter, snapshot.Vertices[i], i == 0 ? 1f : 1f / (i + 1));
            var centerLoop = AttributeTransfer.Average(snapshot.Loops);

            var corners = new List<(GeometryData.Vertex[] Quad, int Edge, int PreviousEdge)>();
            var it = f.Loop;
            do
            {
                corners.Add((new[] { it.Vert, edgeCenters[it.Edge.Id], faceCenter, edgeCenters[it.Prev!.Edge.Id] }, it.Edge.Id, it.Prev.Edge.Id));
                it = it.Next!;
            } while (it != f.Loop);

            mesh.RemoveFace(f);

            for (int i = 0; i < corners.Count; i++)
            {
                var (quad, edgeId, previousId) = corners[i];
                var made = mesh.AddFace(quad);
                if (made == null) continue;

                snapshot.ApplyFaceAttributes(made);
                var here = snapshot.Loops[i];
                var next = snapshot.Loops[(i + 1) % count];
                var previous = snapshot.Loops[(i + count - 1) % count];
                var towardsNext = AttributeTransfer.Lerp(here, next, 0.5f);
                var towardsPrevious = AttributeTransfer.Lerp(previous, here, 0.5f);
                FaceSnapshot.ApplyLoops(made, v =>
                    v == quad[0] ? here : v == quad[1] ? towardsNext : v == quad[3] ? towardsPrevious : centerLoop);

                if (mesh.FindEdge(quad[0], quad[1]) is { } forward) AttributeTransfer.Apply(edgeAttributes[edgeId], forward.Attributes);
                if (mesh.FindEdge(quad[3], quad[0]) is { } backward) AttributeTransfer.Apply(edgeAttributes[previousId], backward.Attributes);
            }
        }

        for (int i = 0; i < originalEdges.Length; i++)
        {
            var e = originalEdges[i];
            if (loose[i])
            {
                var a = mesh.AddEdge(e.Vert1, edgeCenters[i]);
                var b = mesh.AddEdge(edgeCenters[i], e.Vert2);
                AttributeTransfer.Apply(edgeAttributes[i], a.Attributes);
                AttributeTransfer.Apply(edgeAttributes[i], b.Attributes);
            }
            mesh.RemoveEdge(e);
        }
    }
}
