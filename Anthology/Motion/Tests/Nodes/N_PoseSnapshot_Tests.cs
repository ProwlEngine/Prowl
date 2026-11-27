using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Pose Snapshot node: freezing the pose while held.</summary>
public class N_PoseSnapshot_Tests
{
    private static Skeleton Rig() => TestSkeletons.MakeChain();

    private static float RootZ(AnimationGraphInstance instance) => instance.Pose.GetTransform(0).position.Z;

    private static void Run(AnimationGraphInstance instance, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

    [Fact]
    public void Snapshot_HoldsThePoseWhileHeld()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int hold = graph.AddBoolParameter("Hold");
        graph.SetRoot(graph.AddPoseSnapshot(graph.AddClip(TestClips.Ramp(skeleton, endZ: 10f, duration: 1f), loop: false), hold));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 15, 1f / 30f);
        float frozen = RootZ(instance);
        instance.SetBool("Hold", true);
        Run(instance, 10, 1f / 30f);

        Assert.Equal(frozen, RootZ(instance), 3);

        instance.SetBool("Hold", false);
        Run(instance, 15, 1f / 30f);
        Assert.True(RootZ(instance) > frozen, "the clip did not resume after the hold");
    }
}
