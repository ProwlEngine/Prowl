using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class SecondaryAnimationTests
{
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

    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void SimpleAnimator_SamplesAndPushesSecondaryPose()
    {
        Skeleton primary = TestSkeletons.MakeChain();
        Skeleton weapon = TestSkeletons.MakeChain(); // a distinct secondary skeleton

        AnimationClip weaponClip = ConstClip(weapon, 99f);
        AnimationClip mainClip = new AnimationClip(primary, new[] { Ref(primary), Ref(primary) }, 1f, secondaryClips: new[] { weaponClip });

        var animator = new Recorder(primary);
        animator.Play(mainClip);
        animator.Update(0.016f);

        Assert.Single(animator.SecondarySkeletons);
        Assert.NotNull(animator.SecondaryBones);
        Assert.Equal(99.0, (double)animator.SecondaryBones![0].position.Z, 3);
    }

    private static Pose Ref(Skeleton skeleton)
    {
        var p = new Pose(skeleton); p.SetToReferencePose();
        return p;
    }
}
