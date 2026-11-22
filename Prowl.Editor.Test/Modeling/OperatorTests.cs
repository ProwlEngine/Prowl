// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class OperatorTests
{
    private static CSGControlMesh Box() => CSGControlMesh.FromPlanes(GeometryGenerator.BoxPlanes(new Float3(2f), Float3.Zero))!;

    private static SurfaceData[] Marked(CSGControlMesh mesh)
        => Enumerable.Range(0, mesh.Polygons.Count).Select(p => new SurfaceData { Scale = Float2.One, Offset = new Float2(p, 0f) }).ToArray();

    /// <summary>An inset face shrinks inside its outline, ringed by new faces that take its surface, and is what is selected after.</summary>
    [Fact]
    public void InsetRingsAFaceWithFacesOfItsSurface()
    {
        CSGControlMesh box = Box();
        int top = Enumerable.Range(0, box.Polygons.Count).First(p => box.PolygonNormal(p).Y > 0.99f);
        var target = new CSGBrushTarget(box, Marked(box));
        var after = new ElementSelection();

        string? problem = MeshEdit.Run(target.Target, (mesh, selected) => Operators.Inset(mesh, target.FacesOf([top]), 0.25f, selected), after);

        Assert.Null(problem);
        GeometryData.Face inner = Assert.Single(after.Faces);
        Assert.All(inner.NeighborVertices(), v => Assert.True(MathF.Abs(v.Point.X) <= 0.75f + 1e-4f && MathF.Abs(v.Point.Z) <= 0.75f + 1e-4f));

        (CSGControlMesh inset, SurfaceData[] surfaces) = target.ToBrush();
        Assert.Equal(CSGMeshProblem.None, inset.Check());
        Assert.Equal(box.Polygons.Count + 4, inset.Polygons.Count);
        Assert.Equal(8f, inset.Volume(), 4);

        var upward = Enumerable.Range(0, inset.Polygons.Count).Where(p => inset.PolygonNormal(p).Y > 0.99f).ToList();
        Assert.Equal(5, upward.Count);
        Assert.All(upward, p => Assert.Equal(top, (int)surfaces[p].Offset.X));
    }

    /// <summary>An inset wider than its face cannot be made, and leaves the mesh as it was.</summary>
    [Fact]
    public void AnInsetTooWideIsRefused()
    {
        CSGControlMesh box = Box();
        int top = Enumerable.Range(0, box.Polygons.Count).First(p => box.PolygonNormal(p).Y > 0.99f);
        var target = new CSGBrushTarget(box, Marked(box));
        int faces = target.Target.Mesh.Faces.Count;

        string? problem = MeshEdit.Run(target.Target, (mesh, selected) => Operators.Inset(mesh, target.FacesOf([top]), 5f, selected), new ElementSelection());

        Assert.NotNull(problem);
        Assert.Equal(faces, target.Target.Mesh.Faces.Count);
    }
}
