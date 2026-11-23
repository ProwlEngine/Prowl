// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class GeometryOperatorsTests
{
    [Fact]
    public void Clone_IsAWholeSeparateCopy()
    {
        GeometryData mesh = GeometryGenerator.Box(new Float3(2f));
        GeometryData copy = mesh.Clone();
        copy.Vertices[0].Point += new Float3(5f, 0f, 0f);

        Assert.Equal(mesh.Faces.Count, copy.Faces.Count);
        Assert.NotEqual(mesh.Vertices[0].Point, copy.Vertices[0].Point);
        Assert.Equal(4, mesh.Faces[0].Corners().Count());
    }

    [Fact]
    public void Array_WithoutWeld_KeepsEveryCopyWhole()
    {
        GeometryData mesh = GeometryGenerator.Box(new Float3(2f));

        GeometryOperators.Array(mesh, [.. mesh.Faces], [], 2, (p, i) => p + new Float3(2f * i, 0f, 0f), weld: false);

        Assert.Equal(18, mesh.Faces.Count);
        Assert.Equal(24, mesh.Vertices.Count);
    }

    [Fact]
    public void Bounds_HoldsEveryVertex()
    {
        AABB bounds = GeometryGenerator.Box(new Float3(2f, 4f, 6f), new Float3(1f, 0f, 0f)).Bounds();

        Assert.Equal(new Float3(0f, -2f, -3f), bounds.Min);
        Assert.Equal(new Float3(2f, 2f, 3f), bounds.Max);
    }

    [Fact]
    public void EdgesSharperThan_FindsTheCornersOfABox()
    {
        GeometryData mesh = GeometryGenerator.Box(new Float3(2f));

        Assert.Equal(12, GeometryOperators.EdgesSharperThan(mesh, 30f).Count);
        Assert.Empty(GeometryOperators.EdgesSharperThan(mesh, 95f));
    }

    [Fact]
    public void Boolean_CutsAPlacedMeshAway()
    {
        GeometryData box = GeometryGenerator.Box(new Float3(2f));
        GeometryData cutter = GeometryGenerator.Box(new Float3(2f));

        GeometryData result = GeometryOperators.Boolean(box, cutter, Float4x4.CreateTranslation(new Float3(1f, 0f, 0f)), CSGOperation.Subtractive);

        Assert.True(result.IsClosed());
        Assert.Equal(4f, result.Volume(), 3);
        Assert.False(result.HasFaceAttribute(CSGScene.BrushIdAttribute));
        Assert.Equal(8f, box.Volume(), 3);
    }

    [Fact]
    public void ShrinkFatten_MovesVerticesAlongTheirNormals()
    {
        GeometryData mesh = GeometryGenerator.Box(new Float3(2f));
        float before = mesh.Volume();

        GeometryOperators.ShrinkFatten(mesh, mesh.Vertices, MathF.Sqrt(3f));

        Assert.All(mesh.Vertices, v => Assert.Equal(2f, MathF.Abs(v.Point.X), 4));
        Assert.Equal(before * 8f, mesh.Volume(), 2);
    }

    [Fact]
    public void ToSphere_PutsVerticesOnASphereRoundTheirMiddle()
    {
        GeometryData mesh = GeometryGenerator.Box(new Float3(2f, 4f, 6f));

        GeometryOperators.ToSphere(mesh, mesh.Vertices, 1f);

        float radius = Float3.Length(mesh.Vertices[0].Point);
        Assert.All(mesh.Vertices, v => Assert.Equal(radius, Float3.Length(v.Point), 4));
    }

    [Fact]
    public void SplitFaces_TakesFacesOffTheRest()
    {
        GeometryData mesh = GeometryGenerator.Box(new Float3(2f));
        GeometryData.Face top = mesh.Faces.First(f => f.Normal().Y > 0.9f);

        List<GeometryData.Face> split = GeometryOperators.SplitFaces(mesh, [top]);

        Assert.Equal(12, mesh.Vertices.Count);
        GeometryData.Face after = Assert.Single(split);
        Assert.True(mesh.Contains(after));
        Assert.True(after.Normal().Y > 0.9f);
        Assert.All(after.NeighborVertices(), v => Assert.Single(v.NeighborFaces()));
    }

    [Fact]
    public void Transform_MovesOffsetsAndNormalsAndTurnsMirroredFaces()
    {
        GeometryData mesh = GeometryGenerator.Box(new Float3(2f));
        mesh.AddLoopAttribute("offset", GeometryData.AttributeBaseType.Float, 3);
        mesh.AddLoopAttribute("normal", GeometryData.AttributeBaseType.Float, 3);
        foreach (GeometryData.Loop loop in mesh.Loops)
        {
            loop.SetAttribute("offset", new GeometryData.FloatAttributeValue(1f, 0f, 0f));
            loop.SetAttribute("normal", new GeometryData.FloatAttributeValue(1f, 0f, 0f));
        }
        float volume = mesh.Volume();

        GeometryOperators.Transform(mesh, Float4x4.CreateTRS(new Float3(5f, 0f, 0f), Quaternion.Identity, new Float3(-2f, 1f, 1f)), ["offset"], ["normal"]);

        Assert.True(mesh.Volume() > 0f);
        Assert.Equal(volume * 2f, mesh.Volume(), 3);
        Assert.All(mesh.Loops, l =>
        {
            Assert.True(l.TryGetAttribute("offset", out GeometryData.AttributeValue? o));
            Assert.Equal(-2f, ((GeometryData.FloatAttributeValue)o!).Data[0], 4);
            Assert.True(l.TryGetAttribute("normal", out GeometryData.AttributeValue? n));
            Assert.Equal(-1f, ((GeometryData.FloatAttributeValue)n!).Data[0], 4);
        });
    }

    [Fact]
    public void Scale_ScalesAllVerticesFromOrigin()
    {
        var mesh = new GeometryData();
        var v = mesh.AddVertex(1, 2, 3);

        GeometryOperators.Scale(mesh, 2.0f);

        TestUtil.AssertClose(new Float3(2, 4, 6), v.Point);
    }

    [Fact]
    public void Translate_OffsetsAllVertices()
    {
        var mesh = new GeometryData();
        var v = mesh.AddVertex(1, 1, 1);

        GeometryOperators.Translate(mesh, new Float3(1, 2, 3));

        TestUtil.AssertClose(new Float3(2, 3, 4), v.Point);
    }

    [Fact]
    public void ScaleFace_ScalesAboutFaceCenter()
    {
        var mesh = TestUtil.MakeQuad();
        var center = mesh.Faces[0].Center();

        GeometryOperators.ScaleFace(mesh, mesh.Faces[0], 2.0f);

        // Center is invariant under a uniform scale about the center.
        TestUtil.AssertClose(center, mesh.Faces[0].Center());
        // Corner (0,0) should move to twice its distance from the (0.5,0.5) center.
        var corner = mesh.Vertices.First(v => Maths.Abs(v.Point.X - (-0.5f)) < 1e-3f);
        TestUtil.AssertClose(new Float3(-0.5f, -0.5f, 0), corner.Point);
    }

    [Fact]
    public void TranslateFaces_MovesSharedVerticesOnce()
    {
        var mesh = new GeometryData();
        var v0 = mesh.AddVertex(0, 0, 0);
        var v1 = mesh.AddVertex(1, 0, 0);
        var v2 = mesh.AddVertex(1, 1, 0);
        var v3 = mesh.AddVertex(0, 1, 0);
        var f0 = mesh.AddFace(v0, v1, v2)!;
        var f1 = mesh.AddFace(v0, v2, v3)!;

        // v0 and v2 are shared by both faces; they must only move once.
        GeometryOperators.TranslateFaces(mesh, new[] { f0, f1 }, new Float3(0, 0, 1));

        TestUtil.AssertClose(new Float3(0, 0, 1), v0.Point);
        TestUtil.AssertClose(new Float3(1, 1, 1), v2.Point);
    }

    [Fact]
    public void Translate_ThenScale_GetAABBReflectsChange()
    {
        var mesh = TestUtil.MakeQuad();
        GeometryOperators.Scale(mesh, 2.0f);

        var box = mesh.GetAABB();
        TestUtil.AssertClose(new Float3(0, 0, 0), box.Min);
        TestUtil.AssertClose(new Float3(2, 2, 0), box.Max);
    }

    [Fact]
    public void RecalculateNormals_ProducesUnitNormals()
    {
        var mesh = TestUtil.MakeQuad();

        GeometryOperators.RecalculateNormals(mesh);

        Assert.True(mesh.HasVertexAttribute("normal"));
        foreach (var v in mesh.Vertices)
        {
            var n = v.Attributes["normal"].AsFloat()!.AsVector3();
            TestUtil.AssertClose(1.0f, Float3.Length(n));
            TestUtil.AssertClose(1.0f, Maths.Abs(n.Z)); // XY quad -> normal along Z
        }
    }

    [Fact]
    public void Triangulate_ConvertsQuadIntoTriangles()
    {
        var mesh = TestUtil.MakeQuad();

        GeometryOperators.Triangulate(mesh);

        Assert.Equal(2, mesh.Faces.Count);
        Assert.All(mesh.Faces, f => Assert.Equal(3, f.VertCount));
    }

    [Fact]
    public void Triangulate_LeavesTrianglesUnchanged()
    {
        var mesh = TestUtil.MakeTriangle();

        GeometryOperators.Triangulate(mesh);

        Assert.Single(mesh.Faces);
        Assert.Equal(3, mesh.Faces[0].VertCount);
    }

    [Fact]
    public void AttributeLerp_InterpolatesFloatAttributes()
    {
        var mesh = new GeometryData();
        mesh.AddVertexAttribute("value", GeometryData.AttributeBaseType.Float, 1);
        var a = mesh.AddVertex(0, 0, 0);
        var b = mesh.AddVertex(1, 0, 0);
        var dst = mesh.AddVertex(0.5f, 0, 0);

        a.Attributes["value"] = new GeometryData.FloatAttributeValue(0);
        b.Attributes["value"] = new GeometryData.FloatAttributeValue(10);

        GeometryOperators.AttributeLerp(mesh, dst, a, b, 0.25f);

        TestUtil.AssertClose(2.5f, dst.Attributes["value"].AsFloat()!.Data[0]);
    }

    [Fact]
    public void WeldVertices_MergesCoincidentVertices()
    {
        var mesh = new GeometryData();
        mesh.AddVertex(0, 0, 0);
        mesh.AddVertex(0.00001f, 0, 0); // within default threshold

        int welded = GeometryOperators.WeldVertices(mesh);

        Assert.Equal(1, welded);
        Assert.Single(mesh.Vertices);
    }
}
