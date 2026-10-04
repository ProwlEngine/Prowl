// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

public class TimeTests
{
    [Fact]
    public void Modifiers_MultiplyWithTheBaseScale()
    {
        var time = new TimeData { TimeScale = 2f };
        time.AddModifier(0.5f);
        time.AddModifier(0.25f);

        Assert.Equal(0.25f, time.EffectiveTimeScale, 5);
        Assert.Equal(2f, time.TimeScale);
    }

    [Fact]
    public void NestedFreezes_EndingOutOfOrder_RestoreNormalTime()
    {
        var time = new TimeData();
        var first = time.AddModifier(0f);
        var second = time.AddModifier(0f);

        first.Remove();
        Assert.Equal(0f, time.EffectiveTimeScale);

        second.Remove();
        Assert.Equal(1f, time.EffectiveTimeScale);
    }

    [Fact]
    public void Remove_Twice_DoesNothing()
    {
        var time = new TimeData();
        var modifier = time.AddModifier(0.5f);
        time.AddModifier(0.5f);

        modifier.Remove();
        modifier.Remove();

        Assert.False(modifier.IsActive);
        Assert.Equal(1, time.ModifierCount);
    }

    [Fact]
    public void Update_ScalesDeltaByTheEffectiveScale()
    {
        var time = new TimeData();
        time.AddModifier(0.5f);

        time.Update();
        time.Update();

        Assert.Equal(time.UnscaledDeltaTime * 0.5f, time.DeltaTime, 6);
    }
}
