/*
recast4j Copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
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

using System;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Tests;

public class FindNearestPolyTest : AbstractDetourTest
{
    private static readonly long[] POLY_REFS =
    {
        281474976710696L, 281474976710773L, 281474976710680L, 281474976710753L, 281474976710733L
    };

    private static readonly RcVec3f[] POLY_POS =
    {
        new RcVec3f(22.606520f, 10.197294f, -45.918674f),
        new RcVec3f(22.331268f, 10.197294f, -1.040187f),
        new RcVec3f(18.694363f, 15.803535f, -73.090416f),
        new RcVec3f(0.745335f, 10.197294f, -5.940050f),
        new RcVec3f(-20.651257f, 5.904126f, -13.712508f)
    };

    [Fact]
    public void TestFindNearestPoly()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        RcVec3f extents = new RcVec3f(2, 4, 2);
        for (int i = 0; i < startRefs.Length; i++)
        {
            RcVec3f startPos = startPoss[i];
            var status = query.FindNearestPoly(startPos, extents, filter, out var nearestRef, out var nearestPt, out var _);
            Assert.True(status.Succeeded(), $"index({i})");
            Assert.Equal(POLY_REFS[i], nearestRef);
            Assert.Equal(POLY_POS[i].X, nearestPt.X, 0.001f);
            Assert.Equal(POLY_POS[i].Y, nearestPt.Y, 0.001f);
            Assert.Equal(POLY_POS[i].Z, nearestPt.Z, 0.001f);
        }
    }


    [Fact]
    public void ShouldReturnStartPosWhenNoPolyIsValid()
    {
        RcVec3f extents = new RcVec3f(2, 4, 2);
        for (int i = 0; i < startRefs.Length; i++)
        {
            RcVec3f startPos = startPoss[i];
            var status = query.FindNearestPoly(startPos, extents, DtQueryEmptyFilter.Shared, out var nearestRef, out var nearestPt, out var _);
            Assert.True(status.Succeeded());
            Assert.Equal(0L, nearestRef);
            Assert.Equal(startPos.X, nearestPt.X, 0.001f);
            Assert.Equal(startPos.Y, nearestPt.Y, 0.001f);
            Assert.Equal(startPos.Z, nearestPt.Z, 0.001f);
        }
    }

    [Fact]
    public void ShouldNotAllocate()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        RcVec3f extents = new RcVec3f(2, 4, 2);
        RcVec3f startPos = startPoss[0];

        for (int i = 0; i < 256; ++i)
        {
            query.FindNearestPoly(startPos, extents, filter, out _, out _, out _);
        }

        long nearestRefSum = 0;
        Span<long> allocatedBytes = stackalloc long[4];
        for (int batch = 0; batch < allocatedBytes.Length; ++batch)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; ++i)
            {
                query.FindNearestPoly(startPos, extents, filter, out var nearestRef, out _, out _);
                nearestRefSum += nearestRef;
            }

            allocatedBytes[batch] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }

        Assert.True(nearestRefSum != 0);
        Assert.All(allocatedBytes.ToArray(), __e => Assert.True(__e == 0));
    }
}
