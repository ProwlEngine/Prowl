using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class ClipDriverTests
{
    // Bone 0 Z ramps 0..10 across the clip, so it reads back the normalized time.
    private static AnimationClip Ramp(Skeleton skeleton)
    {
        var f0 = new Pose(skeleton); f0.SetToReferencePose();
        f0.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));
        var f1 = new Pose(skeleton); f1.SetToReferencePose();
        f1.SetTransform(0, new Transform3D(new Float3(0f, 0f, 10f), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { f0, f1 }, 1f);
    }

    [Fact]
    public void ReverseAndResetDriversControlClipTime()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int reverse = graph.AddBoolParameter("Reverse");
        int reset = graph.AddBoolParameter("Reset");
        int clip = graph.AddClip(Ramp(skeleton), loop: false);
        graph.SetClipDrivers(clip, reverse, reset);
        graph.SetRoot(clip);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.2f);
        instance.Update(0.2f); // t=0.4
        Assert.Equal(4.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetBool("Reverse", true);
        instance.Update(0.2f); // t=0.2 (played backwards)
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetBool("Reverse", false);
        instance.SetBool("Reset", true);
        instance.Update(0.1f); // reset to 0, then advance 0.1
        Assert.Equal(1.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }
}
