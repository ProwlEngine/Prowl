using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Referenced Graph node: another graph played as one node, with its parameters linked.</summary>
public class N_SubGraph_Tests
{
    private sealed class FlatGround : IGroundProbe
    {
        public FlatGround(float height) => Height = height;

        public float Height { get; }

        public bool Raycast(Float3 worldOrigin, Float3 worldDirection, float maxDistance, out Float3 worldPoint, out Float3 worldNormal)
        {
            worldPoint = new Float3(worldOrigin.X, Height, worldOrigin.Z);
            worldNormal = new Float3(0f, 1f, 0f);
            return worldOrigin.Y - Height <= maxDistance;
        }
    }

    // A reusable sub-graph: blends two clips by its own "Blend" parameter.
    private static AnimationGraph MakeBlendSubGraph(Skeleton skeleton)
    {
        var sub = new AnimationGraph();
        int blendParam = sub.AddFloatParameter("Blend");
        int a = sub.AddClip(TestClips.Const(skeleton, 0f));
        int b = sub.AddClip(TestClips.Const(skeleton, 10f));
        int blend = sub.AddBlend1D(blendParam, new[] { (a, 0f), (b, 1f) });
        sub.SetRoot(blend);
        return sub;
    }

    [Fact]
    public void SubGraph_SpendsTheParentsTriggerOnceItsTransitionFires()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var child = new AnimationGraph();
        int go = child.AddTriggerParameter("Go");
        int machine = child.AddStateMachine();
        int a = child.AddState(machine, child.AddClip(TestClips.Const(skeleton, 0f)), "A");
        int b = child.AddState(machine, child.AddClip(TestClips.Const(skeleton, 10f)), "B");
        child.AddTransition(machine, a, b, go, duration: 0f);
        child.AddTransition(machine, b, a, go, duration: 0f);
        child.SetStateMachineDefault(machine, a);
        child.SetRoot(machine);

        var parent = new AnimationGraph();
        int parentGo = parent.AddTriggerParameter("Go");
        int sub = parent.AddReferencedGraph(child);
        parent.LinkGraphParameter(sub, parentGo, "Go");
        parent.SetRoot(sub);

        AnimationGraphInstance instance = parent.CreateInstance(skeleton);
        instance.Update(1f / 60f);
        instance.SetBool("Go", true);

        var seen = new List<float>();
        for (int i = 0; i < 4; i++)
        {
            instance.Update(1f / 60f);
            seen.Add(TestClips.RootZ(instance));
        }

        Assert.False(instance.GetBool("Go"));
        Assert.All(seen, z => Assert.Equal(10f, z, 3));
    }

    [Fact]
    public void SubGraph_FindsTheGroundThroughItsParent()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        Pose pose = new(skeleton);
        pose.SetToReferencePose();

        var child = new AnimationGraph();
        child.SetRoot(child.AddNode(new FootGroundingDefinition(child.AddClip(new AnimationClip(skeleton, new[] { pose, pose }, 1f)))
        {
            ProbeGround = true,
            ProbeDistance = 2f,
            ProbeRise = 1f,
        }));

        var parent = new AnimationGraph();
        parent.SetRoot(parent.AddReferencedGraph(child));
        AnimationGraphInstance instance = parent.CreateInstance(avatar);
        instance.Ground = new FlatGround(0.2f);
        instance.Update(1f / 60f);

        instance.Pose.CalculateModelSpaceTransforms();
        int foot = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Assert.Equal(0.2, (double)instance.Pose.GetModelSpaceTransform(foot).position.Y, 2);
    }

    [Fact]
    public void SubGraph_SpendsATriggerItGetsThroughLogic()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var child = new AnimationGraph();
        int go = child.AddTriggerParameter("Go");
        int machine = child.AddStateMachine();
        int a = child.AddState(machine, child.AddClip(TestClips.Const(skeleton, 0f)), "A");
        int b = child.AddState(machine, child.AddClip(TestClips.Const(skeleton, 10f)), "B");
        child.AddTransition(machine, a, b, go, duration: 0f);
        child.AddTransition(machine, b, a, go, duration: 0f);
        child.SetStateMachineDefault(machine, a);
        child.SetRoot(machine);

        var parent = new AnimationGraph();
        int jump = parent.AddTriggerParameter("Jump");
        int forced = parent.AddBoolParameter("Forced");
        int sub = parent.AddReferencedGraph(child);
        parent.LinkGraphParameter(sub, parent.AddOr(jump, forced), "Go");
        parent.SetRoot(sub);

        AnimationGraphInstance instance = parent.CreateInstance(skeleton);
        instance.Update(1f / 60f);
        instance.SetBool("Jump", true);

        var seen = new List<float>();
        for (int i = 0; i < 4; i++)
        {
            instance.Update(1f / 60f);
            seen.Add(TestClips.RootZ(instance));
        }

        Assert.False(instance.GetBool("Jump"));
        Assert.All(seen, z => Assert.Equal(10f, z, 3));
    }

    [Fact]
    public void ReferencedGraph_ForwardsParameterAndOutputsChildPose()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationGraph sub = MakeBlendSubGraph(skeleton);

        var graph = new AnimationGraph();
        int hostBlend = graph.AddFloatParameter("HostBlend");
        int sg = graph.AddReferencedGraph(sub);
        graph.LinkGraphParameter(sg, hostBlend, "Blend");
        graph.SetRoot(sg);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.SetFloat("HostBlend", 0f);
        instance.Update(0.016f);
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetFloat("HostBlend", 1f);
        instance.Update(0.016f);
        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetFloat("HostBlend", 0.5f);
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }
}
