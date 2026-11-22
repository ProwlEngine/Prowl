// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class TransformSessionTests
{
    private static EditTarget Box(Float4x4? localToWorld = null)
    {
        CSGControlMesh box = CSGControlMesh.FromPlanes(GeometryGenerator.BoxPlanes(new Float3(2f), Float3.Zero))!;
        return new EditTarget(box.ToMesh(out _), localToWorld ?? Float4x4.Identity);
    }

    private static GeometryData.Face Facing(GeometryData mesh, Float3 normal)
        => mesh.Faces.First(f => Float3.Dot(f.Normal(), normal) > 0.99f);

    private static ElementSelection FaceSelection(GeometryData mesh, params Float3[] normals)
    {
        var selection = new ElementSelection();
        foreach (Float3 normal in normals) selection.Faces.Add(Facing(mesh, normal));
        Selecting.Normalize(selection, SelectMode.Face);
        return selection;
    }

    private static void Near(Float3 expected, Float3 actual) => Assert.True(Float3.Distance(expected, actual) < 1e-4f, $"{expected} != {actual}");

    [Fact]
    public void MovesAreFromTheStartAndRestorePutsThePointsBack()
    {
        EditTarget target = Box();
        ElementSelection top = FaceSelection(target.Mesh, Float3.UnitY);
        var session = new TransformSession(target, top);
        Float3[] before = target.Mesh.Vertices.Select(v => v.Point).ToArray();

        session.Translate(new Float3(0, 5, 0));
        session.Translate(new Float3(0, 1, 0));

        foreach (GeometryData.Vertex vertex in target.Mesh.Vertices)
            Near(before[vertex.Index] + (top.Vertices.Contains(vertex) ? Float3.UnitY : Float3.Zero), vertex.Point);

        session.Restore();
        foreach (GeometryData.Vertex vertex in target.Mesh.Vertices) Near(before[vertex.Index], vertex.Point);
    }

    [Fact]
    public void RotationTurnsAboutTheMedian()
    {
        EditTarget target = Box();
        ElementSelection top = FaceSelection(target.Mesh, Float3.UnitY);
        var session = new TransformSession(target, top);

        Near(new Float3(0, 1, 0), session.Pivot);
        session.Rotate(Float3.UnitY, MathF.PI / 2);

        foreach (GeometryData.Vertex vertex in top.Vertices)
        {
            Near(new Float3(0, 1, 0), new Float3(0, vertex.Point.Y, 0));
            Assert.Equal(1f, MathF.Abs(vertex.Point.X), 3);
            Assert.Equal(1f, MathF.Abs(vertex.Point.Z), 3);
        }
    }

    [Fact]
    public void ScaleFollowsTurnedAxes()
    {
        EditTarget target = Box();
        ElementSelection top = FaceSelection(target.Mesh, Float3.UnitY);
        var session = new TransformSession(target, top);

        session.Scale(new Float3(2, 1, 1), Quaternion.AxisAngle(Float3.UnitY, MathF.PI / 2));

        foreach (GeometryData.Vertex vertex in top.Vertices)
        {
            Assert.Equal(1f, MathF.Abs(vertex.Point.X), 3);
            Assert.Equal(2f, MathF.Abs(vertex.Point.Z), 3);
        }
    }

    [Fact]
    public void IndividualOriginsScaleEachFaceAboutItsOwnCentre()
    {
        EditTarget target = Box();
        ElementSelection caps = FaceSelection(target.Mesh, Float3.UnitY, -Float3.UnitY);
        var session = new TransformSession(target, caps) { Individual = true };

        session.Scale(new Float3(0.5f), Quaternion.Identity);

        foreach (GeometryData.Vertex vertex in caps.Vertices)
        {
            Assert.Equal(1f, MathF.Abs(vertex.Point.Y), 3);
            Assert.Equal(0.5f, MathF.Abs(vertex.Point.X), 3);
        }
    }

    [Fact]
    public void WorldMovesLandInTheTargetsOwnSpace()
    {
        EditTarget target = Box(Float4x4.CreateScale(new Float3(2f)));
        ElementSelection top = FaceSelection(target.Mesh, Float3.UnitY);
        var session = new TransformSession(target, top);

        Near(new Float3(0, 2, 0), session.Pivot);
        session.Translate(new Float3(0, 2, 0));

        foreach (GeometryData.Vertex vertex in top.Vertices) Assert.Equal(2f, vertex.Point.Y, 3);
    }
}
