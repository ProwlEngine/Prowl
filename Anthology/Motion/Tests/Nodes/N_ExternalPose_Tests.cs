using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The External Pose node: a pose the host writes each frame, copied or retargeted.</summary>
public class N_ExternalPose_Tests
{
    private static void Run(AnimationGraphInstance instance, int frames = 2, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

    private static Avatar RigWithChannels(params string[] channels)
        => new HumanoidTestRig { FloatChannels = channels }.BuildAvatar();

    private const float Deg = MathF.PI / 180f;

    private static Pose ArmRaised(Avatar avatar, float degrees)
    {
        var pose = new Pose(avatar.Skeleton);
        pose.SetToReferencePose();
        int bone = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        Transform3D bind = pose.GetTransform(bone);
        pose.SetTransform(bone, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(0f, 0f, 1f), degrees * Deg), bind.scale));
        pose.CalculateModelSpaceTransforms();
        return pose;
    }

    private static float ArmAngle(Avatar avatar, Pose pose)
    {
        pose.CalculateModelSpaceTransforms();
        int upper = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        int lower = avatar.Humanoid.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerArm);
        Float3 direction = pose.GetModelSpaceTransform(lower).position - pose.GetModelSpaceTransform(upper).position;
        return MathF.Atan2(direction.Y, -direction.X) / Deg;
    }

    [Theory]
    [InlineData(PoseTransferMode.Humanoid)]
    [InlineData(PoseTransferMode.ByName)]
    public void ExternalPose_CarriesChannelsInEveryMode(PoseTransferMode mode)
    {
        Avatar source = RigWithChannels("Smile");
        Avatar target = new HumanoidTestRig { FloatChannels = new[] { "Smile" }, LegScale = 1.3f }.BuildAvatar();
        var graph = new AnimationGraph();
        int node = graph.AddExternalPose(source, mode);
        graph.SetRoot(node);
        AnimationGraphInstance instance = graph.CreateInstance(target);

        var external = (ExternalPoseInstance)instance.GetNodeInstance(node);
        external.Source.SetFloat(0, 0.75f);
        external.HasPose = true;
        Run(instance);

        Assert.Equal(0.75f, instance.Pose.GetFloat(0), 3);
    }

    [Fact]
    public void ExternalPose_ShowsTheReferencePoseUntilTheHostWritesOne()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddExternalPose(skeleton));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);

        Assert.Equal(PoseState.ReferencePose, instance.Pose.State);
    }

    [Fact]
    public void ExternalPose_CopiesWhatTheHostWrites()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int node = graph.AddExternalPose(skeleton);
        graph.SetRoot(node);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        var external = (ExternalPoseInstance)instance.GetNodeInstance(node);
        external.Source.SetTransform(0, new Transform3D(new Float3(0f, 0f, 7f), Quaternion.Identity, Float3.One));
        external.HasPose = true;
        Run(instance);

        Assert.Equal(PoseTransferMode.Copy, external.ResolvedMode);
        Assert.Equal(7f, instance.Pose.GetTransform(0).position.Z, 4);
    }

    [Fact]
    public void ExternalPose_FromAnotherRig_MatchesBonesByName()
    {
        Skeleton target = TestSkeletons.MakeChain();
        var sourceIds = new[] { new StringID("Knee"), new StringID("Root") };
        var source = new Skeleton(sourceIds, new[] { Skeleton.InvalidIndex, 0 }, new[] { Transform3D.Identity, Transform3D.Identity });

        var graph = new AnimationGraph();
        int node = graph.AddExternalPose(source);
        graph.SetRoot(node);
        AnimationGraphInstance instance = graph.CreateInstance(target);

        var external = (ExternalPoseInstance)instance.GetNodeInstance(node);
        external.Source.SetTransform(0, new Transform3D(new Float3(0f, 0f, 3f), Quaternion.Identity, Float3.One));
        external.HasPose = true;
        Run(instance);

        Assert.Equal(PoseTransferMode.ByName, external.ResolvedMode);
        Assert.Equal(3f, instance.Pose.GetTransform(target.GetBoneIndex(new StringID("Knee"))).position.Z, 4);
    }

    // A pose from a rig with other proportions has to come in through muscle space.
    [Fact]
    public void ExternalPose_BetweenHumanoids_Retargets()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar target = new HumanoidTestRig { LegScale = 1.5f, Neck = false }.BuildAvatar();

        var graph = new AnimationGraph();
        int node = graph.AddExternalPose(source);
        graph.SetRoot(node);
        AnimationGraphInstance instance = graph.CreateInstance(target);

        var external = (ExternalPoseInstance)instance.GetNodeInstance(node);
        external.Source.CopyFrom(ArmRaised(source, 35f));
        external.HasPose = true;
        Run(instance);

        Assert.Equal(PoseTransferMode.Humanoid, external.ResolvedMode);
        Assert.Equal(ArmAngle(source, ArmRaised(source, 35f)), ArmAngle(target, instance.Pose), 0);
    }

    [Fact]
    public void ExternalPose_AskedToRetargetWithoutAnAvatar_FailsAtBind()
    {
        Skeleton plain = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddExternalPose(plain, PoseTransferMode.Humanoid));

        Assert.Throws<GraphValidationException>(() => graph.CreateInstance(plain));
    }
}
