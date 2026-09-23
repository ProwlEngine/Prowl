using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Copy Constraint node: puts a bone where another bone or a target is.</summary>
public class N_CopyConstraint_Tests
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
}
