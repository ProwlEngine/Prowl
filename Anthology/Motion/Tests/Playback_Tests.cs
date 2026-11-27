using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Playback cursors and frame times: normalizing, wrapping and stepping.</summary>
public class Playback_Tests
{
    [Fact]
    public void FromFloat_SplitsIntoFrameAndPercentage()
    {
        var ft = new FrameTime(3.25f);
        Assert.Equal(3, ft.FrameIndex);
        Assert.Equal(0.25, (double)ft.Percentage, 4);
    }

    [Fact]
    public void ToFloat_RecombinesFrameAndPercentage()
        => Assert.Equal(3.25, (double)new FrameTime(3, 0.25f).ToFloat(), 4);

    [Fact]
    public void Percentage_StaysInUnitRange()
    {
        var ft = new FrameTime(10, 0.75f);
        Assert.InRange(ft.Percentage, 0f, 1f);
    }

    [Theory]
    [InlineData(1e6f)]
    [InlineData(1e12f)]
    [InlineData(float.MaxValue)]
    public void AHugeStepStaysInRangeAndBoundsItsWraps(float delta)
    {
        var cursor = new ClipCursor();

        PlaybackSpan span = cursor.Advance(delta, 0.033f, loop: true, includeStart: false);

        Assert.InRange(cursor.Time, 0f, 1f);
        Assert.InRange(span.Wraps, 0, ClipCursor.MaxWrapsPerStep);
    }

    // Landing exactly on the end of a looping clip is a wrap, otherwise a paused clip reads as finished.
    [Fact]
    public void AStepLandingExactlyOnTheEndWraps()
    {
        var cursor = new ClipCursor { Time = 0.5f };

        PlaybackSpan span = cursor.Advance(0.5f, 1f, loop: true, includeStart: false);

        Assert.Equal(0f, cursor.Time, 5);
        Assert.Equal(1, span.Wraps);
        Assert.Equal(1, cursor.LoopCount);
    }

    [Fact]
    public void NormalizeLoop_Wraps()
        => Assert.Equal(0.5, (double)ClipPlayback.NormalizeLoop(1.5f, 1f), 4);

    [Fact]
    public void NormalizeClamp_Holds()
        => Assert.Equal(1.0, (double)ClipPlayback.NormalizeClamp(1.5f, 1f), 4);

    [Fact]
    public void CrossedLoop_DetectsWrap()
    {
        Assert.True(ClipPlayback.CrossedLoop(0.95f, 0.05f));
        Assert.False(ClipPlayback.CrossedLoop(0.2f, 0.4f));
    }
}
