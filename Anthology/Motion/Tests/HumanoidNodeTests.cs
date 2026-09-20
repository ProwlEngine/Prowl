using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>The host fed pose node and muscle space layering.</summary>
public class HumanoidNodeTests
{
    private const float Deg = MathF.PI / 180f;

    private static void Run(AnimationGraphInstance instance, int frames = 2, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

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

    // ---- external pose -------------------------------------------------------------------------

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

    // ---- muscle layer --------------------------------------------------------------------------

    private static (AnimationGraphInstance Instance, Avatar Avatar) MuscleGraph(float weight, HumanPoseMask? mask = null, bool additive = false)
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        var graph = new AnimationGraph();
        int basePose = graph.AddClip(PoseClip(avatar, ArmRaised(avatar, 0f)));
        int layerPose = graph.AddClip(PoseClip(avatar, ArmRaised(avatar, 60f)));
        int value = graph.AddFloatParameter("Weight", weight);
        graph.SetRoot(graph.AddMuscleLayer(basePose, layerPose, value, mask, additive));
        return (graph.CreateInstance(avatar), avatar);
    }

    private static AnimationClip PoseClip(Avatar avatar, Pose pose) => new(avatar.Skeleton, new[] { pose, pose }, 1f);

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void MuscleLayer_AtTheEnds_MatchesTheInputItIsShowing(float weight)
    {
        (AnimationGraphInstance instance, Avatar avatar) = MuscleGraph(weight);

        Run(instance);

        Assert.Equal(ArmAngle(avatar, ArmRaised(avatar, weight * 60f)), ArmAngle(avatar, instance.Pose), 0);
    }

    // Muscles blend linearly, and a muscle's range is not symmetric, so half weight is near the middle
    // rather than exactly on it.
    [Fact]
    public void MuscleLayer_HalfWeight_SitsBetweenTheTwoPoses()
    {
        (AnimationGraphInstance instance, Avatar avatar) = MuscleGraph(0.5f);

        Run(instance);

        float middle = ArmAngle(avatar, ArmRaised(avatar, 30f));
        float actual = ArmAngle(avatar, instance.Pose);
        Assert.InRange(actual, MathF.Min(0f, ArmAngle(avatar, ArmRaised(avatar, 60f))), MathF.Max(0f, ArmAngle(avatar, ArmRaised(avatar, 60f))));
        Assert.True(MathF.Abs(actual - middle) < 10f, $"half weight landed {actual:N1} against a midpoint of {middle:N1}");
    }

    [Fact]
    public void MuscleLayer_MaskedToTheLegs_LeavesTheArmAlone()
    {
        (AnimationGraphInstance instance, Avatar avatar) = MuscleGraph(1f, HumanPoseMask.ForBodyPart(HumanBodyPart.Legs));

        Run(instance);

        Assert.Equal(0f, ArmAngle(avatar, instance.Pose), 0);
    }

    [Fact]
    public void MuscleLayer_NeedsAHumanoidAvatar()
    {
        Skeleton plain = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int a = graph.AddClip(TestClips.Const(plain, 0f));
        int b = graph.AddClip(TestClips.Const(plain, 1f));
        graph.SetRoot(graph.AddMuscleLayer(a, b));

        Assert.Throws<GraphValidationException>(() => graph.CreateInstance(plain));
    }

    [Fact]
    public void MuscleLayer_Additive_AddsTheLayerOnTop()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        var graph = new AnimationGraph();
        int basePose = graph.AddClip(PoseClip(avatar, ArmRaised(avatar, 20f)));
        int layerPose = graph.AddClip(PoseClip(avatar, ArmRaised(avatar, 30f)));
        int value = graph.AddFloatParameter("Weight", 1f);
        graph.SetRoot(graph.AddMuscleLayer(basePose, layerPose, value, additive: true));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);

        Run(instance);

        // The layer sits 30 degrees from the bind, so it lands near the base's 20 plus that 30. Muscle
        // values are added, not angles, and a muscle's range is not symmetric, so it is a few degrees short.
        float actual = ArmAngle(avatar, instance.Pose);
        Assert.True(MathF.Abs(actual) > MathF.Abs(ArmAngle(avatar, ArmRaised(avatar, 20f))), $"the layer did not add to the base, got {actual:N1}");
        Assert.True(MathF.Abs(actual - ArmAngle(avatar, ArmRaised(avatar, 50f))) < 8f, $"the layer added {actual:N1} instead of about 50 degrees");
    }
}
