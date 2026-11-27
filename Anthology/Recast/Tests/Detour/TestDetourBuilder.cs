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

using Prowl.Recast;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.Tests;

public class TestDetourBuilder : DtDetourBuilder
{
    public DtMeshData Build(IRcInputGeomProvider geom, RcBuilderConfig rcConfig, float agentHeight, float agentRadius,
        float agentMaxClimb, int x, int y, bool applyRecastDemoFlags)
    {
        RcBuilder rcBuilder = new RcBuilder();
        RcBuilderResult rcResult = rcBuilder.Build(geom, rcConfig, false);
        RcPolyMesh pmesh = rcResult.Mesh;

        if (applyRecastDemoFlags)
        {
            // Update poly flags from areas.
            for (int i = 0; i < pmesh.npolys; ++i)
            {
                if (pmesh.areas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_GROUND
                    || pmesh.areas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_GRASS
                    || pmesh.areas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_ROAD)
                {
                    pmesh.flags[i] = SampleAreaModifications.SAMPLE_POLYFLAGS_WALK;
                }
                else if (pmesh.areas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_WATER)
                {
                    pmesh.flags[i] = SampleAreaModifications.SAMPLE_POLYFLAGS_SWIM;
                }
                else if (pmesh.areas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_DOOR)
                {
                    pmesh.flags[i] = SampleAreaModifications.SAMPLE_POLYFLAGS_WALK
                                     | SampleAreaModifications.SAMPLE_POLYFLAGS_DOOR;
                }

                if (pmesh.areas[i] > 0)
                {
                    pmesh.areas[i]--;
                }
            }
        }

        RcPolyMeshDetail dmesh = rcResult.MeshDetail;
        DtNavMeshCreateParams option = GetNavMeshCreateParams(rcConfig.cfg, pmesh, dmesh, agentHeight, agentRadius,
            agentMaxClimb);
        return Build(option, x, y);
    }

    public DtNavMeshCreateParams GetNavMeshCreateParams(RcConfig rcConfig, RcPolyMesh pmesh, RcPolyMeshDetail dmesh,
        float agentHeight, float agentRadius, float agentMaxClimb)
    {
        DtNavMeshCreateParams option = new DtNavMeshCreateParams();
        option.verts = pmesh.verts;
        option.vertCount = pmesh.nverts;
        option.polys = pmesh.polys;
        option.polyAreas = pmesh.areas;
        option.polyFlags = pmesh.flags;
        option.polyCount = pmesh.npolys;
        option.nvp = pmesh.nvp;
        if (dmesh != null)
        {
            option.detailMeshes = dmesh.meshes;
            option.detailVerts = dmesh.verts;
            option.detailVertsCount = dmesh.nverts;
            option.detailTris = dmesh.tris;
            option.detailTriCount = dmesh.ntris;
        }

        option.walkableHeight = agentHeight;
        option.walkableRadius = agentRadius;
        option.walkableClimb = agentMaxClimb;
        option.bmin = pmesh.bmin;
        option.bmax = pmesh.bmax;
        option.cs = rcConfig.Cs;
        option.ch = rcConfig.Ch;
        option.buildBvTree = true;
        /*
         * option.offMeshConVerts = m_geom->GetOffMeshConnectionVerts();
         * option.offMeshConRad = m_geom->GetOffMeshConnectionRads();
         * option.offMeshConDir = m_geom->GetOffMeshConnectionDirs();
         * option.offMeshConAreas = m_geom->GetOffMeshConnectionAreas();
         * option.offMeshConFlags = m_geom->GetOffMeshConnectionFlags();
         * option.offMeshConUserID = m_geom->GetOffMeshConnectionId();
         * option.offMeshConCount = m_geom->GetOffMeshConnectionCount();
         */
        return option;
    }
}