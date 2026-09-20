using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Playing a clip authored for one skeleton on another rig that names its bones the same way.</summary>
public class SkeletonMappingTests
{
    // Same bone names as the chain rig, in another order, with one extra bone and one missing.
    private static Skeleton Reordered()
    {
        var ids = new[] { new StringID("Root"), new StringID("Knee"), new StringID("Hip"), new StringID("Tail") };
        var parents = new[] { Skeleton.InvalidIndex, 2, 0, 0 };
        var pose = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 0f, -0.5f), Quaternion.Identity, Float3.One),
        };
        return new Skeleton(ids, parents, pose);
    }

    private static Pose Sample(AnimationClipBase clip, Skeleton target, float time)
    {
        var pose = new Pose(target);
        clip.GetPose(time, pose, clip.GetMappingTo(target));
        return pose;
    }

    [Fact]
    public void MatchingBonesTakeTheirSourceValues()
    {
        Skeleton source = TestSkeletons.MakeChain();
        Skeleton target = Reordered();
        AnimationClip clip = TestClips.Ramp(source, endZ: 4f);

        Pose pose = Sample(clip, target, 1f);

        Assert.Equal(4f, pose.GetTransform(target.GetBoneIndex(new StringID("Root"))).position.Z, 3);
    }

    // The clip has nothing to say about a bone the source rig lacks, so it must stay at its bind.
    [Fact]
    public void BonesTheClipDoesNotDrive_KeepTheirReferencePose()
    {
        Skeleton target = Reordered();
        AnimationClip clip = TestClips.Ramp(TestSkeletons.MakeChain(), endZ: 4f);
        int tail = target.GetBoneIndex(new StringID("Tail"));

        Pose pose = Sample(clip, target, 1f);

        Assert.Equal(target.GetBoneParentSpaceTransform(tail).position, pose.GetTransform(tail).position);
    }

    [Fact]
    public void TheMappingReportsWhatItCouldNotDrive()
    {
        SkeletonMapping mapping = SkeletonMapping.Create(TestSkeletons.MakeChain(), Reordered());

        Assert.Equal(3, mapping.MappedBoneCount);
        Assert.Equal(new[] { "Tail" }, mapping.UnmappedTargetBones().Select(id => id.DebugName).ToArray());
    }

    [Fact]
    public void TwoRigsThatShareNoNames_GiveAnEmptyMapping()
    {
        Skeleton other = new(new[] { new StringID("A") }, new[] { Skeleton.InvalidIndex }, new[] { Transform3D.Identity });

        Assert.True(SkeletonMapping.Create(TestSkeletons.MakeChain(), other).IsEmpty);
    }

    [Fact]
    public void AClipOnItsOwnSkeleton_NeedsNoMapping()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        Assert.Null(TestClips.Ramp(skeleton).GetMappingTo(skeleton));
    }

    [Fact]
    public void CompressedClipsMapTheSameWay()
    {
        Skeleton target = Reordered();
        var compressed = new CompressedAnimationClip(TestClips.Ramp(TestSkeletons.MakeChain(), endZ: 4f));

        Pose pose = Sample(compressed, target, 0.5f);

        Assert.Equal(2f, pose.GetTransform(target.GetBoneIndex(new StringID("Root"))).position.Z, 2);
    }

    [Fact]
    public void AnAnimatorMapsAClipFromAnotherRig()
    {
        Skeleton target = Reordered();
        var animator = new Recorder(target);
        animator.Play(TestClips.Ramp(TestSkeletons.MakeChain(), endZ: 4f), loop: false);

        animator.Update(1f);

        Assert.Equal(4f, animator.Pose.GetTransform(target.GetBoneIndex(new StringID("Root"))).position.Z, 2);
    }

    [Fact]
    public void AGraphMapsAClipFromAnotherRig()
    {
        Skeleton target = Reordered();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddClip(TestClips.Ramp(TestSkeletons.MakeChain(), endZ: 4f), loop: false));
        AnimationGraphInstance instance = graph.CreateInstance(target);

        instance.Update(1f, Transform3D.Identity);

        Assert.Equal(4f, instance.Pose.GetTransform(target.GetBoneIndex(new StringID("Root"))).position.Z, 2);
    }

    private sealed class Recorder : SimpleAnimator
    {
        public Recorder(Skeleton skeleton) : base(skeleton) { }

        protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) { }
    }
}
