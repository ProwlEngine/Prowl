// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class ModelUVTests
{
    private static GeometryData Grid(int columns, int rows)
    {
        var mesh = new GeometryData();
        for (int z = 0; z <= rows; z++)
            for (int x = 0; x <= columns; x++)
                mesh.AddVertex(x, 0, z);
        int stride = columns + 1;
        for (int z = 0; z < rows; z++)
            for (int x = 0; x < columns; x++)
                mesh.AddFace(z * stride + x, (z + 1) * stride + x, (z + 1) * stride + x + 1, z * stride + x + 1);
        return mesh;
    }

    [Fact]
    public void SeamsAndPinsAreSavedWithTheModel()
    {
        GeometryData mesh = Grid(2, 1);
        GeometryUVs.Reset(mesh, [.. mesh.Faces]);
        GeometryUVs.SetSeams(mesh, [mesh.FindEdge(mesh.Vertices[1], mesh.Vertices[4])!], true);
        GeometryUVs.SetPinned([mesh.Faces[0].Loop!], true);
        var document = new PMeshDocument { Objects = { new PMeshObject { Name = "Body", Mesh = mesh } } };

        GeometryData read = PMeshDocument.Read(document.Write()).Objects[0].Mesh;

        Assert.Single(read.Edges, GeometryUVs.IsSeam);
        Assert.True(GeometryUVs.IsSeam(read.FindEdge(read.Vertices[1], read.Vertices[4])!));
        Assert.Single(read.Faces.SelectMany(f => f.Corners()), GeometryUVs.IsPinned);
    }

    [Fact]
    public void SeamsSavedUnderTheOldNameReadAsSeams()
    {
        GeometryData mesh = GeometryGenerator.Box(new Float3(1f));
        mesh.Edges[0].SetAttribute("seam", new GeometryData.IntAttributeValue(1));
        var document = new PMeshDocument { Objects = { new PMeshObject { Name = "Body", Mesh = mesh } } };

        GeometryData read = PMeshDocument.Read(document.Write()).Objects[0].Mesh;

        Assert.Single(read.Edges, e => e.IsSeam);
        Assert.All(read.Edges, e => Assert.False(e.Attributes.ContainsKey("seam")));
    }
}
