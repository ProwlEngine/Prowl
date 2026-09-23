using System.IO;
using System.Text;
using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Motion binary format: skeletons, clips and descriptions saved and read back.</summary>
public class MotionBinary_Tests
{
    private static T RoundTrip<T>(Action<BinaryWriter> write, Func<BinaryReader, T> read)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            write(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return read(reader);
    }

    private static Skeleton RoundTrip(Skeleton skeleton)
        => RoundTrip(w => MotionBinary.Write(w, skeleton), MotionBinary.ReadSkeleton);

    private static AnimationClipBase RoundTrip(AnimationClipBase clip, Skeleton skeleton)
        => RoundTrip(w => MotionBinary.Write(w, clip), r => MotionBinary.ReadClip(r, skeleton));

    private static void AssertSamplesMatch(AnimationClipBase expected, AnimationClipBase actual, Skeleton skeleton)
    {
        var a = new Pose(skeleton);
        var b = new Pose(skeleton);
        for (float t = 0f; t <= 1f; t += 0.125f)
        {
            expected.GetPose(t, a);
            actual.GetPose(t, b);
            for (int bone = 0; bone < skeleton.BoneCount; bone++)
            {
                Assert.Equal(a.GetTransform(bone).position.Z, b.GetTransform(bone).position.Z, 5);
                Assert.True(Quaternion.Dot(a.GetTransform(bone).rotation, b.GetTransform(bone).rotation) > 0.99999f);
            }
            for (int channel = 0; channel < skeleton.FloatChannelCount; channel++)
                Assert.Equal(a.GetFloat(channel), b.GetFloat(channel), 5);
        }
    }

    [Fact]
    public void ASkeletonKeepsItsBonesAndHierarchy()
    {
        Skeleton original = TestSkeletons.MakeMinimalHumanoid();

        Skeleton loaded = RoundTrip(original);

        Assert.Equal(original.BoneCount, loaded.BoneCount);
        for (int b = 0; b < original.BoneCount; b++)
        {
            Assert.Equal(original.GetBoneID(b).DebugName, loaded.GetBoneID(b).DebugName);
            Assert.Equal(original.GetParentBoneIndex(b), loaded.GetParentBoneIndex(b));
            Assert.Equal(original.GetBoneParentSpaceTransform(b).position, loaded.GetBoneParentSpaceTransform(b).position);
        }
    }

    [Fact]
    public void ASkeletonKeepsItsFloatChannelsAndLodSplit()
    {
        var ids = new[] { new StringID("Root"), new StringID("Head") };
        var parents = new[] { Skeleton.InvalidIndex, 0 };
        var pose = new[] { Transform3D.Identity, Transform3D.Identity };
        var original = new Skeleton(ids, parents, pose, 1, new[] { new StringID("Smile") });

        Skeleton loaded = RoundTrip(original);

        Assert.Equal(1, loaded.LowLodBoneCount);
        Assert.Equal(1, loaded.FloatChannelCount);
        Assert.Equal(0, loaded.GetFloatChannelIndex(new StringID("Smile")));
    }

    [Fact]
    public void AnUncompressedClipComesBackExactly()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip original = TestClips.Ramp(skeleton, endZ: 7f, rootTravel: 2f, syncTrack: TestClips.Track(0f, 0.4f));

        AnimationClipBase loaded = RoundTrip(original, skeleton);

        Assert.IsType<AnimationClip>(loaded);
        Assert.Equal(original.FrameCount, loaded.FrameCount);
        Assert.Equal(original.Duration, loaded.Duration, 5);
        Assert.Equal(2f, loaded.RootMotion!.TotalDelta.position.Z, 4);
        Assert.Equal(2, loaded.SyncTrack.EventCount);
        AssertSamplesMatch(original, loaded, skeleton);
    }

    // The quantized data is stored as is, so loading must not cost a second round of compression.
    [Fact]
    public void ACompressedClipComesBackWithoutLosingMorePrecision()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var original = new CompressedAnimationClip(TestClips.Ramp(skeleton, endZ: 7f));

        AnimationClipBase loaded = RoundTrip(original, skeleton);

        Assert.IsType<CompressedAnimationClip>(loaded);
        AssertSamplesMatch(original, loaded, skeleton);
    }

    [Fact]
    public void AClipKeepsItsFloatChannels()
    {
        var skeleton = new Skeleton(
            new[] { new StringID("Root") },
            new[] { Skeleton.InvalidIndex },
            new[] { Transform3D.Identity },
            -1,
            new[] { new StringID("Smile") });

        var first = new Pose(skeleton);
        first.SetToReferencePose();
        var last = new Pose(skeleton);
        last.SetToReferencePose();
        last.SetFloat(0, 60f);
        var original = new AnimationClip(skeleton, new[] { first, last }, 1f);

        AssertSamplesMatch(original, RoundTrip(original, skeleton), skeleton);
        AssertSamplesMatch(original, RoundTrip(new CompressedAnimationClip(original), skeleton), skeleton);
    }

    [Fact]
    public void AnAdditiveClipStaysAdditive()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var pose = new Pose(skeleton);
        pose.SetToZeroPose();
        var original = new AnimationClip(skeleton, new[] { pose, pose }, 1f, isAdditive: true);

        Assert.True(RoundTrip(original, skeleton).IsAdditive);
    }

    [Fact]
    public void AHumanoidDescriptionKeepsItsMappingAndKnobs()
    {
        (Skeleton _, HumanDescription original) = new HumanoidTestRig().Build();
        original.UpperArmTwist = 0.25f;
        original.FeetSpacing = 0.3f;
        original.SetMuscleRange(3, -20f, 35f);

        HumanDescription loaded = RoundTrip(w => MotionBinary.Write(w, original), MotionBinary.ReadHumanDescription);

        Assert.Equal(original.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot), loaded.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot));
        Assert.Equal(original.MappedBones.Count(), loaded.MappedBones.Count());
        Assert.Equal(0.25f, loaded.UpperArmTwist, 5);
        Assert.Equal(0.3f, loaded.FeetSpacing, 5);
        Assert.Equal((-20f, 35f), loaded.GetMuscleRange(3));
    }

    // A saved description plus the skeleton is all an engine needs to rebuild a humanoid avatar.
    [Fact]
    public void ASavedSkeletonAndDescriptionRebuildTheAvatar()
    {
        Avatar original = new HumanoidTestRig().BuildAvatar();
        Skeleton skeleton = RoundTrip(original.Skeleton);
        HumanDescription description = RoundTrip(w => MotionBinary.Write(w, original.Humanoid!.Description), MotionBinary.ReadHumanDescription);

        Avatar rebuilt = AvatarBuilder.BuildHumanoid(skeleton, description);

        Assert.True(rebuilt.IsHuman);
        Assert.Equal(original.Humanoid!.Scale, rebuilt.Humanoid!.Scale, 4);
        Assert.Equal(original.Humanoid.GetSkeletonBoneIndex(HumanBodyBone.Head), rebuilt.Humanoid.GetSkeletonBoneIndex(HumanBodyBone.Head));
    }

    [Fact]
    public void AHumanoidClipComesBackFrameForFrame()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        HumanoidClip original = HumanoidClip.Bake(avatar, TestClips.Ramp(avatar.Skeleton, endZ: 0.5f, rootTravel: 1f));

        HumanoidClip loaded = RoundTrip(w => MotionBinary.Write(w, original), r => MotionBinary.ReadHumanoidClip(r));

        Assert.Equal(original.FrameCount, loaded.FrameCount);
        Assert.Equal(original.SourceScale, loaded.SourceScale, 5);
        Assert.Equal(1f, loaded.RootMotion!.TotalDelta.position.Z, 4);

        var a = new HumanPose();
        var b = new HumanPose();
        for (float t = 0f; t <= 1f; t += 0.25f)
        {
            original.GetHumanPose(t, a);
            loaded.GetHumanPose(t, b);
            for (int m = 0; m < HumanTrait.MuscleCount; m++)
                Assert.Equal(a.GetMuscle(m), b.GetMuscle(m), 5);
            Assert.Equal(a.BodyPosition.Y, b.BodyPosition.Y, 5);
            Assert.Equal(a.GetGoal(HumanGoal.LeftFoot).PositionWeight, b.GetGoal(HumanGoal.LeftFoot).PositionWeight, 5);
        }
    }

    [Fact]
    public void ReadingSomethingElse_Fails()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            writer.Write(1234u);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);

        Assert.Throws<InvalidDataException>(() => MotionBinary.ReadSkeleton(reader));
    }
}
