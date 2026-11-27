using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Float Remap node.</summary>
public class N_FloatRemap_Tests
{
    private static float Read(AnimationGraph graph, Skeleton skeleton, int node)
    {
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.016f);
        return instance.EvaluateValueNode(node).AsFloat();
    }

    [Fact]
    public void Remap_ClampKeepsTheResultInsideTheOutputRange()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int input = graph.AddConstFloat(5f);
        int free = graph.AddNode(new FloatRemapDefinition(input, 0f, 1f, 0f, 10f));
        int clamped = graph.AddNode(new FloatRemapDefinition(input, 0f, 1f, 0f, 10f) { Clamp = true });
        graph.SetRoot(graph.AddReferencePose());

        Assert.Equal(50f, Read(graph, skeleton, free), 3);
        Assert.Equal(10f, Read(graph, skeleton, clamped), 3);
    }

    [Fact]
    public void Remap_DrivesABlendFromAnotherRange()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int raw = graph.AddFloatParameter("Raw"); // 0..100
        int normalized = graph.AddFloatRemap(raw, 0f, 100f, 0f, 1f); // -> 0..1
        int a = graph.AddClip(TestClips.Const(skeleton, 0f));
        int b = graph.AddClip(TestClips.Const(skeleton, 10f));
        int blend = graph.AddBlend1D(normalized, new[] { (a, 0f), (b, 1f) });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("Raw", 50f); // -> 0.5 -> blend midpoint
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 2);
    }
}
