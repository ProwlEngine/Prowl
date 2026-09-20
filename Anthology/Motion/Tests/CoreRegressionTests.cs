using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class CoreRegressionTests
{
    [Fact]
    public void SyncTrack_FirstEventAfterZero_WrapsTheLastEvent()
    {
        SyncTrack source = TestClips.Track(0.1f, 0.6f);
        SyncTrack target = TestClips.Track(0f, 0.5f);

        Assert.Equal(0.95, (double)source.RemapTo(source.GetTime(0.05f), target), 3);
        Assert.Equal(0.25, (double)source.RemapTo(source.GetTime(0.35f), target), 3);
        Assert.Equal(0.75, (double)source.RemapTo(source.GetTime(0.85f), target), 3);
    }

    [Fact]
    public void SyncTrack_BlendHasTheLowestCommonMultipleOfEvents()
    {
        var blended = new SyncTrack(SyncTrack.Default, TestClips.Track(0f, 0.25f, 0.5f, 0.75f), 0.5f);
        Assert.Equal(4, blended.EventCount);
        Assert.Equal(6, new SyncTrack(TestClips.Track(0f, 0.5f), TestClips.Track(0f, 0.3f, 0.6f), 0.5f).EventCount);
    }

    [Fact]
    public void BlendSynchronized_UsesTheSourceLoopToReachLaterTargetEvents()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip single = TestClips.Ramp(skeleton);
        AnimationClip four = TestClips.Ramp(skeleton, syncTrack: TestClips.Track(0f, 0.25f, 0.5f, 0.75f));
        var result = new Pose(skeleton);

        Blender.BlendSynchronized(result, single, four, 0.5f, 1f, new Pose(skeleton), new Pose(skeleton), sourceLoop: 3);

        Assert.Equal(8.75, (double)result.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void ClipLoop_EventAtTimeZeroFiresEveryLoop()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(TestClips.Ramp(skeleton, events: new AnimationEvent[] { new IdEvent(new StringID("start"), 0f), new IdEvent(new StringID("mid"), 0.5f) })));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        int start = 0, mid = 0;
        for (int i = 0; i < 33; i++)
        {
            instance.Update(0.1f);
            start += TestClips.CountId(instance.Events, "start");
            mid += TestClips.CountId(instance.Events, "mid");
        }

        Assert.Equal(4, start);
        Assert.Equal(3, mid);
    }

    [Fact]
    public void SampleEvents_DurationEventAcrossTheWrap_IsAddedOnce()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip clip = TestClips.Ramp(skeleton, events: new AnimationEvent[] { new IdEvent(new StringID("whole"), 0f, 1f) });
        var buffer = new SampledEventsBuffer();

        clip.SampleEvents(0.95f, 0.05f, buffer, looped: true);

        Assert.Equal(1, TestClips.CountId(buffer, "whole"));
    }

    [Fact]
    public void SampleEvents_DurationEventRunningPastTheEnd_IsActiveInItsWrappedTail()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip clip = TestClips.Ramp(skeleton, events: new AnimationEvent[] { new IdEvent(new StringID("tail"), 0.9f, 0.2f) });
        var buffer = new SampledEventsBuffer();

        clip.SampleEvents(0.02f, 0.05f, buffer);

        Assert.Equal(1, TestClips.CountId(buffer, "tail"));
    }

    [Fact]
    public void CompressedClip_KeepsASmallRotationRamp()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var frames = new Pose[11];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = new Pose(skeleton);
            frames[i].SetToReferencePose();
            float radians = 0.5f * i / 10f * MathF.PI / 180f;
            frames[i].SetTransform(1, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), radians), Float3.One));
        }

        var clip = new CompressedAnimationClip(skeleton, frames, 1f);
        var pose = new Pose(skeleton);
        clip.GetPose(1f, pose);

        Assert.True(clip.CompressedSizeBytes > 0);
        float degrees = 2f * MathF.Acos(Math.Clamp(MathF.Abs(pose.GetTransform(1).rotation.W), 0f, 1f)) * 180f / MathF.PI;
        Assert.Equal(0.5, (double)degrees, 2);
    }

    [Fact]
    public void CompressedClip_PlaysInTheGraphWithRootMotionAndEvents()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip source = TestClips.Ramp(skeleton, rootTravel: 2f, events: new AnimationEvent[] { new IdEvent(new StringID("hit"), 0.05f) });
        var compressed = new CompressedAnimationClip(source);
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(compressed));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0.2, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 2);
        Assert.Equal(1, TestClips.CountId(instance.Events, "hit"));
    }

    [Fact]
    public void RootMotionDelta_IsExactForNonUniformScale()
    {
        var from = new Transform3D(Float3.Zero, Quaternion.AxisAngle(new Float3(0f, 1f, 0f), MathF.PI / 2f), new Float3(2f, 1f, 1f));
        var to = new Transform3D(new Float3(0f, 0f, 4f), from.rotation, from.scale);
        var rootMotion = new RootMotion(new[] { from, to }, 1f);

        Transform3D reached = RootMotionUtil.Apply(from, rootMotion.SampleDelta(0f, 1f));

        Assert.Equal(0.0, (double)reached.position.X, 4);
        Assert.Equal(4.0, (double)reached.position.Z, 4);
    }

    [Fact]
    public void RootMotionDelta_ZeroScaleStaysFinite()
    {
        var from = new Transform3D(Float3.Zero, Quaternion.Identity, new Float3(0f, 1f, 1f));
        var to = new Transform3D(new Float3(1f, 0f, 1f), Quaternion.Identity, Float3.One);
        Transform3D delta = new RootMotion(new[] { from, to }, 1f).SampleDelta(0f, 1f);

        Assert.True(float.IsFinite(delta.position.X) && float.IsFinite(delta.position.Z));
        Assert.True(float.IsFinite(delta.scale.X));
    }

    [Fact]
    public void Skeleton_ChildrenBeforeParents_DeepChainDoesNotOverflow()
    {
        const int count = 20000;
        var ids = new StringID[count];
        var parents = new int[count];
        var local = new Transform3D[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = new StringID("b" + i);
            parents[i] = i == count - 1 ? Skeleton.InvalidIndex : i + 1;
            local[i] = new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One);
        }

        var skeleton = new Skeleton(ids, parents, local);
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();

        Assert.Equal(count, (double)pose.GetModelSpaceTransform(0).position.Y, 1);
    }

    [Fact]
    public void Skeleton_ParentCycle_IsInvalidAndStillEvaluates()
    {
        var ids = new[] { new StringID("a"), new StringID("b") };
        var local = new[] { Transform3D.Identity, Transform3D.Identity };
        var skeleton = new Skeleton(ids, new[] { 1, 0 }, local);

        Assert.False(skeleton.IsValid);
        Assert.Equal(1.0, (double)skeleton.GetBoneModelSpaceTransform(0).scale.X, 5);
        Assert.Equal(1.0, (double)skeleton.GetBoneModelSpaceTransform(1).scale.X, 5);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("Bone_94922", "Bone_633800")]
    public void Skeleton_DuplicateOrCollidingIds_AreInvalid(string first, string second)
    {
        var ids = new[] { new StringID(first), new StringID(second) };
        var skeleton = new Skeleton(ids, new[] { Skeleton.InvalidIndex, 0 }, new[] { Transform3D.Identity, Transform3D.Identity });

        Assert.True(skeleton.HasDuplicateBoneIds);
        Assert.False(skeleton.IsValid);
    }

    [Fact]
    public void Pose_LowLodCache_DoesNotServeStaleHighLodBones()
    {
        var ids = new[] { new StringID("Root"), new StringID("Child") };
        var local = new[] { Transform3D.Identity, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One) };
        var skeleton = new Skeleton(ids, new[] { Skeleton.InvalidIndex, 0 }, local, numBonesToSampleAtLowLOD: 1);
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.CalculateModelSpaceTransforms();

        pose.SetTransform(0, new Transform3D(new Float3(5f, 0f, 0f), Quaternion.Identity, Float3.One));
        pose.CalculateModelSpaceTransforms(SkeletonLOD.Low);

        Assert.Equal(5.0, (double)pose.GetModelSpaceTransform(1).position.X, 4);
    }

    [Fact]
    public void Quantization_NormalizesBeforeEncoding()
    {
        Quantization.EncodeQuaternion(new Quaternion(0f, 0f, 1.2f, 1.6f), out ushort m0, out ushort m1, out ushort m2);
        Quaternion decoded = Quantization.DecodeQuaternion(m0, m1, m2);

        Assert.Equal(0.6, (double)decoded.Z, 3);
        Assert.Equal(0.8, (double)decoded.W, 3);
    }

    [Fact]
    public void NaNTime_SamplesAFinitePose()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var pose = new Pose(skeleton);
        TestClips.Ramp(skeleton).GetPose(float.NaN, pose);

        Assert.True(float.IsFinite(pose.GetTransform(0).position.Z));
    }

    [Fact]
    public void Blend_OfTwoAdditivePoses_StaysAdditive()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var a = new Pose(skeleton);
        a.SetToZeroPose();
        var b = new Pose(skeleton);
        b.SetToZeroPose();
        var result = new Pose(skeleton);

        Blender.Blend(result, a, b, 0.5f);

        Assert.Equal(PoseState.AdditivePose, result.State);
    }

    [Fact]
    public void AdditiveBlend_RejectsAFullPoseAsTheAdditiveInput()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var full = new Pose(skeleton);
        full.SetToReferencePose();

        Assert.Throws<ArgumentException>(() => Blender.AdditiveBlend(new Pose(skeleton), full, full, 1f));
    }

    [Fact]
    public void AdditiveBlend_AddsTheScaleDelta()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var basePose = new Pose(skeleton);
        basePose.SetToReferencePose();
        var additive = new Pose(skeleton);
        additive.SetToZeroPose();
        additive.SetTransform(0, new Transform3D(Float3.Zero, Quaternion.Identity, new Float3(0.5f, 0f, 0f)));
        var result = new Pose(skeleton);

        Blender.AdditiveBlend(result, basePose, additive, 1f);

        Assert.Equal(1.5, (double)result.GetTransform(0).scale.X, 4);
        Assert.Equal(1.0, (double)result.GetTransform(1).scale.X, 4);
    }

    [Fact]
    public void ModelSpace_DoesNotAllocate()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.CalculateModelSpaceTransforms();

        long lowest = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                pose.CalculateModelSpaceTransforms();
            lowest = Math.Min(lowest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, lowest);
    }
}
