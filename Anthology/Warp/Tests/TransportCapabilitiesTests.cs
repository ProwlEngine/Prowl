using Prowl.Warp.Transports.Batching;
using Prowl.Warp.Transports.Encryption;
using Prowl.Warp.Transports.Local;
using Prowl.Warp.Transports.Steam;
using Xunit;

namespace Prowl.Warp.Tests;

public class TransportCapabilitiesTests
{
    [Fact]
    public void WrappersPreserveExecutionAndAvailability()
    {
        using Transport local = new BatchingTransport(new EncryptedTransport(new LocalTransport(new(), "host"), new byte[32]));
        Assert.True(local.Capabilities.IsAvailable);
        Assert.Equal(TransportExecution.InlinePreferred, local.Capabilities.Execution);
        Assert.True(local.Capabilities.Supports(Delivery.Reliable));
        Assert.True(local.Capabilities.Supports(Delivery.Unreliable));
        using Transport steam = new BatchingTransport(new SteamTransport());
        Assert.False(steam.Capabilities.IsAvailable);
    }
}
