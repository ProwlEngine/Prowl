using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Pose Channel node: reading a float channel out of a pose.</summary>
public class N_PoseChannel_Tests
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
}
