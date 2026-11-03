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

using Prowl.Courier.Transports.Kcp;

using Xunit;

namespace Prowl.Courier.Tests;

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
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");

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
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");

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
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");

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
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");

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

    [Fact]
    public void AnEncryptedConnectionNeverSendsPlaintextOnceItsKeysAreGone()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        Pump pump = new(host, client);
        Connection toHost = client.Connect($"127.0.0.1:{host.LocalPort}");
        pump.Until(() => toHost.IsConnected, what: "connect");

        KcpConnection closed = (KcpConnection)toHost;
        closed.Disconnect();
        long sent = closed.Stats.PacketsSent;
        client.SendSession(closed, KcpPacketType.Ping, new byte[] { 1, 2, 3, 4 });
        Assert.Equal(sent, closed.Stats.PacketsSent);
    }

    [Fact]
    public void AnAcceptWhoseKeysDoNotMatchIsIgnoredEvenWithoutAPinnedKey()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        relay.BeforeClient = datagram =>
        {
            if (datagram[0] != (byte)KcpPacketType.Accept)
            {
                return Array.Empty<byte[]>();
            }

            byte[] forged = (byte[])datagram.Clone();
            using ECDiffieHellman other = KcpCipher.CreateKeyExchange(out byte[] otherKey);
            otherKey.CopyTo(forged, KcpPacket.AcceptSize);
            return new[] { forged };
        };
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected; }, what: "connect");

        toHost.Send(new byte[] { 5 }, Delivery.Reliable);
        pump.Until(() => { relay.Step(); return pump.EventsOf(host).Any(ev => ev.Type == TransportEventType.Data); },
            timeoutMs: 2000, what: "data under the agreed keys");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClientWithACookieThatNoLongerWorksAsksForANewOne(bool encrypted)
    {
        using KcpTransport host = new(Options(encrypted ? new KcpEncryption() : null));
        host.Listen();
        using KcpTransport client = new(Options(encrypted ? new KcpEncryption() : null));
        using Relay relay = new(host);
        bool spoiled = false;
        relay.ToClient = datagram =>
        {
            if (datagram[0] == (byte)KcpPacketType.Challenge && !spoiled)
            {
                spoiled = true;
                datagram[^1] ^= 0xFF;
            }

            return datagram;
        };
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.State != ConnectionState.Connecting; }, what: "connect");

        Assert.True(spoiled);
        Assert.True(toHost.IsConnected);
    }

    [Fact]
    public void AForgedAcceptDoesNotStopTheGenuineOne()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using KcpTransport host = new(Options(new KcpEncryption { SigningKey = key }));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption { TrustedHostKey = key.ExportSubjectPublicKeyInfo() }));
        using Relay relay = new(host);
        relay.BeforeClient = datagram =>
        {
            if (datagram[0] != (byte)KcpPacketType.Accept)
            {
                return Array.Empty<byte[]>();
            }

            byte[] forged = (byte[])datagram.Clone();
            forged.AsSpan(KcpPacket.AcceptSize + KcpCipher.PublicKeySize).Fill(0x11);
            return new[] { forged };
        };
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.State != ConnectionState.Connecting; }, what: "connect");

        Assert.True(toHost.IsConnected);
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
    public void SessionsFollowAClientToANewAddress(bool encrypted)
    {
        using KcpTransport host = new(Options(encrypted ? new KcpEncryption() : null));
        host.Listen();
        using KcpTransport client = new(Options(encrypted ? new KcpEncryption() : null));
        using Relay relay = new(host);
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");
        Connection toClient = host.Connections.Single();

        relay.MoveToNewAddress();
        toHost.Send(new byte[] { 1 }, Delivery.Reliable);
        for (int i = 0; i < 300; i++)
        {
            relay.Step();
            pump.Step();
        }

        Assert.Same(toClient, Assert.Single(host.Connections));
        Assert.Equal(relay.HostSideAddress, toClient.Address);
        Assert.Contains(pump.EventsOf(host), ev => ev.Type == TransportEventType.Data);
        toClient.Send(new byte[] { 2 }, Delivery.Reliable);
        pump.Until(() => { relay.Step(); return pump.EventsOf(client).Any(ev => ev.Type == TransportEventType.Data); },
            what: "the reply at the new address");
    }

    [Fact]
    public void ReplayingAClientPacketFromAnotherAddressDoesNotMoveTheSession()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        using Socket attacker = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        attacker.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");
        Connection toClient = host.Connections.Single();
        string address = toClient.Address;

        // An on-path observer races each fresh client packet to the host from its own address.
        relay.Spy = datagram =>
        {
            if (datagram[0] >= 16)
            {
                attacker.SendTo(datagram, new IPEndPoint(IPAddress.Loopback, relay.HostPort));
            }
        };
        for (int i = 0; i < 20; i++)
        {
            toHost.Send(new byte[] { (byte)i }, Delivery.Reliable);
            relay.Step();
            pump.Step();
            Assert.Equal(address, toClient.Address);
        }
    }

    [Fact]
    public void PathChallengesAreRateLimitedAcrossAddresses()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        List<Socket> sources = new();
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");

        // Each fresh client packet also reaches the host first from a new address, as if spoofed.
        relay.Spy = datagram =>
        {
            if (datagram[0] >= 16)
            {
                Socket source = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                source.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                source.SendTo(datagram, new IPEndPoint(IPAddress.Loopback, relay.HostPort));
                sources.Add(source);
            }
        };
        try
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 300)
            {
                toHost.Send(new byte[] { 1 }, Delivery.Unreliable);
                relay.Step();
                pump.Step();
            }

            pump.For(50);
            int challenges = sources.Sum(source =>
            {
                int count = 0;
                byte[] buffer = new byte[2048];
                while (source.Available > 0)
                {
                    count += source.Receive(buffer) > 0 && buffer[0] == (byte)KcpPacketType.PathChallenge ? 1 : 0;
                }

                return count;
            });
            Assert.True(sources.Count > 20);
            Assert.InRange(challenges, 1, 5);
        }
        finally
        {
            foreach (Socket source in sources)
            {
                source.Dispose();
            }
        }
    }

    [Fact]
    public void EncryptedClientsFollowAHostToANewAddress()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        Pump pump = new(host, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");

        relay.MoveClientSide();
        host.Connections.Single().Send(new byte[] { 1 }, Delivery.Reliable);
        pump.Until(() =>
        {
            relay.Step();
            return pump.EventsOf(client).Any(ev => ev.Type == TransportEventType.Data) && toHost.Address == relay.Address;
        }, what: "the client to follow the host");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARestartedHostTellsClientsTheirSessionIsGone(bool encrypted)
    {
        byte[] resetKey = RandomNumberGenerator.GetBytes(32);
        KcpEncryption? Encryption() => encrypted ? new KcpEncryption { ResetKey = resetKey } : null;
        using KcpTransport host = new(Options(Encryption()));
        host.Listen();
        using KcpTransport restarted = new(Options(Encryption()));
        restarted.Listen();
        using KcpTransport client = new(Options(encrypted ? new KcpEncryption() : null));
        using Relay relay = new(host);
        Pump pump = new(host, restarted, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; },
            what: "connect");

        relay.Retarget(restarted);
        toHost.Send(new byte[] { 1 }, Delivery.Reliable);
        pump.Until(() => { relay.Step(); return !toHost.IsConnected; }, timeoutMs: 1000, what: "the reset");
        Assert.Equal(DisconnectReason.RemoteClosed,
            pump.EventsOf(client).Single(ev => ev.Type == TransportEventType.Disconnected).Reason);
    }

    [Fact]
    public void AResetWithoutTheRightTokenIsIgnored()
    {
        using KcpTransport host = new(Options(new KcpEncryption { ResetKey = RandomNumberGenerator.GetBytes(32) }));
        host.Listen();
        using KcpTransport impostor = new(Options(new KcpEncryption { ResetKey = RandomNumberGenerator.GetBytes(32) }));
        impostor.Listen();
        using KcpTransport client = new(Options(new KcpEncryption()));
        using Relay relay = new(host);
        Pump pump = new(host, impostor, client);
        Connection toHost = client.Connect(relay.Address);
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; },
            what: "connect");

        relay.Retarget(impostor);
        toHost.Send(new byte[] { 1 }, Delivery.Reliable);
        for (int i = 0; i < 200; i++)
        {
            relay.Step();
            pump.Step();
        }

        Assert.True(toHost.IsConnected);
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
        pump.Until(() => { relay.Step(); return toHost.IsConnected && host.Connections.Count(c => c.IsConnected) == 1; }, what: "connect");
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
    public void TheSignedTranscriptIsLabelledAndBoundToTheProtocol()
    {
        byte[] client = new byte[KcpCipher.PublicKeySize], host = new byte[KcpCipher.PublicKeySize];
        byte[] first = KcpCipher.Transcript(client, host, 1, 2, 0x50524157);
        byte[] other = KcpCipher.Transcript(client, host, 1, 2, 0x12345678);

        Assert.NotEqual(first, other);
        Assert.StartsWith("Prowl.Courier.Kcp", Encoding.ASCII.GetString(first));
    }

    [Fact]
    public void DeeplyReorderedPacketsStillOpenButReplaysDoNot()
    {
        (KcpCipher client, KcpCipher host) = CipherPair();
        byte[] header = { 16, 1, 2, 3, 4 };
        byte[][] sealedPackets = new byte[500][];
        for (int i = 0; i < sealedPackets.Length; i++)
        {
            byte[] packet = new byte[header.Length + 1 + KcpCipher.Overhead];
            client.Seal(header, new[] { (byte)i }, packet);
            sealedPackets[i] = packet;
        }

        byte[] plain = new byte[16];
        Assert.True(host.Open(sealedPackets[^1], header.Length, plain, out _));
        for (int i = 0; i < sealedPackets.Length - 1; i++)
        {
            Assert.True(host.Open(sealedPackets[i], header.Length, plain, out _), $"packet {i}");
        }

        Assert.False(host.Open(sealedPackets[3], header.Length, plain, out _));
        Assert.False(host.Open(sealedPackets[^1], header.Length, plain, out _));
    }

    [Fact]
    public void KeysMoveOnEveryEpochAndOldEpochsCannotBeOpened()
    {
        (KcpCipher client, KcpCipher host) = CipherPair(epochBits: 4);
        byte[] header = { 16, 1, 2, 3, 4 };
        byte[][] sealedPackets = new byte[100][];
        for (int i = 0; i < sealedPackets.Length; i++)
        {
            sealedPackets[i] = new byte[header.Length + 1 + KcpCipher.Overhead];
            client.Seal(header, new[] { (byte)i }, sealedPackets[i]);
        }

        byte[] plain = new byte[16];
        for (int i = 0; i < 40; i++)
        {
            if (i != 31 && i != 2)
            {
                Assert.True(host.Open(sealedPackets[i], header.Length, plain, out _), $"packet {i}");
                Assert.Equal(i, plain[0]);
            }
        }

        // 31 is from the epoch just before, still open for late packets. 2 is two epochs back, its key gone.
        Assert.True(host.Open(sealedPackets[31], header.Length, plain, out _));
        Assert.False(host.Open(sealedPackets[2], header.Length, plain, out _));
        for (int i = 40; i < 100; i++)
        {
            Assert.True(host.Open(sealedPackets[i], header.Length, plain, out _), $"packet {i}");
        }
    }

    private static (KcpCipher Client, KcpCipher Host) CipherPair(int epochBits = KcpCipher.DefaultEpochBits)
    {
        using ECDiffieHellman clientExchange = KcpCipher.CreateKeyExchange(out byte[] clientPublic);
        using ECDiffieHellman hostExchange = KcpCipher.CreateKeyExchange(out byte[] hostPublic);
        return (KcpCipher.Create(clientExchange, clientPublic, hostPublic, 1, 2, true, new byte[16], epochBits)!,
            KcpCipher.Create(hostExchange, clientPublic, hostPublic, 1, 2, false, new byte[16], epochBits)!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidPublicKeysAreRefusedWithoutThrowing(int kind)
    {
        using ECDiffieHellman local = KcpCipher.CreateKeyExchange(out byte[] mine);
        byte[] key = BadKey(kind);
        Assert.Null(KcpCipher.Create(local, key, mine, 1, 2, false, new byte[16]));
        Assert.Null(KcpCipher.Create(local, mine, key, 1, 2, true, new byte[16]));
    }

    [Fact]
    public void AHandshakeWithAnInvalidKeyDoesNotBreakTheHost()
    {
        using KcpTransport host = new(Options(new KcpEncryption()));
        host.Listen();
        using Socket raw = RawSocket(host);
        byte[] request = EncryptedRequest(0x1234);
        raw.Send(request);
        byte[] challenge = Expect(raw, host, KcpPacketType.Challenge);
        byte[] response = (byte[])request.Clone();
        response[0] = (byte)KcpPacketType.ChallengeResponse;
        challenge.AsSpan(9, KcpPacket.CookieSize).CopyTo(response.AsSpan(9));
        challenge.AsSpan(5, 4).CopyTo(response.AsSpan(KcpPacket.HandshakeIssuedOffset));
        BadKey(1).CopyTo(response, KcpPacket.HandshakeKeyOffset);

        for (int i = 0; i < 20; i++)
        {
            raw.Send(response);
        }

        System.Threading.Thread.Sleep(50);
        host.Receive();
        host.Flush();
        Assert.Empty(host.Connections);
    }

    [Fact]
    public void AnAcceptWithAnInvalidKeyDoesNotBreakTheClient()
    {
        using Socket fakeHost = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        fakeHost.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using KcpTransport client = new(Options(new KcpEncryption()));
        Connection toHost = client.Connect($"127.0.0.1:{((IPEndPoint)fakeHost.LocalEndPoint!).Port}");
        client.Flush();

        byte[] buffer = new byte[2048];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        fakeHost.ReceiveTimeout = 2000;
        fakeHost.ReceiveFrom(buffer, ref from);
        byte[] accept = new byte[KcpPacket.EncryptedAcceptSize];
        accept[0] = (byte)KcpPacketType.Accept;
        accept.AsSpan(1, 4).CopyTo(accept.AsSpan(1));
        buffer.AsSpan(5, 4).CopyTo(accept.AsSpan(1));
        BinaryPrimitives.WriteUInt32LittleEndian(accept.AsSpan(5), 99);
        BadKey(1).CopyTo(accept, KcpPacket.AcceptSize);
        fakeHost.SendTo(accept, from);

        System.Threading.Thread.Sleep(50);
        client.Receive();
        client.Flush();
        Assert.False(toHost.IsConnected);
    }

    private static byte[] BadKey(int kind)
    {
        byte[] key = new byte[KcpCipher.PublicKeySize];
        key[0] = 4;
        switch (kind)
        {
            case 1: key.AsSpan(1).Fill(0xFF); break;
            case 2: new Random(1).NextBytes(key.AsSpan(1)); break;
            case 3: key[0] = 2; break;
        }

        return key;
    }

    private static Socket RawSocket(KcpTransport host)
    {
        Socket raw = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        raw.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        raw.Connect(new IPEndPoint(IPAddress.Loopback, host.LocalPort));
        return raw;
    }

    /// <summary>A ConnectRequest with the default settings, asking for encryption.</summary>
    private static byte[] EncryptedRequest(uint nonce)
    {
        KcpTransportOptions defaults = new();
        byte[] request = new byte[KcpPacket.HandshakeRequestSize];
        request[0] = (byte)KcpPacketType.ConnectRequest;
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(1), defaults.ProtocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(5), nonce);
        request[KcpPacket.HandshakeVersionOffset] = KcpPacket.WireVersion;
        request[KcpPacket.HandshakeChannelsOffset] = (byte)defaults.Channels;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(KcpPacket.HandshakeMtuOffset), (ushort)defaults.Mtu);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(KcpPacket.HandshakeWindowOffset),
            (ushort)defaults.Reliability.ReceiveWindow);
        request[KcpPacket.HandshakeFlagsOffset] = KcpPacket.EncryptedFlag;
        return request;
    }

    private static byte[] Expect(Socket raw, KcpTransport host, KcpPacketType type)
    {
        byte[] buffer = new byte[KcpPacket.MaxDatagramSize];
        raw.ReceiveTimeout = 2000;
        for (int i = 0; i < 100; i++)
        {
            System.Threading.Thread.Sleep(5);
            host.Receive();
            host.Flush();
            if (raw.Available > 0)
            {
                int length = raw.Receive(buffer);
                if (buffer[0] == (byte)type)
                {
                    return buffer[..length];
                }
            }
        }

        throw new TimeoutException(type.ToString());
    }

    [Fact]
    public void KeyAgreementRunsOffTheThreadThatPumpsTheHost()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        KcpTransportOptions hostOptions = Options(new KcpEncryption { SigningKey = key });
        hostOptions.MaxHandshakesPerAddressPerSecond = 100;
        hostOptions.MaxConnectionsPerAddress = 100;
        using KcpTransport host = new(hostOptions);
        host.Listen();
        KcpTransport[] clients = Enumerable.Range(0, 20).Select(_ => new KcpTransport(Options(new KcpEncryption()))).ToArray();
        try
        {
            Connection[] connections = clients.Select(c => c.Connect($"127.0.0.1:{host.LocalPort}")).ToArray();
            System.Diagnostics.Stopwatch onHostThread = new(), total = System.Diagnostics.Stopwatch.StartNew();
            while (!connections.All(c => c.IsConnected) && total.ElapsedMilliseconds < 5000)
            {
                foreach (KcpTransport client in clients)
                {
                    client.Update();
                    while (client.Poll(out _))
                    {
                    }
                }

                onHostThread.Start();
                host.Update();
                while (host.Poll(out _))
                {
                }

                onHostThread.Stop();
                System.Threading.Thread.Sleep(1);
            }

            Assert.True(connections.All(c => c.IsConnected));
            Assert.InRange(onHostThread.ElapsedMilliseconds, 0, 15);
        }
        finally
        {
            foreach (KcpTransport client in clients)
            {
                client.Dispose();
            }
        }
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
        private IPEndPoint _host;
        private readonly List<Socket> _clientSides = new() { Bound() };
        private EndPoint? _client;
        private Socket _hostSide = Bound();
        private Socket _clientSide;

        public Relay(KcpTransport host)
        {
            _host = new IPEndPoint(IPAddress.Loopback, host.LocalPort);
            _clientSide = _clientSides[0];
        }

        /// <summary>Changes each datagram from the host before it reaches the client.</summary>
        public Func<byte[], byte[]>? ToClient { get; set; }

        /// <summary>Extra datagrams to deliver to the client ahead of each one from the host.</summary>
        public Func<byte[], byte[][]>? BeforeClient { get; set; }

        /// <summary>Called with each datagram from the client before it is forwarded.</summary>
        public Action<byte[]>? Spy { get; set; }

        public string Address => $"127.0.0.1:{((IPEndPoint)_clientSide.LocalEndPoint!).Port}";

        public int HostPort => _host.Port;

        /// <summary>The address the host sees this client at.</summary>
        public string HostSideAddress => $"127.0.0.1:{((IPEndPoint)_hostSide.LocalEndPoint!).Port}";

        public List<byte[]> Seen { get; } = new();
        public Func<byte[], byte[]?>? ToHost { get; set; }
        public uint SessionId { get; private set; }

        public void Dispose()
        {
            foreach (Socket socket in _clientSides)
            {
                socket.Dispose();
            }

            _hostSide.Dispose();
        }

        /// <summary>Answers the client from a new address, like a host behind a NAT that rebinds. The old one still listens.</summary>
        public void MoveClientSide()
        {
            _clientSide = Bound();
            _clientSides.Add(_clientSide);
        }

        /// <summary>Sends the client's traffic to another host from now on, like a server that restarted.</summary>
        public void Retarget(KcpTransport host) => _host = new IPEndPoint(IPAddress.Loopback, host.LocalPort);

        public void MoveToNewAddress()
        {
            _hostSide.Dispose();
            _hostSide = Bound();
        }

        public void SendToHost(byte[] datagram) => _hostSide.SendTo(datagram, _host);

        public void Step()
        {
            byte[] buffer = new byte[2048];
            foreach (Socket clientSide in _clientSides)
            {
                while (clientSide.Available > 0)
                {
                    EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    int length = clientSide.ReceiveFrom(buffer, ref from);
                    byte[] datagram = buffer[..length];
                    Seen.Add(datagram);
                    _client = from;
                    Spy?.Invoke(datagram);
                    byte[]? forwarded = ToHost == null ? datagram : ToHost(datagram);
                    if (forwarded != null)
                    {
                        _hostSide.SendTo(forwarded, _host);
                    }
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

                if (ToClient != null)
                {
                    datagram = ToClient(datagram);
                }

                if (_client != null)
                {
                    foreach (byte[] extra in BeforeClient?.Invoke(datagram) ?? Array.Empty<byte[]>())
                    {
                        _clientSide.SendTo(extra, _client);
                    }

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
