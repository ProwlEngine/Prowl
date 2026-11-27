/*
recast4j copyright (c) 2021 Piotr Piastucki piotr@jtilia.org
Prowl.Recast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using Prowl.Recast.Core.Collections;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Tests;

public class PolygonByCircleConstraintTest
{
    private readonly IDtPolygonByCircleConstraint _constraint = DtStrictDtPolygonByCircleConstraint.Shared;

    [Fact]
    public void ShouldHandlePolygonFullyInsideCircle()
    {
        float[] polygon = { -2, 0, 2, 2, 0, 2, 2, 0, -2, -2, 0, -2 };
        RcVec3f center = new RcVec3f(1, 0, 1);
        RcFixedArray256<float> constrained = new RcFixedArray256<float>();

        _constraint.Apply(polygon, center, 6, constrained.AsSpan(), out var ncverts);
        Assert.Equal(polygon, constrained.AsSpan().Slice(0, ncverts).ToArray());
    }

    [Fact]
    public void ShouldHandleVerticalSegment()
    {
        int expectedSize = 21;
        float[] polygon = { -2, 0, 2, 2, 0, 2, 2, 0, -2, -2, 0, -2 };
        RcVec3f center = new RcVec3f(2, 0, 0);
        RcFixedArray256<float> constrained = new RcFixedArray256<float>();

        _constraint.Apply(polygon, center, 3, constrained.AsSpan(), out var ncverts);
        Assert.Equal(expectedSize, ncverts);
        Assert.All(new[] { 2f, 0f, 2f, 2f, 0f, -2f }, __e => Assert.Contains(__e, constrained.AsSpan().Slice(0, ncverts).ToArray()));
    }

    [Fact]
    public void ShouldHandleCircleFullyInsidePolygon()
    {
        int expectedSize = 12 * 3;
        float[] polygon = { -4, 0, 0, -3, 0, 3, 2, 0, 3, 3, 0, -3, -2, 0, -4 };
        RcVec3f center = new RcVec3f(-1, 0, -1);
        RcFixedArray256<float> constrained = new RcFixedArray256<float>();

        _constraint.Apply(polygon, center, 2, constrained.AsSpan(), out var ncverts);

        Assert.Equal(expectedSize, ncverts);

        for (int i = 0; i < expectedSize; i += 3)
        {
            float x = constrained[i] + 1;
            float z = constrained[i + 2] + 1;
            Assert.Equal(4, x * x + z * z, 1e-4f);
        }
    }

    [Fact]
    public void ShouldHandleCircleInsidePolygon()
    {
        int expectedSize = 9 * 3;
        float[] polygon = { -4, 0, 0, -3, 0, 3, 2, 0, 3, 3, 0, -3, -2, 0, -4 };
        RcVec3f center = new RcVec3f(-2, 0, -1);
        RcFixedArray256<float> constrained = new RcFixedArray256<float>();

        _constraint.Apply(polygon, center, 3, constrained.AsSpan(), out var ncverts);

        Assert.Equal(expectedSize, ncverts);
        Assert.All(new[] { -2f, 0f, -4f, -4f, 0f, 0f, -3.4641016f, 0.0f, 1.60769534f, -2.0f, 0.0f, 2.0f }, __e => Assert.Contains(__e, constrained.AsSpan().Slice(0, ncverts).ToArray()));
    }

    [Fact]
    public void ShouldHandleCircleOutsidePolygon()
    {
        int expectedSize = 7 * 3;
        float[] polygon = { -4, 0, 0, -3, 0, 3, 2, 0, 3, 3, 0, -3, -2, 0, -4 };
        RcVec3f center = new RcVec3f(4, 0, 0);
        RcFixedArray256<float> constrained = new RcFixedArray256<float>();

        _constraint.Apply(polygon, center, 4, constrained.AsSpan(), out var ncverts);

        Assert.Equal(expectedSize, ncverts);
        Assert.All(new[] { 1.53589869f, 0f, 3f, 2f, 0f, 3f, 3f, 0f, -3f }, __e => Assert.Contains(__e, constrained.AsSpan().Slice(0, ncverts).ToArray()));
    }
}