using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class ValueNodeExtraTests
{
    private static (AnimationGraph Graph, int Root) NewGraph()
    {
        var g = new AnimationGraph();
        int root = g.AddReferencePose();
        return (g, root);
    }

    [Fact]
    public void FloatAbs_Switch_RangeComparison_AngleMath()
    {
        var (g, root) = NewGraph();
        int abs = g.AddFloatAbs(g.AddConstFloat(-3.5f));
        int sw = g.AddFloatSwitch(g.AddConstBool(true), g.AddConstFloat(11f), g.AddConstFloat(22f));
        int range = g.AddFloatRangeComparison(g.AddConstFloat(5f), 0f, 10f);
        int angle = g.AddFloatAngleMath(g.AddConstFloat(270f), AngleOp.ClampTo180);
        g.SetRoot(root);

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Assert.Equal(3.5, (double)i.EvaluateValueNode(abs).AsFloat(), 4);
        Assert.Equal(11.0, (double)i.EvaluateValueNode(sw).AsFloat(), 4);
        Assert.True(i.EvaluateValueNode(range).AsBool());
        Assert.Equal(-90.0, (double)i.EvaluateValueNode(angle).AsFloat(), 3); // 270 wrapped to [-180,180]
    }

    [Fact]
    public void FloatSelector_PicksFirstTrueCondition()
    {
        var (g, root) = NewGraph();
        int sel = g.AddFloatSelector(
            new[] { g.AddConstBool(false), g.AddConstBool(true) },
            new[] { 1f, 2f },
            defaultValue: 9f);
        g.SetRoot(root);

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Assert.Equal(2.0, (double)i.EvaluateValueNode(sel).AsFloat(), 4);
    }

    [Fact]
    public void VectorNegate_And_TargetNodes()
    {
        var (g, root) = NewGraph();
        int neg = g.AddVectorNegate(g.AddVectorCreate(g.AddConstFloat(1f), g.AddConstFloat(-2f), g.AddConstFloat(3f)));
        int tparam = g.AddTargetParameter("T");
        int isSet = g.AddIsTargetSet(tparam);
        int dist = g.AddTargetInfo(tparam, TargetInfo.Distance);
        int point = g.AddTargetPoint(tparam);
        g.SetRoot(root);

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Float3 n = i.EvaluateValueNode(neg).Vector;
        Assert.Equal(-1.0, (double)n.X, 4);
        Assert.Equal(2.0, (double)n.Y, 4);
        Assert.Equal(-3.0, (double)n.Z, 4);

        Assert.False(i.EvaluateValueNode(isSet).AsBool()); // unset default target

        i.SetTarget("T", Target.FromWorld(new Transform3D(new Float3(0f, 0f, 4f), Quaternion.Identity, Float3.One)));
        Assert.True(i.EvaluateValueNode(isSet).AsBool());
        Assert.Equal(4.0, (double)i.EvaluateValueNode(dist).AsFloat(), 3);
        Assert.Equal(4.0, (double)i.EvaluateValueNode(point).Vector.Z, 3);
    }

    [Fact]
    public void IdComparison_And_IdToFloat()
    {
        var idA = new StringID("A");
        var idB = new StringID("B");
        var (g, root) = NewGraph();
        int idParam = g.AddIdParameter("Id", idA);
        int matches = g.AddIdComparison(idParam, IdComparison.Matches, new[] { idA, idB });
        int toFloat = g.AddIdToFloat(idParam, new[] { idA, idB }, new[] { 10f, 20f }, defaultValue: -1f);
        g.SetRoot(root);

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Assert.True(i.EvaluateValueNode(matches).AsBool());
        Assert.Equal(10.0, (double)i.EvaluateValueNode(toFloat).AsFloat(), 4);

        i.SetId("Id", new StringID("C"));
        Assert.False(i.EvaluateValueNode(matches).AsBool());
        Assert.Equal(-1.0, (double)i.EvaluateValueNode(toFloat).AsFloat(), 4);
    }

    [Fact]
    public void CachedValue_SampleAndHold()
    {
        var (g, root) = NewGraph();
        int src = g.AddFloatParameter("Src", 1f);
        int sample = g.AddBoolParameter("Sample", false);
        int cached = g.AddCachedValue(src, sample);
        g.SetRoot(root);

        AnimationGraphInstance i = g.CreateInstance(TestSkeletons.MakeChain());
        Assert.Equal(1.0, (double)i.EvaluateValueNode(cached).AsFloat(), 4); // latched on first eval

        i.SetFloat("Src", 5f);
        Assert.Equal(1.0, (double)i.EvaluateValueNode(cached).AsFloat(), 4); // still holding 1

        i.SetBool("Sample", true);
        Assert.Equal(5.0, (double)i.EvaluateValueNode(cached).AsFloat(), 4); // re-latched on rising edge

        i.SetFloat("Src", 9f);
        i.SetBool("Sample", false);
        Assert.Equal(5.0, (double)i.EvaluateValueNode(cached).AsFloat(), 4); // holding 5
    }
}
