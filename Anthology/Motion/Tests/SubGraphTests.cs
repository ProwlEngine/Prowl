using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class SubGraphTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    // A reusable sub-graph: blends two clips by its own "Blend" parameter.
    private static AnimationGraph MakeBlendSubGraph(Skeleton skeleton)
    {
        var sub = new AnimationGraph();
        int blendParam = sub.AddFloatParameter("Blend");
        int a = sub.AddClip(ConstClip(skeleton, 0f));
        int b = sub.AddClip(ConstClip(skeleton, 10f));
        int blend = sub.AddBlend1D(blendParam, new[] { (a, 0f), (b, 1f) });
        sub.SetRoot(blend);
        return sub;
    }

    [Fact]
    public void ReferencedGraph_ForwardsParameterAndOutputsChildPose()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationGraph sub = MakeBlendSubGraph(skeleton);

        var graph = new AnimationGraph();
        int hostBlend = graph.AddFloatParameter("HostBlend");
        int sg = graph.AddReferencedGraph(sub);
        graph.LinkGraphParameter(sg, hostBlend, "Blend");
        graph.SetRoot(sg);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.SetFloat("HostBlend", 0f);
        instance.Update(0.016f);
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetFloat("HostBlend", 1f);
        instance.Update(0.016f);
        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetFloat("HostBlend", 0.5f);
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void ExternalGraphSlot_UsesFallbackUntilGraphPluggedIn()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        var graph = new AnimationGraph();
        int fallback = graph.AddClip(ConstClip(skeleton, 2f));
        int slot = graph.AddExternalGraphSlot("Action", fallback);
        graph.SetRoot(slot);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.016f);
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3); // fallback

        // Plug a runtime graph into the slot.
        var external = new AnimationGraph();
        int eClip = external.AddClip(ConstClip(skeleton, 9f));
        external.SetRoot(eClip);
        AnimationGraphInstance externalInstance = external.CreateInstance(skeleton);

        instance.SetExternalGraph("Action", externalInstance);
        instance.Update(0.016f);
        Assert.Equal(9.0, (double)instance.Pose.GetTransform(0).position.Z, 3); // external

        // Clear it again -> back to fallback.
        instance.SetExternalGraph("Action", null);
        instance.Update(0.016f);
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void SetExternalGraph_UnknownSlot_Throws()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int refPose = graph.AddReferencePose();
        graph.SetRoot(refPose);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Assert.Throws<ArgumentException>(() => instance.SetExternalGraph("Nope", null));
    }
}
