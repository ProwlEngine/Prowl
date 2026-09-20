using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Foot lock, stride warp and the root motion filter.</summary>
public class LocomotionNodeTests
{
    private static readonly StringID Hip = new("Hip");
    private static readonly StringID Knee = new("Knee");
    private static readonly StringID Foot = new("Foot");

    // A leg hanging down from the origin: hip, knee, foot, one unit apart.
    private static Skeleton LegRig()
    {
        var ids = new[] { new StringID("Root"), Hip, Knee, Foot };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1, 2 };
        var pose = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 2f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, -1f, 0.05f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, -1f, -0.05f), Quaternion.Identity, Float3.One),
        };
        return new Skeleton(ids, parents, pose);
    }

    private static Float3 FootModel(AnimationGraphInstance instance, Skeleton skeleton)
    {
        instance.Pose.CalculateModelSpaceTransforms();
        return instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Foot)).position;
    }

    // ---- foot lock -----------------------------------------------------------------------------

    [Fact]
    public void FootLock_HoldsTheFootStillWhileTheCharacterWalksOn()
    {
        Skeleton skeleton = LegRig();
        var graph = new AnimationGraph();
        int locked = graph.AddBoolParameter("Locked", true);
        graph.SetRoot(graph.AddFootLock(graph.AddClip(TestClips.Const(skeleton, 0f)), Hip, Knee, Foot, locked));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);
        Float3 plantedWorld = FootModel(instance, skeleton);

        for (int i = 1; i <= 20; i++)
            instance.Update(1f / 60f, new Transform3D(new Float3(0f, 0f, i * 0.01f), Quaternion.Identity, Float3.One));

        // The character has walked 0.2 forward, so in its own space the foot should sit 0.2 behind.
        Float3 foot = FootModel(instance, skeleton);
        Assert.Equal(plantedWorld.Z - 0.2f, foot.Z, 2);
    }

    [Fact]
    public void FootLock_ReleasesBackOntoTheAnimation()
    {
        Skeleton skeleton = LegRig();
        var graph = new AnimationGraph();
        int locked = graph.AddBoolParameter("Locked", true);
        graph.SetRoot(graph.AddFootLock(graph.AddClip(TestClips.Const(skeleton, 0f)), Hip, Knee, Foot, locked));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        for (int i = 1; i <= 20; i++)
            instance.Update(1f / 60f, new Transform3D(new Float3(0f, 0f, i * 0.01f), Quaternion.Identity, Float3.One));

        instance.SetBool("Locked", false);
        var world = new Transform3D(new Float3(0f, 0f, 0.2f), Quaternion.Identity, Float3.One);
        for (int i = 0; i < 30; i++)
            instance.Update(1f / 60f, world);

        Assert.Equal(0f, FootModel(instance, skeleton).Z, 2);
    }

    [Fact]
    public void FootLock_WithoutALock_ChangesNothing()
    {
        Skeleton skeleton = LegRig();
        var graph = new AnimationGraph();
        int locked = graph.AddBoolParameter("Locked");
        graph.SetRoot(graph.AddFootLock(graph.AddClip(TestClips.Const(skeleton, 0f)), Hip, Knee, Foot, locked));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        for (int i = 1; i <= 20; i++)
            instance.Update(1f / 60f, new Transform3D(new Float3(0f, 0f, i * 0.05f), Quaternion.Identity, Float3.One));

        Assert.Equal(0f, FootModel(instance, skeleton).Z, 3);
    }

    // A lock left on must not stretch the leg across the level.
    [Fact]
    public void FootLock_BreaksWhenTheCharacterWalksTooFar()
    {
        Skeleton skeleton = LegRig();
        var graph = new AnimationGraph();
        int locked = graph.AddBoolParameter("Locked", true);
        var definition = new FootLockDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), Hip, Knee, Foot, locked) { BreakDistance = 0.5f };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        for (int i = 1; i <= 120; i++)
            instance.Update(1f / 60f, new Transform3D(new Float3(0f, 0f, i * 0.05f), Quaternion.Identity, Float3.One));

        Assert.Equal(0f, FootModel(instance, skeleton).Z, 2);
    }

    // ---- stride warp ---------------------------------------------------------------------------

    private static (AnimationGraph Graph, int Warp, Skeleton Skeleton) WalkGraph(float desired, float travelPerLoop = 2f)
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed", desired);
        int clip = graph.AddClip(TestClips.Ramp(skeleton, endZ: 10f, duration: 1f, rootTravel: travelPerLoop));
        return (graph, graph.AddStrideWarp(clip, speed), skeleton);
    }

    [Theory]
    [InlineData(2f, 1f)]
    [InlineData(3f, 1.5f)]
    [InlineData(1f, 0.5f)]
    public void StrideWarp_PlaysTheClipAtTheRateThatMatchesTheSpeed(float desired, float expectedScale)
    {
        (AnimationGraph graph, int warp, Skeleton skeleton) = WalkGraph(desired);
        graph.SetRoot(warp);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.1f, Transform3D.Identity);
        instance.Update(0.1f, Transform3D.Identity);

        // The clip's Z ramps 0..10 over its length, so the sampled value reports how far it has played.
        Assert.Equal(2f * expectedScale, instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void StrideWarp_MovesTheCharacterAtTheRequestedSpeed()
    {
        (AnimationGraph graph, int warp, Skeleton skeleton) = WalkGraph(3f);
        graph.SetRoot(warp);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        float travelled = 0f;
        for (int i = 0; i < 60; i++)
        {
            instance.Update(1f / 60f, Transform3D.Identity);
            travelled += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(3f, travelled, 1);
    }

    [Fact]
    public void StrideWarp_StaysWithinItsLimits()
    {
        (AnimationGraph graph, int warp, Skeleton skeleton) = WalkGraph(100f);
        graph.SetRoot(warp);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.1f, Transform3D.Identity);
        instance.Update(0.1f, Transform3D.Identity);

        Assert.Equal(4f, instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void StrideWarp_OnAClipThatDoesNotTravel_LeavesItAlone()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed", 5f);
        graph.SetRoot(graph.AddStrideWarp(graph.AddClip(TestClips.Ramp(skeleton, endZ: 10f)), speed));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.1f, Transform3D.Identity);
        instance.Update(0.1f, Transform3D.Identity);

        Assert.Equal(2f, instance.Pose.GetTransform(0).position.Z, 2);
    }

    // ---- root motion filter --------------------------------------------------------------------

    [Fact]
    public void RootMotionFilter_CanStripTravelEntirely()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Ramp(skeleton, rootTravel: 4f));
        graph.SetRoot(graph.AddRootMotionFilter(clip, RootMotionChannels.None));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.25f, Transform3D.Identity);

        Assert.Equal(0f, instance.RootMotionDelta.position.Z, 5);
    }

    [Fact]
    public void RootMotionFilter_CanKeepTheGroundPlaneOnly()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        var poses = new Pose[3];
        var root = new Transform3D[3];
        for (int i = 0; i < 3; i++)
        {
            poses[i] = TestClips.At(skeleton, i);
            root[i] = new Transform3D(new Float3(0f, i * 0.5f, i * 1f), Quaternion.Identity, Float3.One);
        }
        var clip = new AnimationClip(skeleton, poses, 1f, rootMotion: new RootMotion(root, 1f));
        graph.SetRoot(graph.AddRootMotionFilter(graph.AddClip(clip), RootMotionChannels.Horizontal));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.5f, Transform3D.Identity);

        Assert.Equal(0f, instance.RootMotionDelta.position.Y, 5);
        Assert.True(instance.RootMotionDelta.position.Z > 0.5f);
    }

    [Fact]
    public void RootMotionFilter_CanDampWhatItKeeps()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Ramp(skeleton, rootTravel: 4f));
        graph.SetRoot(graph.AddRootMotionFilter(clip, RootMotionChannels.All, 0.5f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.25f, Transform3D.Identity);

        Assert.Equal(0.5f, instance.RootMotionDelta.position.Z, 3);
    }
}
