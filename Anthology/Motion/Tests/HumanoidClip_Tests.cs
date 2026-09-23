using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Humanoid clips: baked in muscle space and played on any humanoid rig.</summary>
public class HumanoidClip_Tests
{
    private static Avatar RigWithChannels(params string[] channels)
        => new HumanoidTestRig { FloatChannels = channels }.BuildAvatar();

    private static Pose Frame(Avatar avatar, params float[] channels)
    {
        var pose = new Pose(avatar.Skeleton);
        pose.SetToReferencePose();
        for (int c = 0; c < channels.Length; c++)
            pose.SetFloat(c, channels[c]);
        return pose;
    }

    private static AnimationClip Clip(Avatar avatar, Pose pose) => new(avatar.Skeleton, new[] { pose, pose }, 1f);

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

    private sealed class Recorder : SimpleAnimator
    {
        public Recorder(Avatar avatar) : base(avatar.Skeleton, avatar) { }

        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) { }
    }

    [Fact]
    public void ABakedClip_CarriesItsChannels()
    {
        Avatar avatar = RigWithChannels("Smile", "Blink");
        var clip = new AnimationClip(avatar.Skeleton, new[] { Frame(avatar, 0f, 100f), Frame(avatar, 60f, 100f) }, 1f);
        AnimationClipBase bound = HumanoidClip.Bake(avatar, clip).Bind(avatar);

        var pose = new Pose(avatar.Skeleton);
        bound.GetPose(1f, pose);

        Assert.Equal(60f, pose.GetFloat(0), 3);
        Assert.Equal(100f, pose.GetFloat(1), 3);
    }

    // A clip sampler owns every channel of the pose it writes, so nothing from an earlier sample survives.
    [Fact]
    public void ABakedClip_LeavesNoStaleChannelBehind()
    {
        Avatar avatar = RigWithChannels("Smile");
        AnimationClipBase bound = HumanoidClip.Bake(avatar, Clip(avatar, Frame(avatar, 0f))).Bind(avatar);

        var pose = new Pose(avatar.Skeleton);
        pose.SetFloat(0, 99f);
        bound.GetPose(1f, pose);

        Assert.Equal(0f, pose.GetFloat(0), 3);
    }

    // Channels belong to the rig that authored them, so they are matched by name like the bones are.
    [Fact]
    public void ABakedClip_MatchesChannelsByNameOnAnotherRig()
    {
        Avatar source = RigWithChannels("Smile", "Blink");
        Avatar target = new HumanoidTestRig { FloatChannels = new[] { "Frown", "Smile" }, LegScale = 1.4f }.BuildAvatar();
        var clip = new AnimationClip(source.Skeleton, new[] { Frame(source, 0f, 5f), Frame(source, 60f, 5f) }, 1f);

        var pose = new Pose(target.Skeleton);
        HumanoidClip.Bake(source, clip).Bind(target).GetPose(1f, pose);

        Assert.Equal(0f, pose.GetFloat(0), 3);
        Assert.Equal(60f, pose.GetFloat(1), 3);
    }

    [Fact]
    public void ABakedClipsChannelsSurviveARoundTrip()
    {
        Avatar avatar = RigWithChannels("Smile", "Blink");
        var clip = new AnimationClip(avatar.Skeleton, new[] { Frame(avatar, 0f, 100f), Frame(avatar, 60f, 20f) }, 1f);
        HumanoidClip baked = HumanoidClip.Bake(avatar, clip);

        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            MotionBinary.Write(writer, baked);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        HumanoidClip read = MotionBinary.ReadHumanoidClip(reader);

        Assert.Equal(baked.FloatChannelIds.Count, read.FloatChannelIds.Count);
        var pose = new Pose(avatar.Skeleton);
        read.Bind(avatar).GetPose(1f, pose);
        Assert.Equal(60f, pose.GetFloat(0), 3);
        Assert.Equal(20f, pose.GetFloat(1), 3);
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
}
