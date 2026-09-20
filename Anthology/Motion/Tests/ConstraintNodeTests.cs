using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>The aim, copy and twist distribution constraint nodes.</summary>
public class ConstraintNodeTests
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

    private static Float3 AimOf(AnimationGraphInstance instance, Skeleton skeleton, StringID bone, Float3 axis)
    {
        instance.Pose.CalculateModelSpaceTransforms();
        return Float3.Normalize(instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(bone)).rotation * axis);
    }

    private static float AngleDeg(Float3 a, Float3 b)
        => MathF.Acos(Math.Clamp(Float3.Dot(Float3.Normalize(a), Float3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    // ---- aim -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(5f, 1f, 0f)]
    [InlineData(0f, 1f, 5f)]
    [InlineData(-4f, 3f, 2f)]
    public void Aim_PointsTheBoneAtItsTarget(float x, float y, float z)
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        var target = new Float3(x, y, z);
        int goal = graph.AddConstVector(target);
        graph.SetRoot(graph.AddAimConstraint(graph.AddClip(TestClips.Const(skeleton, 0f)), Turret, goal));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);

        Float3 expected = target - new Float3(0f, 1f, 0f);
        Assert.True(AngleDeg(AimOf(instance, skeleton, Turret, new Float3(0f, 1f, 0f)), expected) < 0.01f);
    }

    [Fact]
    public void Aim_CanUseAnotherAxisOfTheBone()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int goal = graph.AddConstVector(new Float3(0f, 1f, 6f));
        graph.SetRoot(graph.AddAimConstraint(graph.AddClip(TestClips.Const(skeleton, 0f)), Turret, goal, new Float3(0f, 0f, 1f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);

        Assert.True(AngleDeg(AimOf(instance, skeleton, Turret, new Float3(0f, 0f, 1f)), new Float3(0f, 0f, 1f)) < 0.01f);
    }

    [Fact]
    public void Aim_StopsAtItsAngleLimit()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int goal = graph.AddConstVector(new Float3(0f, 1f, 6f));
        var definition = new AimConstraintDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), Turret, goal) { MaxAngleDegrees = 30f };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);

        Assert.Equal(30f, AngleDeg(AimOf(instance, skeleton, Turret, new Float3(0f, 1f, 0f)), new Float3(0f, 1f, 0f)), 1);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void Aim_WeightsInTheTurn(float weight)
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int goal = graph.AddConstVector(new Float3(0f, 1f, 6f));
        var definition = new AimConstraintDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), Turret, goal) { Weight = weight };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);

        Assert.Equal(90f * weight, AngleDeg(AimOf(instance, skeleton, Turret, new Float3(0f, 1f, 0f)), new Float3(0f, 1f, 0f)), 1);
    }

    // The bone takes the shortest turn onto the target, so the roll the animation gave it survives.
    [Theory]
    [InlineData(40f)]
    [InlineData(-75f)]
    public void Aim_KeepsTheRollTheAnimationGaveTheBone(float rollDegrees)
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int goal = graph.AddConstVector(new Float3(0f, 1f, 6f));
        graph.SetRoot(graph.AddAimConstraint(graph.AddClip(Rolled(skeleton, rollDegrees, Turret, new Float3(0f, 1f, 0f)), loop: false), Turret, goal));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 40, 1f / 30f);

        Assert.True(AngleDeg(AimOf(instance, skeleton, Turret, new Float3(0f, 1f, 0f)), new Float3(0f, 0f, 1f)) < 0.01f);
        Assert.Equal(rollDegrees, AngleDeg(AimOf(instance, skeleton, Turret, new Float3(1f, 0f, 0f)), new Float3(1f, 0f, 0f)) * MathF.Sign(rollDegrees), 1);
    }

    [Fact]
    public void Aim_AtAMissingBone_ChangesNothing()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int goal = graph.AddConstVector(new Float3(0f, 1f, 6f));
        graph.SetRoot(graph.AddAimConstraint(graph.AddClip(TestClips.Const(skeleton, 0f)), new StringID("Nope"), goal));

        Exception? error = Record.Exception(() => Run(graph.CreateInstance(skeleton)));

        Assert.Null(error);
    }

    // ---- copy ----------------------------------------------------------------------------------

    [Fact]
    public void Copy_PutsOneBoneOnTopOfAnother()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddCopyConstraint(graph.AddClip(TestClips.Const(skeleton, 0f)), Prop, Turret));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);
        instance.Pose.CalculateModelSpaceTransforms();

        Assert.Equal(
            instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Turret)).position,
            instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Prop)).position);
    }

    [Fact]
    public void Copy_AppliesItsOffsetInTheSourceSpace()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        var definition = new CopyConstraintDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), Prop, Turret)
        {
            Offset = new Transform3D(new Float3(0f, 0.5f, 0f), Quaternion.Identity, Float3.One),
        };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);
        instance.Pose.CalculateModelSpaceTransforms();

        Assert.Equal(1.5f, instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Prop)).position.Y, 4);
    }

    [Fact]
    public void Copy_CanTakeRotationOnly()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddCopyConstraint(graph.AddClip(TestClips.Const(skeleton, 0f)), Prop, Turret, TransformChannels.Rotation));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);
        instance.Pose.CalculateModelSpaceTransforms();

        Assert.Equal(2f, instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Prop)).position.X, 4);
    }

    [Fact]
    public void Copy_HalfWeight_LandsHalfWay()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        var definition = new CopyConstraintDefinition(graph.AddClip(TestClips.Const(skeleton, 0f)), Prop, Turret) { Weight = 0.5f };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance);
        instance.Pose.CalculateModelSpaceTransforms();

        Float3 position = instance.Pose.GetModelSpaceTransform(skeleton.GetBoneIndex(Prop)).position;
        Assert.Equal(1f, position.X, 4);
        Assert.Equal(0.5f, position.Y, 4);
    }

    // Copying from a descendant would feed the node its own output, so it is rejected at bind.
    [Fact]
    public void Copy_FromItsOwnDescendant_IsRejected()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddCopyConstraint(graph.AddClip(TestClips.Const(skeleton, 0f)), TwistA, Hand));

        Assert.Throws<GraphValidationException>(() => graph.CreateInstance(skeleton));
    }

    // ---- twist distribution --------------------------------------------------------------------

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
}
