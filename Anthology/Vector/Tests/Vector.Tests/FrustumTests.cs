// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Vector.Tests;

/// <summary>
/// Culling asks these once per renderable per camera per frame, so they have to be right and cheap.
/// The frustum looks down +Z from the origin with a 90 degree field of view, near 1 and far 100,
/// which puts the side planes at |x| = z and |y| = z.
/// </summary>
public class FrustumTests
{
    private static readonly Frustum View = Frustum.FromCamera(Float3.Zero, Float3.UnitZ, Float3.UnitY, Maths.PI / 2, 1f, 1f, 100f);

    [Theory]
    [InlineData(0f, 0f, 10f, true)]      // straight ahead
    [InlineData(0f, 0f, 0f, true)]       // straddling the near plane
    [InlineData(0f, 0f, -10f, false)]    // behind the camera
    [InlineData(0f, 0f, 200f, false)]    // past the far plane
    [InlineData(50f, 0f, 10f, false)]    // off to one side
    [InlineData(-50f, 0f, 10f, false)]   // off to the other
    [InlineData(0f, 50f, 10f, false)]    // above
    [InlineData(10.5f, 0f, 10f, true)]   // overlapping a side plane
    public void Boxes(float x, float y, float z, bool expected)
    {
        var box = new AABB(new Float3(x - 1, y - 1, z - 1), new Float3(x + 1, y + 1, z + 1));
        Assert.Equal(expected, View.Intersects(box));
    }

    [Theory]
    [InlineData(0f, 0f, 10f, 1f, true)]
    [InlineData(30f, 0f, 10f, 1f, false)]
    [InlineData(10.5f, 0f, 10f, 2f, true)]
    [InlineData(0f, 0f, -5f, 1f, false)]
    public void Spheres(float x, float y, float z, float radius, bool expected)
    {
        Assert.Equal(expected, View.Intersects(new Sphere(new Float3(x, y, z), radius)));
    }

    [Theory]
    [InlineData(0f, 0f, 10f, true)]
    [InlineData(0f, 0f, 0.5f, false)]
    [InlineData(20f, 0f, 10f, false)]
    [InlineData(0f, 0f, 150f, false)]
    public void Points(float x, float y, float z, bool expected)
    {
        Assert.Equal(expected, View.Contains(new Float3(x, y, z)));
    }
}
