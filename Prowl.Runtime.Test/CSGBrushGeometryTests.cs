// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

    [Fact]
    public void MirrorReflectsAnUnrotatedSolid()
    {
        // A box sitting entirely in +X.
        Plane[] planes = Box(new Float3(2, 2, 2), new Float3(3, 0, 0));
        Plane[] mirrored = CSGBrushGeometry.Mirror(planes, Float3.UnitX, Quaternion.Identity);

        Assert.True(Inside(planes, new Float3(3, 0, 0)));
        Assert.False(Inside(planes, new Float3(-3, 0, 0)));

        Assert.True(Inside(mirrored, new Float3(-3, 0, 0)));
        Assert.False(Inside(mirrored, new Float3(3, 0, 0)));
    }

    private static Float3 Reflect(Float3 v, Float3 axis) => v - axis * (2f * Float3.Dot(v, axis));

    /// <summary>
    /// The contract for a rotated brush, stated in world space: a local point of the mirrored solid
    /// is where the original point lands after being reflected in world space. The angle is
    /// deliberately not a right angle - at 90 degrees, conjugating by the rotation and by its
    /// inverse give the same answer, so a rotation like that cannot catch the two being swapped.
    /// </summary>
    [Fact]
    public void MirrorReflectsARotatedBrushAboutTheWorldAxis()
    {
        Plane[] planes = Box(new Float3(2, 2, 2), new Float3(4, 0, 0));
        var rotation = Quaternion.AxisAngle(Float3.UnitZ, MathF.PI * 0.25f);
        Quaternion inverse = Quaternion.Inverse(rotation);

        Plane[] mirrored = CSGBrushGeometry.Mirror(planes, Float3.UnitX, rotation);

        Float3 point = new(4, 0, 0);
        Assert.True(Inside(planes, point));

        Float3 expected = inverse * Reflect(rotation * point, Float3.UnitX);
        Assert.True(Inside(mirrored, expected));

        // The same reflection with the rotation applied the other way round - the mistake this test
        // exists to catch - lands somewhere else entirely.
        Float3 swapped = rotation * Reflect(inverse * point, Float3.UnitX);
        Assert.False(Inside(mirrored, swapped));
    }

    [Fact]
    public void MirroringTwiceRestoresTheOriginal()
    {
        Plane[] planes = Box(new Float3(2, 4, 6), new Float3(3, 1, -2));
        var rotation = Quaternion.AxisAngle(Float3.UnitZ, 0.7f);

        Plane[] once = CSGBrushGeometry.Mirror(planes, Float3.UnitX, rotation);
        Plane[] twice = CSGBrushGeometry.Mirror(once, Float3.UnitX, rotation);

        for (int i = 0; i < planes.Length; i++)
        {
            Assert.Equal(planes[i].D, twice[i].D, 4);
            Assert.True(Float3.Distance(planes[i].Normal, twice[i].Normal) < 1e-4f);
        }
    }

    /// <summary>A mirrored plane set must still build a solid, not an inside-out one.</summary>
    [Fact]
    public void AMirroredBrushIsStillASolid()
    {
        Plane[] mirrored = CSGBrushGeometry.Mirror(Box(new Float3(2, 3, 4)), Float3.UnitY, Quaternion.Identity);

        ConvexSolidResult result = GeometryGenerator.ConvexFromPlanes(mirrored);

        Assert.Equal(ConvexSolidStatus.Valid, result.Status);
        Assert.Equal(6, result.Solid!.Faces.Count);
    }

    // ================================================================
    //  Pivot
    // ================================================================

    /// <summary>
    /// Centring a pivot moves the planes by the negated centroid and the transform by the same
    /// amount the other way, so the solid stays where it was in world space.
    /// </summary>
    [Fact]
    public void TranslatingByTheNegatedCentroidCentresTheSolid()
    {
        Plane[] planes = Box(Float3.One * 2f, new Float3(5, -3, 2));
        Float3 centroid = CSGBrushGeometry.Centroid(Solid(planes));

        Assert.Equal(5f, centroid.X, 3);
        Assert.Equal(-3f, centroid.Y, 3);

        Plane[] centred = CSGBrushGeometry.Translate(planes, -centroid);

        Assert.True(Inside(centred, Float3.Zero));
        Assert.Equal(0f, Float3.Length(CSGBrushGeometry.Centroid(Solid(centred))), 3);
    }

    [Fact]
    public void TranslateMovesTheSolidByExactlyTheOffset()
    {
        Plane[] planes = Box(Float3.One * 2f);
        Plane[] moved = CSGBrushGeometry.Translate(planes, new Float3(10, 0, 0));

        Assert.True(Inside(moved, new Float3(10, 0, 0)));
        Assert.False(Inside(moved, Float3.Zero));
    }

    [Fact]
    public void BoundsCoverTheSolid()
    {
        AABB bounds = CSGBrushGeometry.Bounds(Solid(Box(new Float3(2, 4, 6))));

        Assert.Equal(new Float3(-1, -2, -3), bounds.Min);
        Assert.Equal(new Float3(1, 2, 3), bounds.Max);
    }

    [Fact]
    public void MirrorThroughAScaledRotatedTransformReflectsInWorld()
    {
        Plane[] planes = Box(new Float3(2, 1, 1), new Float3(0.5f, 0, 0));
        Float4x4 toWorld = Float4x4.CreateTRS(Float3.Zero, Quaternion.AxisAngle(Float3.UnitY, 0.7f), new Float3(3, 1, 1));

        Plane[] mirrored = CSGBrushGeometry.Mirror(planes, Float3.UnitX, toWorld);

        Float4x4 toLocal = toWorld.Invert();
        foreach (GeometryData.Vertex v in Solid(planes).Vertices)
        {
            Float3 world = Float4x4.TransformPoint(v.Point, toWorld);
            Float3 reflected = new(-world.X, world.Y, world.Z);
            Float3 back = Float4x4.TransformPoint(reflected, toLocal);
            Assert.True(Inside(mirrored, back + (Float3.Zero - back) * 1e-3f), $"{back} not on the mirrored solid");
        }
    }
}
