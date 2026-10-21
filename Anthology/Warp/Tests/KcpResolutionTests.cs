using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Prowl.Warp.Transports.Kcp;
using Xunit;

namespace Prowl.Warp.Tests;

public class KcpResolutionTests
{
    [Fact]
    public void PendingResolutionDoesNotBlockPumpingAndDisconnectCancelsIt()
    {
        CancellationToken token = default;
        TaskCompletionSource<IPAddress[]> completion = new();
        using KcpTransport transport = new(new(), (_, cancellation) => { token = cancellation; return completion.Task; });
        Connection peer = transport.Connect("pending.test:7777");
        transport.Update();
        Assert.Equal(ConnectionState.Connecting, peer.State);
        peer.Disconnect();
        Assert.True(token.IsCancellationRequested);
        completion.SetResult(new[] { IPAddress.Loopback });
        transport.Update();
        Assert.True(transport.Poll(out TransportEvent ev));
        Assert.Equal(DisconnectReason.Requested, ev.Reason);
        Assert.False(transport.Poll(out _));
        Assert.Empty(transport.Connections);
    }

    [Fact]
    public void ShutdownCancelsResolutionAndTransportCanBeReused()
    {
        CancellationToken token = default;
        using KcpTransport transport = new(new() { Port = 0 }, (_, cancellation) =>
        { token = cancellation; return new TaskCompletionSource<IPAddress[]>().Task; });
        Connection peer = transport.Connect("pending.test:7777");
        transport.Shutdown();
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(ConnectionState.Disconnected, peer.State);
        transport.Listen();
        Assert.True(transport.IsListening);
    }

    [Fact]
    public void ConnectTimeoutIncludesResolution()
    {
        CancellationToken token = default;
        using KcpTransport transport = new(new() { ConnectTimeout = TimeSpan.FromMilliseconds(20) }, (_, cancellation) =>
        { token = cancellation; return new TaskCompletionSource<IPAddress[]>().Task; });
        Connection peer = transport.Connect("pending.test:7777");
        Thread.Sleep(30);
        transport.Update();
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(ConnectionState.Disconnected, peer.State);
        Assert.True(transport.Poll(out TransportEvent ev));
        Assert.Equal(DisconnectReason.Timeout, ev.Reason);
    }

    [Fact]
    public async Task HostNamesAndLiteralsResolveWithoutChangingAddressSemantics()
    {
        var family = System.Net.Sockets.AddressFamily.InterNetworkV6;
        Assert.NotNull(await KcpTransport.TryResolveAsync("localhost:7777", 0, family, default));
        Assert.NotNull(await KcpTransport.TryResolveAsync("[::1]:7777", 0, family, default));
        Assert.Null(await KcpTransport.TryResolveAsync("localhost:notaport", 0, family, default));
    }
}
