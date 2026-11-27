using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Zero Pose node.</summary>
public class N_ZeroPose_Tests
{
    [Fact]
    public void ZeroPose_IsIdentity()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddZeroPose());
        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.Update(0.016f);
        Assert.Equal(0.0, (double)i.Pose.GetTransform(0).position.Z, 4);
    }
}
