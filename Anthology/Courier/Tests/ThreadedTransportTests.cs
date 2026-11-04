// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading;

using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Courier.Tests;

public sealed class ThreadedTransportTests
{
    [Fact]
    public void AShutdownThatTimesOutKeepsTheTransportAndCanBeRetried()
    {
        BlockingTransport inner = new();
        ThreadedTransport transport = new(inner);
        transport.Listen();
        Assert.True(inner.Entered.Wait(TimeSpan.FromSeconds(2)));

        Assert.Throws<TimeoutException>(() => transport.Shutdown(TimeSpan.FromMilliseconds(20)));
        Assert.Throws<TimeoutException>(() => transport.Shutdown(TimeSpan.FromMilliseconds(20)));
        Assert.False(inner.Disposed);
        Assert.Equal(0, inner.Shutdowns);

        inner.Release.Set();
        transport.Shutdown(TimeSpan.FromSeconds(5));
        Assert.Equal(1, inner.Shutdowns);
        Assert.False(transport.IsListening);
        transport.Dispose();
        Assert.True(inner.Disposed);
    }

    [Fact]
    public void AShutdownWithTimeLeftWorksLikeAPlainOne()
    {
        LocalNetwork network = new();
        using ThreadedTransport host = new(new LocalTransport(network, "host"));
        using ThreadedTransport client = new(new LocalTransport(network, "client"));
        host.Listen();
        Connection connection = client.Connect("host");
        Pump pump = new(host, client);
        pump.Until(() => connection.IsConnected, what: "connect");
        client.Shutdown(TimeSpan.FromSeconds(5));
        host.Shutdown(TimeSpan.FromSeconds(5));
        Assert.Empty(host.Connections);
        Assert.Empty(client.Connections);
        host.Listen();
        Connection again = client.Connect("host");
        pump.Until(() => again.IsConnected, what: "reconnect");
    }

    private sealed class BlockingTransport : Transport
    {
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false);
        public bool Disposed;
        public int Shutdowns;
        public override bool IsListening => true;
        public override void Listen() { }
        public override Connection Connect(string address) => throw new NotSupportedException();

        public override void Receive()
        {
            Entered.Set();
            Release.Wait();
        }

        public override void Flush() { }
        public override void Shutdown() => Shutdowns++;
        protected override void DisposeCore() => Disposed = true;
    }
}
