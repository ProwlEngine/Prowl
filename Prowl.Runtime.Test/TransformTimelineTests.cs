using Prowl.Runtime.Networking;

using Xunit;

namespace Prowl.Runtime.Test;

public class TransformTimelineTests
{
    [Fact]
    public void JitterAddsDelayWithoutRewindingPlayback()
    {
        TransformTimeline timeline = new();
        timeline.Observe(0, 10);
        timeline.Observe(1, 11);
        double before = timeline.RenderTime(11, 0.1);
        timeline.Observe(2, 12.4);
        double after = timeline.RenderTime(12.4, 0.1);
        Assert.True(after >= before);
        Assert.True(after < 2); // Jitter adds a buffer even though local time passed the latest snapshot.
    }

    [Fact]
    public void PlaybackUsesTheSendersClockAndDoesNotDependOnTickRate()
    {
        TransformTimeline timeline = new();
        timeline.Observe(100, 10);
        timeline.Observe(100.2, 10.2);
        timeline.Observe(100.4, 10.4);
        Assert.Equal(100.3, timeline.RenderTime(10.4, 0.1), 8);
        Assert.Equal(100.4, timeline.RenderTime(20, 0.1), 8); // No extrapolation across a long loss.
    }

    [Fact]
    public void OlderTimesAreIgnoredAndOwnershipResetStartsANewClock()
    {
        TransformTimeline timeline = new();
        Assert.True(timeline.Observe(100, 10));
        Assert.False(timeline.Observe(99, 11));
        timeline.RenderTime(12, 0.1);
        timeline.Clear();
        Assert.True(timeline.Observe(1, 20));
        Assert.Equal(0.9, timeline.RenderTime(20, 0.1), 8);
    }
}
