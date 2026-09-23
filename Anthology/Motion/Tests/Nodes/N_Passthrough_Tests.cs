using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Passthrough node.</summary>
public class N_Passthrough_Tests
{
    [Fact]
    public void Passthrough_ForwardsChild()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int clip = g.AddClip(TestClips.Const(skeleton, 3f));
        g.SetRoot(g.AddPassthrough(clip));
        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.Update(0.016f);
        Assert.Equal(3.0, (double)i.Pose.GetTransform(0).position.Z, 3);
    }
}
