using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Extras.Jumplink
{
    public class DtGroundSegment
    {
        public RcVec3f p;
        public RcVec3f q;
        public DtGroundSample[] gsamples;
        public float height;
    }
}