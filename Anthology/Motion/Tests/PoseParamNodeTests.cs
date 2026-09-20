using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class PoseParamNodeTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    private static AnimationClip Ramp(Skeleton skeleton)
    {
        var f0 = new Pose(skeleton); f0.SetToReferencePose();
        f0.SetTransform(0, new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One));
        var f1 = new Pose(skeleton); f1.SetToReferencePose();
        f1.SetTransform(0, new Transform3D(new Float3(0f, 0f, 10f), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { f0, f1 }, 1f);
    }

    [Fact]
    public void ZeroPose_IsIdentity()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        g.SetRoot(g.AddZeroPose());
        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.Update(0.016f);
        Assert.Equal(0.0, (double)i.Pose.GetTransform(0).position.Z, 4);
    }

    [Fact]
    public void AnimationPose_SamplesAtValueNodeTime()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int time = g.AddFloatParameter("T", 0.5f);
        g.SetRoot(g.AddAnimationPose(Ramp(skeleton), time));
        AnimationGraphInstance i = g.CreateInstance(skeleton);

        i.Update(0.016f);
        Assert.Equal(5.0, (double)i.Pose.GetTransform(0).position.Z, 3);

        i.SetFloat("T", 0.8f);
        i.Update(0.016f);
        Assert.Equal(8.0, (double)i.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void ConditionSelector_PicksFirstTrue()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int cond = g.AddBoolParameter("UseA");
        int a = g.AddClip(ConstClip(skeleton, 1f));
        int b = g.AddClip(ConstClip(skeleton, 5f));
        g.SetRoot(g.AddConditionSelector(new[] { (a, cond), (b, -1) }));
        AnimationGraphInstance i = g.CreateInstance(skeleton);

        i.Update(0.016f); // UseA false -> fallback to B
        Assert.Equal(5.0, (double)i.Pose.GetTransform(0).position.Z, 3);

        i.SetBool("UseA", true);
        i.Update(0.016f);
        Assert.Equal(1.0, (double)i.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void Passthrough_ForwardsChild()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int clip = g.AddClip(ConstClip(skeleton, 3f));
        g.SetRoot(g.AddPassthrough(clip));
        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.Update(0.016f);
        Assert.Equal(3.0, (double)i.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void VirtualParameter_IsFindableByName()
    {
        var g = new AnimationGraph();
        int math = g.AddFloatMath(g.AddConstFloat(2f), g.AddConstFloat(3f), FloatMathOp.Add);
        int vp = g.AddVirtualParameter("Sum", math);
        g.SetRoot(g.AddReferencePose());

        Assert.Equal(math, vp);
        Assert.Equal(math, g.GetNodeIndex("Sum"));

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Assert.Equal(5.0, (double)i.EvaluateValueNode(g.GetNodeIndex("Sum")).AsFloat(), 4);
    }
}
