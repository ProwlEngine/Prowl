// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

/// <summary>Snapping with the screen as the X and Y of the world, one pixel a unit.</summary>
public class SnappingTests
{
    private static Float2? Screen(Float3 point) => new Float2(point.X, point.Y);

    [Fact]
    public void ACornerNearTheMouseWinsOverAnEdgeNearer()
    {
        Float3[] corners = [new(10, 0, 0)];
        (Float3, Float3)[] edges = [(new Float3(-50, 3, 0), new Float3(50, 3, 0))];

        Assert.True(Snapping.Find(corners, edges, null, Screen, new Float2(0, 0), out Float3 point));

        Assert.Equal(new Float3(10, 0, 0), point);
    }

    [Fact]
    public void AnEdgeSnapsToItsNearestPointOrItsMiddleWhenClose()
    {
        (Float3, Float3)[] edges = [(new Float3(0, 0, 0), new Float3(100, 0, 0))];

        Assert.True(Snapping.Find([], edges, null, Screen, new Float2(20, 5), out Float3 along));
        Assert.Equal(new Float3(20, 0, 0), along);

        Assert.True(Snapping.Find([], edges, null, Screen, new Float2(46, 3), out Float3 middle));
        Assert.Equal(new Float3(50, 0, 0), middle);
    }

    [Fact]
    public void TheFaceUnderTheMouseIsTheLastChoice()
    {
        Float3 hit = new(5, 5, -3);

        Assert.True(Snapping.Find([new Float3(200, 200, 0)], [], hit, Screen, new Float2(5, 5), out Float3 point));
        Assert.Equal(hit, point);

        Assert.False(Snapping.Find([new Float3(200, 200, 0)], [], null, Screen, new Float2(5, 5), out _));
    }
}

public class MeshPickingTests
{
    [Fact]
    public void ARayFromAboveHitsTheTopOfAMovedBox()
    {
        GeometryData box = Prowl.Vector.Geometry.GeometryGenerator.Box(new Float3(2f));
        Float4x4 toWorld = Float4x4.CreateTranslation(new Float3(0, 5, 0));

        Assert.True(MeshPicking.Raycast(box, toWorld, new Ray(new Float3(0.2f, 20, 0.3f), -Float3.UnitY), out GeometryData.Face? face, out float distance));

        Assert.True(face!.Normal().Y > 0.99f);
        Assert.Equal(14f, distance, 3);
        Assert.False(MeshPicking.Raycast(box, toWorld, new Ray(new Float3(5, 20, 0), -Float3.UnitY), out _, out _));
    }
}
