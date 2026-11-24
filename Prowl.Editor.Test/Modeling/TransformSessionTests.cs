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

    [Fact]
    public void ProportionalEditingPullsNearPointsLessAndLeavesFarOnes()
    {
        EditTarget target = Box();
        GeometryData.Vertex corner = target.Mesh.Vertices.First(v => v.Point == new Float3(1, 1, 1));
        GeometryData.Vertex near = target.Mesh.Vertices.First(v => v.Point == new Float3(-1, 1, 1));
        GeometryData.Vertex far = target.Mesh.Vertices.First(v => v.Point == new Float3(-1, -1, -1));
        var selection = new ElementSelection();
        selection.Vertices.Add(corner);
        var session = new TransformSession(target, selection, falloff: 2.5f);

        session.Translate(new Float3(0, 1, 0));

        Assert.Equal(2f, corner.Point.Y, 4);
        Assert.InRange(near.Point.Y, 1.01f, 1.5f);
        Assert.Equal(-1f, far.Point.Y, 4);

        session.Falloff = 0f;
        session.Translate(new Float3(0, 1, 0));
        Assert.Equal(1f, near.Point.Y, 4);
        Assert.Equal(2f, corner.Point.Y, 4);
    }

    /// <summary>Two objects edited together share one pivot in the world and move together, each in its own space.</summary>
    [Fact]
    public void ASessionOverSeveralObjectsMovesThemTogether()
    {
        EditTarget left = Box(), right = Box(Float4x4.CreateTranslation(new Float3(10, 0, 0)));
        ElementSelection leftTop = FaceSelection(left.Mesh, Float3.UnitY), rightTop = FaceSelection(right.Mesh, Float3.UnitY);

        var session = new TransformSession([(left, leftTop), (right, rightTop)]);

        Near(new Float3(5, 1, 0), session.Pivot);
        Assert.Equal(8, session.Count);
        session.Translate(new Float3(0, 2, 0));
        Assert.All(leftTop.Vertices, v => Assert.Equal(3f, v.Point.Y, 4));
        Assert.All(rightTop.Vertices, v => Assert.Equal(3f, v.Point.Y, 4));

        session.Scale(new Float3(0.5f, 1f, 1f), Quaternion.Identity);
        Assert.All(rightTop.Vertices, v => Assert.InRange(v.Point.X, -3.6f, -1.4f));

        session.Restore();
        Assert.All(rightTop.Vertices, v => Assert.Equal(1f, v.Point.Y, 4));
    }

    [Fact]
    public void ObjectsTurnAndScaleAboutTheirMiddle()
    {
        var a = new Prowl.Runtime.GameObject("A");
        var b = new Prowl.Runtime.GameObject("B");
        a.Transform.Position = new Float3(-1f, 0f, 0f);
        b.Transform.Position = new Float3(1f, 0f, 0f);
        b.Transform.Rotation = Quaternion.AxisAngle(Float3.UnitY, MathF.PI / 2f);
        var session = new ObjectTransformSession([a, b]);

        session.Rotate(Float3.UnitY, MathF.PI);
        Near(new Float3(1f, 0f, 0f), a.Transform.Position);
        Near(new Float3(-1f, 0f, 0f), b.Transform.Position);

        session.Scale(new Float3(2f, 1f, 1f), Quaternion.Identity);
        Near(new Float3(-2f, 0f, 0f), a.Transform.Position);
        Near(new Float3(2f, 1f, 1f), a.Transform.LocalScale);
        // B is turned a quarter, so its own Z axis lies along the world X that grew
        Near(new Float3(1f, 1f, 2f), b.Transform.LocalScale);

        session.Restore();
        Near(new Float3(1f, 0f, 0f), b.Transform.Position);
        Near(Float3.One, b.Transform.LocalScale);
    }

    [Fact]
    public void MirrorClipKeepsPointsOnThePlaneAndStopsOthersCrossingIt()
    {
        EditTarget box = Box();
        var target = new EditTarget(box.Mesh, Float4x4.Identity) { MirrorClip = (0, 0f) };
        GeometryData.Vertex onPlane = target.Mesh.AddVertex(0f, 3f, 0f);
        GeometryData.Vertex right = target.Mesh.Vertices.First(v => v.Point.X > 0.5f);
        var selection = new ElementSelection();
        selection.Vertices.UnionWith([onPlane, right]);
        var session = new TransformSession(target, selection);

        session.Translate(new Float3(-5f, 0f, 0f));

        Assert.Equal(0f, onPlane.Point.X);
        Assert.Equal(0f, right.Point.X);
        session.Translate(new Float3(0.5f, 0f, 0f));
        Assert.Equal(0f, onPlane.Point.X);
        Assert.Equal(1.5f, right.Point.X, 4);
    }

    [Fact]
    public void ObjectsTurnAboutAPivotSetElsewhere()
    {
        var a = new Prowl.Runtime.GameObject("A");
        a.Transform.Position = new Float3(1f, 0f, 0f);
        var session = new ObjectTransformSession([a]) { Pivot = new Float3(3f, 0f, 0f) };

        session.Rotate(Float3.UnitY, MathF.PI);

        Near(new Float3(5f, 0f, 0f), a.Transform.Position);
    }
}
