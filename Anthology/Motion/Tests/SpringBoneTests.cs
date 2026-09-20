using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Secondary motion on a bone chain: lag, swing, settling and limits.</summary>
public class SpringBoneTests
{
    private static readonly StringID Root = new("Root");
    private static readonly StringID Hair1 = new("Hair1");
    private static readonly StringID Hair2 = new("Hair2");

    // Root with a two bone chain hanging up the Y axis.
    private static Skeleton Rig()
    {
        var ids = new[] { Root, Hair1, Hair2 };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1 };
        var pose = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 0.5f, 0f), Quaternion.Identity, Float3.One),
        };
        return new Skeleton(ids, parents, pose);
    }

    private static (AnimationGraphInstance Instance, Skeleton Skeleton) Build(Action<SpringBonesDefinition>? configure = null)
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        var definition = new SpringBonesDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), new[] { Hair1, Hair2 });
        configure?.Invoke(definition);
        graph.SetRoot(graph.AddNode(definition));
        return (graph.CreateInstance(skeleton), skeleton);
    }

    private static Float3 Tip(AnimationGraphInstance instance, Skeleton skeleton)
    {
        instance.Pose.CalculateModelSpaceTransforms();
        return instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Hair2)).position;
    }

    private static void Run(AnimationGraphInstance instance, int frames, Transform3D world, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, world);
    }

    [Fact]
    public void AStillCharacterKeepsTheAnimatedPose()
    {
        (AnimationGraphInstance instance, Skeleton skeleton) = Build();

        Run(instance, 30, Transform3D.Identity);

        Assert.Equal(1.5f, Tip(instance, skeleton).Y, 3);
        Assert.Equal(0f, Tip(instance, skeleton).Z, 3);
    }

    // Moving the character should leave the chain behind before it catches up.
    [Fact]
    public void MovingTheCharacter_DragsTheChainBehind()
    {
        (AnimationGraphInstance instance, Skeleton skeleton) = Build(d => d.Damping = 2f);

        Run(instance, 5, Transform3D.Identity);
        for (int i = 1; i <= 10; i++)
            instance.Update(1f / 60f, new Transform3D(new Float3(0f, 0f, i * 0.05f), Quaternion.Identity, Float3.One));

        Assert.True(Tip(instance, skeleton).Z < -0.01f, $"the chain did not lag, tip Z {Tip(instance, skeleton).Z:N4}");
    }

    [Fact]
    public void AfterTheCharacterStops_TheChainSettlesBack()
    {
        (AnimationGraphInstance instance, Skeleton skeleton) = Build(d => d.Damping = 8f);

        Run(instance, 5, Transform3D.Identity);
        for (int i = 1; i <= 10; i++)
            instance.Update(1f / 60f, new Transform3D(new Float3(0f, 0f, i * 0.05f), Quaternion.Identity, Float3.One));

        var resting = new Transform3D(new Float3(0f, 0f, 0.5f), Quaternion.Identity, Float3.One);
        Run(instance, 240, resting);

        Assert.Equal(0f, Tip(instance, skeleton).Z, 2);
        Assert.Equal(1.5f, Tip(instance, skeleton).Y, 2);
    }

    // A chain standing straight up along gravity is balanced, so the pull has to come from the side.
    [Fact]
    public void GravityPullsTheChainOver()
    {
        (AnimationGraphInstance instance, Skeleton skeleton) = Build(d =>
        {
            d.Gravity = new Float3(0f, 0f, -30f);
            d.Stiffness = 5f;
            d.MaxAngleDegrees = 0f;
        });

        Run(instance, 200, Transform3D.Identity);

        Float3 tip = Tip(instance, skeleton);
        Assert.True(tip.Z < -0.1f, $"gravity did not bend the chain, tip {tip}");
        Assert.True(tip.Y < 1.5f, $"the chain bent without lowering the tip, tip {tip}");
    }

    [Fact]
    public void TheAngleLimitCapsTheSwing()
    {
        (AnimationGraphInstance instance, Skeleton skeleton) = Build(d =>
        {
            d.Gravity = new Float3(0f, 0f, -60f);
            d.Stiffness = 1f;
            d.MaxAngleDegrees = 20f;
        });

        Run(instance, 200, Transform3D.Identity);

        instance.Pose.CalculateModelSpaceTransforms();
        Float3 direction = Float3.Normalize(Tip(instance, skeleton) - instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Hair1)).position);
        float angle = MathF.Acos(Math.Clamp(Float3.Dot(direction, new Float3(0f, 1f, 0f)), -1f, 1f)) * 180f / MathF.PI;

        Assert.InRange(angle, 0f, 20.5f);
    }

    [Fact]
    public void AZeroWeight_LeavesTheAnimationUntouched()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int weight = graph.AddFloatParameter("Weight", 0f);
        var definition = new SpringBonesDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), new[] { Hair1, Hair2 })
        {
            Gravity = new Float3(0f, 0f, -60f),
            WeightNodeIndex = weight,
        };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 120, Transform3D.Identity);

        Assert.Equal(1.5f, Tip(instance, skeleton).Y, 3);
    }

    // A long jump in one frame is a teleport, not motion, so the chain must not whip across the level.
    [Fact]
    public void ATeleportSnapsTheChainAlong()
    {
        (AnimationGraphInstance instance, Skeleton skeleton) = Build();

        Run(instance, 10, Transform3D.Identity);
        instance.Update(1f / 60f, new Transform3D(new Float3(0f, 0f, 50f), Quaternion.Identity, Float3.One));

        Assert.Equal(1.5f, Tip(instance, skeleton).Y, 3);
        Assert.Equal(0f, Tip(instance, skeleton).Z, 3);
    }

    // A chain read off the rig: name the root and how many bones follow it.
    [Fact]
    public void AChainTakenFromTheRig_MatchesTheSameChainNamedBoneByBone()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        var definition = new SpringBonesDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), Hair1, 2)
        {
            Gravity = new Float3(0f, 0f, -30f),
            Stiffness = 5f,
            MaxAngleDegrees = 0f,
        };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance fromRig = graph.CreateInstance(skeleton);

        (AnimationGraphInstance named, Skeleton namedSkeleton) = Build(d =>
        {
            d.Gravity = new Float3(0f, 0f, -30f);
            d.Stiffness = 5f;
            d.MaxAngleDegrees = 0f;
        });

        Run(fromRig, 120, Transform3D.Identity);
        Run(named, 120, Transform3D.Identity);

        Assert.Equal(Tip(named, namedSkeleton).Z, Tip(fromRig, skeleton).Z, 4);
    }

    // Two nodes stacked: the first holds the upper bones, the second takes the tip with its own feel.
    [Fact]
    public void TwoStackedChains_GiveTheTipItsOwnSettings()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int upper = graph.AddSpringBones(graph.AddClip(TestClips.Const(skeleton, 0f)), Hair1, 1, stiffness: 400f, damping: 20f);
        graph.SetRoot(graph.AddSpringBones(upper, Hair2, 1, stiffness: 2f, damping: 1f, gravity: new Float3(0f, 0f, -30f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 200, Transform3D.Identity);
        instance.Pose.CalculateModelSpaceTransforms();

        // The stiff bone holds its position, while the loose one below it turns away under gravity.
        // The last bone of a chain has no child, so its swing shows in its direction, not its position.
        Assert.Equal(0f, Tip(instance, skeleton).Z, 2);
        Float3 aim = instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Hair2)).rotation * new Float3(0f, 1f, 0f);
        Assert.True(aim.Z < -0.2f, $"the tip did not swing, it aims {aim}");
    }

    [Fact]
    public void AChainRootedOnAMissingBone_ChangesNothing()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddSpringBones(graph.AddClip(TestClips.Const(skeleton, 0f)), new StringID("Nope"), 3, gravity: new Float3(0f, 0f, -60f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 60, Transform3D.Identity);

        Assert.Equal(1.5f, Tip(instance, skeleton).Y, 3);
    }

    [Fact]
    public void ABoneTheRigDoesNotHave_IsSkipped()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        var definition = new SpringBonesDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), new[] { new StringID("Missing"), Hair1 });
        graph.SetRoot(graph.AddNode(definition));

        Exception? error = Record.Exception(() => Run(graph.CreateInstance(skeleton), 5, Transform3D.Identity));

        Assert.Null(error);
    }

    [Fact]
    public void TheSimulationDoesNotAllocateOnceItIsRunning()
    {
        (AnimationGraphInstance instance, Skeleton _) = Build(d => d.Gravity = new Float3(0f, -9.8f, 0f));
        Run(instance, 10, Transform3D.Identity);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Run(instance, 60, Transform3D.Identity);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
