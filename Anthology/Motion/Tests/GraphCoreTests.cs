using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class GraphCoreTests
{
    // A one-frame clip holding bone 0 at a constant Z, for predictable blend math.
    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void Graph_PlaysAClip()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(ConstClip(skeleton, 7f));
        graph.SetRoot(clip);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.1f);

        Assert.Equal(7.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void Blend1D_BlendsByParameter()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed");
        int idle = graph.AddClip(ConstClip(skeleton, 0f));
        int run = graph.AddClip(ConstClip(skeleton, 10f));
        int blend = graph.AddBlend1D(speed, new[] { (idle, 0f), (run, 1f) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.SetFloat("Speed", 0.5f);
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetFloat("Speed", 1f);
        instance.Update(0.016f);
        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void Blend1D_ClampsBelowFirstThreshold()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed");
        int a = graph.AddClip(ConstClip(skeleton, 2f));
        int b = graph.AddClip(ConstClip(skeleton, 8f));
        int blend = graph.AddBlend1D(speed, new[] { (a, 1f), (b, 3f) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("Speed", -5f); // below first threshold -> clamps to a
        instance.Update(0.016f);
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void MissingParameter_Throws()
    {
        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddReferencePose());
        AnimationGraphInstance instance = graph.CreateInstance(TestSkeletons.MakeChain());
        Assert.Throws<ArgumentException>(() => instance.SetFloat("Nope", 1f));
    }

    [Fact]
    public void NoRoot_ThrowsOnCreate()
    {
        var graph = new AnimationGraph();
        graph.AddReferencePose();
        Assert.Throws<InvalidOperationException>(() => graph.CreateInstance(TestSkeletons.MakeChain()));
    }
}
