using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Crowd.Tests;

/// What the crowd leaves in an agent's steering state when there is nothing valid to steer toward.
/// Both cases used to leave a value that only looked harmless because obstacle avoidance recomputed
/// over it every frame.
public class DtCrowdSteeringStateTest : AbstractCrowdTest
{
    [Fact]
    public void LosingTheMoveTarget_ClearsDesiredVelocityAndStopsTheAgent()
    {
        DtCrowdAgent agent = crowd.AddAgent(startPoss[0], GetAgentParams(0, 0));
        SetMoveTarget(endPoss[0], false);

        // Up to speed first, so there is a desired velocity to leave behind.
        for (int i = 0; i < 10; i++)
            crowd.Update(0.1f, null);
        Assert.True(agent.dvel.Length() > 0.1f, "the agent has to be steering before its target is taken away");

        // The destination polygon is gone and nothing near the old position replaces it, which is
        // what an obstacle carved over the target leaves behind.
        agent.targetRef = 0;
        agent.targetPos = new RcVec3f(10000f, 10000f, 10000f);
        crowd.Update(0.1f, null);

        Assert.Equal(DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE, agent.targetState);
        Assert.Equal(RcVec3f.Zero, agent.dvel);

        // Nothing recomputes dvel once the target is gone, so a stale one is walked forever.
        RcVec3f stoppedAt = agent.npos;
        for (int i = 0; i < 20; i++)
            crowd.Update(0.1f, null);

        Assert.True(agent.vel.Length() < 0.01f, $"still moving at {agent.vel.Length():0.###}");
        float coasted = RcVec.Dist2D(agent.npos, stoppedAt);
        Assert.True(coasted < 1.5f, $"coasted a further {coasted:0.##} after braking");
    }

    /// FindCorners prunes a corner the agent is standing on unless it carries an off-mesh link, and
    /// a zero-length direction normalizes to NaN, which reaches npos through nvel and vel.
    [Fact]
    public void CalcSteerDirection_WithACornerOnTheAgent_IsZeroNotNaN()
    {
        DtCrowdAgent agent = crowd.AddAgent(startPoss[0], GetAgentParams(0, 0));
        agent.ncorners = 1;
        agent.corners[0] = new DtStraightPath(agent.npos, DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION, 0);

        Assert.Equal(RcVec3f.Zero, agent.CalcStraightSteerDirection());
        Assert.Equal(RcVec3f.Zero, agent.CalcSmoothSteerDirection());
    }
}
