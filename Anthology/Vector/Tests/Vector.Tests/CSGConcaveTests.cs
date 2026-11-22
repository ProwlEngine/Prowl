// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

/// <summary>Brushes that are concave, bent or have holes through them, built and combined without being split up first.</summary>
public class CSGConcaveTests
{
    private const float Tolerance = 1e-3f;

    // ================================================================
    //  Shapes
    // ================================================================

    /// <summary>A prism raised from an outline in the XZ plane, counter clockwise seen from above.</summary>
    private static GeometryData Prism(IReadOnlyList<Float2> outline, float bottom, float top)
    {
        var mesh = new GeometryData();
        var low = outline.Select(p => mesh.AddVertex(new Float3(p.X, bottom, p.Y))).ToArray();
        var high = outline.Select(p => mesh.AddVertex(new Float3(p.X, top, p.Y))).ToArray();

        mesh.AddFace(high.Reverse().ToArray());
        mesh.AddFace(low);
        for (int i = 0; i < outline.Count; i++)
        {
            int j = (i + 1) % outline.Count;
            mesh.AddFace(low[j], low[i], high[i], high[j]);
        }

        return Outward(mesh);
    }

    /// <summary>An L: two by two square with the top right one by one quarter missing.</summary>
    private static readonly Float2[] L = [new(0, 0), new(0, 2), new(1, 2), new(1, 1), new(2, 1), new(2, 0)];

    /// <summary>A U opening toward +Z: three wide, two deep, the middle one wide slot cut from z = 1 out.</summary>
    private static readonly Float2[] U = [new(0, 0), new(0, 2), new(1, 2), new(1, 1), new(2, 1), new(2, 2), new(3, 2), new(3, 0)];

    /// <summary>A square frame: four by four outside, two by two hole through the middle, raised from bottom to top.</summary>
    private static GeometryData Ring(float bottom, float top)
    {
        var mesh = new GeometryData();
        Float2[] outer = [new(-2, -2), new(-2, 2), new(2, 2), new(2, -2)];
        Float2[] inner = [new(-1, -1), new(-1, 1), new(1, 1), new(1, -1)];

        GeometryData.Vertex V(Float2 p, float y) => mesh.AddVertex(new Float3(p.X, y, p.Y));
        var ob = outer.Select(p => V(p, bottom)).ToArray();
        var ot = outer.Select(p => V(p, top)).ToArray();
        var ib = inner.Select(p => V(p, bottom)).ToArray();
        var it = inner.Select(p => V(p, top)).ToArray();

        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            mesh.AddFace(ot[j], ot[i], it[i], it[j]);
            mesh.AddFace(ob[i], ob[j], ib[j], ib[i]);
            mesh.AddFace(ob[j], ob[i], ot[i], ot[j]);
            mesh.AddFace(ib[i], ib[j], it[j], it[i]);
        }

        return Outward(mesh);
    }

    /// <summary>Turns a hand built mesh round if it was wound inside out.</summary>
    private static GeometryData Outward(GeometryData mesh)
    {
        if (mesh.Volume() < 0f) GeometryOperators.FlipFaces(mesh, mesh.Faces.ToList());
        Assert.True(mesh.Volume() > 0f && mesh.IsClosed());
        return mesh;
    }

    private static GeometryData Box(Float3 size, Float3 centre = default) => GeometryGenerator.Box(size, centre);

    // ================================================================
    //  Measures
    // ================================================================

    private static float Area(GeometryData mesh)
    {
        float total = 0f;
        foreach (var face in mesh.Faces)
        {
            var ring = face.NeighborVertices();
            Float3 sum = Float3.Zero;
            for (int i = 1; i + 1 < ring.Count; i++)
                sum += Float3.Cross(ring[i].Point - ring[0].Point, ring[i + 1].Point - ring[0].Point);
            total += Float3.Length(sum) * 0.5f;
        }
        return total;
    }

    /// <summary>Separate solids in a mesh, by faces joined through corners at the same place.</summary>
    private static int Pieces(GeometryData mesh)
    {
        static (int, int, int) Key(Float3 p) => ((int)MathF.Round(p.X * 1000), (int)MathF.Round(p.Y * 1000), (int)MathF.Round(p.Z * 1000));

        var byCorner = new Dictionary<(int, int, int), List<GeometryData.Face>>();
        foreach (var face in mesh.Faces)
            foreach (var vertex in face.NeighborVertices())
            {
                if (!byCorner.TryGetValue(Key(vertex.Point), out var list)) byCorner[Key(vertex.Point)] = list = new();
                list.Add(face);
            }

        var seen = new HashSet<GeometryData.Face>();
        int pieces = 0;
        foreach (var start in mesh.Faces)
        {
            if (!seen.Add(start)) continue;
            pieces++;
            var stack = new Stack<GeometryData.Face>();
            stack.Push(start);
            while (stack.Count > 0)
                foreach (var vertex in stack.Pop().NeighborVertices())
                    foreach (var next in byCorner[Key(vertex.Point)])
                        if (seen.Add(next)) stack.Push(next);
        }
        return pieces;
    }

    private static GeometryData Build(params (GeometryData Shape, CSGOperation Op)[] brushes)
    {
        var scene = new CSGScene();
        foreach (var (shape, op) in brushes) scene.Add(shape, op);
        return scene.Build();
    }

    // ================================================================
    //  Tests
    // ================================================================

    [Fact]
    public void AConcaveBrushAloneIsItsOwnSolid()
    {
        GeometryData output = Build((Prism(L, 0, 1), CSGOperation.Additive));

        Assert.Equal(3f, output.Volume(), Tolerance);
        Assert.True(output.IsClosed());
    }

    [Fact]
    public void AConcaveBrushCarvesItsOwnShapeOutOfABox()
    {
        // The box spans 0..4 on every axis, the L runs right through it top to bottom
        GeometryData output = Build(
            (Box(new Float3(4), new Float3(2)), CSGOperation.Additive),
            (Prism(L.Select(p => p + new Float2(1, 1)).ToArray(), -1, 5), CSGOperation.Subtractive));

        Assert.Equal(64f - 3f * 4f, output.Volume(), Tolerance);
        Assert.True(output.IsClosed());
    }

    [Fact]
    public void ABoxCarvesAConcaveBrush()
    {
        // The box covers x 0.5..1.5 of the L's full height and depth, which takes 1.5 of its area out
        GeometryData output = Build(
            (Prism(L, 0, 1), CSGOperation.Additive),
            (Box(new Float3(1, 3, 3), new Float3(1, 0.5f, 1)), CSGOperation.Subtractive));

        Assert.Equal(3f - 1.5f, output.Volume(), Tolerance);
        Assert.True(output.IsClosed());
    }

    [Fact]
    public void AConcaveBrushIntersectedWithABoxKeepsOnlyTheirOverlap()
    {
        GeometryData output = Build(
            (Prism(L, 0, 1), CSGOperation.Additive),
            (Box(new Float3(1, 3, 3), new Float3(1, 0.5f, 1)), CSGOperation.Intersect));

        Assert.Equal(1.5f, output.Volume(), Tolerance);
        Assert.True(output.IsClosed());
    }

    [Fact]
    public void AUShapeCutThroughABoxLeavesTwoSolids()
    {
        // The box sits in the U's slot and past it, so subtracting the U leaves the slot's contents and the part beyond
        GeometryData output = Build(
            (Box(new Float3(1, 1, 3), new Float3(1.5f, 0.5f, 1.5f)), CSGOperation.Additive),
            (Prism(U, -1, 2), CSGOperation.Subtractive));

        Assert.Equal(1f * 1f * 3f - 1f * 1f * 1f, output.Volume(), Tolerance);
        Assert.Equal(1, Pieces(output));
        Assert.True(output.IsClosed());

        GeometryData two = Build(
            (Box(new Float3(3, 1, 1), new Float3(1.5f, 0.5f, 0.5f)), CSGOperation.Additive),
            (Prism([new(1, -1), new(1, 2), new(2, 2), new(2, -1)], -1, 2), CSGOperation.Subtractive));
        Assert.Equal(2, Pieces(two));
    }

    [Fact]
    public void ABrushWithAHoleThroughItKeepsTheHole()
    {
        GeometryData output = Build((Ring(0, 1), CSGOperation.Additive));

        Assert.Equal(16f - 4f, output.Volume(), Tolerance);
        Assert.True(output.IsClosed());

        // A rod through the hole touches nothing, so subtracting it changes nothing
        GeometryData rod = Build(
            (Ring(0, 1), CSGOperation.Additive),
            (Box(new Float3(1, 3, 1), new Float3(0, 0.5f, 0)), CSGOperation.Subtractive));
        Assert.Equal(12f, rod.Volume(), Tolerance);
    }

    [Fact]
    public void ConcaveBrushesMeetingFaceToFaceInAGroupLoseTheirSharedFace()
    {
        // An L and the square that completes it make a two by two by one box
        var scene = new CSGScene();
        var group = scene.AddGroup();
        scene.AddTo(group, Prism(L, 0, 1));
        scene.AddTo(group, Box(new Float3(1, 1, 1), new Float3(1.5f, 0.5f, 1.5f)));
        GeometryData output = scene.Build();

        Assert.Equal(4f, output.Volume(), Tolerance);
        Assert.Equal(2f * (4f + 2f + 2f), Area(output), Tolerance);
    }

    [Fact]
    public void ConcaveAndConvexBrushesSharingASurfaceBothWays()
    {
        // A box resting on top of the L shares its top face
        GeometryData stacked = Build(
            (Prism(L, 0, 1), CSGOperation.Additive),
            (Box(new Float3(1, 1, 1), new Float3(0.5f, 1.5f, 0.5f)), CSGOperation.Additive));
        Assert.Equal(4f, stacked.Volume(), Tolerance);
        Assert.True(stacked.IsClosed());

        // A smaller box subtracted from inside the L's top, flush with it, leaves a pit
        GeometryData pit = Build(
            (Prism(L, 0, 1), CSGOperation.Additive),
            (Box(new Float3(0.5f, 0.5f, 0.5f), new Float3(0.5f, 0.75f, 0.5f)), CSGOperation.Subtractive));
        Assert.Equal(3f - 0.125f, pit.Volume(), Tolerance);
        Assert.True(pit.IsClosed());
    }

    [Fact]
    public void ABentFaceKeepsEveryCornerWhereItWasPut()
    {
        // A box whose top face has one corner raised, so the face is no longer flat
        GeometryData box = Box(new Float3(2, 2, 2));
        var corner = box.Vertices.First(v => v.Point.X > 0 && v.Point.Y > 0 && v.Point.Z > 0);
        corner.Point = new Float3(1, 1.5f, 1);

        GeometryData output = Build((box, CSGOperation.Additive));

        Assert.Contains(output.Vertices, v => Float3.Distance(v.Point, new Float3(1, 1.5f, 1)) < Tolerance);
        Assert.True(output.IsClosed());
        Assert.True(output.Volume() > 8f);
    }

    [Fact]
    public void DraggingAPointStaysClosedEveryStep()
    {
        for (int step = 0; step <= 10; step++)
        {
            var outline = (Float2[])L.Clone();
            outline[3] = new Float2(1 + step * 0.05f, 1 - step * 0.05f);

            GeometryData output = Build(
                (Box(new Float3(3, 1, 3), new Float3(1, 0.5f, 1)), CSGOperation.Additive),
                (Prism(outline, -1, 0.5f), CSGOperation.Subtractive));

            Assert.True(output.IsClosed(), $"open at step {step}");
        }
    }

    [Fact]
    public void ASlightlyOpenBrushStillCarvesByWhatItEncloses()
    {
        // The missing face is outside the solid, so the pit it digs is still closed
        GeometryData open = Box(new Float3(2, 2, 2), new Float3(0, 2, 0));
        open.RemoveFace(open.Faces.First(f => f.Normal().Y > 0.9f));

        GeometryData output = Build(
            (Box(new Float3(4, 4, 4)), CSGOperation.Additive),
            (open, CSGOperation.Subtractive));

        Assert.Equal(64f - 4f, output.Volume(), Tolerance);
        Assert.True(output.IsClosed());
    }
}
