using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Clips baked into muscle space, which play on any humanoid.</summary>
public class HumanoidClipTests
{
    private const float Deg = MathF.PI / 180f;

    // A clip that lifts the left upper leg, on whatever rig it is built for.
    private static AnimationClip LiftKnee(Avatar avatar, float degrees = 40f, int frames = 5)
    {
        int bone = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        var poses = new Pose[frames];
        for (int f = 0; f < frames; f++)
        {
            var pose = new Pose(avatar.Skeleton);
            pose.SetToReferencePose();
            Transform3D bind = pose.GetTransform(bone);
            float t = f / (float)(frames - 1);
            pose.SetTransform(bone, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), degrees * t * Deg), bind.scale));
            poses[f] = pose;
        }
        return new AnimationClip(avatar.Skeleton, poses, 1f);
    }

    private static float ModelAngle(Avatar avatar, Pose pose, HumanBodyBone bone, Float3 axis)
    {
        pose.CalculateModelSpaceTransforms();
        int index = avatar.Humanoid!.GetSkeletonBoneIndex(bone);
        Quaternion rotation = pose.GetModelSpaceTransform(index).rotation;
        Quaternion bind = avatar.Skeleton.GetBoneModelSpaceTransform(index).rotation;
        Float3 from = bind * axis;
        Float3 to = rotation * axis;
        return MathF.Acos(Math.Clamp(Float3.Dot(Float3.Normalize(from), Float3.Normalize(to)), -1f, 1f)) / Deg;
    }

    [Fact]
    public void ABakedClipPlaysBackOnItsOwnRig()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        AnimationClip source = LiftKnee(avatar);
        AnimationClipBase bound = HumanoidClip.Bake(avatar, source).Bind(avatar);

        var baked = new Pose(avatar.Skeleton);
        var direct = new Pose(avatar.Skeleton);
        bound.GetPose(1f, baked);
        source.GetPose(1f, direct);

        Assert.Equal(ModelAngle(avatar, direct, HumanBodyBone.LeftLowerLeg, new Float3(0f, 1f, 0f)),
            ModelAngle(avatar, baked, HumanBodyBone.LeftLowerLeg, new Float3(0f, 1f, 0f)), 1);
    }

    // The point of muscle space: bake once, play on a rig with other proportions.
    [Fact]
    public void ABakedClipPlaysOnADifferentRig()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar tall = new HumanoidTestRig { LegScale = 1.6f, Neck = false }.BuildAvatar();
        HumanoidClip baked = HumanoidClip.Bake(source, LiftKnee(source));

        var pose = new Pose(tall.Skeleton);
        baked.Bind(tall).GetPose(1f, pose);

        Assert.InRange(ModelAngle(tall, pose, HumanBodyBone.LeftUpperLeg, new Float3(0f, 1f, 0f)), 35f, 45f);
    }

    [Fact]
    public void SamplingBetweenFramesInterpolatesTheMuscles()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        HumanoidClip baked = HumanoidClip.Bake(avatar, LiftKnee(avatar, frames: 2));

        var start = new HumanPose();
        var middle = new HumanPose();
        var end = new HumanPose();
        baked.GetHumanPose(0f, start);
        baked.GetHumanPose(0.5f, middle);
        baked.GetHumanPose(1f, end);

        int muscle = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperLeg, MuscleAxis.Z);
        Assert.Equal((start.GetMuscle(muscle) + end.GetMuscle(muscle)) * 0.5f, middle.GetMuscle(muscle), 4);
    }

    // A bigger rig should cover proportionally more ground for the same baked motion.
    [Fact]
    public void RootMotionScalesWithTheTargetRig()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar tall = new HumanoidTestRig { LegScale = 2f }.BuildAvatar();

        AnimationClip walk = TestClips.Ramp(source.Skeleton, endZ: 0f, rootTravel: 1f);
        HumanoidClip baked = HumanoidClip.Bake(source, walk);

        float ratio = tall.Humanoid!.Scale / source.Humanoid!.Scale;
        Transform3D delta = baked.Bind(tall).RootMotion!.SampleDelta(0f, 1f);

        Assert.Equal(ratio, delta.position.Z, 2);
    }

    [Fact]
    public void BakingNeedsAHumanoidAvatar()
    {
        Skeleton chain = TestSkeletons.MakeChain();
        Avatar generic = AvatarBuilder.BuildGeneric(chain);

        Assert.Throws<ArgumentException>(() => HumanoidClip.Bake(generic, TestClips.Ramp(chain)));
    }

    [Fact]
    public void ABoundClipPlaysThroughAnAnimator()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Avatar other = new HumanoidTestRig { LegScale = 1.3f }.BuildAvatar();
        AnimationClipBase bound = HumanoidClip.Bake(avatar, LiftKnee(avatar)).Bind(other);

        var animator = new Recorder(other);
        animator.Play(bound, loop: false);
        animator.Update(1f);

        Assert.InRange(ModelAngle(other, animator.Pose, HumanBodyBone.LeftUpperLeg, new Float3(0f, 1f, 0f)), 35f, 45f);
    }

    private sealed class Recorder : SimpleAnimator
    {
        public Recorder(Avatar avatar) : base(avatar.Skeleton, avatar) { }

        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) { }
    }
}
