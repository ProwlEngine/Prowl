using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Float Clamp node.</summary>
public class N_FloatClamp_Tests
{
    [Fact]
    public void FloatClamp_WithMinAboveMax_FailsWhenBuilt()
    {
        var g = new AnimationGraph();
        int x = g.AddFloatParameter("X");
        Assert.Throws<ArgumentException>(() => g.AddFloatClamp(x, 5f, 1f));
    }
}
