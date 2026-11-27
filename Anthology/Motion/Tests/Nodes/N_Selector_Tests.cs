using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Selector node: playing whichever child an integer picks.</summary>
public class N_Selector_Tests
{
    private static AnimationClip ConstClip(Skeleton skeleton, int bone, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(bone, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void Selector_PicksChildByInt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int which = graph.AddIntParameter("Which");
        int a = graph.AddClip(ConstClip(skeleton, 0, 1f));
        int b = graph.AddClip(ConstClip(skeleton, 0, 2f));
        int c = graph.AddClip(ConstClip(skeleton, 0, 3f));
        int selector = graph.AddSelector(which, new[] { a, b, c });
        graph.SetRoot(selector);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetInt("Which", 2);
        instance.Update(0.016f);
        Assert.Equal(3.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void Selector_WithNoOptions_FailsValidation()
    {
        var g = new AnimationGraph();
        g.SetRoot(g.AddSelector(g.AddIntParameter("Which"), Array.Empty<int>()));
        Assert.Throws<GraphValidationException>(() => g.CreateInstance(TestSkeletons.MakeChain()));
    }
}
