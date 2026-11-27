using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Extras.Jumplink
{
    public interface IDtTrajectory
    {
        RcVec3f Apply(RcVec3f start, RcVec3f end, float u);
    }
}