using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class PoseTests
{
    [Fact]
    public void NewPose_IsUnset()
        => Assert.Equal(PoseState.Unset, new Pose(TestSkeletons.MakeChain()).State);

    [Fact]
    public void SetToReferencePose_SetsState()
    {
        var p = new Pose(TestSkeletons.MakeChain());
        p.SetToReferencePose();
        Assert.Equal(PoseState.ReferencePose, p.State);
    }

    [Fact]
    public void SetTransform_PromotesToPoseState()
    {
        var p = new Pose(TestSkeletons.MakeChain());
        p.SetTransform(0, Transform3D.Identity);
        Assert.Equal(PoseState.Pose, p.State);
    }

    [Fact]
    public void CalculateModelSpace_AccumulatesDownChain()
    {
        var p = new Pose(TestSkeletons.MakeChain());
        p.SetToReferencePose();
        p.CalculateModelSpaceTransforms();
        Assert.True(p.HasModelSpaceTransforms);
        Assert.Equal(2.0, (double)p.GetModelSpaceTransform(2).position.Y, 4);
    }

    [Fact]
    public void Reset_ReturnsToUnset()
    {
        var p = new Pose(TestSkeletons.MakeChain());
        p.SetToReferencePose();
        p.Reset();
        Assert.Equal(PoseState.Unset, p.State);
    }
}
