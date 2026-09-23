using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Bone Mask Blend node: mixing two masks by a value.</summary>
public class N_BoneMaskBlend_Tests
{
    [Fact]
    public void BoneMaskBlend_LerpsBetweenMasks()
    {
        var g = new AnimationGraph();
        int a = g.AddFixedWeightBoneMask(0f);
        int b = g.AddFixedWeightBoneMask(1f);
        int blendParam = g.AddFloatParameter("B", 0.25f);
        int blended = g.AddBoneMaskBlend(a, b, blendParam);
        g.SetRoot(g.AddReferencePose());

        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationGraphInstance i = g.CreateInstance(skeleton);
        Assert.Equal(0.25, (double)i.EvaluateBoneMaskNode(blended).GetWeight(0), 4);

        i.SetFloat("B", 0.75f);
        Assert.Equal(0.75, (double)i.EvaluateBoneMaskNode(blended).GetWeight(0), 4);
    }
}
