using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour
{
    public readonly struct DtPolyPoint
    {
        public readonly long refs;
        public readonly RcVec3f pt;

        public DtPolyPoint(long polyRefs, RcVec3f polyPt)
        {
            refs = polyRefs;
            pt = polyPt;
        }
    }
}