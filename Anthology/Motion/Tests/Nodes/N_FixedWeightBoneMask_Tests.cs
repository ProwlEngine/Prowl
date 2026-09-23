using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Fixed Weight Bone Mask node.</summary>
public class N_FixedWeightBoneMask_Tests
{
    [Fact]
    public void FixedWeightMask_HasUniformWeights()
    {
        var g = new AnimationGraph();
        int mask = g.AddFixedWeightBoneMask(0.5f);
        g.SetRoot(g.AddReferencePose());

        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationGraphInstance i = g.CreateInstance(skeleton);
        BoneMask m = i.EvaluateBoneMaskNode(mask);
        for (int b = 0; b < skeleton.BoneCount; b++)
            Assert.Equal(0.5, (double)m.GetWeight(b), 4);
    }
}
