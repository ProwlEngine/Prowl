// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using Prowl.Warp.Transports.Kcp;

using Xunit;

namespace Prowl.Warp.Tests;

public class KcpEncryptionTests
{
    private static KcpTransportOptions Options(KcpEncryption? encryption) => new()
    {
        Port = 0,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        Timeout = TimeSpan.FromSeconds(3),
        HandshakeResendInterval = TimeSpan.FromMilliseconds(50),
        Encryption = encryption
    };

    [Fact]
    public void EncryptedSessionsCarryEveryDeliveryWithoutRevealingPayloads()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count == 1; }, what: "connect");

        byte[] secret = Encoding.ASCII.GetBytes("SECRETSECRETSECRET");
        byte[] large = TestData.Pattern(5000, 1);
        secret.CopyTo(large, 100);
        toHost.Send(large, Delivery.Reliable);
        toHost.Send(secret, Delivery.Unreliable);
        toHost.Send(secret, Delivery.UnreliableSequenced, channel: 1);
        host.Connections.Single().Send(large, Delivery.Reliable, channel: 2);
        pump.Until(() =>
        {
            relay.Step();
            return pump.EventsOf(host).Count(ev => ev.Type == TransportEventType.Data) == 3 &&
                   pump.EventsOf(client).Any(ev => ev.Type == TransportEventType.Data);
        }, what: "all messages");

        Received[] atHost = pump.EventsOf(host).Where(ev => ev.Type == TransportEventType.Data).ToArray();
        Assert.Contains(atHost, ev => ev.Data.SequenceEqual(large) && ev.Delivery == Delivery.Reliable);
        Assert.Contains(atHost, ev => ev.Data.SequenceEqual(secret) && ev.Delivery == Delivery.Unreliable);
        Assert.Contains(atHost, ev => ev.Data.SequenceEqual(secret) && ev.Delivery == Delivery.UnreliableSequenced);
        Assert.Equal(large, pump.EventsOf(client).Single(ev => ev.Type == TransportEventType.Data).Data);
        Assert.DoesNotContain(relay.Seen, datagram => Contains(datagram, secret));
    }

    [Fact]
    public void TamperedPacketsAreDroppedAndReliableDataStillArrivesIntact()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count == 1; }, what: "connect");

        int count = 0;
        relay.ToHost = datagram =>
        {
            if (datagram[0] >= 16 && ++count % 3 == 0)
            {
                datagram[^1] ^= 0x5A;
            }

            return datagram;
        };
        byte[] message = TestData.Pattern(20_000, 2);
        toHost.Send(message, Delivery.Reliable);
        pump.Until(() => { relay.Step(); return pump.EventsOf(host).Any(ev => ev.Type == TransportEventType.Data); },
            what: "the message");

        Assert.Equal(message, pump.EventsOf(host).Single(ev => ev.Type == TransportEventType.Data).Data);
        Assert.True(toHost.IsConnected);
    }

    [Fact]
    public void ReplayedPacketsAreIgnored()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count == 1; }, what: "connect");

        byte[]? captured = null;
        relay.ToHost = datagram =>
        {
            if (datagram[0] == 17)
            {
                captured ??= (byte[])datagram.Clone();
            }

            return datagram;
        };
        toHost.Send(new byte[] { 1, 2, 3 }, Delivery.Unreliable);
        pump.Until(() => { relay.Step(); return captured != null; }, what: "the unreliable packet");
        for (int i = 0; i < 3; i++)
        {
            relay.SendToHost(captured!);
        }

        pump.For(100);
        Assert.Single(pump.EventsOf(host), ev => ev.Type == TransportEventType.Data);
    }

    [Fact]
    public void ForgedDisconnectsAreIgnored()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count == 1; }, what: "connect");

        byte[] disconnect = new byte[KcpPacket.SessionHeaderSize];
        disconnect[0] = 20;
        BinaryPrimitives.WriteUInt32LittleEndian(disconnect.AsSpan(1), relay.SessionId);
        relay.SendToHost(disconnect);
        pump.For(100);

        Assert.Single(host.Connections);
    }

    [Fact]
    public void ClientsThatTrustTheHostKeyConnect()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using KcpTransport host = new(Options(new KcpEncryption { SigningKey = key }));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption
        {
            TrustedHostKey = key.ExportSubjectPublicKeyInfo()
        }));
        Pump pump = new(host, client);
        Connection toHost = client.Connect($"127.0.0.1:{host.LocalPort}");
        pump.Until(() => toHost.IsConnected, what: "connect");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClientsRefuseAHostWithoutTheTrustedKey(bool hostSigns)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using KcpTransport host = new(Options(new KcpEncryption { SigningKey = hostSigns ? other : null }));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption
        {
            TrustedHostKey = key.ExportSubjectPublicKeyInfo()
        }));
        Pump pump = new(host, client);
        Connection toHost = client.Connect($"127.0.0.1:{host.LocalPort}");
        pump.Until(() => toHost.State == ConnectionState.Disconnected, what: "refusal");

        Assert.Equal(DisconnectReason.AuthenticationFailed, Assert.Single(pump.EventsOf(client)).Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EncryptedAndPlainEndsRefuseEachOther(bool hostEncrypts)
    {
        using KcpTransport host = new(Options(hostEncrypts ? new KcpEncryption() : null));
        host.Listen();
        using KcpTransport client = new(Options(hostEncrypts ? null : new KcpEncryption()));
        Pump pump = new(host, client);
        Connection toHost = client.Connect($"127.0.0.1:{host.LocalPort}");
        pump.Until(() => toHost.State == ConnectionState.Disconnected, what: "refusal");

        Assert.Equal(DisconnectReason.ProtocolError, Assert.Single(pump.EventsOf(client)).Reason);
        Assert.Empty(host.Connections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyEncryptedSessionsFollowAClientToANewAddress(bool encrypted)
    {
        using KcpTransport host = new(Options(encrypted ? new KcpEncryption() : null));
        host.Listen();
        using KcpTransport client = new(Options(encrypted ? new KcpEncryption() : null));
        using Relay relay = new(host);
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count == 1; }, what: "connect");
        Connection toClient = host.Connections.Single();

        relay.MoveToNewAddress();
        toHost.Send(new byte[] { 1 }, Delivery.Reliable);
        for (int i = 0; i < 300; i++)
        {
            relay.Step();
            pump.Step();
        }

        if (!encrypted)
        {
            Assert.DoesNotContain(pump.EventsOf(host), ev => ev.Type == TransportEventType.Data);
            return;
        }

        Assert.Same(toClient, Assert.Single(host.Connections));
        Assert.Equal(relay.HostSideAddress, toClient.Address);
        Assert.Contains(pump.EventsOf(host), ev => ev.Type == TransportEventType.Data);
        toClient.Send(new byte[] { 2 }, Delivery.Reliable);
        pump.Until(() => { relay.Step(); return pump.EventsOf(client).Any(ev => ev.Type == TransportEventType.Data); },
            what: "the reply at the new address");
    }

    [Fact]
    public void ForgedPacketsFromANewAddressDoNotMoveTheSession()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count == 1; }, what: "connect");
        string address = host.Connections.Single().Address;

        byte[] forged = new byte[100];
        forged[0] = 16;
        BinaryPrimitives.WriteUInt32LittleEndian(forged.AsSpan(1), relay.SessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(forged.AsSpan(5), 1000);
        relay.MoveToNewAddress();
        relay.SendToHost(forged);
        pump.For(100);

        Assert.Equal(address, host.Connections.Single().Address);
    }

    [Fact]
    public void OnlyP256KeysAreAccepted()
    {
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ArgumentException>(() => new KcpTransport(Options(new KcpEncryption { SigningKey = p384 })));
        Assert.Throws<ArgumentException>(() =>
            new KcpTransport(Options(new KcpEncryption { TrustedHostKey = p384.ExportSubjectPublicKeyInfo() })));
        Assert.Throws<ArgumentException>(() =>
            new KcpTransport(Options(new KcpEncryption { TrustedHostKey = new byte[] { 1, 2, 3 } })));
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Sits between a client and a host on loopback, forwarding datagrams and recording them. It reaches the
    ///     host from a socket of its own, which <see cref="MoveToNewAddress" /> swaps, like a NAT rebinding.
    /// </summary>
    private sealed class Relay : IDisposable
    {
        private readonly IPEndPoint _host;
        private readonly Socket _clientSide = Bound();
        private EndPoint? _client;
        private Socket _hostSide = Bound();

        public Relay(KcpTransport host) => _host = new IPEndPoint(IPAddress.Loopback, host.LocalPort);

        public string Address => $"127.0.0.1:{((IPEndPoint)_clientSide.LocalEndPoint!).Port}";

        /// <summary>The address the host sees this client at.</summary>
        public string HostSideAddress => $"127.0.0.1:{((IPEndPoint)_hostSide.LocalEndPoint!).Port}";

        public List<byte[]> Seen { get; } = new();
        public Func<byte[], byte[]?>? ToHost { get; set; }
        public uint SessionId { get; private set; }

        public void Dispose()
        {
            _clientSide.Dispose();
            _hostSide.Dispose();
        }

        public void MoveToNewAddress()
        {
            _hostSide.Dispose();
            _hostSide = Bound();
        }

        public void SendToHost(byte[] datagram) => _hostSide.SendTo(datagram, _host);

        public void Step()
        {
            byte[] buffer = new byte[2048];
            while (_clientSide.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int length = _clientSide.ReceiveFrom(buffer, ref from);
                byte[] datagram = buffer[..length];
                Seen.Add(datagram);
                _client = from;
                byte[]? forwarded = ToHost == null ? datagram : ToHost(datagram);
                if (forwarded != null)
                {
                    _hostSide.SendTo(forwarded, _host);
                }
            }

            while (_hostSide.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int length = _hostSide.ReceiveFrom(buffer, ref from);
                byte[] datagram = buffer[..length];
                Seen.Add(datagram);
                if (datagram[0] == 4)
                {
                    SessionId = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(5));
                }

                if (_client != null)
                {
                    _clientSide.SendTo(datagram, _client);
                }
            }
        }

        private static Socket Bound()
        {
            Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return socket;
        }
    }
}
