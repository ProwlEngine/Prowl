using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Animation Pose node: a clip sampled at a time a value picks, with no clock of its own.</summary>
public class N_AnimationPose_Tests
{
    [Fact]
    public void AnimationPose_DoesNotTimeABlendItIsPartOf()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int walk = graph.AddClip(TestClips.Ramp(skeleton, duration: 1f));
        int pose = graph.AddAnimationPose(TestClips.Ramp(skeleton, duration: 10f), graph.AddConstFloat(0.5f));
        graph.SetRoot(graph.AddBlend1D(graph.AddConstFloat(0.5f), new[] { (walk, 0f), (pose, 1f) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f);

        Assert.Equal(1f, instance.Duration, 3);
    }

    [Fact]
    public void AnimationPose_SamplesAtValueNodeTime()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int time = g.AddFloatParameter("T", 0.5f);
        g.SetRoot(g.AddAnimationPose(TestClips.Ramp(skeleton), time));
        AnimationGraphInstance i = g.CreateInstance(skeleton);

        i.Update(0.016f);
        Assert.Equal(5.0, (double)i.Pose.GetTransform(0).position.Z, 3);

        i.SetFloat("T", 0.8f);
        i.Update(0.016f);
        Assert.Equal(8.0, (double)i.Pose.GetTransform(0).position.Z, 3);
    }
}
