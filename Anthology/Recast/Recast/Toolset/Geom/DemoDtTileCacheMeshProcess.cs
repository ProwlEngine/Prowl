using Prowl.Recast.Detour;
using Prowl.Recast.Detour.TileCache;
using Prowl.Recast.Geom;
using Prowl.Recast.Toolset.Builder;

namespace Prowl.Recast.Toolset.Geom
{
    public class DemoDtTileCacheMeshProcess : IDtTileCacheMeshProcess
    {
        private IRcInputGeomProvider _geom;

        public DemoDtTileCacheMeshProcess()
        {
        }

        public void Init(IRcInputGeomProvider geom)
        {
            _geom = geom;
        }

        public void Process(DtNavMeshCreateParams option)
        {
            // Update poly flags from areas.
            for (int i = 0; i < option.polyCount; ++i)
            {
                if (option.polyAreas[i] == DtTileCacheBuilder.DT_TILECACHE_WALKABLE_AREA)
                    option.polyAreas[i] = SampleAreaModifications.SAMPLE_POLYAREA_TYPE_GROUND;

                if (option.polyAreas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_GROUND ||
                    option.polyAreas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_GRASS ||
                    option.polyAreas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_ROAD)
                {
                    option.polyFlags[i] = SampleAreaModifications.SAMPLE_POLYFLAGS_WALK;
                }
                else if (option.polyAreas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_WATER)
                {
                    option.polyFlags[i] = SampleAreaModifications.SAMPLE_POLYFLAGS_SWIM;
                }
                else if (option.polyAreas[i] == SampleAreaModifications.SAMPLE_POLYAREA_TYPE_DOOR)
                {
                    option.polyFlags[i] = SampleAreaModifications.SAMPLE_POLYFLAGS_WALK | SampleAreaModifications.SAMPLE_POLYFLAGS_DOOR;
                }
            }

            // Pass in off-mesh connections.
            if (null != _geom)
            {
                var offMeshConnections = _geom.GetOffMeshConnections();
                option.offMeshConVerts = new float[option.offMeshConCount * 6];
                option.offMeshConRad = new float[option.offMeshConCount];
                option.offMeshConDir = new int[option.offMeshConCount];
                option.offMeshConAreas = new int[option.offMeshConCount];
                option.offMeshConFlags = new int[option.offMeshConCount];
                option.offMeshConUserID = new int[option.offMeshConCount];
                option.offMeshConCount = offMeshConnections.Count;
                for (int i = 0; i < option.offMeshConCount; i++)
                {
                    RcOffMeshConnection offMeshCon = offMeshConnections[i];
                    for (int j = 0; j < 6; j++)
                    {
                        option.offMeshConVerts[6 * i + j] = offMeshCon.verts[j];
                    }

                    option.offMeshConRad[i] = offMeshCon.radius;
                    option.offMeshConDir[i] = offMeshCon.bidir ? 1 : 0;
                    option.offMeshConAreas[i] = offMeshCon.area;
                    option.offMeshConFlags[i] = offMeshCon.flags;
                    // option.offMeshConUserID[i] = offMeshCon.userId;
                }
            }
        }
    }
}