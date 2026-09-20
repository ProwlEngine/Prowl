using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class TwoBoneIKTests
{
    // Root(0,0,0) -> Hip(0,1,0) -> Knee(0,2,0): two unit segments, total reach 2.
    private static Pose MakeChainPose()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();
        return pose;
    }

    [Fact]
    public void Solve_ReachesReachableTarget()
    {
        var pose = MakeChainPose();
        var target = new Float3(1f, 1f, 0f); // distance ~1.414 from root, within reach

        TwoBoneIK.Solve(pose, upper: 0, mid: 1, end: 2, target);

        pose.CalculateModelSpaceTransforms();
        Float3 end = pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(end, target) < 1e-3f, $"end {end} did not reach {target}");
    }

    [Fact]
    public void Solve_UnreachableTarget_StraightensTowardIt()
    {
        var pose = MakeChainPose();
        var target = new Float3(0f, 10f, 0f); // far beyond reach of 2

        TwoBoneIK.Solve(pose, 0, 1, 2, target);

        pose.CalculateModelSpaceTransforms();
        Float3 end = pose.GetModelSpaceTransform(2).position;
        // Fully extended toward the target => end is ~2 units up the Y axis.
        Assert.Equal(2.0, (double)end.Y, 2);
        Assert.True(Float3.Distance(new Float3(0f, 0f, 0f), end) > 1.99f);
    }

    [Fact]
    public void Solve_PreservesBoneLengths()
    {
        var pose = MakeChainPose();
        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(1.2f, 0.8f, 0.3f));

        pose.CalculateModelSpaceTransforms();
        Float3 a = pose.GetModelSpaceTransform(0).position;
        Float3 b = pose.GetModelSpaceTransform(1).position;
        Float3 c = pose.GetModelSpaceTransform(2).position;
        Assert.Equal(1.0, (double)Float3.Distance(a, b), 3);
        Assert.Equal(1.0, (double)Float3.Distance(b, c), 3);
    }
}

public class ChainIKTests
{
    [Fact]
    public void Solve_ReachesTarget()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        var target = new Float3(1f, 1f, 0.5f);
        ChainIK.Solve(pose, new[] { 0, 1, 2 }, target, iterations: 20);

        Float3 end = pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(end, target) < 1e-2f, $"end {end} did not reach {target}");
    }
}
