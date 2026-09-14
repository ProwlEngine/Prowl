// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

public class FrameLimiterTests
{
    [Fact]
    public void NoTargetNeverWaits()
    {
        var limiter = new FrameLimiter();
        Assert.Equal(0.0, limiter.NextWait(0.0));
        Assert.Equal(0.0, limiter.NextWait(100.0));
    }

    [Fact]
    public void ANegativeTargetIsTreatedAsUnlimited()
    {
        var limiter = new FrameLimiter { TargetFrameRate = -60 };
        Assert.Equal(0, limiter.TargetFrameRate);
        Assert.Equal(0.0, limiter.NextWait(0.0));
    }

    [Fact]
    public void AFrameThatCostNothingWaitsAWholePeriod()
    {
        var limiter = new FrameLimiter { TargetFrameRate = 60 };
        Assert.Equal(1.0 / 60.0, limiter.NextWait(0.0), 6);
    }

    /// <summary>The wait is what is left of the slot, so the rate holds however long a frame takes.</summary>
    [Fact]
    public void TheWaitIsWhatIsLeftOfTheSlot()
    {
        var limiter = new FrameLimiter { TargetFrameRate = 100 };
        limiter.NextWait(0.0);

        // The frame started at 10ms and took 4ms, so 6ms of its 10ms slot is left.
        Assert.Equal(0.006, limiter.NextWait(0.014), 6);
    }

    [Fact]
    public void AFrameThatOverranItsSlotIsNotHeldBackFurther()
    {
        var limiter = new FrameLimiter { TargetFrameRate = 100 };
        limiter.NextWait(0.0);
        Assert.Equal(0.0, limiter.NextWait(0.024));
    }

    /// <summary>
    /// After a stall the schedule starts again, rather than firing every frame it missed back to
    /// back to catch up.
    /// </summary>
    [Fact]
    public void ALongStallRestartsTheScheduleInsteadOfCatchingUp()
    {
        var limiter = new FrameLimiter { TargetFrameRate = 100 };
        limiter.NextWait(0.0);

        Assert.Equal(0.0, limiter.NextWait(2.0));
        Assert.Equal(0.01, limiter.NextWait(2.0), 6);
    }

    [Fact]
    public void ResetStartsTheScheduleFromTheNextFrame()
    {
        var limiter = new FrameLimiter { TargetFrameRate = 50 };
        limiter.NextWait(0.0);
        limiter.Reset();

        Assert.Equal(0.02, limiter.NextWait(5.0), 6);
    }

    [Fact]
    public void ClearingTheTargetForgetsTheSchedule()
    {
        var limiter = new FrameLimiter { TargetFrameRate = 60 };
        limiter.NextWait(0.0);

        limiter.TargetFrameRate = 0;
        Assert.Equal(0.0, limiter.NextWait(0.001));

        limiter.TargetFrameRate = 60;
        Assert.Equal(1.0 / 60.0, limiter.NextWait(0.002), 6);
    }
}
