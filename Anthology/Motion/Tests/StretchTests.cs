using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class StretchTests
{
    [Fact]
    public void NoStretch_ClampsToNaturalReach()
    {
        // Chain reach is 2; target at distance 4 with no stretch stays at reach 2.
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(4f, 0f, 0f), stretch: 0f);

        pose.CalculateModelSpaceTransforms();
        float reached = Float3.Distance(pose.GetModelSpaceTransform(0).position, pose.GetModelSpaceTransform(2).position);
        Assert.True(reached < 2.01f, $"reached {reached} should not exceed natural reach 2");
    }

    [Fact]
    public void WithStretch_ExtendsBeyondNaturalReach()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        // 50% stretch allows reach up to 3.
        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(2.8f, 0f, 0f), stretch: 0.5f);

        pose.CalculateModelSpaceTransforms();
        Float3 end = pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(new Float3(2.8f, 0f, 0f), end) < 1e-2f, $"end {end} should reach the stretched target");
    }
}
