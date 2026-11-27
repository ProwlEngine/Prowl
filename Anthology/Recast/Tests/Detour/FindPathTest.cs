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
using System.Collections.Generic;
using Prowl.Recast.Core.Collections;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Tests;

public class FindPathTest : AbstractDetourTest
{
    private static readonly DtStatus[] STATUSES =
    {
        DtStatus.DT_SUCCESS,
        DtStatus.DT_SUCCESS | DtStatus.DT_PARTIAL_RESULT,
        DtStatus.DT_SUCCESS,
        DtStatus.DT_SUCCESS,
        DtStatus.DT_SUCCESS
    };

    private static readonly long[][] RESULTS =
    {
        new[]
        {
            281474976710696L, 281474976710695L, 281474976710694L, 281474976710703L, 281474976710706L,
            281474976710705L, 281474976710702L, 281474976710701L, 281474976710714L, 281474976710713L,
            281474976710712L, 281474976710727L, 281474976710730L, 281474976710717L, 281474976710721L
        },
        new[]
        {
            281474976710773L, 281474976710772L, 281474976710768L, 281474976710754L, 281474976710755L,
            281474976710753L, 281474976710748L, 281474976710752L, 281474976710731L, 281474976710729L,
            281474976710717L, 281474976710724L, 281474976710728L, 281474976710737L, 281474976710738L,
            281474976710736L, 281474976710733L, 281474976710735L, 281474976710742L, 281474976710740L,
            281474976710746L, 281474976710745L, 281474976710744L
        },
        new[]
        {
            281474976710680L, 281474976710684L, 281474976710688L, 281474976710687L, 281474976710686L,
            281474976710697L, 281474976710695L, 281474976710694L, 281474976710703L, 281474976710706L,
            281474976710705L, 281474976710702L, 281474976710701L, 281474976710714L, 281474976710713L,
            281474976710712L, 281474976710727L, 281474976710730L, 281474976710717L, 281474976710729L,
            281474976710731L, 281474976710752L, 281474976710748L, 281474976710753L, 281474976710755L,
            281474976710754L, 281474976710768L, 281474976710772L, 281474976710773L, 281474976710770L,
            281474976710757L, 281474976710761L, 281474976710758L
        },
        new[] { 281474976710753L, 281474976710748L, 281474976710752L, 281474976710731L },
        new[]
        {
            281474976710733L, 281474976710736L, 281474976710738L, 281474976710737L, 281474976710728L,
            281474976710724L, 281474976710717L, 281474976710729L, 281474976710731L, 281474976710752L,
            281474976710748L, 281474976710753L, 281474976710755L, 281474976710754L, 281474976710768L,
            281474976710772L
        }
    };

    private static readonly DtStraightPath[][] STRAIGHT_PATHS =
    {
        new[]
        {
            new DtStraightPath(new RcVec3f(22.606520f, 10.197294f, -45.918674f), 1, 281474976710696L),
            new DtStraightPath(new RcVec3f(3.484785f, 10.197294f, -34.241272f), 0, 281474976710713L),
            new DtStraightPath(new RcVec3f(1.984785f, 10.197294f, -31.241272f), 0, 281474976710712L),
            new DtStraightPath(new RcVec3f(1.984785f, 10.197294f, -29.741272f), 0, 281474976710727L),
            new DtStraightPath(new RcVec3f(2.584784f, 10.197294f, -27.941273f), 0, 281474976710730L),
            new DtStraightPath(new RcVec3f(6.457663f, 10.197294f, -18.334061f), 2, 0L)
        },

        new[]
        {
            new DtStraightPath(new RcVec3f(22.331268f, 10.197294f, -1.040187f), 1, 281474976710773L),
            new DtStraightPath(new RcVec3f(9.784786f, 10.197294f, -2.141273f), 0, 281474976710755L),
            new DtStraightPath(new RcVec3f(7.984783f, 10.197294f, -2.441269f), 0, 281474976710753L),
            new DtStraightPath(new RcVec3f(1.984785f, 10.197294f, -8.441269f), 0, 281474976710752L),
            new DtStraightPath(new RcVec3f(-4.315216f, 10.197294f, -15.341270f), 0, 281474976710724L),
            new DtStraightPath(new RcVec3f(-8.215216f, 10.197294f, -17.441269f), 0, 281474976710728L),
            new DtStraightPath(new RcVec3f(-10.015216f, 10.197294f, -17.741272f), 0, 281474976710738L),
            new DtStraightPath(new RcVec3f(-11.815216f, 9.997294f, -17.441269f), 0, 281474976710736L),
            new DtStraightPath(new RcVec3f(-17.815216f, 5.197294f, -11.441269f), 0, 281474976710735L),
            new DtStraightPath(new RcVec3f(-17.815216f, 5.197294f, -8.441269f), 0, 281474976710746L),
            new DtStraightPath(new RcVec3f(-11.815216f, 0.197294f, 3.008419f), 2, 0L)
        },

        new[]
        {
            new DtStraightPath(new RcVec3f(18.694363f, 15.803535f, -73.090416f), 1, 281474976710680L),
            new DtStraightPath(new RcVec3f(17.584785f, 10.197294f, -49.841274f), 0, 281474976710697L),
            new DtStraightPath(new RcVec3f(17.284786f, 10.197294f, -48.041275f), 0, 281474976710695L),
            new DtStraightPath(new RcVec3f(16.084785f, 10.197294f, -45.341274f), 0, 281474976710694L),
            new DtStraightPath(new RcVec3f(3.484785f, 10.197294f, -34.241272f), 0, 281474976710713L),
            new DtStraightPath(new RcVec3f(1.984785f, 10.197294f, -31.241272f), 0, 281474976710712L),
            new DtStraightPath(new RcVec3f(1.984785f, 10.197294f, -8.441269f), 0, 281474976710753L),
            new DtStraightPath(new RcVec3f(7.984783f, 10.197294f, -2.441269f), 0, 281474976710755L),
            new DtStraightPath(new RcVec3f(9.784786f, 10.197294f, -2.141273f), 0, 281474976710768L),
            new DtStraightPath(new RcVec3f(38.423977f, 10.197294f, -0.116067f), 2, 0L)
        },

        new[]
        {
            new DtStraightPath(new RcVec3f(0.745335f, 10.197294f, -5.940050f), 1, 281474976710753L),
            new DtStraightPath(new RcVec3f(0.863553f, 10.197294f, -10.310320f), 2, 0L)
        },

        new[]
        {
            new DtStraightPath(new RcVec3f(-20.651257f, 5.904126f, -13.712508f), 1, 281474976710733L),
            new DtStraightPath(new RcVec3f(-11.815216f, 9.997294f, -17.441269f), 0, 281474976710738L),
            new DtStraightPath(new RcVec3f(-10.015216f, 10.197294f, -17.741272f), 0, 281474976710728L),
            new DtStraightPath(new RcVec3f(-8.215216f, 10.197294f, -17.441269f), 0, 281474976710724L),
            new DtStraightPath(new RcVec3f(-4.315216f, 10.197294f, -15.341270f), 0, 281474976710729L),
            new DtStraightPath(new RcVec3f(1.984785f, 10.197294f, -8.441269f), 0, 281474976710753L),
            new DtStraightPath(new RcVec3f(7.984783f, 10.197294f, -2.441269f), 0, 281474976710755L),
            new DtStraightPath(new RcVec3f(18.784092f, 10.197294f, 3.054368f), 2, 0L)
        }
    };

    [Fact]
    public void TestFindPath()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        for (int i = 0; i < startRefs.Length; i++)
        {
            long startRef = startRefs[i];
            long endRef = endRefs[i];
            RcVec3f startPos = startPoss[i];
            RcVec3f endPos = endPoss[i];

            RcFixedArray256<long> path = new RcFixedArray256<long>();
            var status = query.FindPath(startRef, endRef, startPos, endPos, filter, path.AsSpan(), out var npath, path.Length);
            Assert.Equal(STATUSES[i], status);
            Assert.Equal(RESULTS[i].Length, npath);
            for (int j = 0; j < RESULTS[i].Length; j++)
            {
                Assert.Equal(RESULTS[i][j], path[j]);
            }
        }
    }

    [Fact]
    public void TestFindPathSliced()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        for (int i = 0; i < startRefs.Length; i++)
        {
            long startRef = startRefs[i];
            long endRef = endRefs[i];
            var startPos = startPoss[i];
            var endPos = endPoss[i];
            query.InitSlicedFindPath(startRef, endRef, startPos, endPos, filter, DtFindPathOptions.DT_FINDPATH_ANY_ANGLE);
            DtStatus status = DtStatus.DT_IN_PROGRESS;
            while (status.InProgress())
            {
                status = query.UpdateSlicedFindPath(10, out var _);
            }

            RcFixedArray256<long> path = new RcFixedArray256<long>();
            status = query.FinalizeSlicedFindPath(path.AsSpan(), out var npath, path.Length);
            Assert.Equal(STATUSES[i], status);
            Assert.Equal(RESULTS[i].Length, npath);
            for (int j = 0; j < RESULTS[i].Length; j++)
            {
                Assert.Equal(RESULTS[i][j], path[j]);
            }
        }
    }

    [Fact]
    public void TestFindPathStraight()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        for (int i = 0; i < STRAIGHT_PATHS.Length; i++)
        {
            // startRefs.Length; i++) {
            long startRef = startRefs[i];
            long endRef = endRefs[i];
            var startPos = startPoss[i];
            var endPos = endPoss[i];

            RcFixedArray256<long> path = new RcFixedArray256<long>();
            RcFixedArray256<DtStraightPath> straightPath = new RcFixedArray256<DtStraightPath>();

            var status = query.FindPath(startRef, endRef, startPos, endPos, filter, path.AsSpan(), out var npath, path.Length);
            query.FindStraightPath(startPos, endPos, path.AsSpan(), npath, straightPath.AsSpan(), out var nstraightPath, straightPath.Length, 0);
            Assert.Equal(STRAIGHT_PATHS[i].Length, nstraightPath);
            for (int j = 0; j < STRAIGHT_PATHS[i].Length; j++)
            {
                Assert.Equal(STRAIGHT_PATHS[i][j].refs, straightPath[j].refs);
                Assert.Equal(STRAIGHT_PATHS[i][j].pos.X, straightPath[j].pos.X, 0.01f);
                Assert.Equal(STRAIGHT_PATHS[i][j].pos.Y, straightPath[j].pos.Y, 0.01f);
                Assert.Equal(STRAIGHT_PATHS[i][j].pos.Z, straightPath[j].pos.Z, 0.01f);
                Assert.Equal(STRAIGHT_PATHS[i][j].flags, straightPath[j].flags);
            }
        }
    }
}