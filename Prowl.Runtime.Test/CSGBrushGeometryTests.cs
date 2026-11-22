// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the plane-set operations the CSG editing tools are built from: picking a brush,
/// mirroring it, and moving its pivot without moving its geometry.
/// </summary>
public class CSGBrushGeometryTests
{
    private static Plane[] Box(Float3 size, Float3 center = default)
        => GeometryGenerator.BoxPlanes(size, center);

    private static GeometryData Solid(Plane[] planes)
        => GeometryGenerator.ConvexFromPlanes(planes).Solid!;

    /// <summary>Sample the half-spaces directly rather than trusting derived geometry.</summary>
    private static bool Inside(Plane[] planes, Float3 point)
    {
        foreach (Plane plane in planes)
            if (plane.GetSignedDistanceToPoint(point) > 1e-4f) return false;
        return true;
    }

    // ================================================================
    //  Picking
    // ================================================================

    [Fact]
    public void RaycastHitsTheNearFaceOfABox()
    {
        Plane[] planes = Box(Float3.One * 2f);

        Assert.True(CSGBrushGeometry.Raycast(planes, new Float3(0, 0, -10), Float3.UnitZ, out float t));
        Assert.Equal(9f, t, 3);
    }

    [Fact]
    public void RaycastMissesWhenTheRayPassesBeside()
    {
        Plane[] planes = Box(Float3.One * 2f);

        Assert.False(CSGBrushGeometry.Raycast(planes, new Float3(5, 0, -10), Float3.UnitZ, out _));
    }

    /// <summary>A ray pointing away must not report the hit behind it.</summary>
    [Fact]
    public void RaycastIgnoresGeometryBehindTheRay()
    {
        Plane[] planes = Box(Float3.One * 2f);

        Assert.False(CSGBrushGeometry.Raycast(planes, new Float3(0, 0, -10), -Float3.UnitZ, out _));
    }

    /// <summary>From inside, the hit is the exit point, so clicking inside a brush still picks it.</summary>
    [Fact]
    public void RaycastFromInsideReturnsTheExit()
    {
        Plane[] planes = Box(Float3.One * 2f);

        Assert.True(CSGBrushGeometry.Raycast(planes, Float3.Zero, Float3.UnitZ, out float t));
        Assert.Equal(1f, t, 3);
    }

    /// <summary>A ray from inside the points hits the face it leaves through, as from inside a room, when both sides count.</summary>
    [Fact]
    public void ControlMeshRaycastFromInsideHitsTheFaceItLeavesThrough()
    {
        CSGControlMesh box = CSGControlMesh.FromPlanes(Box(Float3.One * 2f))!;

        Assert.False(box.Raycast(Float3.Zero, -Float3.UnitY, out _, out _));
        Assert.True(box.Raycast(Float3.Zero, -Float3.UnitY, out float t, out int polygon, bothSides: true));
        Assert.Equal(1f, t, 3);
        Assert.True(box.PolygonNormal(polygon).Y < -0.99f);

        Assert.True(box.Raycast(new Float3(0f, 5f, 0f), -Float3.UnitY, out t, out polygon, bothSides: true));
        Assert.Equal(4f, t, 3);
        Assert.True(box.PolygonNormal(polygon).Y > 0.99f);
    }

    [Fact]
    public void RaycastHitsAnOffCenterBox()
    {
        Plane[] planes = Box(Float3.One * 2f, new Float3(0, 0, 5));

        Assert.True(CSGBrushGeometry.Raycast(planes, Float3.Zero, Float3.UnitZ, out float t));
        Assert.Equal(4f, t, 3);
    }

    // ================================================================
    //  Mirroring
    // ================================================================

    private static Float3 Reflect(Float3 v, Float3 axis) => v - axis * (2f * Float3.Dot(v, axis));

    // ================================================================
    //  Pivot
    // ================================================================

    [Fact]
    public void BoundsCoverTheSolid()
    {
        AABB bounds = CSGBrushGeometry.Bounds(Solid(Box(new Float3(2, 4, 6))));

        Assert.Equal(new Float3(-1, -2, -3), bounds.Min);
        Assert.Equal(new Float3(1, 2, 3), bounds.Max);
    }

    [Fact]
    public void AnEditedControlMeshSurvivesSaveAndLoad()
    {
        CSGControlMesh mesh = CSGControlMesh.FromPlanes(Box(Float3.One))!;
        mesh.MovePoints([0], new Float3(0.1f, 0.2f, 0.3f));

        CSGControlMesh loaded = Serializer.Deserialize<CSGControlMesh>(Serializer.Serialize(typeof(CSGControlMesh), mesh))!;

        Assert.Equal(mesh.Points, loaded.Points);
        Assert.Equal(mesh.Polygons.Count, loaded.Polygons.Count);
        for (int i = 0; i < mesh.Polygons.Count; i++)
            Assert.Equal(mesh.Polygons[i], loaded.Polygons[i]);
        Assert.Equal(mesh.ToPlanes(), loaded.ToPlanes());
    }

    [Fact]
    public void TransformingPlanesMovesTheSolidLikeItsCorners()
    {
        Plane[] planes = Box(new Float3(2, 1, 1));
        Float4x4 transform = Float4x4.CreateTranslation(new Float3(1, 2, 3))
                           * Float4x4.CreateFromQuaternion(Quaternion.AxisAngle(Float3.UnitY, 0.6f))
                           * Float4x4.CreateScale(new Float3(0.5f, 2, 1));

        Plane[] moved = CSGBrushGeometry.Transform(planes, transform);

        foreach (GeometryData.Vertex v in Solid(planes).Vertices)
        {
            Float3 corner = Float4x4.TransformPoint(v.Point, transform);
            float worst = 0f;
            foreach (Plane plane in moved) worst = MathF.Max(worst, plane.GetSignedDistanceToPoint(corner));
            Assert.InRange(worst, -1e-3f, 1e-3f);
        }
    }

    /// <summary>An inset face shrinks inside its outline, ringed by new faces that take its surface, and the solid stays the same.</summary>
    [Fact]
    public void InsetAFaceRingsItWithFacesOfItsSurface()
    {
        CSGControlMesh box = CSGControlMesh.FromPlanes(Box(new Float3(2f)))!;
        int top = Enumerable.Range(0, box.Polygons.Count).First(p => box.PolygonNormal(p).Y > 0.99f);
        SurfaceData[] surfaces = Enumerable.Range(0, box.Polygons.Count).Select(p => new SurfaceData { Scale = Float2.One, Offset = new Float2(p, 0f) }).ToArray();

        Assert.True(CSGMeshEdit.Inset(box, surfaces, [top], 0.25f, out CSGControlMesh inset, out SurfaceData[] kept));

        Assert.True(inset.IsValid());
        Assert.Equal(box.Polygons.Count + 4, inset.Polygons.Count);
        Assert.Equal(kept.Length, inset.Polygons.Count);
        Assert.Equal(8f, inset.Volume(), 4);

        var upward = Enumerable.Range(0, inset.Polygons.Count).Where(p => inset.PolygonNormal(p).Y > 0.99f).ToList();
        Assert.Equal(5, upward.Count);
        Assert.All(upward, p => Assert.Equal(top, (int)kept[p].Offset.X));
        Assert.Contains(upward, p => inset.PolygonPoints(p).All(i => MathF.Abs(inset.Points[i].X) <= 0.75f + 1e-4f && MathF.Abs(inset.Points[i].Z) <= 0.75f + 1e-4f));
    }

    /// <summary>New faces project their textures in world space, so a moved brush slides under its texture.</summary>
    [Fact]
    public void NewFacesProjectTexturesInWorldSpace()
    {
        Assert.True(SurfaceData.Default.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture));
    }

    /// <summary>Fitting a face's texture to the UVs it showed before its frame turned, moved or mirrored about it gives the same UVs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FittedUVsMatchTheUVsTheFaceShowedBefore(bool mirrored)
    {
        var data = new SurfaceData { Scale = new Float2(1.5f, 0.75f), Offset = new Float2(0.3f, -0.2f), Rotation = 20f, Flags = CSGSurfaceFlags.WorldSpaceTexture };
        Float3 normal = Float3.UnitZ;
        Float3[] before = [new(0f, 0f, 0.5f), new(2f, 0f, 0.5f), new(2f, 1.5f, 0.5f), new(0f, 1.5f, 0.5f)];
        Float2[] uvs = before.Select(p => CSGSurfaceGeometry.UVAt(data, normal, p, 2f)).ToArray();

        Float4x4 move = Float4x4.CreateTRS(new Float3(3f, -1f, 2f), Quaternion.FromEuler(new Float3(0f, 0f, 0.7f)), mirrored ? new Float3(-1f, 1f, 1f) : Float3.One);
        Float3[] after = before.Select(p => Float4x4.TransformPoint(p, move)).ToArray();
        Float3 moved = Float3.Normalize(Float4x4.TransformNormal(normal, move));

        SurfaceData fitted = CSGSurfaceGeometry.FitUVs(data, moved, after, uvs, 2f);

        Assert.Equal(data.Flags, fitted.Flags);
        for (int i = 0; i < after.Length; i++)
        {
            Float2 uv = CSGSurfaceGeometry.UVAt(fitted, moved, after[i], 2f);
            Assert.Equal(uvs[i].X, uv.X, 3);
            Assert.Equal(uvs[i].Y, uv.Y, 3);
        }
    }

    // ================================================================
    //  Repair
    // ================================================================

    private static CSGControlMesh TwoUnitBox() => CSGControlMesh.FromPlanes(Box(new Float3(2f)))!;

    private static CSGControlMesh Rebuilt(CSGControlMesh mesh, Func<int, bool> keep, IEnumerable<Float3>? extraPoints = null, IEnumerable<int[]>? extraPolygons = null)
    {
        var points = mesh.Points.ToList();
        if (extraPoints != null) points.AddRange(extraPoints);
        var polygons = Enumerable.Range(0, mesh.Polygons.Count).Where(keep).Select(p => mesh.Polygons[p]).ToList();
        if (extraPolygons != null) polygons.AddRange(extraPolygons);
        return CSGControlMesh.FromPolygons(points, polygons);
    }

    private static int Top(CSGControlMesh mesh) => Enumerable.Range(0, mesh.Polygons.Count).First(p => mesh.PolygonNormal(p).Y > 0.99f);

    private static int PointAt(CSGControlMesh mesh, Float3 at) => Enumerable.Range(0, mesh.Points.Count).First(i => Float3.Distance(mesh.Points[i], at) < 1e-4f);

    /// <summary>A brush missing a face is closed by filling the hole, its other faces kept as they were.</summary>
    [Fact]
    public void AnOpenBrushIsRepairedByFillingTheHole()
    {
        CSGControlMesh box = TwoUnitBox();
        int top = Top(box);
        CSGControlMesh open = Rebuilt(box, p => p != top);
        Assert.False(open.IsValid());

        CSGControlMesh? repaired = open.Repaired(out int[] source);

        Assert.NotNull(repaired);
        Assert.True(repaired!.IsValid());
        Assert.Equal(8f, repaired.Volume(), 4);
        Assert.Equal(6, repaired.Polygons.Count);
        Assert.Equal(repaired.Polygons.Count, source.Length);
        Assert.All(source, s => Assert.InRange(s, 0, open.Polygons.Count - 1));
    }

    /// <summary>A flat face sticking out of a brush encloses nothing, so it is clipped off.</summary>
    [Fact]
    public void AFinIsClippedOff()
    {
        CSGControlMesh box = TwoUnitBox();
        int a = PointAt(box, new Float3(-1f, 1f, 1f)), b = PointAt(box, new Float3(1f, 1f, 1f));
        int c = box.Points.Count, d = c + 1;
        CSGControlMesh fin = Rebuilt(box, _ => true, [new Float3(1f, 2f, 1f), new Float3(-1f, 2f, 1f)], [[a, b, c, d]]);
        Assert.False(fin.IsValid());

        CSGControlMesh? repaired = fin.Repaired(out int[] source);

        Assert.NotNull(repaired);
        Assert.True(repaired!.IsValid());
        Assert.Equal(8f, repaired.Volume(), 4);
        Assert.Equal(6, repaired.Polygons.Count);
        Assert.DoesNotContain(box.Polygons.Count, source);
        Assert.Equal(8, repaired.Points.Count);
    }

    /// <summary>A loose sheet inside a brush has solid on both sides, so it is clipped off too.</summary>
    [Fact]
    public void ASheetInsideABrushIsClippedOff()
    {
        CSGControlMesh box = TwoUnitBox();
        int n = box.Points.Count;
        CSGControlMesh sheet = Rebuilt(box, _ => true,
            [new Float3(-0.5f, 0f, -0.5f), new Float3(0.5f, 0f, -0.5f), new Float3(0.5f, 0f, 0.5f), new Float3(-0.5f, 0f, 0.5f)],
            [[n, n + 3, n + 2, n + 1]]);
        Assert.False(sheet.IsValid());

        CSGControlMesh? repaired = sheet.Repaired(out _);

        Assert.NotNull(repaired);
        Assert.Equal(6, repaired!.Polygons.Count);
        Assert.Equal(8f, repaired.Volume(), 4);
    }

    /// <summary>A lone quad encloses nothing however it is repaired.</summary>
    [Fact]
    public void ALoneQuadCannotBeRepaired()
    {
        CSGControlMesh quad = CSGControlMesh.FromPolygons(
            [new Float3(0f, 0f, 0f), new Float3(1f, 0f, 0f), new Float3(1f, 0f, 1f), new Float3(0f, 0f, 1f)],
            [[0, 3, 2, 1]]);

        Assert.Null(quad.Repaired(out _));
    }

    // ================================================================
    //  Check
    // ================================================================

    [Fact]
    public void ABoxHasNoProblem() => Assert.Equal(CSGMeshProblem.None, TwoUnitBox().Check());

    [Fact]
    public void ABoxMissingAFaceIsOpen()
    {
        CSGControlMesh box = TwoUnitBox();
        int top = Top(box);
        Assert.Equal(CSGMeshProblem.Open, Rebuilt(box, p => p != top).Check());
    }

    [Fact]
    public void ABoxTurnedInsideOutIsInsideOut()
    {
        CSGControlMesh box = TwoUnitBox();
        box.ReverseWinding();
        Assert.Equal(CSGMeshProblem.InsideOut, box.Check());
    }

    /// <summary>A corner pushed down through the floor makes faces cross the faces opposite them.</summary>
    [Fact]
    public void ACornerPushedThroughTheFloorFoldsThroughItself()
    {
        CSGControlMesh box = TwoUnitBox();
        int corner = PointAt(box, new Float3(1f, 1f, 1f));
        box.MovePoints([corner], new Float3(-1.5f, -3.5f, -1.5f));
        Assert.Equal(CSGMeshProblem.SelfIntersecting, box.Check());
    }

    [Fact]
    public void APolygonNamingAPointTwiceRepeatsIt()
    {
        CSGControlMesh box = TwoUnitBox();
        int top = Top(box);
        int[] ring = box.Polygons[top];
        Assert.Equal(CSGMeshProblem.RepeatedPoint, Rebuilt(box, p => p != top, extraPolygons: [[ring[0], ring[1], ring[2], ring[2], ring[3]]]).Check());
    }

    /// <summary>Two boxes meeting at one shared corner are closed and solid, but the corner is pinched.</summary>
    [Fact]
    public void TwoBoxesSharingACornerArePinched()
    {
        CSGControlMesh a = TwoUnitBox();
        var points = a.Points.ToList();
        var polygons = a.Polygons.Select(p => (int[])p.Clone()).ToList();

        int shared = PointAt(a, new Float3(1f, 1f, 1f));
        var offset = new Float3(2f, 2f, 2f);
        var map = new int[a.Points.Count];
        for (int i = 0; i < a.Points.Count; i++)
        {
            Float3 moved = a.Points[i] + offset;
            if (Float3.Distance(moved, a.Points[shared]) < 1e-4f) { map[i] = shared; continue; }
            map[i] = points.Count;
            points.Add(moved);
        }
        foreach (int[] ring in a.Polygons) polygons.Add(ring.Select(i => map[i]).ToArray());

        CSGControlMesh pinched = CSGControlMesh.FromPolygons(points, polygons);
        Assert.True(pinched.IsClosed());
        Assert.Equal(CSGMeshProblem.PinchedPoint, pinched.Check());
    }
}
