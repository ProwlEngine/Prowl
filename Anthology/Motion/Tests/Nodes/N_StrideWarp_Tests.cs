using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Stride Warp node: matching playback rate to a requested speed.</summary>
public class N_StrideWarp_Tests
{
    private static (AnimationGraph Graph, int Warp, Skeleton Skeleton) WalkGraph(float desired, float travelPerLoop = 2f)
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed", desired);
        int clip = graph.AddClip(TestClips.Ramp(skeleton, endZ: 10f, duration: 1f, rootTravel: travelPerLoop));
        return (graph, graph.AddStrideWarp(clip, speed), skeleton);
    }

    [Theory]
    [InlineData(2f, 1f)]
    [InlineData(3f, 1.5f)]
    [InlineData(1f, 0.5f)]
    public void StrideWarp_PlaysTheClipAtTheRateThatMatchesTheSpeed(float desired, float expectedScale)
    {
        (AnimationGraph graph, int warp, Skeleton skeleton) = WalkGraph(desired);
        graph.SetRoot(warp);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.1f, Transform3D.Identity);
        instance.Update(0.1f, Transform3D.Identity);

        // The clip's Z ramps 0..10 over its length, so the sampled value reports how far it has played.
        Assert.Equal(2f * expectedScale, instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void StrideWarp_MovesTheCharacterAtTheRequestedSpeed()
    {
        (AnimationGraph graph, int warp, Skeleton skeleton) = WalkGraph(3f);
        graph.SetRoot(warp);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        float travelled = 0f;
        for (int i = 0; i < 60; i++)
        {
            instance.Update(1f / 60f, Transform3D.Identity);
            travelled += instance.RootMotionDelta.position.Z;
        }

        Assert.Equal(3f, travelled, 1);
    }

    [Fact]
    public void StrideWarp_StaysWithinItsLimits()
    {
        (AnimationGraph graph, int warp, Skeleton skeleton) = WalkGraph(100f);
        graph.SetRoot(warp);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.1f, Transform3D.Identity);
        instance.Update(0.1f, Transform3D.Identity);

        Assert.Equal(4f, instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void StrideWarp_OnAClipThatDoesNotTravel_LeavesItAlone()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddFloatParameter("Speed", 5f);
        graph.SetRoot(graph.AddStrideWarp(graph.AddClip(TestClips.Ramp(skeleton, endZ: 10f)), speed));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.1f, Transform3D.Identity);
        instance.Update(0.1f, Transform3D.Identity);

        Assert.Equal(2f, instance.Pose.GetTransform(0).position.Z, 2);
    }

    [Fact]
    public void StrideWarp_TakesCrossedScaleBoundsWithoutThrowing()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int speed = graph.AddConstFloat(10f);
        int clip = graph.AddClip(TestClips.Ramp(skeleton, rootTravel: 1f));
        graph.SetRoot(graph.AddNode(new StrideWarpDefinition(clip, speed) { MinScale = 2f, MaxScale = 0.5f }));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(1f / 60f);
    }
}
