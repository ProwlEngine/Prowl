using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Target Info node: distances and angles to a target, in character space.</summary>
public class N_TargetInfo_Tests
{
    [Fact]
    public void TargetInfo_MeasuresWorldTargetsInCharacterSpace()
    {
        var g = new AnimationGraph();
        int target = g.AddTargetParameter("T");
        int distance = g.AddTargetInfo(target, TargetInfo.Distance);
        int angle = g.AddTargetInfo(target, TargetInfo.AngleHorizontal);
        g.SetRoot(g.AddReferencePose());
        AnimationGraphInstance instance = g.CreateInstance(TestSkeletons.MakeChain());

        instance.SetTarget("T", Target.FromWorld(new Transform3D(new Float3(100f, 0f, 5f), Quaternion.Identity, Float3.One)));
        instance.Update(0.1f, new Transform3D(new Float3(100f, 0f, 0f), Quaternion.Identity, Float3.One));

        Assert.Equal(5.0, (double)instance.EvaluateValueNode(distance).AsFloat(), 3);
        Assert.Equal(0.0, (double)instance.EvaluateValueNode(angle).AsFloat(), 3);
    }
}
