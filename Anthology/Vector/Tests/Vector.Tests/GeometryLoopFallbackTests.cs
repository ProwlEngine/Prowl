// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class GeometryLoopFallbackTests
{
    // An L shaped prism, whose caps are six sided and whose corners have three edges
    private static GeometryData LPrism()
    {
        Float2[] outline = [new(0, 0), new(2, 0), new(2, 1), new(1, 1), new(1, 2), new(0, 2)];
        var mesh = new GeometryData();
        foreach (Float2 p in outline) mesh.AddVertex(p.X, 0f, p.Y);
        foreach (Float2 p in outline) mesh.AddVertex(p.X, 1f, p.Y);
        int n = outline.Length;
        mesh.AddFace(Enumerable.Range(0, n).ToArray());
        mesh.AddFace(Enumerable.Range(n, n).Reverse().ToArray());
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AddFace(j, i, n + i, n + j);
        }
        if (mesh.Volume() < 0f) GeometryOperators.FlipFaces(mesh, [.. mesh.Faces]);
        return mesh;
    }

    private static GeometryData.Face Facing(GeometryData mesh, Float3 normal)
        => mesh.Faces.First(f => Float3.Dot(f.Normal(), normal) > 0.99f);

    /// <summary>A loop from a cap's edge on a prism, whose corners have three edges, is the cap's outline on the cursor's side.</summary>
    [Fact]
    public void ALoopOnAPrismCapIsTheCapOutline()
    {
        GeometryData mesh = LPrism();
        GeometryData.Face top = Facing(mesh, Float3.UnitY);
        GeometryData.Edge edge = top.NeighborEdges()[0];

        List<GeometryData.Edge> loop = GeometryOperators.EdgeLoopOrOutline(edge, top);

        Assert.Equal(6, loop.Count);
        Assert.All(top.NeighborEdges(), e => Assert.Contains(e, loop));
    }

    /// <summary>A ring from a prism's upright edge goes round every upright edge, across its n gon sides.</summary>
    [Fact]
    public void ARingOnAPrismIsEveryUprightEdge()
    {
        GeometryData mesh = LPrism();
        GeometryData.Edge upright = mesh.Edges.First(e => MathF.Abs(e.Vert1.Point.Y - e.Vert2.Point.Y) > 0.5f);

        List<GeometryData.Edge> ring = GeometryOperators.EdgeRingAcross(upright);

        Assert.Equal(6, ring.Count);
        Assert.All(ring, e => Assert.True(MathF.Abs(e.Vert1.Point.Y - e.Vert2.Point.Y) > 0.5f));
    }
}
