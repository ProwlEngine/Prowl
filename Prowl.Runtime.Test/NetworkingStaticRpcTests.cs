using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

// The prologues and descriptors are what the script compiler generates, exercised against real sessions.
public static class StaticRpcProbe
{
    public static readonly List<(int Value, NetPeer? Caller)> Calls = new();
    public static Action? BodyAction;
    private static readonly StaticRpcMember s_broadcast = Create(901, RpcMode.Broadcast);
    private static readonly StaticRpcMember s_host = Create(902, RpcMode.Host);
    private static readonly StaticRpcMember s_hostOnly = Create(903, RpcMode.Broadcast, RpcFlags.HostOnly);
    private static readonly StaticRpcMember s_unreliable = Create(904, RpcMode.Broadcast, RpcFlags.Unreliable);

    private static StaticRpcMember Create(uint id, RpcMode mode, RpcFlags flags = RpcFlags.None) =>
        new(typeof(StaticRpcProbe), id, $"Call{id}(int)", mode, flags,
            (ref NetReader reader, bool execute) =>
            {
                int value = NetSerializer<int>.Read(ref reader);
                if (!execute) return;
                StaticRpcMember.ArgumentsRead();
                Call(id, value);
            });

    public static void Call(uint id, int value)
    {
        StaticRpcMember rpc = id switch { 901 => s_broadcast, 902 => s_host, 903 => s_hostOnly, 904 => s_unreliable,
            _ => throw new ArgumentOutOfRangeException(nameof(id)) };
        if (rpc.Begin(out NetWriter writer))
        {
            using (writer)
            {
                NetSerializer<int>.Write(writer, value);
                if (!rpc.Send(writer)) return;
            }
        }
        Calls.Add((value, Rpc.Caller));
        BodyAction?.Invoke();
    }
}

public partial class NetworkingTests
{
    private static void WriteStaticRpc(NetWriter output, uint id, Action<NetWriter> arguments, int? caller = null)
    {
        if (caller is { } callerId) output.WriteVarUInt32((uint)callerId);
        output.WriteString($"{typeof(StaticRpcProbe).FullName}, {typeof(StaticRpcProbe).Assembly.GetName().Name}");
        output.WriteUInt32(id);
        using NetWriter payload = new(); arguments(payload);
        output.WriteBytesWithLength(payload.WrittenSpan);
    }

    private static (int Caller, uint Method, int Value) ReadStaticRpc(byte[] message)
    {
        NetReader reader = new(message);
        int caller = (int)reader.ReadVarUInt32();
        reader.ReadString();
        uint method = reader.ReadUInt32();
        NetReader arguments = new(reader.ReadBytesWithLength());
        return (caller, method, NetSerializer<int>.Read(ref arguments));
    }

    private static void ClearStaticProbe()
    {
        StaticRpcProbe.Calls.Clear();
        StaticRpcProbe.BodyAction = null;
    }

    [Fact]
    public void StaticBroadcastRelaysOnceAndPreservesTheOriginalCaller()
    {
        var (_, _, network) = ReadinessHost();
        using FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        ClearStaticProbe();
        try
        {
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            alice.Send(FakePeer.StaticRpcToHost, w => WriteStaticRpc(w, 901, a => NetSerializer<int>.Write(a, 17)));
            Pump(() => bob.Has(FakePeer.StaticRpcFromHost), alice, bob);
            Assert.Equal(17, Assert.Single(StaticRpcProbe.Calls).Value);
            Assert.Equal("alice", StaticRpcProbe.Calls[0].Caller!.Name);
            Assert.Equal((alice.Session.LocalPeer!.Id, 901u, 17), ReadStaticRpc(bob.Last(FakePeer.StaticRpcFromHost)));
            Assert.False(alice.Has(FakePeer.StaticRpcFromHost));
            Assert.Same(Networking.Networking.Session!.LocalPeer, Rpc.Caller);

            StaticRpcProbe.Call(901, 22);
            Pump(() => alice.Has(FakePeer.StaticRpcFromHost), alice, bob);
            Assert.Equal(2, StaticRpcProbe.Calls.Count);
            Assert.Equal((0, 901u, 22), ReadStaticRpc(alice.Last(FakePeer.StaticRpcFromHost)));
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Fact]
    public void StaticHostCallsStayOnTheHostAndHostOnlyCallsRejectClients()
    {
        var (_, _, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        ClearStaticProbe();
        try
        {
            Pump(() => client.IsReady, client);
            client.Send(FakePeer.StaticRpcToHost, w => WriteStaticRpc(w, 902, a => NetSerializer<int>.Write(a, 3)));
            Pump(() => StaticRpcProbe.Calls.Count == 1, client);
            Assert.Equal(3, StaticRpcProbe.Calls[0].Value);
            Assert.False(client.Has(FakePeer.StaticRpcFromHost));

            client.Send(FakePeer.StaticRpcToHost, w => WriteStaticRpc(w, 903, a => NetSerializer<int>.Write(a, 4)));
            PumpFor(20, client);
            Assert.Single(StaticRpcProbe.Calls);
            StaticRpcProbe.Call(902, 5); // Host-local calls execute directly.
            StaticRpcProbe.Call(903, 6);
            Pump(() => client.Has(FakePeer.StaticRpcFromHost), client);
            Assert.Equal(new[] { 3, 5, 6 }, StaticRpcProbe.Calls.Select(c => c.Value));
            Assert.Equal((0, 903u, 6), ReadStaticRpc(client.Last(FakePeer.StaticRpcFromHost)));
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Fact]
    public void StaticRelayUsesTheDeclaredDeliveryPolicy()
    {
        var (_, _, network) = ReadinessHost();
        using FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        ClearStaticProbe();
        try
        {
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            alice.Send(FakePeer.StaticRpcToHost, w => WriteStaticRpc(w, 904, a => NetSerializer<int>.Write(a, 7)));
            Pump(() => bob.Has(FakePeer.StaticRpcFromHost), alice, bob);
            Assert.Equal(Delivery.Unreliable, bob.Deliveries.Last(m => m.Id == FakePeer.StaticRpcFromHost).Delivery);
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedStaticArgumentsAreRejectedBeforeRelayOrBody(bool trailing)
    {
        var (_, _, network) = ReadinessHost();
        using FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        ClearStaticProbe();
        try
        {
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            alice.Send(FakePeer.StaticRpcToHost, w => WriteStaticRpc(w, 901, a =>
            {
                if (trailing) { NetSerializer<int>.Write(a, 1); a.WriteByte(0); }
            }));
            Pump(() => alice.Session.State == SessionState.Ended, alice, bob);
            Assert.Empty(StaticRpcProbe.Calls);
            Assert.False(bob.Has(FakePeer.StaticRpcFromHost));
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Fact]
    public void StaticBodiesMayThrowOrEndTheSessionWithoutLeakingCallerContext()
    {
        var (_, _, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        ClearStaticProbe();
        try
        {
            Pump(() => client.IsReady, client);
            StaticRpcProbe.BodyAction = () => throw new InvalidOperationException("gameplay failure");
            client.Send(FakePeer.StaticRpcToHost, w => WriteStaticRpc(w, 902, a => NetSerializer<int>.Write(a, 1)));
            Pump(() => StaticRpcProbe.Calls.Count == 1, client);
            Assert.True(Networking.Networking.IsActive);
            Assert.Same(Networking.Networking.Session!.LocalPeer, Rpc.Caller);
            StaticRpcProbe.BodyAction = () => Networking.Networking.Shutdown();
            client.Send(FakePeer.StaticRpcToHost, w => WriteStaticRpc(w, 902, a => NetSerializer<int>.Write(a, 2)));
            Pump(() => Networking.Networking.Session == null, client);
            Assert.Null(Rpc.Caller);
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Fact]
    public void StaticClientCallsAndHostRelaysNeedNoSceneObject()
    {
        Scene scene = CreateScene();
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(scene, snapshot);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        ClearStaticProbe();
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsActive, host);
            StaticRpcProbe.Call(901, 9); // Admitted, but still waiting for a scene.
            Assert.Empty(StaticRpcProbe.Calls);
            host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan),
                host.Session.Peers.Single(p => !p.IsLocal));
            Pump(() => Networking.Networking.IsReady, host);
            StaticRpcProbe.Call(902, 10);
            Pump(() => host.Has(FakePeer.StaticRpcToHost), host);
            Assert.Empty(StaticRpcProbe.Calls);
            StaticRpcProbe.Call(901, 11);
            Assert.Equal(11, Assert.Single(StaticRpcProbe.Calls).Value);
            StaticRpcProbe.Call(903, 12); // Client-side HostOnly guard.
            Assert.Single(StaticRpcProbe.Calls);

            host.Send(FakePeer.StaticRpcFromHost, w => WriteStaticRpc(w, 901, a => NetSerializer<int>.Write(a, 13), 0),
                host.Session.Peers.Single(p => !p.IsLocal));
            Pump(() => StaticRpcProbe.Calls.Count == 2, host);
            Assert.Equal(13, StaticRpcProbe.Calls[1].Value);
            Assert.True(StaticRpcProbe.Calls[1].Caller!.IsHost);
            Assert.Equal(0, Networking.Networking.NetworkObjectCount);
            host.Send(FakePeer.StaticRpcFromHost, w => WriteStaticRpc(w, 903, a => NetSerializer<int>.Write(a, 14),
                Networking.Networking.Session!.LocalPeer!.Id), host.Session.Peers.Single(p => !p.IsLocal));
            PumpFor(20, host);
            Assert.Equal(2, StaticRpcProbe.Calls.Count); // A relay cannot invent a host-only call from a client.
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }

    [Fact]
    public void LoadingPeersCannotSendOrReceiveStaticRpcs()
    {
        var (_, _, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        client.AutoReady = false;
        ClearStaticProbe();
        try
        {
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            client.Send(FakePeer.StaticRpcToHost, w => WriteStaticRpc(w, 901, a => NetSerializer<int>.Write(a, 1)));
            StaticRpcProbe.Call(901, 2);
            PumpFor(20, client);
            Assert.Equal(2, Assert.Single(StaticRpcProbe.Calls).Value);
            Assert.False(client.Has(FakePeer.StaticRpcFromHost));
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }
}
