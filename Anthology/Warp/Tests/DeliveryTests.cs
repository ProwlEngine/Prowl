using System;
using Xunit;

namespace Prowl.Warp.Tests;

public class DeliveryTests
{
    [Fact]
    public void OnlyReliableAndUnreliableDeliveryAreSupported()
    {
        Assert.Equal(new[] { Delivery.Reliable, Delivery.Unreliable }, Enum.GetValues<Delivery>());
    }
}
