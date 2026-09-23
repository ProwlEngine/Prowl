using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Curve node: shapes a value through an authored curve.</summary>
public class N_Curve_Tests
{
    // Reads a value node by driving a clip's playback speed with it would be indirect, so the value is
    // written onto a float channel instead, where a test can read it straight off the pose.
    private static readonly StringID Channel = new("Value");

    private static Skeleton ChannelRig()
        => new(new[] { new StringID("Root") }, new[] { Skeleton.InvalidIndex }, new[] { Transform3D.Identity }, -1, new[] { Channel });

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 10f)]
    [InlineData(2f, 10f)]
    public void Curve_ShapesItsInput(float input, float expected)
    {
        var curve = new AnimationCurve();
        curve.AddKey(0f, 0f);
        curve.AddKey(1f, 10f);

        Skeleton skeleton = ChannelRig();
        var graph = new AnimationGraph();
        int value = graph.AddFloatParameter("Value", input);
        int shaped = graph.AddCurve(value, curve);
        int clip = graph.AddClip(TestClips.Const(skeleton, 0f));
        graph.SetRoot(graph.AddFloatChannelLayer(clip, new[] { new ChannelDriver(Channel, shaped) }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f, Transform3D.Identity);

        Assert.Equal(expected, instance.Pose.GetFloat(0), 2);
    }
}
