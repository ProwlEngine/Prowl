using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class AdvancedTransitionTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    // Bone 0 Z ramps 0..10 across the clip, reporting normalized time.
    private static AnimationClip Ramp(Skeleton skeleton)
    {
        var f0 = new Pose(skeleton); f0.SetToReferencePose();
        f0.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));
        var f1 = new Pose(skeleton); f1.SetToReferencePose();
        f1.SetTransform(0, new Transform3D(new Float3(0f, 0f, 10f), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { f0, f1 }, 1f);
    }

    [Fact]
    public void EaseIn_ShapesBlendWeight()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int a = graph.AddClip(ConstClip(skeleton, 0f));
        int b = graph.AddClip(ConstClip(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int sa = graph.AddState(sm, a);
        int sb = graph.AddState(sm, b);
        TransitionInfo tr = graph.AddTransition(sm, sa, sb, conditionNodeIndex: -1, duration: 0.2f);
        tr.Easing = TransitionEasing.EaseIn;
        graph.SetStateMachineDefault(sm, sa);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.1f); // raw weight 0.5 -> EaseIn squares it to 0.25 -> z = 2.5
        Assert.Equal(2.5, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void MatchSourceTime_SeedsTargetToSourcePhase()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int go = graph.AddBoolParameter("Go");
        int a = graph.AddClip(Ramp(skeleton));
        int b = graph.AddClip(Ramp(skeleton));
        int sm = graph.AddStateMachine();
        int sa = graph.AddState(sm, a);
        int sb = graph.AddState(sm, b);
        TransitionInfo tr = graph.AddTransition(sm, sa, sb, conditionNodeIndex: go, duration: 0f);
        tr.Sync = TransitionSync.MatchSourceTime;
        graph.SetStateMachineDefault(sm, sa);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        for (int i = 0; i < 5; i++)
            instance.Update(0.1f); // source advances to normalized 0.5

        instance.SetBool("Go", true);
        instance.Update(0.001f); // transition fires; target seeded to ~0.5, not 0
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 1);
    }
}
