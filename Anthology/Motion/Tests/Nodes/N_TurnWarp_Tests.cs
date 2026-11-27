using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Turn Warp node: scaling a turn on the spot to the angle asked for.</summary>
public class N_TurnWarp_Tests
{
    private static Skeleton OneBone() => new(
        new[] { new StringID("Root") },
        new[] { Skeleton.InvalidIndex },
        new[] { Transform3D.Identity });

    // A second of turning on the spot by the given angle, evenly across the clip.
    private static AnimationClip Turn(Skeleton skeleton, float degrees)
    {
        const int frames = 31;
        var poses = new Pose[frames];
        var root = new Transform3D[frames];
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)(frames - 1);
            poses[i] = TestClips.At(skeleton, 0f);
            root[i] = new Transform3D(Float3.Zero, Quaternion.AxisAngle(Float3.UnitY, degrees * Maths.Deg2Rad * t), Float3.One);
        }
        return new AnimationClip(skeleton, poses, 1f, rootMotion: new RootMotion(root, 1f));
    }

    private static float TurnedDegrees(AnimationGraphInstance instance, float seconds)
    {
        const float dt = 1f / 60f;
        float total = 0f;
        for (int i = 0; i < (int)MathF.Round(seconds / dt); i++)
        {
            instance.Update(dt, Transform3D.Identity);
            Float3 forward = instance.RootMotionDelta.rotation * Float3.UnitZ;
            total += MathF.Atan2(forward.X, forward.Z) * Maths.Rad2Deg;
        }
        return total;
    }

    [Fact]
    public void TurnWarp_MeasuresATurnPastHalfACircle()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var frames = new Transform3D[31];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = new Transform3D(Float3.Zero, Quaternion.AxisAngle(Float3.UnitY, 270f * Maths.Deg2Rad * i / (frames.Length - 1)), Float3.One);
        Pose still = TestClips.At(skeleton, 0f);
        var clip = new AnimationClip(skeleton, new[] { still, still }, 1f, rootMotion: new RootMotion(frames, 1f));

        var graph = new AnimationGraph();
        graph.SetRoot(graph.AddTurnWarp(graph.AddClip(clip, loop: false), graph.AddConstFloat(135f)));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        float turned = 0f;
        for (int i = 0; i < 60; i++)
        {
            instance.Update(1f / 60f);
            Float3 forward = instance.RootMotionDelta.rotation * Float3.UnitZ;
            turned += MathF.Atan2(forward.X, forward.Z) * Maths.Rad2Deg;
        }

        Assert.Equal(135f, MathF.Abs(turned), 0);
    }

    [Theory]
    [InlineData(90f, 180f, 180f)]
    [InlineData(90f, 45f, 45f)]
    [InlineData(-90f, 150f, -150f)]
    public void TheClipTurnsByTheAngleAsked(float clipTurn, float asked, float expected)
    {
        Skeleton skeleton = OneBone();
        var graph = new AnimationGraph();
        int angle = graph.AddFloatParameter("Angle", asked);
        graph.SetRoot(graph.AddTurnWarp(graph.AddClip(Turn(skeleton, clipTurn), loop: false), angle));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Assert.Equal(expected, TurnedDegrees(instance, 1f), 0);
    }

    /// <summary>The caller reports what is left to turn as it goes; the turn is still the one asked at the start.</summary>
    [Fact]
    public void TheAngleIsTakenWhenTheTurnStarts()
    {
        Skeleton skeleton = OneBone();
        var graph = new AnimationGraph();
        int angle = graph.AddFloatParameter("Angle", 180f);
        graph.SetRoot(graph.AddTurnWarp(graph.AddClip(Turn(skeleton, 90f), loop: false), angle));
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        float first = TurnedDegrees(instance, 0.5f);
        instance.SetFloat("Angle", 10f);
        float rest = TurnedDegrees(instance, 0.5f);

        Assert.Equal(180f, first + rest, 0);
    }
}
