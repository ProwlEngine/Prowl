using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Foot Lock node: pinning a planted foot in the world, and letting it go.</summary>
public class N_FootLock_Tests
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

    [Fact]
    public void FootLock_HoldsItsReleaseThroughAPausedFrame()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        HumanoidRig rig = avatar.Humanoid!;
        Pose pose = new(skeleton);
        pose.SetToReferencePose();

        var graph = new AnimationGraph();
        int locked = graph.AddBoolParameter("Locked", true);
        int clip = graph.AddClip(new AnimationClip(skeleton, new[] { pose, pose }, 1f));
        graph.SetRoot(graph.AddFootLock(clip, rig.GetSkeletonBoneId(HumanBodyBone.LeftUpperLeg),
            rig.GetSkeletonBoneId(HumanBodyBone.LeftLowerLeg), rig.GetSkeletonBoneId(HumanBodyBone.LeftFoot), locked));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);

        int foot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Float3 Foot()
        {
            instance.Pose.CalculateModelSpaceTransforms();
            return instance.Pose.GetModelSpaceTransform(foot).position;
        }

        // The lock takes its release time to fully take hold.
        for (int i = 0; i < 20; i++)
            instance.Update(1f / 60f, Transform3D.Identity);
        var moved = new Transform3D(new Float3(0f, 0f, 0.1f), Quaternion.Identity, Float3.One);
        instance.Update(1f / 60f, moved);
        Float3 pinned = Foot();

        instance.SetBool("Locked", false);
        instance.Update(0f, moved);

        Assert.True(MathF.Abs(pinned.Z) > 0.02f, $"the lock never pinned the foot, z {pinned.Z:N4}");
        Assert.Equal(pinned.Z, Foot().Z, 3);
    }
}
