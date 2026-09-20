using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class SynchronizedGraphBlendTests
{
    // Bone 0 ramps Z from 0 to 10 across the clip, so its value reports the normalized phase.
    private static AnimationClip Ramp(Skeleton skeleton, float duration)
    {
        var f0 = new Pose(skeleton); f0.SetToReferencePose();
        f0.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));
        var f1 = new Pose(skeleton); f1.SetToReferencePose();
        f1.SetTransform(0, new Transform3D(new Float3(0f, 0f, 10f), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { f0, f1 }, duration);
    }

    [Fact]
    public void Blend1D_PhaseLocksChildrenWithDifferentDurations()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int p = graph.AddFloatParameter("P");
        int a = graph.AddClip(Ramp(skeleton, 1.0f)); // 1s clip
        int b = graph.AddClip(Ramp(skeleton, 2.0f)); // 2s clip (different duration)
        int blend = graph.AddBlend1D(p, new[] { (a, 0f), (b, 1f) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("P", 0.5f);
        instance.Update(0.3f);
        instance.Update(0.3f);

        var aNode = (PoseNodeInstance)instance.GetNodeInstance(a);
        var bNode = (PoseNodeInstance)instance.GetNodeInstance(b);

        // Despite different durations, both children are at the same normalized phase.
        Assert.Equal((double)aNode.NormalizedTime, (double)bNode.NormalizedTime, 4);
        Assert.True(aNode.NormalizedTime > 0.01f); // the shared clock actually advanced

        // The blended (identical) content therefore reads the shared phase.
        Assert.Equal((double)(aNode.NormalizedTime * 10f), (double)instance.Pose.GetTransform(0).position.Z, 3);
    }
}
