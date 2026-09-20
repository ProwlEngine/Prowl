using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class StateMachineGraphTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    private static (AnimationGraphInstance Instance, AnimationGraph Graph) BuildIdleWalk(Skeleton skeleton)
    {
        var graph = new AnimationGraph();
        int isWalking = graph.AddBoolParameter("IsWalking");
        int idle = graph.AddClip(ConstClip(skeleton, 0f));
        int walk = graph.AddClip(ConstClip(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int idleState = graph.AddState(sm, idle, "Idle");
        int walkState = graph.AddState(sm, walk, "Walk");
        graph.AddTransition(sm, idleState, walkState, isWalking, duration: 0.2f);
        graph.SetStateMachineDefault(sm, idleState);
        graph.SetRoot(sm);
        return (graph.CreateInstance(skeleton), graph);
    }

    [Fact]
    public void StartsInDefaultState()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        (AnimationGraphInstance instance, _) = BuildIdleWalk(skeleton);
        instance.Update(0.016f);
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void Transition_CrossFadesToTargetState()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        (AnimationGraphInstance instance, _) = BuildIdleWalk(skeleton);

        instance.SetBool("IsWalking", true);
        instance.Update(0.1f); // halfway through the 0.2s transition
        float mid = instance.Pose.GetTransform(0).position.Z;
        Assert.InRange(mid, 1f, 9f); // blending between idle (0) and walk (10)

        for (int i = 0; i < 10; i++)
            instance.Update(0.05f); // finish the transition

        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void TimeInState_FiresTransitionAfterDelay()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int idle = graph.AddClip(ConstClip(skeleton, 0f));
        int walk = graph.AddClip(ConstClip(skeleton, 10f));
        int sm = graph.AddStateMachine();
        int idleState = graph.AddState(sm, idle, "Idle");
        int walkState = graph.AddState(sm, walk, "Walk");
        int afterHalfSecond = graph.AddStateTimeElapsed(sm, 0.5f);
        graph.AddTransition(sm, idleState, walkState, afterHalfSecond, duration: 0f);
        graph.SetStateMachineDefault(sm, idleState);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.1f); // 0.1s in -> still idle
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        for (int i = 0; i < 8; i++)
            instance.Update(0.1f); // well past 0.5s

        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void StateFinished_FiresWhenContentClipCompletes()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int oneShot = graph.AddClip(ConstClip(skeleton, 0f), loop: false); // 1s clamped clip
        int done = graph.AddClip(ConstClip(skeleton, 7f));
        int sm = graph.AddStateMachine();
        int playing = graph.AddState(sm, oneShot, "Playing");
        int finished = graph.AddState(sm, done, "Finished");
        graph.AddTransition(sm, playing, finished, graph.AddStateFinished(sm), duration: 0f);
        graph.SetStateMachineDefault(sm, playing);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.5f); // halfway through the 1s clip -> not finished
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        for (int i = 0; i < 4; i++)
            instance.Update(0.2f); // run the clip past its end

        Assert.Equal(7.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void AlwaysTransition_FiresImmediately()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int a = graph.AddClip(ConstClip(skeleton, 1f));
        int b = graph.AddClip(ConstClip(skeleton, 5f));
        int sm = graph.AddStateMachine();
        int sa = graph.AddState(sm, a);
        int sb = graph.AddState(sm, b);
        graph.AddTransition(sm, sa, sb, conditionNodeIndex: -1, duration: 0f); // always, instant
        graph.SetStateMachineDefault(sm, sa);
        graph.SetRoot(sm);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }
}
