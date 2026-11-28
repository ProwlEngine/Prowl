using System;

using Prowl.Recast;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Extras.Jumplink;

public class DtNavMeshGroundSampler : DtAbstractGroundSampler
{
    public override void Sample(DtJumpLinkBuilderConfig acfg, RcBuilderResult result, DtEdgeSampler es)
    {
        DtNavMeshQuery navMeshQuery = CreateNavMesh(result, acfg.agentRadius, acfg.agentHeight, acfg.agentClimb);
        SampleGround(acfg, es, (RcVec3f pt, float heightRange, out float height) => GetNavMeshHeight(navMeshQuery, pt, acfg.cellSize, heightRange, out height));
    }

    private static DtNavMeshQuery CreateNavMesh(RcBuilderResult r, float agentRadius, float agentHeight, float agentClimb)
    {
        DtNavMeshCreateParams option = new DtNavMeshCreateParams
        {
            verts = r.Mesh.verts,
            vertCount = r.Mesh.nverts,
            polys = r.Mesh.polys,
            polyAreas = r.Mesh.areas,
            polyFlags = r.Mesh.flags,
            polyCount = r.Mesh.npolys,
            nvp = r.Mesh.nvp,
            detailMeshes = r.MeshDetail.meshes,
            detailVerts = r.MeshDetail.verts,
            detailVertsCount = r.MeshDetail.nverts,
            detailTris = r.MeshDetail.tris,
            detailTriCount = r.MeshDetail.ntris,
            walkableRadius = agentRadius,
            walkableHeight = agentHeight,
            walkableClimb = agentClimb,
            bmin = r.Mesh.bmin,
            bmax = r.Mesh.bmax,
            cs = r.Mesh.cs,
            ch = r.Mesh.ch,
            buildBvTree = true
        };
        var mesh = new DtNavMesh();
        DtStatus status = mesh.Init(DtNavMeshBuilder.CreateNavMeshData(option), option.nvp, 0);
        if (status.Failed())
        {
            return null;
        }

        return new DtNavMeshQuery(mesh);
    }


    private bool GetNavMeshHeight(DtNavMeshQuery navMeshQuery, RcVec3f pt, float cs, float heightRange, out float height)
    {
        height = 0;

        RcVec3f halfExtents = new RcVec3f(cs, heightRange, cs);
        float maxHeight = pt.Y + heightRange;
        RcAtomicBoolean found = new RcAtomicBoolean();
        RcAtomicFloat minHeight = new RcAtomicFloat(pt.Y);

        void UpdateMinHeight(DtMeshTile tile, DtPoly poly, long refs)
        {
            DtStatus status = navMeshQuery.GetPolyHeight(refs, pt, out float h);
            if (status.Succeeded())
            {
                if (h > minHeight.Get() && h < maxHeight)
                {
                    minHeight.Exchange(h);
                    found.Set(true);
                }
            }
        }

        DtCallbackPolyQuery query = new DtCallbackPolyQuery(UpdateMinHeight);

        navMeshQuery.QueryPolygons(pt, halfExtents, DtQueryNoOpFilter.Shared, ref query);

        if (found.Get())
        {
            height = minHeight.Get();
            return true;
        }

        height = pt.Y;
        return false;
    }
}