// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Diagnostics;

namespace Prowl.Vector.Geometry.Operators;

internal static class SplitFaceOp
{
    /// <summary>Splits a face along the line between two of its vertices. Returns the second of the two new faces.</summary>
    internal static GeometryData.Face? SplitFace(GeometryData mesh, GeometryData.Face face, GeometryData.Vertex vert1, GeometryData.Vertex vert2, out GeometryData.Edge? newEdge)
    {
        var (_, second) = SplitFaceBoth(mesh, face, vert1, vert2, out newEdge);
        return second;
    }

    /// <summary>Splits a face and returns both halves: the one running from vert1 to vert2, and the one running back.</summary>
    internal static (GeometryData.Face? First, GeometryData.Face? Second) SplitFaceBoth(GeometryData mesh, GeometryData.Face face,
        GeometryData.Vertex vert1, GeometryData.Vertex vert2, out GeometryData.Edge? newEdge)
    {
        newEdge = null;

        if (vert1 == vert2)
        {
            Debug.WriteLine("SplitFace: vertices must be different");
            return (null, null);
        }

        var loop1 = face.GetLoop(vert1);
        var loop2 = face.GetLoop(vert2);
        if (loop1 == null || loop2 == null)
        {
            Debug.WriteLine("SplitFace: one or both vertices not found in face");
            return (null, null);
        }

        // Neighbouring corners already share an edge
        if (loop1.Next == loop2 || loop1.Prev == loop2)
        {
            Debug.WriteLine("SplitFace: vertices are adjacent in the face");
            return (null, null);
        }

        FaceSnapshot snapshot = FaceSnapshot.Capture(mesh, face);
        var firstVerts = new List<GeometryData.Vertex>();
        var secondVerts = new List<GeometryData.Vertex>();

        var it = loop1;
        while (true)
        {
            firstVerts.Add(it.Vert);
            if (it == loop2) break;
            it = it.Next!;
        }
        it = loop2;
        while (true)
        {
            secondVerts.Add(it.Vert);
            if (it == loop1) break;
            it = it.Next!;
        }

        mesh.RemoveFace(face);
        var splitEdge = mesh.AddEdge(vert1, vert2);

        var first = mesh.AddFace(firstVerts.ToArray());
        var second = mesh.AddFace(secondVerts.ToArray());
        if (first == null || second == null)
        {
            Debug.WriteLine($"SplitFace: Failed to create faces (first={first != null}, second={second != null})");
            return (first, second);
        }

        foreach (var half in new[] { first, second })
        {
            snapshot.ApplyFaceAttributes(half);
            FaceSnapshot.ApplyLoops(half, snapshot.LoopOf);
        }

        newEdge = splitEdge;
        return (first, second);
    }
}
