using System.Collections.Generic;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour.Extras.Jumplink;
using Prowl.Recast.Geom;
using Prowl.Recast.Toolset.Builder;

namespace Prowl.Recast.Toolset.Tools
{
    public class RcJumpLinkBuilderTool : IRcToolable
    {
        private readonly List<DtJumpLink> _links;
        private DtJumpLinkBuilder _annotationBuilder;
        private readonly int _selEdge = -1;

        public RcJumpLinkBuilderTool()
        {
            _links = new List<DtJumpLink>();
        }


        public string GetName()
        {
            return "Annotation Builder";
        }

        public void Clear()
        {
            _annotationBuilder = null;
        }

        public DtJumpLinkBuilder GetAnnotationBuilder()
        {
            return _annotationBuilder;
        }

        public int GetSelEdge()
        {
            return _selEdge;
        }

        public List<DtJumpLink> GetLinks()
        {
            return _links;
        }

        public void Build(IRcInputGeomProvider geom, RcNavMeshBuildSettings settings, IList<RcBuilderResult> results, RcJumpLinkBuilderToolConfig cfg)
        {
            if (_annotationBuilder == null)
            {
                if (0 < results.Count)
                {
                    _annotationBuilder = new DtJumpLinkBuilder(results);
                }
            }

            _links.Clear();
            if (_annotationBuilder != null)
            {
                float cellSize = settings.cellSize;
                float agentHeight = settings.agentHeight;
                float agentRadius = settings.agentRadius;
                float agentClimb = settings.agentMaxClimb;
                float cellHeight = settings.cellHeight;

                if ((cfg.buildTypes & DtJumpLinkType.EDGE_CLIMB_DOWN.Bit) != 0)
                {
                    DtJumpLinkBuilderConfig config = new DtJumpLinkBuilderConfig(
                        cellSize,
                        cellHeight,
                        agentRadius,
                        agentHeight,
                        agentClimb,
                        cfg.groundTolerance,
                        -agentRadius * 0.2f,
                        cellSize + 2 * agentRadius + cfg.climbDownDistance,
                        -cfg.climbDownMaxHeight,
                        -cfg.climbDownMinHeight,
                        0
                    );
                    _links.AddRange(_annotationBuilder.Build(config, DtJumpLinkType.EDGE_CLIMB_DOWN));
                }

                if ((cfg.buildTypes & DtJumpLinkType.EDGE_JUMP.Bit) != 0)
                {
                    DtJumpLinkBuilderConfig config = new DtJumpLinkBuilderConfig(
                        cellSize,
                        cellHeight,
                        agentRadius,
                        agentHeight,
                        agentClimb,
                        cfg.groundTolerance,
                        -agentRadius * 0.2f,
                        cfg.edgeJumpEndDistance,
                        -cfg.edgeJumpDownMaxHeight,
                        cfg.edgeJumpUpMaxHeight,
                        cfg.edgeJumpHeight
                    );
                    _links.AddRange(_annotationBuilder.Build(config, DtJumpLinkType.EDGE_JUMP));
                }

                if (cfg.buildOffMeshConnections)
                {
                    int area = SampleAreaModifications.SAMPLE_POLYAREA_TYPE_JUMP_AUTO;
                    geom.RemoveOffMeshConnections(c => c.area == area);
                    _links.ForEach(l => AddOffMeshLink(l, geom, agentRadius));
                }
            }
        }

        private void AddOffMeshLink(DtJumpLink link, IRcInputGeomProvider geom, float agentRadius)
        {
            int area = SampleAreaModifications.SAMPLE_POLYAREA_TYPE_JUMP_AUTO;
            int flags = SampleAreaModifications.SAMPLE_POLYFLAGS_JUMP;
            RcVec3f prev = new RcVec3f();
            for (int i = 0; i < link.startSamples.Length; i++)
            {
                RcVec3f p = link.startSamples[i].p;
                RcVec3f q = link.endSamples[i].p;
                if (i == 0 || RcVec.Dist2D(prev, p) > agentRadius)
                {
                    geom.AddOffMeshConnection(p, q, agentRadius, false, area, flags);
                    prev = p;
                }
            }
        }
    }
}