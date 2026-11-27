using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Crowd.Tests;

/// Direct control over an agent already in the crowd: teleporting it and installing a path.
public class DtCrowdAgentControlTest : AbstractCrowdTest
{
    private DtCrowdAgent AddOne() => crowd.AddAgent(startPoss[0], GetAgentParams(0, 0));

    [Fact]
    public void WarpAgent_PlacesTheAgentAndKeepsTheObject()
    {
        DtCrowdAgent agent = AddOne();
        SetMoveTarget(endPoss[0], false);
        crowd.Update(0.1f, null);

        Assert.True(crowd.WarpAgent(agent, endPoss[0]));

        Assert.Same(agent, crowd.GetAgent(agent.idx));
        Assert.True(RcVec.Dist2D(agent.npos, endPoss[0]) < 1f);
        Assert.Equal(DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING, agent.state);
        Assert.Equal(DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE, agent.targetState);
        Assert.Equal(0, agent.targetRef);
        Assert.Equal(RcVec3f.Zero, agent.vel);
        Assert.Equal(RcVec3f.Zero, agent.dvel);
        Assert.Equal(0, agent.ncorners);

        // The corridor has to move with it. Left behind, the agent steps from its old polygon the
        // moment it is given a target again.
        Assert.Equal(endRefs[0], agent.corridor.GetFirstPoly());
        Assert.True(RcVec.Dist2D(agent.corridor.GetPos(), endPoss[0]) < 1f);
    }

    [Fact]
    public void WarpAgent_OnAnAgentOfAnotherCrowd_ReturnsFalse()
    {
        DtCrowdAgent agent = AddOne();
        var other = new DtCrowd(new DtCrowdConfig(0.6f), navmesh);

        Assert.False(other.WarpAgent(agent, endPoss[0]));
    }

    [Fact]
    public void WarpAgent_OffTheNavMesh_ReturnsFalseAndLeavesTheAgent()
    {
        DtCrowdAgent agent = AddOne();
        RcVec3f before = agent.npos;

        Assert.False(crowd.WarpAgent(agent, new RcVec3f(10000f, 10000f, 10000f)));

        Assert.Equal(before, agent.npos);
    }

    /// A live animation lerps npos from the link's endpoints, so one left running drags the agent
    /// off the position it was warped to.
    [Fact]
    public void WarpAgent_ClearsAnActiveLinkAnimation()
    {
        DtCrowdAgent agent = AddOne();
        DtCrowdAgentAnimation anim = agent.animation;
        anim.active = true;
        anim.t = 0f;
        anim.tmax = 10f;
        anim.initPos = startPoss[0];
        anim.startPos = startPoss[0];
        anim.endPos = startPoss[1];
        agent.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;

        Assert.True(crowd.WarpAgent(agent, endPoss[0]));
        Assert.False(anim.active);
        Assert.Equal(DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING, agent.state);

        crowd.Update(0.1f, null);
        Assert.True(RcVec.Dist2D(agent.npos, endPoss[0]) < 1f);
    }

    [Fact]
    public void SetAgentPath_HonoursThePartialFlag()
    {
        DtCrowdAgent agent = AddOne();
        long[] path = [startRefs[0]];

        Assert.True(crowd.SetAgentPath(agent, startRefs[0], startPoss[0], path, 1, true));
        Assert.True(agent.partial);

        Assert.True(crowd.SetAgentPath(agent, startRefs[0], startPoss[0], path, 1, false));
        Assert.False(agent.partial);
    }
}
