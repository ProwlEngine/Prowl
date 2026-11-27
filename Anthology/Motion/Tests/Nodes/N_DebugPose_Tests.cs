using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Debug Pose node: hands each frame to an inspector and repairs broken bones.</summary>
public class N_DebugPose_Tests
{
    private static readonly Skeleton s_skeleton = TestSkeletons.MakeChain();

    [Fact]
    public void DebugPose_HandsEachFrameToItsInspector()
    {
        int seen = 0;
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddDebugPose(graph.AddClip(TestClips.Const(s_skeleton, 3f)), _ => seen++));
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        for (int i = 0; i < 5; i++)
            instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(5, seen);
        Assert.Equal(3f, instance.Pose.GetTransform(0).position.Z, 4);
    }

    // One bad solve upstream should not spread through everything downstream.
    [Fact]
    public void DebugPose_PutsABrokenBoneBackToItsReferencePose()
    {
        var poses = new[] { TestClips.At(s_skeleton, 0f) };
        poses[0].SetTransform(1, new Transform3D(new Float3(float.NaN, 0f, 0f), Quaternion.Identity, Float3.One));
        var clip = new AnimationClip(s_skeleton, poses, 1f);

        var graph = new AnimationGraph();
        int debug = graph.AddDebugPose(graph.AddClip(clip));
        graph.SetRoot(debug);
        AnimationGraphInstance instance = graph.CreateInstance(s_skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(s_skeleton.GetBoneParentSpaceTransform(1).position, instance.Pose.GetTransform(1).position);
    }
}
