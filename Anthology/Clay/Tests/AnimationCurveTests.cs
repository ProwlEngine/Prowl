using Prowl.Clay;
using Prowl.Vector;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Unit tests for <see cref="AnimationCurve"/> evaluation. No format readers involved; the curve
/// data is built by hand so the assertions catch regressions in the interpolation math directly.
/// </summary>
public sealed class AnimationCurveTests
{
    [Fact]
    public void StepInterpolation_HoldsLeftValue_AndClampsAtBothEnds()
    {
        var curve = new AnimationCurve
        {
            Interpolation = AnimationInterpolation.Step,
            Dimension = 1,
            Times  = new[] { 0f, 1f, 2f },
            Values = new[] { 10f, 20f, 30f },
        };

        Assert.Equal(10f, curve.EvaluateFloat(0.0f));
        Assert.Equal(10f, curve.EvaluateFloat(0.5f));   // before t=1, still on left key
        Assert.Equal(20f, curve.EvaluateFloat(1.0f));
        Assert.Equal(20f, curve.EvaluateFloat(1.99f));
        Assert.Equal(30f, curve.EvaluateFloat(2.0f));
        Assert.Equal(30f, curve.EvaluateFloat(99.0f));  // clamped to last key
    }

    [Fact]
    public void LinearVec3_InterpolatesEachComponentIndependently()
    {
        var curve = new AnimationCurve
        {
            Interpolation = AnimationInterpolation.Linear,
            Dimension = 3,
            Times  = new[] { 0f, 1f },
            Values = new[] { 0f, 0f, 0f,   10f, 20f, 30f },
        };

        var mid = curve.EvaluateVector3(0.5f);
        Assert.Equal( 5f, mid.X, precision: 4);
        Assert.Equal(10f, mid.Y, precision: 4);
        Assert.Equal(15f, mid.Z, precision: 4);
    }

    [Fact]
    public void Quaternion_SlerpStaysUnitLength_AcrossFullRange()
    {
        // Quarter-turns around X and Y axes as the two endpoints; slerp between them should
        // produce unit-length quaternions at every sample.
        var qx = new Quaternion(MathF.Sin(MathF.PI / 4f), 0f, 0f, MathF.Cos(MathF.PI / 4f));
        var qy = new Quaternion(0f, MathF.Sin(MathF.PI / 4f), 0f, MathF.Cos(MathF.PI / 4f));
        var curve = new AnimationCurve
        {
            Interpolation = AnimationInterpolation.Linear,
            Dimension = 4,
            Times  = new[] { 0f, 1f },
            Values = new[] { qx.X, qx.Y, qx.Z, qx.W,  qy.X, qy.Y, qy.Z, qy.W },
        };

        for (float t = 0f; t <= 1f; t += 0.1f)
        {
            var q = curve.EvaluateQuaternion(t);
            float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            Assert.InRange(len, 0.999f, 1.001f);
        }
    }
}
