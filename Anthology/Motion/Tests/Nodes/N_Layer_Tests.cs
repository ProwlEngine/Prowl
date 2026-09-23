using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Override Layer node.</summary>
public class N_Layer_Tests
{
    private static AnimationClip ConstClip(Skeleton skeleton, int bone, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(bone, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void OverrideLayer_BlendsByWeight()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int w = graph.AddFloatParameter("W");
        int basePose = graph.AddClip(ConstClip(skeleton, 0, 0f));
        int layer = graph.AddClip(ConstClip(skeleton, 0, 8f));
        int layered = graph.AddOverrideLayer(basePose, layer, w);
        graph.SetRoot(layered);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("W", 0.25f);
        instance.Update(0.016f);
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3); // lerp(0,8,0.25)
    }
}
