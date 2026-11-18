using Prowl.Runtime.Networking;

using Xunit;

namespace Prowl.Runtime.Test;

public class TransformTimelineTests
{
    private const double Interval = 0.1;

    private static TransformTimeline Steady(int samples)
    {
        TransformTimeline timeline = new();
        for (int i = 0; i < samples; i++)
            timeline.Observe(100 + i * Interval, 10 + i * Interval);
        return timeline;
    }

    [Fact]
    public void ASteadyStreamPlaysTwoSendIntervalsBehindTheSender()
    {
        TransformTimeline timeline = Steady(10);
        Assert.Equal(100.9 - 2 * Interval, timeline.RenderTime(10.9, Interval, 0), 8);
    }

    [Fact]
    public void TheMinimumDelayHoldsWhenItIsLongerThanTheBuffer()
    {
        TransformTimeline timeline = Steady(10);
        Assert.Equal(100.4, timeline.RenderTime(10.9, Interval, 0.5), 8);
    }

    [Fact]
    public void JitterGrowsTheBufferWithoutRewindingPlayback()
    {
        TransformTimeline timeline = Steady(10);
        double before = timeline.RenderTime(10.9, Interval, 0);
        timeline.Observe(101.0, 11.3);
        double after = timeline.RenderTime(10.95, Interval, 0);
        Assert.True(after >= before);
        Assert.True(after < before + 0.05, $"{after - before} advanced in 0.05 s while the buffer grew");
    }

    [Fact]
    public void FallingBehindSpeedsPlaybackUpInsteadOfJumping()
    {
        TransformTimeline timeline = Steady(10);
        double start = timeline.RenderTime(10.9, Interval, 0.5);
        double next = timeline.RenderTime(10.95, Interval, 0);
        Assert.True(next - start > 0.05 && next - start < 0.06, $"advanced {next - start} in 0.05 s");
    }

    [Fact]
    public void BeingAheadSlowsPlaybackDown()
    {
        TransformTimeline timeline = Steady(10);
        double start = timeline.RenderTime(10.9, Interval, 0);
        double next = timeline.RenderTime(10.95, Interval, 0.5);
        Assert.True(next - start < 0.05 && next - start > 0.04, $"advanced {next - start} in 0.05 s");
    }

    [Fact]
    public void FarBehindJumpsToTheBuffer()
    {
        TransformTimeline timeline = Steady(10);
        timeline.RenderTime(10.9, Interval, 5);
        Assert.Equal(100.75, timeline.RenderTime(10.95, Interval, 0), 8);
    }

    [Fact]
    public void PlaybackNeverPassesTheNewestSample()
    {
        TransformTimeline timeline = Steady(3);
        timeline.RenderTime(10.2, Interval, 0);
        Assert.Equal(100.2, timeline.RenderTime(20, Interval, 0), 8);
    }

    [Fact]
    public void OlderTimesAreIgnoredAndOwnershipResetStartsANewClock()
    {
        TransformTimeline timeline = new();
        Assert.True(timeline.Observe(100, 10));
        Assert.False(timeline.Observe(99, 11));
        timeline.RenderTime(12, Interval, 0);
        timeline.Clear();
        Assert.True(timeline.Observe(1, 20));
        Assert.Equal(0.8, timeline.RenderTime(20, Interval, 0), 8);
    }
}
