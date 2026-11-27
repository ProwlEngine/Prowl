using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Toolset.Tools
{
    public class RcCrowdAgentData
    {
        public readonly RcCrowdAgentType type;
        public readonly RcVec3f home = new RcVec3f();

        public RcCrowdAgentData(RcCrowdAgentType type, RcVec3f home)
        {
            this.type = type;
            this.home = home;
        }
    }
}