// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class GeometryRenderMeshTests
{
    private static GeometryData Cube() => GeometryGenerator.Box(new Float3(1, 1, 1));

    [Fact]
    public void FlatCube_HasFourVerticesAFace_AndFaceNormals()
    {
        GeometryData cube = Cube();
        RenderMesh mesh = cube.ToRenderMesh();

        Assert.Equal(24, mesh.Positions.Length);
        Assert.Equal(36, mesh.Indices.Length);
        Assert.Equal(12, mesh.TriangleFaces.Length);
        for (int v = 0; v < mesh.Positions.Length; v++)
            Assert.True(Float3.Dot(mesh.Normals[v], cube.Faces[mesh.VertexFaces[v]].Normal()) > 0.999f);
    }

    [Fact]
    public void SmoothCube_SharesItsEightVertices()
    {
        GeometryData cube = Cube();
        RenderMesh mesh = cube.ToRenderMesh(new RenderMeshOptions { ForceSmooth = true });

        Assert.Equal(8, mesh.Positions.Length);
        for (int v = 0; v < 8; v++)
            Assert.True(Float3.Dot(mesh.Normals[v], Float3.Normalize(mesh.Positions[v])) > 0.99f);
    }

    [Fact]
    public void SharpEdges_SplitSmoothNormals()
    {
        GeometryData cube = Cube();
        cube.AddEdgeAttribute(GeometryData.SharpAttribute, GeometryData.AttributeBaseType.Int, 1);
        foreach (GeometryData.Edge edge in cube.Edges)
            edge.IsSharp = true;

        RenderMesh mesh = cube.ToRenderMesh(new RenderMeshOptions { ForceSmooth = true });

        Assert.Equal(24, mesh.Positions.Length);
    }

    [Fact]
    public void SplitFaces_KeepsSmoothNormals_ButNoSharedVertices()
    {
        RenderMesh mesh = Cube().ToRenderMesh(new RenderMeshOptions { ForceSmooth = true, SplitFaces = true });

        Assert.Equal(24, mesh.Positions.Length);
        Assert.All(mesh.VertexFaces, f => Assert.True(f >= 0));
        for (int v = 0; v < mesh.Positions.Length; v++)
            Assert.True(Float3.Dot(mesh.Normals[v], Float3.Normalize(mesh.Positions[v])) > 0.99f);
    }

    [Fact]
    public void HiddenFaces_AreLeftOut_WhenAsked()
    {
        GeometryData cube = Cube();
        cube.AddFaceAttribute(GeometryData.HideAttribute, GeometryData.AttributeBaseType.Int, 1);
        cube.Faces[0].Hide = true;

        Assert.Equal(12, cube.ToRenderMesh().TriangleFaces.Length);
        RenderMesh mesh = cube.ToRenderMesh(new RenderMeshOptions { SkipHidden = true });
        Assert.Equal(10, mesh.TriangleFaces.Length);
        Assert.DoesNotContain(0, mesh.TriangleFaces);
    }

    [Fact]
    public void Triangles_WindTheWayTheirFaceDoes_UnlessReversed()
    {
        GeometryData cube = Cube();
        RenderMesh mesh = cube.ToRenderMesh();

        for (int t = 0; t < mesh.TriangleFaces.Length; t++)
        {
            Float3 a = mesh.Positions[mesh.Indices[t * 3]], b = mesh.Positions[mesh.Indices[t * 3 + 1]], c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            Float3 winding = Float3.Normalize(Float3.Cross(b - a, c - a));
            Assert.True(Float3.Dot(winding, cube.Faces[mesh.TriangleFaces[t]].Normal()) > 0.999f);
        }

        RenderMesh reversed = cube.ToRenderMesh(new RenderMeshOptions { ReverseWinding = true });
        for (int t = 0; t < reversed.TriangleFaces.Length; t++)
        {
            Float3 a = reversed.Positions[reversed.Indices[t * 3]], b = reversed.Positions[reversed.Indices[t * 3 + 1]], c = reversed.Positions[reversed.Indices[t * 3 + 2]];
            Float3 winding = Float3.Normalize(Float3.Cross(b - a, c - a));
            Assert.True(Float3.Dot(winding, cube.Faces[reversed.TriangleFaces[t]].Normal()) < -0.999f);
        }
    }

    [Fact]
    public void ConcaveQuad_IsSplitAlongTheDiagonalInsideIt()
    {
        // A dart that bends in at its second corner, so the diagonal from the first to the third lies outside it
        GeometryData mesh = new();
        Float3[] points = [new(-1, 0, 0), new(0, 0.5f, 0), new(1, 0, 0), new(0, 2, 0)];
        mesh.AddFace(points.Select(p => mesh.AddVertex(p)).ToArray());

        RenderMesh render = mesh.ToRenderMesh();

        Assert.Equal(2, render.TriangleFaces.Length);
        float area = 0;
        for (int t = 0; t < 2; t++)
        {
            Float3 a = render.Positions[render.Indices[t * 3]], b = render.Positions[render.Indices[t * 3 + 1]], c = render.Positions[render.Indices[t * 3 + 2]];
            area += Float3.Length(Float3.Cross(b - a, c - a)) * 0.5f;
        }
        Assert.Equal(1.5f, area, 4);
    }

    [Fact]
    public void ConcaveFace_IsCutInsideItsOutline()
    {
        // An L shape, six corners, counter clockwise seen from +Z
        GeometryData mesh = new();
        Float3[] points = [new(0, 0, 0), new(2, 0, 0), new(2, 1, 0), new(1, 1, 0), new(1, 2, 0), new(0, 2, 0)];
        mesh.AddFace(points.Select(p => mesh.AddVertex(p)).ToArray());

        RenderMesh render = mesh.ToRenderMesh();

        Assert.Equal(4, render.TriangleFaces.Length);
        float area = 0;
        for (int t = 0; t < 4; t++)
        {
            Float3 a = render.Positions[render.Indices[t * 3]], b = render.Positions[render.Indices[t * 3 + 1]], c = render.Positions[render.Indices[t * 3 + 2]];
            area += Float3.Length(Float3.Cross(b - a, c - a)) * 0.5f;
        }
        Assert.Equal(3f, area, 3);
    }

    [Fact]
    public void LoopUvs_SplitVertices_WhereTheyDiffer()
    {
        GeometryData mesh = GeometryGenerator.Plane(new Float2(2, 1), segments: new Int2(2, 1));
        mesh.AddLoopAttribute(GeometryData.UvAttribute, GeometryData.AttributeBaseType.Float, 2);
        // Each face gets its own 0 to 1 square, so the two middle vertices carry two coordinates each
        foreach (GeometryData.Face face in mesh.Faces)
        {
            GeometryData.Loop loop = face.Loop!;
            for (int i = 0; i < 4; i++)
            {
                loop.Attributes[GeometryData.UvAttribute] = new GeometryData.FloatAttributeValue(i is 1 or 2 ? 1 : 0, i >= 2 ? 1 : 0);
                loop = loop.Next!;
            }
        }

        RenderMesh render = mesh.ToRenderMesh(new RenderMeshOptions { ForceSmooth = true });

        Assert.NotNull(render.UVs);
        Assert.Equal(8, render.Positions.Length);
    }

    [Fact]
    public void ToRenderMesh_FacesDrawsSomeFacesShadedAsTheWhole()
    {
        GeometryData mesh = GeometryGenerator.Plane(new Float2(2, 1), segments: new Int2(2, 1));
        GeometryData.Face first = mesh.Faces[0];

        RenderMesh whole = mesh.ToRenderMesh(new RenderMeshOptions { ForceSmooth = true });
        RenderMesh piece = mesh.ToRenderMesh(new RenderMeshOptions { ForceSmooth = true, Faces = [first] });

        Assert.Equal(6, piece.Indices.Length);
        Assert.Equal(4, piece.Positions.Length);
        foreach (Float3 normal in piece.Normals) Assert.Contains(whole.Normals, n => Float3.Distance(n, normal) < 1e-5f);
    }
}
