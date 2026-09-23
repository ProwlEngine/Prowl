using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Float Channel Layer node: writing float channels onto a pose.</summary>
public class N_FloatChannelLayer_Tests
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

    private static float Channel(AnimationGraphInstance instance, int index) => instance.Pose.GetFloat(index);

    private static void Run(AnimationGraphInstance instance, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            instance.Update(step, Transform3D.Identity);
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

    [Theory]
    [InlineData(ChannelBlendMode.Override, 30f)]
    [InlineData(ChannelBlendMode.Add, 80f)]
    [InlineData(ChannelBlendMode.Max, 50f)]
    public void FloatChannelLayer_CombinesWithWhatTheClipAuthored(ChannelBlendMode mode, float expected)
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int value = graph.AddFloatParameter("Value", 30f);
        int clip = graph.AddClip(ChannelClip(skeleton, 50f, 50f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Smile, value, mode) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(expected, Channel(instance, 0), 3);
    }

    [Fact]
    public void FloatChannelLayer_LeavesTheBonesAlone()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int value = graph.AddFloatParameter("Value", 70f);
        int clip = graph.AddClip(BendClip(skeleton, 45f), loop: false);
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Smile, value) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 40, 1f / 30f);

        Assert.Equal(70f, Channel(instance, 0), 3);
        Assert.True(MathF.Abs(instance.Pose.GetTransform(1).rotation.X) > 0.3f, "the bend was lost");
    }

    [Fact]
    public void FloatChannelLayer_IgnoresChannelsTheRigDoesNotHave()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int value = graph.AddFloatParameter("Value", 12f);
        int clip = graph.AddClip(ChannelClip(skeleton, 0f, 0f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(new StringID("Missing"), value) }));

        Exception? error = Record.Exception(() => Run(graph.CreateInstance(skeleton), 2));

        Assert.Null(error);
    }

    [Fact]
    public void FloatChannelLayer_SkipsANonFiniteValue()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int bad = graph.AddFloatParameter("Bad", float.NaN);
        int clip = graph.AddClip(ChannelClip(skeleton, 25f, 25f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Smile, bad) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(25f, Channel(instance, 0), 3);
    }
}
