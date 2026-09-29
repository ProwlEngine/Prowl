// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Vector.Tests;

public class AABBTests
{
    [Fact]
    public void TransformBy_bounds_every_transformed_corner()
    {
        var box = new AABB(new Float3(-1, 0.5f, 2), new Float3(3, 4, 2.5f));
        Float4x4[] matrices =
        [
            Float4x4.Identity,
            Float4x4.RotateZ(0.7f),
            Float4x4.CreateTranslation(new Float3(5, -2, 9)),
            Float4x4.CreateTranslation(new Float3(1, 2, 3)) * Float4x4.RotateZ(-1.3f) * Float4x4.CreateScale(2, 0.5f, 3),
        ];

        foreach (var m in matrices)
        {
            var corners = box.GetCorners();
            Float3 min = Float4x4.TransformPoint(corners[0], m), max = min;
            foreach (var c in corners)
            {
                var p = Float4x4.TransformPoint(c, m);
                min = new Float3(Maths.Min(min.X, p.X), Maths.Min(min.Y, p.Y), Maths.Min(min.Z, p.Z));
                max = new Float3(Maths.Max(max.X, p.X), Maths.Max(max.Y, p.Y), Maths.Max(max.Z, p.Z));
            }

            var result = box.TransformBy(m);
            Assert.Equal(min, result.Min);
            Assert.Equal(max, result.Max);
        }
    }

    [Fact]
    public void TransformBy_a_quarter_turn_of_a_cube_grows_it_by_root_two()
    {
        var box = new AABB(new Float3(-1, -1, -1), new Float3(1, 1, 1));

        var result = box.TransformBy(Float4x4.RotateZ(Maths.PI / 4));

        float r = Maths.Sqrt(2f);
        Assert.Equal(-r, result.Min.X, 4);
        Assert.Equal(r, result.Max.X, 4);
        Assert.Equal(-r, result.Min.Y, 4);
        Assert.Equal(r, result.Max.Y, 4);
        Assert.Equal(-1f, result.Min.Z, 4);
        Assert.Equal(1f, result.Max.Z, 4);
    }
}
