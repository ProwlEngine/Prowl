using Prowl.Vector;

namespace Prowl.Motion.Tests;

public class IKRigTests
{
    [Fact]
    public void Rig_SolvesEffectorToTarget()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        var rig = new IKRig();
        IKEffector arm = rig.AddEffector("arm", new[] { 0, 1, 2 });
        arm.Target = new Float3(1f, 1f, 0f);

        rig.Solve(pose);

        pose.CalculateModelSpaceTransforms();
        Float3 end = pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(end, arm.Target) < 1e-3f);
    }

    [Fact]
    public void Find_ReturnsAddedEffector()
    {
        var rig = new IKRig();
        rig.AddEffector("leg", new[] { 0, 1, 2 });
        Assert.NotNull(rig.Find("leg"));
        Assert.Null(rig.Find("missing"));
    }
}
