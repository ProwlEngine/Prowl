// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class GeometryStorageTests
{
    private static GeometryData Cube() => GeometryGenerator.Box(new Float3(2, 2, 2));

    private static void AssertIndexed(GeometryData mesh)
    {
        for (int i = 0; i < mesh.Vertices.Count; i++) Assert.Equal(i, mesh.Vertices[i].Index);
        for (int i = 0; i < mesh.Edges.Count; i++) Assert.Equal(i, mesh.Edges[i].Index);
        for (int i = 0; i < mesh.Loops.Count; i++) Assert.Equal(i, mesh.Loops[i].Index);
        for (int i = 0; i < mesh.Faces.Count; i++) Assert.Equal(i, mesh.Faces[i].Index);
    }

    [Fact]
    public void Elements_KnowTheirPlace_AndTheMeshKnowsThem()
    {
        GeometryData cube = Cube();

        AssertIndexed(cube);
        Assert.All(cube.Faces, f => Assert.True(cube.Contains(f)));
        Assert.False(Cube().Contains(cube.Faces[0]));
        Assert.Equal(-1, new GeometryData.Vertex(Float3.Zero).Index);
    }

    [Fact]
    public void RemovingAnElement_MovesTheLastIntoItsPlace()
    {
        GeometryData cube = Cube();
        GeometryData.Face removed = cube.Faces[1], last = cube.Faces[^1];

        cube.RemoveFace(removed);

        Assert.Equal(5, cube.Faces.Count);
        Assert.Same(last, cube.Faces[1]);
        Assert.False(cube.Contains(removed));
        Assert.Equal(-1, removed.Index);
        AssertIndexed(cube);

        cube.RemoveVertex(cube.Vertices[0]);
        AssertIndexed(cube);
        Assert.Null(cube.FindDefect());
    }

    [Fact]
    public void Attributes_FollowTheirElement_WhenOthersAreRemoved()
    {
        GeometryData cube = Cube();
        cube.AddFaceAttribute("tag", GeometryData.AttributeBaseType.Int, 1);
        for (int i = 0; i < cube.Faces.Count; i++) cube.Faces[i].Attributes["tag"] = new GeometryData.IntAttributeValue(i + 10);
        var tagged = cube.Faces.ToDictionary(f => f, f => f.Attributes["tag"].AsInt()!.Data[0]);

        cube.RemoveFace(cube.Faces[0]);
        cube.RemoveFace(cube.Faces[2]);

        Assert.All(cube.Faces, f => Assert.Equal(tagged[f], f.Attributes["tag"].AsInt()!.Data[0]));
    }

    [Fact]
    public void ARemovedElement_KeepsWhatItCarried_AndBringsItBack()
    {
        GeometryData mesh = new();
        GeometryData.Vertex vertex = mesh.AddVertex(1, 2, 3);
        mesh.AddVertex(4, 5, 6);
        vertex.Attributes["weight"] = new GeometryData.FloatAttributeValue(0.5f);

        mesh.RemoveVertex(vertex);

        Assert.Equal(0.5f, vertex.Attributes["weight"].AsFloat()!.Data[0]);
        Assert.False(mesh.Vertices[0].Attributes.ContainsKey("weight"));

        mesh.AddVertex(vertex);
        Assert.Equal(0.5f, vertex.Attributes["weight"].AsFloat()!.Data[0]);
        Assert.Equal(1, vertex.Index);
    }

    [Fact]
    public void ADeclaredAttribute_ReadsItsDefault_UntilSet_AndAgainOnceCleared()
    {
        GeometryData mesh = new();
        mesh.AddVertexAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
        GeometryData.Vertex vertex = mesh.AddVertex(0, 0, 0);

        Assert.True(vertex.Attributes.ContainsKey("uv"));
        Assert.Equal([0f, 0f], vertex.Attributes["uv"].AsFloat()!.Data);

        // The default can be changed in place, as a stored value can
        vertex.Attributes["uv"].AsFloat()!.Data[0] = 7;
        Assert.Equal(7f, vertex.Attributes["uv"].AsFloat()!.Data[0]);

        Assert.True(vertex.ClearAttribute("uv"));
        Assert.Equal(0f, vertex.Attributes["uv"].AsFloat()!.Data[0]);
        Assert.False(vertex.Attributes.ContainsKey("nothing"));
        Assert.Throws<KeyNotFoundException>(() => vertex.Attributes["nothing"]);
    }

    [Fact]
    public void Attributes_AssignedFromADictionary_ReplaceWhatWasThere()
    {
        GeometryData mesh = new();
        GeometryData.Vertex vertex = mesh.AddVertex(0, 0, 0);
        vertex.Attributes["old"] = new GeometryData.IntAttributeValue(1);

        vertex.Attributes = new Dictionary<string, GeometryData.AttributeValue> { ["new"] = new GeometryData.IntAttributeValue(2) };

        Assert.False(vertex.Attributes.ContainsKey("old"));
        Assert.Equal(2, vertex.Attributes["new"].AsInt()!.Data[0]);
        Assert.Single(vertex.Attributes);
        Assert.Equal(["new"], vertex.Attributes.Select(p => p.Key).ToArray());

        Dictionary<string, GeometryData.AttributeValue> copy = vertex.Attributes.ToDictionary();
        copy["other"] = new GeometryData.IntAttributeValue(3);
        Assert.False(vertex.Attributes.ContainsKey("other"));
    }

    [Fact]
    public void ASnapshot_BringsBackEverything_AttributesAndCornerOrderIncluded()
    {
        GeometryData cube = Cube();
        cube.AddLoopAttribute("corner", GeometryData.AttributeBaseType.Int, 1);
        cube.AddEdgeAttribute("mark", GeometryData.AttributeBaseType.Int, 1);
        for (int f = 0; f < cube.Faces.Count; f++)
        {
            cube.Faces[f].Attributes["tag"] = new GeometryData.IntAttributeValue(f);
            GeometryData.Loop loop = cube.Faces[f].Loop!;
            for (int k = 0; k < 4; k++, loop = loop.Next!) loop.Attributes["corner"] = new GeometryData.IntAttributeValue(f * 10 + k);
        }
        cube.Edges[3].Attributes["mark"] = new GeometryData.IntAttributeValue(1);
        Float3[] corners = cube.Faces.SelectMany(f => f.NeighborVertices()).Select(v => v.Point).ToArray();
        (Float3, Float3) marked = (cube.Edges[3].Vert1.Point, cube.Edges[3].Vert2.Point);

        GeometrySnapshot snapshot = cube.Capture();

        // Removals scramble the lists, and more attributes are set, before going back
        cube.RemoveFace(cube.Faces[0]);
        cube.RemoveVertex(cube.Vertices[2]);
        cube.AddVertexAttribute("late", GeometryData.AttributeBaseType.Float, 1);
        cube.Restore(snapshot);

        Assert.Equal(8, cube.Vertices.Count);
        Assert.Equal(12, cube.Edges.Count);
        Assert.Equal(6, cube.Faces.Count);
        Assert.Null(cube.FindDefect());
        AssertIndexed(cube);
        Assert.False(cube.HasVertexAttribute("late"));
        Assert.Equal(corners, cube.Faces.SelectMany(f => f.NeighborVertices()).Select(v => v.Point).ToArray());
        for (int f = 0; f < 6; f++)
        {
            Assert.Equal(f, cube.Faces[f].Attributes["tag"].AsInt()!.Data[0]);
            GeometryData.Loop loop = cube.Faces[f].Loop!;
            for (int k = 0; k < 4; k++, loop = loop.Next!) Assert.Equal(f * 10 + k, loop.Attributes["corner"].AsInt()!.Data[0]);
        }
        GeometryData.Edge again = Assert.Single(cube.Edges, e => e.Attributes["mark"].AsInt()!.Data[0] == 1);
        Assert.Equal(marked, (again.Vert1.Point, again.Vert2.Point));

        // The snapshot is still whole after being used, and what it gave back is not shared with it
        cube.Faces[0].Attributes["tag"].AsInt()!.Data[0] = 99;
        cube.Restore(snapshot);
        Assert.Equal(0, cube.Faces[0].Attributes["tag"].AsInt()!.Data[0]);
    }

    [Fact]
    public void FlaggedElements_AreFoundByTheirPlace()
    {
        GeometryData cube = Cube();
        cube.Faces[1].Hide = true;
        cube.Faces[4].Hide = true;
        cube.Faces[4].Hide = false;
        cube.Vertices[5].Hide = true;

        Assert.Equal([1], cube.FlaggedFaces(GeometryData.HideAttribute));
        Assert.Equal([5], cube.FlaggedVertices(GeometryData.HideAttribute));
        Assert.Empty(cube.FlaggedEdges(GeometryData.HideAttribute));
        Assert.Empty(cube.FlaggedFaces("nothing"));

        cube.RemoveFace(cube.Faces[0]);
        Assert.True(cube.Faces[cube.FlaggedFaces(GeometryData.HideAttribute).Single()].Hide);
    }

    [Fact]
    public void Clear_EmptiesTheMesh_AndKeepsDeclaredAttributes()
    {
        GeometryData cube = Cube();
        cube.AddVertexAttribute("uv", GeometryData.AttributeBaseType.Float, 2);
        cube.Vertices[0].Attributes["uv"] = new GeometryData.FloatAttributeValue(1, 1);
        GeometryData.Vertex old = cube.Vertices[0];

        cube.Clear();

        Assert.Empty(cube.Vertices);
        Assert.Empty(cube.Faces);
        Assert.True(cube.HasVertexAttribute("uv"));
        Assert.False(cube.Contains(old));
        Assert.Equal(0f, cube.AddVertex(0, 0, 0).Attributes["uv"].AsFloat()!.Data[0]);
    }
}
