using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Speed Scale node.</summary>
public class N_SpeedScale_Tests
{
    [Fact]
    public void SpeedScale_InsideABlend_SpeedsUpTheChild()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int p = g.AddFloatParameter("P", 0f);
        int scaled = g.AddSpeedScale(g.AddClip(TestClips.Ramp(skeleton)), defaultSpeed: 2f);
        g.SetRoot(g.AddBlend1D(p, new[] { (scaled, 0f), (g.AddClip(TestClips.Ramp(skeleton)), 1f) }));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 12; i++)
            instance.Update(0.02f);

        Assert.Equal(0.5, (double)((PoseNodeInstance)instance.GetNodeInstance(scaled)).Duration, 3);
        Assert.Equal(0.48, (double)instance.NormalizedTime, 3);
    }

    [Fact]
    public void SpeedScale_ClampsNegativeSpeed()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var g = new AnimationGraph();
        int speed = g.AddFloatParameter("Speed", -1f);
        g.SetRoot(g.AddSpeedScale(g.AddClip(TestClips.Ramp(skeleton), loop: false), speed));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        for (int i = 0; i < 5; i++)
            instance.Update(0.1f);
        instance.SetFloat("Speed", 1f);
        for (int i = 0; i < 5; i++)
            instance.Update(0.05f);

        Assert.Equal(0.25, (double)instance.NormalizedTime, 3);
    }
}
