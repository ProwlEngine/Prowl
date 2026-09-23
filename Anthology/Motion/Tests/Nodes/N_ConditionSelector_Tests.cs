using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Condition Selector node: plays the first child whose condition holds.</summary>
public class N_ConditionSelector_Tests
{
    [Fact]
    public void ConditionSelector_PicksFirstTrue()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int cond = g.AddBoolParameter("UseA");
        int a = g.AddClip(TestClips.Const(skeleton, 1f));
        int b = g.AddClip(TestClips.Const(skeleton, 5f));
        g.SetRoot(g.AddConditionSelector(new[] { (a, cond), (b, -1) }));
        AnimationGraphInstance i = g.CreateInstance(skeleton);

        i.Update(0.016f); // UseA false -> fallback to B
        Assert.Equal(5.0, (double)i.Pose.GetTransform(0).position.Z, 3);

        i.SetBool("UseA", true);
        i.Update(0.016f);
        Assert.Equal(1.0, (double)i.Pose.GetTransform(0).position.Z, 3);
    }
}
