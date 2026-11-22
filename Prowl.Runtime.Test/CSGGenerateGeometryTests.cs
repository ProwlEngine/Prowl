// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the geometry behind draw-then-extrude: which face a ray entered through, the frame a
/// work plane defines, and the box a footprint plus a height describes.
/// </summary>
public class CSGGenerateGeometryTests
{
    private static Plane[] Box(Float3 size, Float3 center = default)
        => GeometryGenerator.BoxPlanes(size, center);

    // ================================================================
    //  Which face was hit
    // ================================================================

    /// <summary>
    /// Drawing on a surface needs the face the ray entered through, not just the distance - that is
    /// what orients the work plane to the wall you clicked.
    /// </summary>
    [Fact]
    public void RaycastReportsTheEnteredFace()
    {
        Plane[] planes = Box(Float3.One * 2f);

        Assert.True(CSGBrushGeometry.Raycast(planes, new Float3(0, 0, -10), Float3.UnitZ, out _, out int plane));

        // The entered face must be the one pointing back at the ray.
        Assert.True(Float3.Dot(planes[plane].Normal, Float3.UnitZ) < 0f);
        Assert.True(Float3.Distance(planes[plane].Normal, -Float3.UnitZ) < 1e-4f);
    }

    [Fact]
    public void RaycastReportsTheFaceForEachDirection()
    {
        Plane[] planes = Box(Float3.One * 2f);

        (Float3 from, Float3 dir, Float3 expected)[] cases =
        {
            (new Float3(-10, 0, 0), Float3.UnitX, -Float3.UnitX),
            (new Float3(10, 0, 0), -Float3.UnitX, Float3.UnitX),
            (new Float3(0, -10, 0), Float3.UnitY, -Float3.UnitY),
            (new Float3(0, 10, 0), -Float3.UnitY, Float3.UnitY),
        };

        foreach ((Float3 from, Float3 dir, Float3 expected) in cases)
        {
            Assert.True(CSGBrushGeometry.Raycast(planes, from, dir, out _, out int plane));
            Assert.True(Float3.Distance(planes[plane].Normal, expected) < 1e-4f,
                $"ray from {from} entered through {planes[plane].Normal}, expected {expected}");
        }
    }

    /// <summary>A ray starting inside has no entry face, and must say so rather than name one.</summary>
    [Fact]
    public void RaycastFromInsideReportsNoEnteredFace()
    {
        Assert.True(CSGBrushGeometry.Raycast(Box(Float3.One * 2f), Float3.Zero, Float3.UnitZ, out _, out int plane));
        Assert.Equal(-1, plane);
    }

    // ================================================================
    //  Work plane frame
    // ================================================================

    /// <summary>The frame's local Y is the surface normal - that is what puts the footprint flat on
    /// the surface and the extrude along it.</summary>
    [Fact]
    public void FrameForPutsTheNormalOnLocalY()
    {
        Float3[] normals =
        {
            Float3.UnitY, -Float3.UnitY, Float3.UnitX, -Float3.UnitZ,
            Float3.Normalize(new Float3(1, 2, 3)),
        };

        foreach (Float3 normal in normals)
        {
            Quaternion frame = CSGBrushGeometry.FrameFor(normal);
            Float3 up = frame * Float3.UnitY;

            Assert.True(Float3.Distance(up, normal) < 1e-4f, $"{normal} became {up}");
        }
    }

    [Fact]
    public void FrameForIsOrthonormal()
    {
        Quaternion frame = CSGBrushGeometry.FrameFor(Float3.Normalize(new Float3(-2, 1, 4)));

        Float3 x = frame * Float3.UnitX, y = frame * Float3.UnitY, z = frame * Float3.UnitZ;

        Assert.Equal(1f, Float3.Length(x), 3);
        Assert.Equal(1f, Float3.Length(z), 3);
        Assert.Equal(0f, Float3.Dot(x, y), 3);
        Assert.Equal(0f, Float3.Dot(y, z), 3);
        Assert.Equal(0f, Float3.Dot(x, z), 3);
    }

    // ================================================================
    //  Footprint to box
    // ================================================================

    [Fact]
    public void BoxFromDragSpansTheFootprintAndHeight()
    {
        Assert.True(CSGBrushGeometry.BoxFromDrag(Float3.Zero, new Float3(4, 0, 6), 3f,
            out Float3 center, out Float3 size));

        Assert.Equal(new Float3(4, 3, 6), size);
        Assert.Equal(new Float3(2, 1.5f, 3), center);
    }

    /// <summary>Dragging back past the start is normal, and must not produce a negative size.</summary>
    [Fact]
    public void BoxFromDragHandlesCornersInAnyOrder()
    {
        Assert.True(CSGBrushGeometry.BoxFromDrag(new Float3(4, 0, 6), Float3.Zero, 3f,
            out Float3 center, out Float3 size));

        Assert.Equal(new Float3(4, 3, 6), size);
        Assert.Equal(new Float3(2, 1.5f, 3), center);
    }

    /// <summary>Pulling the height downward builds a box below the work plane, not an inverted one.</summary>
    [Fact]
    public void NegativeHeightExtrudesBelowThePlane()
    {
        Assert.True(CSGBrushGeometry.BoxFromDrag(Float3.Zero, new Float3(2, 0, 2), -5f,
            out Float3 center, out Float3 size));

        Assert.Equal(5f, size.Y, 4);
        Assert.Equal(-2.5f, center.Y, 4);
    }

    [Fact]
    public void ADragWithNoVolumeIsRejected()
    {
        Assert.False(CSGBrushGeometry.BoxFromDrag(Float3.Zero, new Float3(4, 0, 6), 0f, out _, out _));
        Assert.False(CSGBrushGeometry.BoxFromDrag(Float3.Zero, new Float3(0, 0, 6), 3f, out _, out _));
        Assert.False(CSGBrushGeometry.BoxFromDrag(Float3.Zero, Float3.Zero, 0f, out _, out _));
    }

    /// <summary>
    /// The whole point of the frame: a box drawn on a tilted surface has to land on that surface in
    /// world space, sitting on it rather than through it.
    /// </summary>
    [Fact]
    public void ABoxDrawnOnATiltedSurfaceSitsOnIt()
    {
        Float3 normal = Float3.Normalize(new Float3(0, 1, 1));
        Quaternion frame = CSGBrushGeometry.FrameFor(normal);
        Float3 origin = new(5, 2, -3);

        CSGBrushGeometry.BoxFromDrag(Float3.Zero, new Float3(4, 0, 4), 2f, out Float3 center, out Float3 size);

        Float3 worldCenter = origin + frame * center;

        // Half the height above the surface, measured along its normal.
        Assert.Equal(size.Y * 0.5f, Float3.Dot(worldCenter - origin, normal), 3);

        // And no drift sideways off the point that was drawn on.
        Float3 flat = worldCenter - origin - normal * Float3.Dot(worldCenter - origin, normal);
        Assert.Equal(MathF.Sqrt(2 * 2 + 2 * 2), Float3.Length(flat), 3);
    }

    [Fact]
    public void ARoomIsASolidWithASmallerHoleInside()
    {
        var settings = CSGGeneratorSettings.Default;
        settings.WallThickness = 0.5f;

        List<CSGShapePart> parts = CSGShapeBuilder.Build(CSGGeneratorKind.Room, new Float3(4, 3, 6), settings);

        Assert.Equal(2, parts.Count);
        Assert.Equal(Prowl.Vector.Geometry.CSGOperation.Additive, parts[0].Operation);
        Assert.Equal(Prowl.Vector.Geometry.CSGOperation.Subtractive, parts[1].Operation);

        AABB inner = GeometryGenerator.ConvexFromPlanes(parts[1].Planes).Solid!.GetAABB();
        Assert.Equal(3f, inner.Max.X - inner.Min.X, 3);
        Assert.Equal(2f, inner.Max.Y - inner.Min.Y, 3);
        Assert.Equal(5f, inner.Max.Z - inner.Min.Z, 3);
    }
}
