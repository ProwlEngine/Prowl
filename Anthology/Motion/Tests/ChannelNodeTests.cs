using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Nodes that read and write the pose's float channels.</summary>
public class ChannelNodeTests
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

    // ---- reading -------------------------------------------------------------------------------

    [Fact]
    public void PoseChannel_ReadsTheValueAuthoredIntoAClip()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(ChannelClip(skeleton, 0f, 100f), loop: false);
        int channel = graph.AddPoseChannel(clip, Smile);
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Blink, channel) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 30, 1f / 30f);

        Assert.Equal(Channel(instance, 0), Channel(instance, 1), 2);
        Assert.True(Channel(instance, 1) > 50f, "the authored channel never reached the layer");
    }

    [Fact]
    public void PoseChannel_OnAMissingChannel_ReportsItsDefault()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(ChannelClip(skeleton, 0f, 100f));
        int channel = graph.AddNode(new PoseChannelDefinition(clip, new StringID("NotThere")) { DefaultValue = 42f });
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Blink, channel) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Run(instance, 2);

        Assert.Equal(42f, Channel(instance, 1), 3);
    }

    // Reading a pose must not count as playing it, or the graph would reject the second parent.
    [Fact]
    public void PoseChannel_DoesNotClaimThePoseNode()
    {
        Skeleton skeleton = Rig();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(ChannelClip(skeleton, 10f, 10f));
        graph.AddPoseChannel(clip, Smile);
        graph.SetRoot(clip);

        Exception? error = Record.Exception(() => graph.CreateInstance(skeleton).Update(1f / 60f, Transform3D.Identity));

        Assert.Null(error);
    }

    // ---- writing -------------------------------------------------------------------------------

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

    // ---- driven by a joint ---------------------------------------------------------------------

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
