using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Scalar channels carried alongside the bones (blend shape weights and the like).</summary>
public class FloatChannelTests
{
    private static Skeleton WithChannels(params string[] names)
    {
        var ids = new[] { new StringID("Root"), new StringID("Hip") };
        var parents = new[] { Skeleton.InvalidIndex, 0 };
        var pose = new[] { Transform3D.Identity, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One) };
        return new Skeleton(ids, parents, pose, -1, names.Select(n => new StringID(n)).ToArray());
    }

    private static Pose Frame(Skeleton skeleton, params float[] values)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        for (int i = 0; i < values.Length; i++)
            pose.SetFloat(i, values[i]);
        return pose;
    }

    private static AnimationClip Clip(Skeleton skeleton)
        => new(skeleton, new[] { Frame(skeleton, 0f, 100f), Frame(skeleton, 50f, 100f) }, 1f);

    [Fact]
    public void ASkeletonWithoutChannels_CostsNothing()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        Assert.Equal(0, pose.FloatChannelCount);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 25f)]
    [InlineData(1f, 50f)]
    public void ClipsInterpolateTheirChannels(float time, float expected)
    {
        Skeleton skeleton = WithChannels("Smile", "Blink");
        var pose = new Pose(skeleton);

        Clip(skeleton).GetPose(time, pose);

        Assert.Equal(expected, pose.GetFloat(0), 3);
        Assert.Equal(100f, pose.GetFloat(1), 3);
    }

    // A channel that never changes is stored once, so the compressed clip has to give it back intact.
    [Fact]
    public void CompressedClipsKeepConstantAndChangingChannels()
    {
        Skeleton skeleton = WithChannels("Smile", "Blink");
        var compressed = new CompressedAnimationClip(Clip(skeleton));
        var pose = new Pose(skeleton);

        compressed.GetPose(0.5f, pose);

        Assert.Equal(25f, pose.GetFloat(0), 2);
        Assert.Equal(100f, pose.GetFloat(1), 2);
    }

    [Fact]
    public void BlendingMovesChannelsWithThePose()
    {
        Skeleton skeleton = WithChannels("Smile");
        var result = new Pose(skeleton);

        Blender.Blend(result, Frame(skeleton, 0f), Frame(skeleton, 80f), 0.25f);

        Assert.Equal(20f, result.GetFloat(0), 3);
    }

    // A bone mask names bones, so it cannot weight a channel: the blend weight alone applies.
    [Fact]
    public void ABoneMaskDoesNotWeightChannels()
    {
        Skeleton skeleton = WithChannels("Smile");
        var result = new Pose(skeleton);
        var mask = new BoneMask(skeleton, 0f);

        Blender.Blend(result, Frame(skeleton, 0f), Frame(skeleton, 80f), 0.5f, mask);

        Assert.Equal(40f, result.GetFloat(0), 3);
    }

    [Fact]
    public void AdditiveBlendingAddsChannels()
    {
        Skeleton skeleton = WithChannels("Smile");
        var result = new Pose(skeleton);
        Pose additive = Frame(skeleton, 10f);
        additive.SetToZeroPose();
        additive.SetFloat(0, 10f);

        Blender.AdditiveBlend(result, Frame(skeleton, 30f), additive, 0.5f);

        Assert.Equal(35f, result.GetFloat(0), 3);
    }

    [Fact]
    public void TheAnimatorPushesChannelsToTheEngine()
    {
        Skeleton skeleton = WithChannels("Smile", "Blink");
        var animator = new Recorder(skeleton);
        animator.Play(Clip(skeleton));

        animator.Update(0.5f);

        Assert.Equal(25f, animator.Channels[0], 2);
        Assert.Equal(100f, animator.Channels[1], 2);
    }

    private sealed class Recorder : SimpleAnimator
    {
        public Recorder(Skeleton skeleton) : base(skeleton) => Channels = new float[skeleton.FloatChannelCount];

        public float[] Channels { get; }

        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) { }

        protected override void ApplyFloatChannel(int channelIndex, float value) => Channels[channelIndex] = value;
    }
}
