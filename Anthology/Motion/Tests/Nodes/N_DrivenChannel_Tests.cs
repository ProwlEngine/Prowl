using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Driven Channel node: a float channel driven by how far a bone has turned.</summary>
public class N_DrivenChannel_Tests
{
    private static readonly StringID Smile = new("Smile");

    private static readonly StringID Blink = new("Blink");

    private static readonly StringID Elbow = new("Elbow");

    // Root -> Elbow, with two scalar channels.
    private static Skeleton Rig()
    {
        var ids = new[] { new StringID("Root"), Elbow };
        var parents = new[] { Skeleton.InvalidIndex, 0 };
        var pose = new[] { Transform3D.Identity, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One) };
        return new Skeleton(ids, parents, pose, -1, new[] { Smile, Blink });
    }

    // A clip bending the elbow to the given angle over its length.
    private static AnimationClip BendClip(Skeleton skeleton, float degrees)
    {
        Pose Frame(float angle)
        {
            var pose = new Pose(skeleton);
            pose.SetToReferencePose();
            pose.SetTransform(1, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.AxisAngle(new Float3(1f, 0f, 0f), angle * MathF.PI / 180f), Float3.One));
            return pose;
        }
        return new AnimationClip(skeleton, new[] { Frame(0f), Frame(degrees) }, 1f);
    }

    private static float Channel(AnimationGraphInstance instance, int index) => instance.Pose.GetFloat(index);

    private static void Run(AnimationGraphInstance instance, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
    }

    private static AnimationClip ChannelClip(Skeleton skeleton, float startSmile, float endSmile)
    {
        Pose Frame(float smile)
        {
            var pose = new Pose(skeleton);
            pose.SetToReferencePose();
            pose.SetFloat(0, smile);
            return pose;
        }
        return new AnimationClip(skeleton, new[] { Frame(startSmile), Frame(endSmile) }, 1f);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(45f, 0.5f)]
    [InlineData(90f, 1f)]
    [InlineData(120f, 1f)]
    public void DrivenChannel_FollowsTheBoneAngle(float bend, float expected)
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(BendClip(skeleton, bend), loop: false);
        graph.SetRoot(graph.AddDrivenChannel(clip, Elbow, Smile, 0f, 90f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 60, 1f / 30f);

        Assert.Equal(expected, Channel(instance, 0), 2);
    }

    // About an axis the angle is signed, so a joint bending the other way reads zero rather than full.
    [Fact]
    public void DrivenChannel_AboutAnAxis_IsSigned()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(BendClip(skeleton, -60f), loop: false);
        var definition = new DrivenChannelDefinition(clip, Elbow, Smile, 0f, 90f) { Axis = new Float3(1f, 0f, 0f) };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 60, 1f / 30f);

        Assert.Equal(0f, Channel(instance, 0), 3);
    }

    [Fact]
    public void DrivenChannel_CanMapOntoItsOwnRange()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(BendClip(skeleton, 90f), loop: false);
        var definition = new DrivenChannelDefinition(clip, Elbow, Smile, 0f, 90f) { FromValue = 10f, ToValue = 100f };
        graph.SetRoot(graph.AddNode(definition));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 60, 1f / 30f);

        Assert.Equal(100f, Channel(instance, 0), 2);
    }

    [Fact]
    public void DrivenChannel_OnAMissingBone_ChangesNothing()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(ChannelClip(skeleton, 33f, 33f));
        graph.SetRoot(graph.AddDrivenChannel(clip, new StringID("Nope"), Smile, 0f, 90f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(33f, Channel(instance, 0), 3);
    }
}
