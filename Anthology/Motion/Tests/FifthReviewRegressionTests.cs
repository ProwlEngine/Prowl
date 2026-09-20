using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Regressions from the fifth adversarial review: scalar channels crossing muscle space, and playback under hostile deltas.</summary>
public class FifthReviewRegressionTests
{
    private const float Deg = MathF.PI / 180f;

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

    // ---- muscle layer channels -----------------------------------------------------------------

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

    // ---- humanoid clip channels ----------------------------------------------------------------

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

    // The root motion track keeps its own duration, which need not be the clip's.
    [Fact]
    public void ARescaledRootMotionKeepsItsOwnDuration()
    {
        Avatar source = new HumanoidTestRig().BuildAvatar();
        Avatar tall = new HumanoidTestRig { LegScale = 2f }.BuildAvatar();
        var frames = new[] { Transform3D.Identity, new Transform3D(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One) };
        var rootMotion = new RootMotion(frames, 4f);
        HumanoidClip clip = HumanoidClip.FromFrames(new[] { new HumanPose(), new HumanPose() }, 1f, source.Humanoid!.Scale, rootMotion);

        Assert.Equal(4f, clip.Bind(tall).RootMotion!.Duration, 3);
    }

    // ---- external pose channels ----------------------------------------------------------------

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

    // ---- pose copying --------------------------------------------------------------------------

    [Fact]
    public void CopyingAPoseWithFewerChannels_ClearsTheRest()
    {
        Avatar with = RigWithChannels("Smile");
        Avatar without = new HumanoidTestRig().BuildAvatar();
        var target = new Pose(with.Skeleton);
        target.SetToReferencePose();
        target.SetFloat(0, 9f);

        target.CopyFrom(HumanoidTestRig.BindPose(without));

        Assert.Equal(0f, target.GetFloat(0), 3);
    }

    [Fact]
    public void AMappedSampleChecksTheChannelCount()
    {
        Skeleton source = TestSkeletons.MakeChain();
        var target = new Skeleton(
            Enumerable.Range(0, source.BoneCount).Select(source.GetBoneID).ToArray(),
            Enumerable.Range(0, source.BoneCount).Select(source.GetParentBoneIndex).ToArray(),
            Enumerable.Range(0, source.BoneCount).Select(source.GetBoneParentSpaceTransform).ToArray(),
            -1,
            new[] { new StringID("Smile") });

        SkeletonMapping mapping = SkeletonMapping.Create(source, target);
        var wrongPose = new Pose(source);

        Assert.Throws<ArgumentException>(() => TestClips.Const(source, 0f).GetPose(0f, wrongPose, mapping));
    }

    // ---- playback under hostile deltas ----------------------------------------------------------

    [Theory]
    [InlineData(1e6f)]
    [InlineData(1e12f)]
    [InlineData(float.MaxValue)]
    public void AHugeStepStaysInRangeAndBoundsItsWraps(float delta)
    {
        var cursor = new ClipCursor();

        PlaybackSpan span = cursor.Advance(delta, 0.033f, loop: true, includeStart: false);

        Assert.InRange(cursor.Time, 0f, 1f);
        Assert.InRange(span.Wraps, 0, ClipCursor.MaxWrapsPerStep);
    }

    [Fact]
    public void AHugeStepDoesNotStallTheGraph()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        var frame = new Pose(skeleton);
        frame.SetToReferencePose();
        graph.SetRoot(graph.AddClip(new AnimationClip(skeleton, new[] { frame, frame }, 0.033f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        instance.Update(1e6f, Transform3D.Identity);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 500, $"one update took {watch.ElapsedMilliseconds} ms");
    }

    // Landing exactly on the end of a looping clip is a wrap, otherwise a paused clip reads as finished.
    [Fact]
    public void AStepLandingExactlyOnTheEndWraps()
    {
        var cursor = new ClipCursor { Time = 0.5f };

        PlaybackSpan span = cursor.Advance(0.5f, 1f, loop: true, includeStart: false);

        Assert.Equal(0f, cursor.Time, 5);
        Assert.Equal(1, span.Wraps);
        Assert.Equal(1, cursor.LoopCount);
    }

    // ---- twist distribution --------------------------------------------------------------------

    private static readonly StringID Root = new("Root");
    private static readonly StringID Forearm = new("Forearm");
    private static readonly StringID Hand = new("Hand");
    private static readonly StringID Twist = new("Twist");

    // The twist bone is turned a quarter about Y from the driver's parent, so the axis has to be
    // carried into its space rather than reused as written.
    private static Skeleton TurnedTwistRig()
    {
        var ids = new[] { Root, Forearm, Hand, Twist };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1, 0 };
        var pose = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0.5f, 0f, 0f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 90f * Deg), Float3.One),
        };
        return new Skeleton(ids, parents, pose);
    }

    [Fact]
    public void Twist_TurnsATurnedBoneAboutTheDriversAxis()
    {
        Skeleton skeleton = TurnedTwistRig();
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        int hand = skeleton.GetBoneIndex(Hand);
        Transform3D bind = pose.GetTransform(hand);
        pose.SetTransform(hand, new Transform3D(bind.position, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 80f * Deg), bind.scale));

        var graph = new AnimationGraph();
        int clip = graph.AddClip(new AnimationClip(skeleton, new[] { pose, pose }, 1f), loop: false);
        graph.SetRoot(graph.AddTwistDistribution(clip, Hand, new[] { (Twist, 0.5f) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 4);
        instance.Pose.CalculateModelSpaceTransforms();

        int twist = skeleton.GetBoneIndex(Twist);
        Quaternion turned = instance.Pose.GetModelSpaceTransform(twist).rotation;
        Quaternion bound = skeleton.GetBoneModelSpaceTransform(twist).rotation;
        Float3 modelAxis = new(1f, 0f, 0f);

        // Turning about the driver's axis leaves that axis alone and swings anything across it.
        Assert.True(HumanoidTestRig.AngleDeg(turned * (Quaternion.Inverse(bound) * modelAxis), modelAxis) < 0.05f);
        Assert.Equal(40f, HumanoidTestRig.AngleDeg(turned * (Quaternion.Inverse(bound) * new Float3(0f, 1f, 0f)), new Float3(0f, 1f, 0f)), 1);
    }

    // ---- spring chains -------------------------------------------------------------------------

    // A named chain need not be a straight parent to child walk, so the model space shortcut has to
    // fall back to the pose when it is not.
    [Fact]
    public void SpringBones_SwingAChainThatSkipsABone()
    {
        var ids = new[] { Root, new StringID("A"), new StringID("B"), new StringID("C") };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1, 0 };
        var pose = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One),
        };
        var skeleton = new Skeleton(ids, parents, pose);

        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddSpringBones(clip, new[] { ids[1], ids[3] }, gravity: new Float3(4f, 0f, 0f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        for (int i = 0; i < 90; i++)
            instance.Update(1f / 60f, Transform3D.Identity);
        instance.Pose.CalculateModelSpaceTransforms();

        Quaternion swung = instance.Pose.GetTransform(1).rotation;
        Assert.True(HumanoidTestRig.AngleDeg(swung, Quaternion.Identity) > 1f, "gravity across the chain should have swung it");
    }
}
