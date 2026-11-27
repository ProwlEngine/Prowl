/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
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
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Crowd.Tests;

public abstract class AbstractCrowdTest
{
    protected readonly long[] startRefs =
    {
        281474976710696L, 281474976710773L, 281474976710680L, 281474976710753L,
        281474976710733L
    };

    protected readonly long[] endRefs = { 281474976710721L, 281474976710767L, 281474976710758L, 281474976710731L, 281474976710772L };

    protected readonly RcVec3f[] startPoss =
    {
        new RcVec3f(22.60652f, 10.197294f, -45.918674f),
        new RcVec3f(22.331268f, 10.197294f, -1.0401875f),
        new RcVec3f(18.694363f, 15.803535f, -73.090416f),
        new RcVec3f(0.7453353f, 10.197294f, -5.94005f),
        new RcVec3f(-20.651257f, 5.904126f, -13.712508f),
    };

    protected readonly RcVec3f[] endPoss =
    {
        new RcVec3f(6.4576626f, 10.197294f, -18.33406f),
        new RcVec3f(-5.8023443f, 0.19729415f, 3.008419f),
        new RcVec3f(38.423977f, 10.197294f, -0.116066754f),
        new RcVec3f(0.8635526f, 10.197294f, -10.31032f),
        new RcVec3f(18.784092f, 10.197294f, 3.0543678f),
    };

    protected DtMeshData nmd;
    protected DtNavMeshQuery query;
    protected DtNavMesh navmesh;
    protected DtCrowd crowd;
    protected List<DtCrowdAgent> agents;

    public AbstractCrowdTest()
    {
        nmd = TestMeshDataFactory.Create();
        navmesh = new DtNavMesh();
        navmesh.Init(nmd, 6, 0);
        query = new DtNavMeshQuery(navmesh);
        DtCrowdConfig config = new DtCrowdConfig(0.6f);
        crowd = new DtCrowd(config, navmesh);
        DtObstacleAvoidanceParams option = new DtObstacleAvoidanceParams();
        option.velBias = 0.5f;
        option.adaptiveDivs = 5;
        option.adaptiveRings = 2;
        option.adaptiveDepth = 1;
        crowd.SetObstacleAvoidanceParams(0, option);
        option = new DtObstacleAvoidanceParams();
        option.velBias = 0.5f;
        option.adaptiveDivs = 5;
        option.adaptiveRings = 2;
        option.adaptiveDepth = 2;
        crowd.SetObstacleAvoidanceParams(1, option);
        option = new DtObstacleAvoidanceParams();
        option.velBias = 0.5f;
        option.adaptiveDivs = 7;
        option.adaptiveRings = 2;
        option.adaptiveDepth = 3;
        crowd.SetObstacleAvoidanceParams(2, option);
        option = new DtObstacleAvoidanceParams();
        option.velBias = 0.5f;
        option.adaptiveDivs = 7;
        option.adaptiveRings = 3;
        option.adaptiveDepth = 3;
        crowd.SetObstacleAvoidanceParams(3, option);
        agents = new();
    }

    protected DtCrowdAgentParams GetAgentParams(int updateFlags, int obstacleAvoidanceType)
    {
        DtCrowdAgentParams ap = new DtCrowdAgentParams();
        ap.radius = 0.6f;
        ap.height = 2f;
        ap.maxAcceleration = 8.0f;
        ap.maxSpeed = 3.5f;
        ap.collisionQueryRange = ap.radius * 12f;
        ap.pathOptimizationRange = ap.radius * 30f;
        ap.updateFlags = updateFlags;
        ap.obstacleAvoidanceType = obstacleAvoidanceType;
        ap.separationWeight = 2f;
        return ap;
    }

    protected void AddAgentGrid(int size, float distance, int updateFlags, int obstacleAvoidanceType, RcVec3f startPos)
    {
        DtCrowdAgentParams ap = GetAgentParams(updateFlags, obstacleAvoidanceType);
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                RcVec3f pos = new RcVec3f();
                pos.X = startPos.X + i * distance;
                pos.Y = startPos.Y;
                pos.Z = startPos.Z + j * distance;
                agents.Add(crowd.AddAgent(pos, ap));
            }
        }
    }

    protected void SetMoveTarget(RcVec3f pos, bool adjust)
    {
        RcVec3f ext = crowd.GetQueryExtents();
        IDtQueryFilter filter = crowd.GetFilter(0);
        if (adjust)
        {
            foreach (DtCrowdAgent ag in crowd.GetActiveAgents())
            {
                RcVec3f vel = CalcVel(ag.npos, pos, ag.option.maxSpeed);
                crowd.RequestMoveVelocity(ag, vel);
            }
        }
        else
        {
            query.FindNearestPoly(pos, ext, filter, out var nearestRef, out var nearestPt, out var _);
            foreach (DtCrowdAgent ag in crowd.GetActiveAgents())
            {
                crowd.RequestMoveTarget(ag, nearestRef, nearestPt);
            }
        }
    }

    protected RcVec3f CalcVel(RcVec3f pos, RcVec3f tgt, float speed)
    {
        RcVec3f vel = RcVec3f.Subtract(tgt, pos);
        vel.Y = 0.0f;
        vel = RcVec3f.Normalize(vel);
        vel = vel * speed;
        return vel;
    }

    protected void DumpActiveAgents(int i)
    {
        Console.WriteLine(crowd.GetActiveAgents().Count);
        foreach (DtCrowdAgent ag in crowd.GetActiveAgents())
        {
            Console.WriteLine(ag.state + ", " + ag.targetState);
            Console.WriteLine(ag.npos.X + ", " + ag.npos.Y + ", " + ag.npos.Z);
            Console.WriteLine(ag.nvel.X + ", " + ag.nvel.Y + ", " + ag.nvel.Z);
        }
    }
}