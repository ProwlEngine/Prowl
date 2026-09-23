using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Muscle Layer node: layering in muscle space, with masks, channels and events.</summary>
public class N_MuscleLayer_Tests
{
    private static void Run(AnimationGraphInstance instance, int frames = 2, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

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

    // Muscle space holds bones only, so a layer that goes through it used to leave the channels at
    // whatever they held the last time its weight was zero.
    [Theory]
    [InlineData(0f, 10f)]
    [InlineData(0.5f, 40f)]
    [InlineData(1f, 70f)]
    public void MuscleLayer_CarriesChannelsAtEveryWeight(float weight, float expected)
    {
        Avatar avatar = RigWithChannels("Smile");
        var graph = new AnimationGraph();
        int basePose = graph.AddClip(Clip(avatar, Frame(avatar, 10f)));
        int layerPose = graph.AddClip(Clip(avatar, Frame(avatar, 70f)));
        graph.SetRoot(graph.AddMuscleLayer(basePose, layerPose, graph.AddFloatParameter("Weight", weight)));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);

        Run(instance);

        Assert.Equal(expected, instance.Pose.GetFloat(0), 3);
    }

    [Fact]
    public void MuscleLayer_Additive_AddsTheLayersChannelsOnTop()
    {
        Avatar avatar = RigWithChannels("Smile");
        var graph = new AnimationGraph();
        int basePose = graph.AddClip(Clip(avatar, Frame(avatar, 10f)));
        int layerPose = graph.AddClip(Clip(avatar, Frame(avatar, 25f)));
        graph.SetRoot(graph.AddMuscleLayer(basePose, layerPose, graph.AddFloatParameter("Weight", 1f), additive: true));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);

        Run(instance);

        Assert.Equal(35f, instance.Pose.GetFloat(0), 3);
    }

    // With a reference node the layer contributes only how far it sits from that reference.
    [Fact]
    public void MuscleLayer_Additive_AgainstAReferenceNode_AddsOnlyTheDifference()
    {
        Avatar avatar = RigWithChannels("Smile");
        var graph = new AnimationGraph();
        int basePose = graph.AddClip(Clip(avatar, Frame(avatar, 10f)));
        int layerPose = graph.AddClip(Clip(avatar, Frame(avatar, 25f)));
        int reference = graph.AddClip(Clip(avatar, Frame(avatar, 20f)));
        var definition = new MuscleLayerDefinition(basePose, layerPose, graph.AddFloatParameter("Weight", 1f))
        {
            Additive = true,
            ReferenceNodeIndex = reference,
        };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);

        Run(instance);

        Assert.Equal(15f, instance.Pose.GetFloat(0), 3);
    }

    // The reference pose used to be encoded only when the definition was already additive at bind time.
    [Fact]
    public void MuscleLayer_TurnedAdditiveAfterBinding_StillMeasuresAgainstTheRig()
    {
        Avatar avatar = RigWithChannels("Smile");
        var graph = new AnimationGraph();
        int basePose = graph.AddClip(Clip(avatar, Frame(avatar, 10f)));
        int layerPose = graph.AddClip(Clip(avatar, Frame(avatar, 25f)));
        var definition = new MuscleLayerDefinition(basePose, layerPose, graph.AddFloatParameter("Weight", 1f));
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);

        definition.Additive = true;
        Run(instance);

        Assert.Equal(35f, instance.Pose.GetFloat(0), 3);
    }

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

    [Fact]
    public void MuscleLayer_FiresItsLayersEventsByItsWeight()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        Pose pose = new(skeleton);
        pose.SetToReferencePose();

        var graph = new AnimationGraph();
        int basePose = graph.AddClip(new AnimationClip(skeleton, new[] { pose, pose }, 1f));
        int layer = graph.AddClip(new AnimationClip(skeleton, new[] { pose, pose }, 1f, events: new AnimationEvent[] { new IdEvent(new StringID("Layer"), 0f, 1f) }));
        graph.SetRoot(graph.AddMuscleLayer(basePose, layer, graph.AddConstFloat(0f)));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);
        instance.Update(1f / 60f);

        Assert.Contains(instance.Events, e => e.Event is IdEvent);
        Assert.All(instance.Events, e => Assert.Equal(0f, e.Weight));
    }

    [Fact]
    public void AMaskedMuscleLayer_LeavesChannelsItsMaskKeepsOut()
    {
        Skeleton humanoid = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        var ids = new StringID[humanoid.BoneCount];
        for (int i = 0; i < ids.Length; i++) ids[i] = humanoid.GetBoneID(i);
        var skeleton = new Skeleton(ids, humanoid.ParentBoneIndices.ToArray(), humanoid.ParentSpaceReferencePose.ToArray(),
            -1, new[] { new StringID("Smile"), new StringID("Blink") });
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);

        var rest = new Pose(skeleton);
        rest.SetToReferencePose();
        var face = new Pose(skeleton);
        face.SetToReferencePose();
        face.SetFloat(0, 1f);
        face.SetFloat(1, 1f);

        HumanPoseMask mask = HumanPoseMask.ForBodyPart(HumanBodyPart.Arms);
        mask.SetChannelWeight(new StringID("Smile"), 0f);

        var graph = new AnimationGraph();
        int basePose = graph.AddClip(new AnimationClip(skeleton, new[] { rest, rest }, 1f));
        int layer = graph.AddClip(new AnimationClip(skeleton, new[] { face, face }, 1f));
        graph.SetRoot(graph.AddMuscleLayer(basePose, layer, mask: mask));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);
        instance.Update(1f / 60f);

        Assert.Equal(0f, instance.Pose.GetFloat(0), 4);
        Assert.Equal(1f, instance.Pose.GetFloat(1), 4);
    }
}
