using System;

using Xunit;

namespace Prowl.Courier.Tests;

public class DeliveryTests
{
    [Fact]
    public void DeliveryModesAreReliableUnreliableAndSequenced() =>
        Assert.Equal(new[] { Delivery.Reliable, Delivery.Unreliable, Delivery.UnreliableSequenced },
            Enum.GetValues<Delivery>());
}
