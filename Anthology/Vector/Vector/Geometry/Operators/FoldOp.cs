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

internal static class FoldOp
{
    /// <summary>Splits faces into triangles joined by hidden edges, so each can be bent independently.</summary>
    internal static void TriangulateFaces(GeometryData mesh, IEnumerable<Face> faces)
    {
        foreach (Face f in faces.Where(mesh.Faces.Contains).SelectMany(f => f.LogicalFace()).Distinct().ToList())
            if (mesh.Faces.Contains(f)) Fold(mesh, f);
        mesh.MarkChanged();
    }
}
