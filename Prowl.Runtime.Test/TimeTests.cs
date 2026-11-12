// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

public class TimeTests
{
    [Fact]
    public void Modifiers_MultiplyWithTheBaseScale()
    {
        var time = new SceneClock { TimeScale = 2f };
        time.AddTimeScaleModifier(0.5f);
        time.AddTimeScaleModifier(0.25f);

        Assert.Equal(0.25f, time.EffectiveTimeScale, 5);
        Assert.Equal(2f, time.TimeScale);
    }

    [Fact]
    public void NestedFreezes_EndingOutOfOrder_RestoreNormalTime()
    {
        var time = new SceneClock();
        TimeScaleModifier first = time.AddTimeScaleModifier(0f);
        TimeScaleModifier second = time.AddTimeScaleModifier(0f);

        first.Remove();
        Assert.Equal(0f, time.EffectiveTimeScale);

        second.Remove();
        Assert.Equal(1f, time.EffectiveTimeScale);
    }

    [Fact]
    public void Remove_Twice_DoesNothing()
    {
        var time = new SceneClock();
        TimeScaleModifier modifier = time.AddTimeScaleModifier(0.5f);
        time.AddTimeScaleModifier(0.5f);

        modifier.Remove();
        modifier.Remove();

        Assert.False(modifier.IsActive);
        Assert.Equal(1, time.ModifierCount);
    }

    [Fact]
    public void Advance_ScalesDeltaByTheEffectiveScale()
    {
        var time = new SceneClock();
        time.AddTimeScaleModifier(0.5f);

        time.Advance(0.1f);
        time.Advance(0.1f);

        Assert.Equal(0.05f, time.DeltaTime, 6);
        Assert.Equal(0.1, time.TimeSinceStartup, 6);
    }

    [Fact]
    public void FixedSteps_FollowTheScale_AndDropABacklogPastTheCap()
    {
        var time = new SceneClock { TimeScale = 2f };
        time.Advance(Time.FixedDeltaTime);
        Assert.Equal(2, time.TakeFixedSteps());

        time.Advance(Time.FixedDeltaTime * 10f);
        Assert.Equal(Time.MaxFixedIterations, time.TakeFixedSteps());
        Assert.Equal(0f, time.FixedAccumulator);
    }

    [Fact]
    public void WithNoWorldBound_ScaledTimeReadsAsAStoppedClock()
    {
        Resources.SceneInstance.Shutdown();
        Assert.False(Resources.SceneInstance.HasMain);
        Assert.Equal(0f, Time.DeltaTime);
        Assert.Equal(0f, Time.TimeSinceStartup);
        Assert.Throws<System.InvalidOperationException>(() => Time.TimeScale = 2f);
    }

}
