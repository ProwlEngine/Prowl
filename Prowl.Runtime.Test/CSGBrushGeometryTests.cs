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
}
