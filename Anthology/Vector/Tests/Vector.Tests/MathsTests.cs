// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Vector.Tests;

public class MathsTests
{
    [Fact]
    public void SmoothDamp_GivesExpectedValues()
    {
        float currentVelocity = 0;
        Assert.Equal(4.129158f, Maths.SmoothDamp(current: 0, target: 100, ref currentVelocity, smoothTime: 1, maxSpeed: float.PositiveInfinity, deltaTime: 0.16f), precision: 4);
        Assert.Equal(46.48283f, currentVelocity, precision: 4);
        Assert.Equal(57.46621f, Maths.SmoothDamp(current: 50, target: 100, ref currentVelocity, smoothTime: 1, maxSpeed: float.PositiveInfinity, deltaTime: 0.16f), precision: 4);
        Assert.Equal(46.19836f, currentVelocity, precision: 4);
        Assert.Equal(7.207428f, Maths.SmoothDamp(current: 0, target: 100, ref currentVelocity, smoothTime: 10, maxSpeed: float.PositiveInfinity, deltaTime: 0.16f), precision: 4);
        Assert.Equal(43.93225f, currentVelocity, precision: 4);
        Assert.Equal(21.77090f, Maths.SmoothDamp(current: 0, target: 100, ref currentVelocity, smoothTime: 1, maxSpeed: float.PositiveInfinity, deltaTime: 0.33f), precision: 4);
        Assert.Equal(75.87174f, currentVelocity, precision: 4);
    }

    [Fact]
    public void SmoothDamp_WithMaxSpeedGivesExpectedValues()
    {
        float currentVelocity = 0;
        Assert.Equal(0.4129165f, Maths.SmoothDamp(current: 0, target: 100, ref currentVelocity, smoothTime: 1, maxSpeed: 10f, deltaTime: 0.16f), precision: 4);
        Assert.Equal(4.648283f, currentVelocity, precision: 4);
    }

    [Fact]
    public void SmoothDamp_DoesntOvershoot()
    {
        float currentVelocity = 50;
        Assert.Equal(100, Maths.SmoothDamp(current: 95, target: 100, ref currentVelocity, smoothTime: 1, maxSpeed: float.PositiveInfinity, deltaTime: 0.16f), precision: 4);
        Assert.Equal(0, currentVelocity, precision: 4);
    }

    [Fact]
    public void SmoothDampAngle_GivesExpectedValues()
    {
        float currentVelocity = 0;
        Assert.Equal(357.5225f, Maths.SmoothDampDegrees(current: 0, target: 300, ref currentVelocity, smoothTime: 1, maxSpeed: float.PositiveInfinity, deltaTime: 0.16f), precision: 4);
        Assert.Equal(-27.889698f, currentVelocity, precision: 4);
        Assert.Equal(351.9063f, Maths.SmoothDampDegrees(current: 357.5225f, target: 300, ref currentVelocity, smoothTime: 1, maxSpeed: float.PositiveInfinity, deltaTime: 0.16f), precision: 4);
        Assert.Equal(-40.51225f, currentVelocity, precision: 4);
        Assert.Equal(345.0552f, Maths.SmoothDampDegrees(current: 351.9063f, target: 300, ref currentVelocity, smoothTime: 1, maxSpeed: float.PositiveInfinity, deltaTime: 0.16f), precision: 4);
        Assert.Equal(-44.13572f, currentVelocity, precision: 4);
    }

    [Fact]
    public void SmoothDampDouble_GivesExpectedValues()
    {
        double currentVelocity = 0;
        Assert.Equal(4.12916269, Maths.SmoothDamp(current: 0, target: 100, ref currentVelocity, smoothTime: 1, maxSpeed: double.PositiveInfinity, deltaTime: 0.16), precision: 8);
        Assert.Equal(46.48283021, currentVelocity, precision: 8);
    }

    [Fact]
    public void SmoothDampFloat3_GivesExpectedValues()
    {
        Float3 currentVelocity = Float3.Zero;
        Assert.Equal(new Float3(4.129158f, 0, 4.129158f), Maths.SmoothDamp(current: Float3.Zero, target: new Float3(100, 0, 100), ref currentVelocity, smoothTime: 1, maxSpeed: float.PositiveInfinity, deltaTime: 0.16f));
        Assert.Equal(new Float3(46.482834f, 0, 46.482834f), currentVelocity);

    }
}
