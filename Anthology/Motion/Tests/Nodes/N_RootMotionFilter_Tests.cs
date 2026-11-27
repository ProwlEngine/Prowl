using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Root Motion Filter node: keeping and scaling root motion channels.</summary>
public class N_RootMotionFilter_Tests
{
    [Fact]
    public void RootMotionFilter_CanStripTravelEntirely()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Ramp(skeleton, rootTravel: 4f));
        graph.SetRoot(graph.AddRootMotionFilter(clip, RootMotionChannels.None));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.25f, Transform3D.Identity);

        Assert.Equal(0f, instance.RootMotionDelta.position.Z, 5);
    }

    [Fact]
    public void RootMotionFilter_CanKeepTheGroundPlaneOnly()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        var poses = new Pose[3];
        var root = new Transform3D[3];
        for (int i = 0; i < 3; i++)
        {
            poses[i] = TestClips.At(skeleton, i);
            root[i] = new Transform3D(new Float3(0f, i * 0.5f, i * 1f), Quaternion.Identity, Float3.One);
        }
        var clip = new AnimationClip(skeleton, poses, 1f, rootMotion: new RootMotion(root, 1f));
        graph.SetRoot(graph.AddRootMotionFilter(graph.AddClip(clip), RootMotionChannels.Horizontal));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.5f, Transform3D.Identity);

        Assert.Equal(0f, instance.RootMotionDelta.position.Y, 5);
        Assert.True(instance.RootMotionDelta.position.Z > 0.5f);
    }

    [Fact]
    public void RootMotionFilter_CanDampWhatItKeeps()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int clip = graph.AddClip(TestClips.Ramp(skeleton, rootTravel: 4f));
        graph.SetRoot(graph.AddRootMotionFilter(clip, RootMotionChannels.All, 0.5f));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.25f, Transform3D.Identity);

        Assert.Equal(0.5f, instance.RootMotionDelta.position.Z, 3);
    }

    [Fact]
    public void RootMotionFilter_ScalesTravelAndTurnApart()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var start = new Pose(skeleton);
        start.SetToReferencePose();
        var end = new Pose(skeleton);
        end.SetToReferencePose();

        var clip = new AnimationClip(skeleton, new[] { start, end }, 1f, rootMotion: new RootMotion(new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 0f, 2f), Quaternion.AxisAngle(Float3.UnitY, 1f), Float3.One),
        }, 1f));

        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddNode(new RootMotionFilterDefinition(graph.AddClip(clip, loop: false), RootMotionChannels.All)
        {
            TravelScale = 0.5f,
            TurnScale = 0f,
        }));

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.5f);

        Assert.Equal(0.5, (double)instance.RootMotionDelta.position.Z, 2);
        Assert.True(MathF.Abs(Quaternion.Dot(instance.RootMotionDelta.rotation, Quaternion.Identity)) > 0.9999f);
    }
}
