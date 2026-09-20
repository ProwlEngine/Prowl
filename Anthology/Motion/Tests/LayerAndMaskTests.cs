using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class LayerAndMaskTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

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

    [Fact]
    public void LayerBlend_AppliesLayerByWeight()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int basePose = g.AddClip(ConstClip(skeleton, 0f));
        int layerPose = g.AddClip(ConstClip(skeleton, 10f));
        int w = g.AddFloatParameter("W", 1f);
        int layers = g.AddLayerBlend(basePose, new[] { new LayerInfo(layerPose, w) });
        g.SetRoot(layers);

        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.Update(0.016f);
        Assert.Equal(10.0, (double)i.Pose.GetTransform(0).position.Z, 3); // full layer

        i.SetFloat("W", 0.5f);
        i.Update(0.016f);
        Assert.Equal(5.0, (double)i.Pose.GetTransform(0).position.Z, 3); // half blend
    }

    [Fact]
    public void LayerBlend_MaskGatesWhichBonesGetTheLayer()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int basePose = g.AddClip(ConstClip(skeleton, 0f));
        int layerPose = g.AddClip(ConstClip(skeleton, 10f));
        int zeroMask = g.AddFixedWeightBoneMask(0f); // mask fully zero -> layer contributes nothing
        int layers = g.AddLayerBlend(basePose, new[] { new LayerInfo(layerPose, weightNodeIndex: -1, maskNodeIndex: zeroMask, additive: false, defaultWeight: 1f) });
        g.SetRoot(layers);

        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.Update(0.016f);
        Assert.Equal(0.0, (double)i.Pose.GetTransform(0).position.Z, 3); // masked out -> base only
    }
}
