using Prowl.Runtime.Networking;

using Xunit;

namespace Prowl.Runtime.Test;

public class ClockOffsetTests
{
    [Fact]
    public void TheOffsetIsTheLeastDelaySeenLately()
    {
        ClockOffset clock = default;
        Assert.Equal(0.25, clock.Take(10.25, 10), 6);
        Assert.Equal(0.25, clock.Take(10.5, 10.1), 6);
        Assert.Equal(0.125, clock.Take(10.725, 10.6), 6);
    }

    [Fact]
    public void OneMessageClaimingAnImpossibleTimeDoesNotSetTheOffsetForGood()
    {
        ClockOffset clock = default;
        clock.Take(10.1, 10);
        Assert.True(clock.Take(10.2, 1e12) < -1e11);

        // Within two windows what it claimed is forgotten, and honest messages set the offset again.
        double now = 10.2, offset = 0;
        for (int i = 0; i < 300; i++)
        {
            now += 0.05;
            offset = clock.Take(now, now - 0.1);
        }

        Assert.Equal(0.1, offset, 6);
    }
}
