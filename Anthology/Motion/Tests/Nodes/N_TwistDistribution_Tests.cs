using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Twist Distribution node: spreading a bone's roll across twist bones.</summary>
public class N_TwistDistribution_Tests
{
    private static readonly StringID Root = new("Root");

    private static readonly StringID Turret = new("Turret");

    private static readonly StringID Prop = new("Prop");

    private static readonly StringID Hand = new("Hand");

    private static readonly StringID TwistA = new("TwistA");

    private static readonly StringID TwistB = new("TwistB");

    // Root, a turret one unit up, a free prop bone, plus a forearm chain with two twist bones.
    private static Skeleton Rig()
    {
        var ids = new[] { Root, Turret, Prop, TwistA, TwistB, Hand };
        var parents = new[] { Skeleton.InvalidIndex, 0, 0, 0, 3, 4 };
        var pose = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(2f, 0f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(1f, 0f, 0f), Quaternion.Identity, Float3.One),
        };
        return new Skeleton(ids, parents, pose);
    }

    private static void Run(AnimationGraphInstance instance, int frames = 2, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

    private static AnimationClip Rolled(Skeleton skeleton, float degrees) => Rolled(skeleton, degrees, Hand, new Float3(1f, 0f, 0f));

    private static AnimationClip Rolled(Skeleton skeleton, float degrees, StringID bone, Float3 axis)
    {
        Pose Frame(float angle)
        {
            var pose = new Pose(skeleton);
            pose.SetToReferencePose();
            int index = skeleton.GetBoneIndex(bone);
            Transform3D bind = pose.GetTransform(index);
            pose.SetTransform(index, new Transform3D(bind.position, Quaternion.AxisAngle(axis, angle * MathF.PI / 180f), bind.scale));
            return pose;
        }
        return new AnimationClip(skeleton, new[] { Frame(0f), Frame(degrees) }, 1f);
    }

    private static float RollDeg(AnimationGraphInstance instance, Skeleton skeleton, StringID bone)
    {
        Quaternion rotation = instance.Pose.GetTransform(skeleton.GetBoneIndex(bone)).rotation;
        if (rotation.W < 0f)
            rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
        return 2f * MathF.Atan2(rotation.X, rotation.W) * 180f / MathF.PI;
    }

    private const float Deg = MathF.PI / 180f;

    private static readonly StringID Forearm = new("Forearm");

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

    [Theory]
    [InlineData(90f)]
    [InlineData(-60f)]
    public void Twist_SpreadsTheDriverRollAcrossTheTwistBones(float degrees)
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(Rolled(skeleton, degrees), loop: false);
        graph.SetRoot(graph.AddTwistDistribution(clip, Hand, new[] { (TwistA, 0.25f), (TwistB, 0.5f) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 40, 1f / 30f);

        Assert.Equal(degrees * 0.25f, RollDeg(instance, skeleton, TwistA), 1);
        Assert.Equal(degrees * 0.5f, RollDeg(instance, skeleton, TwistB), 1);
    }

    [Fact]
    public void Twist_LeavesTheDriverAlone()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(Rolled(skeleton, 80f), loop: false);
        graph.SetRoot(graph.AddTwistDistribution(clip, Hand, new[] { (TwistA, 0.5f) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 40, 1f / 30f);

        Assert.Equal(80f, RollDeg(instance, skeleton, Hand), 1);
    }

    [Fact]
    public void Twist_WithNoRoll_LeavesTheRigAtItsBind()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(Rolled(skeleton, 0f), loop: false);
        graph.SetRoot(graph.AddTwistDistribution(clip, Hand, new[] { (TwistA, 0.5f), (TwistB, 0.5f) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 20, 1f / 30f);

        Assert.Equal(0f, RollDeg(instance, skeleton, TwistA), 3);
        Assert.Equal(0f, RollDeg(instance, skeleton, TwistB), 3);
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

    [Fact]
    public void TwistDistribution_TurnsTwistBonesAboutTheDriversOwnAxis()
    {
        // The driver lies turned a quarter about Z, so its own X is the model's Y, and the twist bone does not share its frame.
        Quaternion driverFrame = Quaternion.AxisAngle(Float3.UnitZ, MathF.PI / 2f);
        var skeleton = new Skeleton(new[] { new StringID("Root"), new StringID("Driver"), new StringID("Twist") },
            new[] { Skeleton.InvalidIndex, 0, 0 },
            new[] { Transform3D.Identity, new Transform3D(Float3.Zero, driverFrame, Float3.One), Transform3D.Identity });

        var rolled = new Pose(skeleton);
        rolled.SetToReferencePose();
        rolled.SetTransform(1, new Transform3D(Float3.Zero, driverFrame * Quaternion.AxisAngle(Float3.UnitX, MathF.PI / 3f), Float3.One));

        var graph = new AnimationGraph();
        int clip = graph.AddClip(new AnimationClip(skeleton, new[] { rolled, rolled }, 1f));
        graph.SetRoot(graph.AddTwistDistribution(clip, new StringID("Driver"), new[] { (new StringID("Twist"), 1f) }, Float3.UnitX));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(1f / 60f);

        Quaternion expected = Quaternion.AxisAngle(Float3.UnitY, MathF.PI / 3f);
        Assert.True(MathF.Abs(Quaternion.Dot(instance.Pose.GetTransform(2).rotation, expected)) > 0.999f);
    }
}
