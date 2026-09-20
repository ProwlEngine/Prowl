using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class AnimatorTests
{
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

    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void SimpleAnimator_PushesPlayedClipToBones()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var animator = new RecordingSimpleAnimator(skeleton);
        animator.Play(ConstClip(skeleton, 7f));
        animator.Update(0.1f);

        Assert.Equal(7.0, (double)animator.Bones[0].position.Z, 3);
    }

    [Fact]
    public void SimpleAnimator_CrossFadeBlendsToTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var animator = new RecordingSimpleAnimator(skeleton);
        animator.Play(ConstClip(skeleton, 0f));
        animator.Update(0.016f);

        animator.CrossFade(ConstClip(skeleton, 10f), duration: 0.2f);
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
        int clip = graph.AddClip(ConstClip(skeleton, 3f));
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
}
