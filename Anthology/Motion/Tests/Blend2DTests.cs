using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class Blend2DTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void Blend2D_AtSamplePoint_OutputsThatSample()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int x = graph.AddFloatParameter("X");
        int y = graph.AddFloatParameter("Y");
        int a = graph.AddClip(ConstClip(skeleton, 1f));
        int b = graph.AddClip(ConstClip(skeleton, 2f));
        int c = graph.AddClip(ConstClip(skeleton, 3f));
        int blend = graph.AddBlend2D(x, y, new[]
        {
            (a, new Float2(-1f, 0f)),
            (b, new Float2(1f, 0f)),
            (c, new Float2(0f, 1f)),
        });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.SetFloat("X", 0f);
        instance.SetFloat("Y", 1f); // exactly sample c
        instance.Update(0.016f);
        Assert.Equal(3.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void Blend2D_NearSample_LeansTowardIt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int x = graph.AddFloatParameter("X");
        int y = graph.AddFloatParameter("Y");
        int a = graph.AddClip(ConstClip(skeleton, 0f));
        int b = graph.AddClip(ConstClip(skeleton, 10f));
        int blend = graph.AddBlend2D(x, y, new[] { (a, new Float2(0f, 0f)), (b, new Float2(2f, 0f)) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("X", 1.8f); // close to b
        instance.SetFloat("Y", 0f);
        instance.Update(0.016f);
        Assert.True(instance.Pose.GetTransform(0).position.Z > 8f);
    }
}
