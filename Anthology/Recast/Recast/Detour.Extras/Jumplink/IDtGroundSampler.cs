using Prowl.Recast;

namespace Prowl.Recast.Detour.Extras.Jumplink
{
    public interface IDtGroundSampler
    {
        void Sample(DtJumpLinkBuilderConfig acfg, RcBuilderResult result, DtEdgeSampler es);
    }
}