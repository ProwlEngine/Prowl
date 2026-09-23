using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Velocity Blend node: blending by the speed each clip travels.</summary>
public class N_VelocityBlend_Tests
{
    // A clip whose bone 0 sits at a constant Z (to observe the blend) and whose root travels
    // 'speed' units over its 1s duration (so its average linear speed equals 'speed').
    private static AnimationClip MoverClip(Skeleton skeleton, float markerZ, float speed)
    {
        var pose = new Pose(skeleton); pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, markerZ), Quaternion.Identity, Float3.One));

        var rootFrames = new[]
        {
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 0f, speed), Quaternion.Identity, Float3.One),
        };
        var rootMotion = new RootMotion(rootFrames, 1f);
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f, rootMotion: rootMotion);
    }

    [Fact]
    public void VelocityBlend_BlendsBySpeedDerivedThresholds()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int desiredSpeed = graph.AddFloatParameter("Speed");
        int slow = graph.AddClip(MoverClip(skeleton, 0f, 1f));  // speed 1
        int fast = graph.AddClip(MoverClip(skeleton, 10f, 4f)); // speed 4
        int blend = graph.AddVelocityBlend(desiredSpeed, new[] { slow, fast });
        graph.SetRoot(blend);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.SetFloat("Speed", 1f); // at the slow clip
        instance.Update(0.016f);
        Assert.Equal(0.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetFloat("Speed", 4f); // at the fast clip
        instance.Update(0.016f);
        Assert.Equal(10.0, (double)instance.Pose.GetTransform(0).position.Z, 3);

        instance.SetFloat("Speed", 2.5f); // halfway between speed 1 and 4
        instance.Update(0.016f);
        Assert.Equal(5.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }
}
