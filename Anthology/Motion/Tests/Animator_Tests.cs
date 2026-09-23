using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The animators: the simple clip animator and the graph animator, cross fades and what they push to the engine.</summary>
public class Animator_Tests
{
    private sealed class Player : SimpleAnimator
    {
        public Player(Skeleton skeleton) : base(skeleton) { }
        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) { }
    }

    // A stand-in engine: records the local transform pushed to each bone and offers a flat ground plane.
    private sealed class RecordingSimpleAnimator : SimpleAnimator
    {
        public readonly Transform3D[] Bones;
        public bool HasGround;

        public RecordingSimpleAnimator(Skeleton skeleton, Avatar? avatar = null) : base(skeleton, avatar)
            => Bones = new Transform3D[skeleton.BoneCount];

        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) => Bones[boneIndex] = localTransform;

        protected override bool RaycastGround(Float3 worldOrigin, Float3 worldDirection, float maxDistance, out Float3 worldHitPoint, out Float3 worldHitNormal)
        {
            worldHitPoint = new Float3(worldOrigin.X, 0f, worldOrigin.Z); // flat ground at Y=0
            worldHitNormal = new Float3(0f, 1f, 0f);
            return HasGround;
        }
    }

    private sealed class RecordingGraphAnimator : GraphAnimator
    {
        public readonly Transform3D[] Bones;
        public RecordingGraphAnimator(AnimationGraph graph, Skeleton skeleton) : base(graph, skeleton)
            => Bones = new Transform3D[skeleton.BoneCount];
        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) => Bones[boneIndex] = localTransform;
    }

    private sealed class Recorder : SimpleAnimator
    {
        public Transform3D[]? SecondaryBones;
        public Recorder(Skeleton skeleton) : base(skeleton) { }
        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) { }
        protected override void ApplySecondaryBoneTransform(int secondaryIndex, Skeleton skeleton, int boneIndex, in Transform3D localTransform)
        {
            SecondaryBones ??= new Transform3D[skeleton.BoneCount];
            SecondaryBones[boneIndex] = localTransform;
        }
    }

    private static Pose Ref(Skeleton skeleton)
    {
        var p = new Pose(skeleton); p.SetToReferencePose();
        return p;
    }

    [Fact]
    public void CrossFade_DuringACrossFade_BlendsFromTheCurrentMix()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var player = new Player(skeleton);
        player.Play(TestClips.Const(skeleton, 0f));
        player.Update(0.1f);
        player.CrossFade(TestClips.Const(skeleton, 10f), 1f);
        for (int i = 0; i < 9; i++)
            player.Update(0.1f);
        float before = player.Pose.GetTransform(0).position.Z;

        player.CrossFade(TestClips.Const(skeleton, 0f), 1f);
        player.Update(0.1f);
        float after = player.Pose.GetTransform(0).position.Z;

        Assert.Equal(9.0, (double)before, 2);
        Assert.InRange(after, 8.5f, 9.5f);
    }

    [Fact]
    public void NegativeSpeed_PlaysRootMotionBackward()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var player = new Player(skeleton) { Speed = -1f };
        player.Play(TestClips.Ramp(skeleton, rootTravel: 1f));

        for (int i = 0; i < 5; i++)
        {
            player.Update(0.1f);
            Assert.Equal(-0.1, (double)player.RootMotionDelta.position.Z, 3);
        }
    }

    [Fact]
    public void Secondaries_FollowTheDestinationClipDuringACrossFade()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        Skeleton weaponA = TestSkeletons.MakeChain();
        Skeleton weaponB = TestSkeletons.MakeChain();
        var clipA = new AnimationClip(skeleton, new[] { TestClips.At(skeleton, 0f), TestClips.At(skeleton, 0f) }, 1f, secondaryClips: new[] { TestClips.Const(weaponA, 1f) });
        var clipB = new AnimationClip(skeleton, new[] { TestClips.At(skeleton, 0f), TestClips.At(skeleton, 0f) }, 1f, secondaryClips: new[] { TestClips.Const(weaponB, 2f) });
        var player = new Player(skeleton);

        player.Play(clipA);
        player.Update(0.1f);
        player.CrossFade(clipB, 1f);
        player.Update(0.1f);

        Assert.Same(clipB, player.CurrentClip);
        Assert.Same(weaponB, Assert.Single(player.SecondarySkeletons));
    }

    [Fact]
    public void SimpleAnimator_PushesPlayedClipToBones()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var animator = new RecordingSimpleAnimator(skeleton);
        animator.Play(TestClips.Const(skeleton, 7f));
        animator.Update(0.1f);

        Assert.Equal(7.0, (double)animator.Bones[0].position.Z, 3);
    }

    [Fact]
    public void SimpleAnimator_CrossFadeBlendsToTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var animator = new RecordingSimpleAnimator(skeleton);
        animator.Play(TestClips.Const(skeleton, 0f));
        animator.Update(0.016f);

        animator.CrossFade(TestClips.Const(skeleton, 10f), duration: 0.2f);
        animator.Update(0.1f); // halfway through the fade
        Assert.InRange(animator.Bones[0].position.Z, 1f, 9f);

        for (int i = 0; i < 4; i++)
            animator.Update(0.1f); // finish the fade
        Assert.Equal(10.0, (double)animator.Bones[0].position.Z, 2);
    }

    [Fact]
    public void GraphAnimator_DrivesGraphAndPushesPose()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Const(skeleton, 3f));
        graph.SetRoot(clip);

        var animator = new RecordingGraphAnimator(graph, skeleton);
        animator.Update(0.016f);

        Assert.Equal(3.0, (double)animator.Bones[0].position.Z, 3);
    }

    [Fact]
    public void SimpleAnimator_FootIk_GroundsTheFootViaRaycast()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        HumanoidRig rig = avatar.Humanoid!;

        var pose = new Pose(skeleton); pose.SetToReferencePose();
        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        Transform3D ub = skeleton.GetBoneParentSpaceTransform(upper);
        pose.SetTransform(upper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), ub.scale));
        Transform3D lb = skeleton.GetBoneParentSpaceTransform(lower);
        pose.SetTransform(lower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), lb.scale));
        var clip = new AnimationClip(skeleton, new[] { pose, pose }, 1f);

        var animator = new RecordingSimpleAnimator(skeleton, avatar)
        {
            FootIkEnabled = true,
            HasGround = true,
            FootRayStartHeight = 1.0f,
            FootRayLength = 2.0f,
        };
        animator.Play(clip);
        animator.Update(0.016f);

        animator.Pose.CalculateModelSpaceTransforms();
        int leftFoot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Assert.Equal(0.0, (double)animator.Pose.GetModelSpaceTransform(leftFoot).position.Y, 2);
    }

    [Fact]
    public void SimpleAnimator_SamplesAndPushesSecondaryPose()
    {
        Skeleton primary = TestSkeletons.MakeChain();
        Skeleton weapon = TestSkeletons.MakeChain(); // a distinct secondary skeleton

        AnimationClip weaponClip = TestClips.Const(weapon, 99f);
        AnimationClip mainClip = new AnimationClip(primary, new[] { Ref(primary), Ref(primary) }, 1f, secondaryClips: new[] { weaponClip });

        var animator = new Recorder(primary);
        animator.Play(mainClip);
        animator.Update(0.016f);

        Assert.Single(animator.SecondarySkeletons);
        Assert.NotNull(animator.SecondaryBones);
        Assert.Equal(99.0, (double)animator.SecondaryBones![0].position.Z, 3);
    }

    [Fact]
    public void SimpleAnimator_NaNDeltaTime_DoesNotBreakTheCrossFade()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var player = new Player(skeleton);
        player.Play(TestClips.Const(skeleton, 0f));
        player.Update(0.1f);
        player.CrossFade(TestClips.Const(skeleton, 2f), 0.5f);
        player.Update(0.1f);
        player.Update(float.NaN);
        for (int i = 0; i < 10; i++)
            player.Update(0.1f);

        Assert.False(player.IsCrossFading);
        Assert.Equal(2.0, (double)player.Pose.GetTransform(0).position.Z, 3);
    }
}
