using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Layer Blend node: a stack of layers over a base pose, with masks and root motion.</summary>
public class N_LayerBlend_Tests
{
    [Fact]
    public void LayerBlend_AppliesLayerByWeight()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int basePose = g.AddClip(TestClips.Const(skeleton, 0f));
        int layerPose = g.AddClip(TestClips.Const(skeleton, 10f));
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
        int basePose = g.AddClip(TestClips.Const(skeleton, 0f));
        int layerPose = g.AddClip(TestClips.Const(skeleton, 10f));
        int zeroMask = g.AddFixedWeightBoneMask(0f); // mask fully zero -> layer contributes nothing
        int layers = g.AddLayerBlend(basePose, new[] { new LayerInfo(layerPose, weightNodeIndex: -1, maskNodeIndex: zeroMask, additive: false, defaultWeight: 1f) });
        g.SetRoot(layers);

        AnimationGraphInstance i = g.CreateInstance(skeleton);
        i.Update(0.016f);
        Assert.Equal(0.0, (double)i.Pose.GetTransform(0).position.Z, 3); // masked out -> base only
    }

    [Fact]
    public void LayerBlend_AdditiveLayerKeepsTheBaseRootMotion()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int basePose = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f));
        int layer = g.AddZeroPose();
        g.SetRoot(g.AddLayerBlend(basePose, new[] { new LayerInfo(layer, additive: true) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
    }

    [Fact]
    public void LayerBlend_FullyMaskedOverrideLayerKeepsTheBaseRootMotion()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int basePose = g.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f));
        int layer = g.AddClip(TestClips.Const(skeleton, 3f));
        int mask = g.AddFixedWeightBoneMask(0f);
        g.SetRoot(g.AddLayerBlend(basePose, new[] { new LayerInfo(layer, maskNodeIndex: mask) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 3);
    }

    [Fact]
    public void LayerBlend_NaNRootMotionWeight_KeepsRootMotionFinite()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int weight = g.AddFloatParameter("RootMotionWeight", float.NaN);
        int basePose = g.AddClip(TestClips.Ramp(skeleton, 1f, rootTravel: 1f));
        int layer = g.AddClip(TestClips.Ramp(skeleton, 2f, rootTravel: 2f));
        g.SetRoot(g.AddLayerBlend(basePose, new[] { new LayerInfo(layer) { RootMotionWeightNodeIndex = weight } }, onlySampleBaseRootMotion: false));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0.1, (double)instance.RootMotionDelta.position.Z, 3);
    }
}
