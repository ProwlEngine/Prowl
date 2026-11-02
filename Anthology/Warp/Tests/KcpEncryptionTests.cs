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

    /// <summary>Sits between a client and a host on loopback, forwarding datagrams and recording them.</summary>
    private sealed class Relay : IDisposable
    {
        private readonly IPEndPoint _host;
        private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private EndPoint? _client;

        public Relay(KcpTransport host)
        {
            _host = new IPEndPoint(IPAddress.Loopback, host.LocalPort);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        }

        public string Address => $"127.0.0.1:{((IPEndPoint)_socket.LocalEndPoint!).Port}";
        public List<byte[]> Seen { get; } = new();
        public Func<byte[], byte[]?>? ToHost { get; set; }
        public uint SessionId { get; private set; }

        public void Dispose() => _socket.Dispose();

        public void SendToHost(byte[] datagram) => _socket.SendTo(datagram, _host);

        public void Step()
        {
            byte[] buffer = new byte[2048];
            while (_socket.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int length = _socket.ReceiveFrom(buffer, ref from);
                byte[] datagram = buffer[..length];
                Seen.Add(datagram);
                if (from.Equals(_host))
                {
                    if (datagram[0] == 4)
                    {
                        SessionId = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(5));
                    }

                    if (_client != null)
                    {
                        _socket.SendTo(datagram, _client);
                    }
                }
                else
                {
                    _client = from;
                    byte[]? forwarded = ToHost == null ? datagram : ToHost(datagram);
                    if (forwarded != null)
                    {
                        _socket.SendTo(forwarded, _host);
                    }
                }
            }
        }
    }
}
