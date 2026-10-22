using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Warp.Tests;

public class SessionBatchingTests
{
    [Theory]
    [InlineData(true, true, Delivery.Reliable)]
    [InlineData(true, false, Delivery.Reliable)]
    [InlineData(false, true, Delivery.Reliable)]
    [InlineData(false, false, Delivery.Reliable)]
    [InlineData(true, true, Delivery.Unreliable)]
    [InlineData(true, false, Delivery.Unreliable)]
    [InlineData(false, true, Delivery.Unreliable)]
    [InlineData(false, false, Delivery.Unreliable)]
    public void NegotiatesToggleAndPreservesMessagesInBothDirections(bool hostBatches, bool clientBatches,
        Delivery delivery)
    {
        using Pair pair = new(hostBatches, clientBatches);
        List<byte> atHost = new(), atClient = new();
        pair.Host.OnRaw(42, (_, ref reader, channel) => { Assert.Equal(delivery, channel); atHost.Add(reader.ReadByte()); });
        pair.Client.OnRaw(42, (_, ref reader, channel) => { Assert.Equal(delivery, channel); atClient.Add(reader.ReadByte()); });
        long before = pair.Client.HostPeer!.Connection!.Stats.PacketsSent;
        for (byte i = 0; i < 10; i++)
        {
            pair.Client.SendRaw(pair.Client.HostPeer, 42, new[] { i }, delivery);
            pair.Host.SendRaw(pair.Remote, 42, new[] { i }, delivery);
        }

        long expectedBeforeFlush = hostBatches && clientBatches ? 0 : 10;
        Assert.Equal(expectedBeforeFlush, pair.Client.HostPeer.Connection.Stats.PacketsSent - before);
        pair.Client.Flush();
        Assert.Equal(hostBatches && clientBatches ? 1 : 10,
            pair.Client.HostPeer.Connection.Stats.PacketsSent - before);
        pair.Until(() => atHost.Count == 10 && atClient.Count == 10);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => (byte)i), atHost);
        Assert.Equal(atHost, atClient);
    }

    [Fact]
    public void LargeAndEmptyMessagesDoNotOvertakeBufferedMessages()
    {
        using Pair pair = new();
        List<byte[]> received = new();
        pair.Host.OnRaw(42, (_, ref reader, _) => received.Add(reader.ReadBytes(reader.Remaining).ToArray()));
        byte[][] payloads = { new byte[] { 1 }, new byte[2000], Array.Empty<byte>(), new byte[] { 2 } };
        foreach (byte[] payload in payloads)
        {
            pair.Client.SendRaw(pair.Client.HostPeer!, 42, payload);
        }

        pair.Until(() => received.Count == payloads.Length);
        Assert.Equal(payloads, received);
    }

    [Theory]
    [InlineData(false, false, Delivery.Reliable)]
    [InlineData(false, true, Delivery.Reliable)]
    [InlineData(true, false, Delivery.Reliable)]
    [InlineData(true, true, Delivery.Reliable)]
    [InlineData(false, false, Delivery.Unreliable)]
    [InlineData(false, true, Delivery.Unreliable)]
    [InlineData(true, false, Delivery.Unreliable)]
    [InlineData(true, true, Delivery.Unreliable)]
    public void QueuedBatchIsDeliveredBeforeDisconnect(bool toClient, bool externalPump, Delivery delivery)
    {
        using Pair pair = new(externalPump: externalPump);
        NetSession sender = toClient ? pair.Host : pair.Client;
        NetSession receiver = toClient ? pair.Client : pair.Host;
        NetPeer destination = toClient ? pair.Remote : pair.Client.HostPeer!;
        Connection receivingConnection = toClient ? pair.Client.HostPeer!.Connection! : pair.Remote.Connection!;
        List<string> events = new();
        receiver.OnRaw(42, (_, ref reader, channel) =>
        {
            Assert.Equal(delivery, channel);
            events.Add($"message {reader.ReadByte()}");
        });
        receiver.PeerLeft += (_, _) => events.Add("disconnected");
        receiver.Ended += (_, _) => events.Add("disconnected");
        sender.SendRaw(destination, 42, new byte[] { 1 }, delivery);
        sender.SendRaw(destination, 42, new byte[] { 2 }, delivery);
        sender.Flush();
        sender.Shutdown();
        if (externalPump)
        {
            sender.Transport.Shutdown();
        }

        // Read all packets first: the physical connection has closed, but its Data event precedes
        // the Disconnected event. The session must honor that queue order for every batch member.
        receiver.Transport.Receive();
        Assert.Equal(ConnectionState.Disconnected, receivingConnection.State);
        Assert.Empty(events);
        pair.Receive(receiver);
        Assert.Equal(new[] { "message 1", "message 2", "disconnected" }, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandlerShutdownStopsRemainingBatchMessages(bool toClient)
    {
        using Pair pair = new();
        NetSession sender = toClient ? pair.Host : pair.Client;
        NetSession receiver = toClient ? pair.Client : pair.Host;
        NetPeer destination = toClient ? pair.Remote : pair.Client.HostPeer!;
        int received = 0;
        receiver.OnRaw(42, (_, ref reader, _) =>
        {
            received++;
            receiver.Shutdown();
        });
        sender.SendRaw(destination, 42, new byte[] { 1 });
        sender.SendRaw(destination, 42, new byte[] { 2 });
        sender.Flush();
        pair.Receive(receiver);
        Assert.Equal(1, received);
        Assert.Equal(SessionState.Ended, receiver.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateAlsoSubmitsPendingMessagesOnOtherChannel(bool large)
    {
        using Pair pair = new();
        List<byte> received = new();
        pair.Host.OnRaw(42, (_, ref reader, _) => received.Add(reader.ReadByte()));
        pair.Client.SendRaw(pair.Client.HostPeer!, 42, new byte[] { 1 }, Delivery.Unreliable);
        byte[] payload = new byte[large ? 2000 : 1];
        payload[0] = 2;
        pair.Client.SendRaw(pair.Client.HostPeer!, 42, payload, flags: SendFlags.Immediate);
        // Receiving alone must see both sends; no session Flush follows the immediate send.
        pair.Host.Receive();
        Assert.Equal(new byte[] { 1, 2 }, received.OrderBy(x => x));
    }

    [Fact]
    public void GoodbyeDoesNotOvertakeEarlierReliableMessages()
    {
        using Pair pair = new();
        bool received = false;
        SessionEndReason? ended = null;
        pair.Client.OnRaw(42, (_, ref reader, _) => received = reader.ReadByte() == 1);
        pair.Client.Ended += (reason, _) => ended = reason;
        pair.Host.SendRaw(pair.Remote, 42, new byte[] { 1 });
        pair.Host.Kick(pair.Remote, "done");
        pair.Until(() => ended != null);
        Assert.True(received);
        Assert.Equal(SessionEndReason.Kicked, ended);
    }

    [Fact]
    public void BatchEnvelopeDoesNotCountAgainstIndividualMessageLimit()
    {
        using Pair pair = new(hostMaximum: 64);
        int received = 0;
        pair.Host.OnRaw(42, (_, ref reader, _) => received++);
        for (int i = 0; i < 30; i++)
        {
            pair.Client.SendRaw(pair.Client.HostPeer!, 42, new byte[] { 1 });
        }

        pair.Until(() => received == 30);
        Assert.Equal(SessionState.Active, pair.Client.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingWhenBatchSubmissionExhaustsBackendBudgetDoesNotThrow(bool shutdown)
    {
        using Pair pair = new();
        pair.Client.Transport.MaxQueuedDataEventsPerConnection = 1;
        pair.Host.SendRaw(pair.Remote, 42, new byte[] { 1 });
        // Fill the recipient's backend queue before the pending batch is submitted.
        pair.Remote.Connection!.Send(new byte[] { 42, 0, 2 }, Delivery.Reliable);
        if (shutdown)
        {
            pair.Host.Shutdown();
        }
        else
        {
            pair.Host.Kick(pair.Remote, "done");
        }

        pair.Until(() => pair.Client.State == SessionState.Ended);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedBatchDeliversNoPartialMessages(bool toClient)
    {
        using Pair pair = new();
        NetSession receiver = toClient ? pair.Client : pair.Host;
        int received = 0;
        receiver.OnRaw(42, (_, ref reader, _) => received++);
        SessionEndReason? ended = null;
        pair.Client.Ended += (reason, _) => ended = reason;
        using NetWriter frame = new();
        frame.WriteUInt16(SessionBatcher.MessageId);
        frame.WriteInt32(3);
        frame.WriteUInt16(42);
        frame.WriteByte(1);
        frame.WriteByte(1); // Truncated next length, following a completely valid first message.
        Connection sender = toClient ? pair.Remote.Connection! : pair.Client.HostPeer!.Connection!;
        sender.Send(frame.WrittenSpan, Delivery.Reliable);
        pair.Until(() => ended != null);
        Assert.Equal(0, received);
        Assert.Equal(SessionEndReason.ProtocolError, ended);
    }

    private sealed class Pair : IDisposable
    {
        private readonly LocalTransport _hostTransport, _clientTransport;
        private readonly bool _externalPump;
        public readonly NetSession Host, Client;
        public NetPeer Remote => Host.Peers.Single(p => !p.IsLocal);

        public Pair(bool hostBatches = true, bool clientBatches = true, int hostMaximum = 16 * 1024 * 1024,
            bool externalPump = false)
        {
            _externalPump = externalPump;
            LocalNetwork network = new();
            _hostTransport = new LocalTransport(network, "host");
            _clientTransport = new LocalTransport(network, "client");
            NetSessionOptions hostOptions = new() { BatchMessages = hostBatches, MaxMessageSize = hostMaximum };
            NetSessionOptions clientOptions = new() { BatchMessages = clientBatches };
            if (externalPump)
            {
                _hostTransport.Listen();
                Host = NetSession.CreateHost(_hostTransport, options: hostOptions);
                Client = NetSession.CreateClient(_clientTransport.Connect("host"), "client", options: clientOptions);
            }
            else
            {
                Host = NetSession.Host(_hostTransport, options: hostOptions);
                Client = NetSession.Join(_clientTransport, "host", "client", options: clientOptions);
            }

            Until(() => Client.State == SessionState.Active);
        }

        public void Receive(NetSession session)
        {
            if (_externalPump)
            {
                session.Transport.Receive();
                while (session.Transport.Poll(out TransportEvent ev))
                {
                    session.ProcessEvent(ev);
                }
            }

            session.Receive();
        }

        public void Until(Func<bool> condition)
        {
            for (int i = 0; i < 100 && !condition(); i++)
            {
                Receive(Host);
                Host.Flush();
                Receive(Client);
                Client.Flush();
                if (_externalPump)
                {
                    _hostTransport.Flush();
                    _clientTransport.Flush();
                }
            }

            Assert.True(condition(), "Expected session state did not arrive.");
        }

        public void Dispose()
        {
            Client.Dispose();
            Host.Dispose();
            _clientTransport.Dispose();
            _hostTransport.Dispose();
        }
    }
}
