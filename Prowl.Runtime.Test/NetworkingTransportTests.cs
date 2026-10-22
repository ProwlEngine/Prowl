using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void SceneSessionsRejectSessionLimitsSmallerThanOneTransformBeforeIo()
    {
        using LimitedTransport transport = new(new(), _ => 74);
        NetSessionOptions options = new() { MaxMessageSize = 73 };
        Assert.Throws<NotSupportedException>(() => Networking.Networking.Host(transport, options: options));
        Assert.Throws<NotSupportedException>(() => Networking.Networking.Join(transport, "host", "client", options: options));
        Assert.Equal(0, transport.Starts);
        Assert.Null(Networking.Networking.Session);
    }

    [Theory]
    [InlineData(false, NetworkExecution.Inline, 74, DeliverySupport.All)]
    [InlineData(true, NetworkExecution.Inline, 74, DeliverySupport.All)]
    [InlineData(false, NetworkExecution.DedicatedThread, 74, DeliverySupport.All)]
    [InlineData(true, NetworkExecution.DedicatedThread, 74, DeliverySupport.All)]
    [InlineData(false, NetworkExecution.Inline, 73, DeliverySupport.All)]
    [InlineData(true, NetworkExecution.DedicatedThread, 73, DeliverySupport.All)]
    [InlineData(false, NetworkExecution.Inline, 0, DeliverySupport.Reliable)]
    [InlineData(true, NetworkExecution.DedicatedThread, 0, DeliverySupport.Reliable)]
    public void TransformBatchesAndRelaysRespectEachConnectionsLimit(bool batching, NetworkExecution execution, int smallLimit, DeliverySupport deliveries)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution, BatchMessages = batching });
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        using LimitedTransport transport = new(network, address => address == "small" ? smallLimit : address == "wide" ? 155 : 1000, deliveries);
        Networking.Networking.Host(transport);
        using FakePeer small = FakePeer.Join(network, "small"), wide = FakePeer.Join(network, "wide"), sender = FakePeer.Join(network, "sender");
        FakePeer[] peers = [small, wide, sender];
        try
        {
            Pump(() => peers.All(p => p.IsReady), peers);
            GameObject[] objects = Enumerable.Range(0, 5).Select(i =>
            {
                GameObject go = CreateGameObject($"Object {i}");
                scene.Add(go);
                go.Network.Spawn();
                return go;
            }).ToArray();
            Pump(() => TransformIds(small).Count == 5 && TransformIds(wide).Count == 5, peers);
            int smallMax = smallLimit >= 74 ? 72 : 1000;
            int wideMax = deliveries == DeliverySupport.Reliable ? 1000 : 144;
            AssertTransformBatches(small, smallMax, objects);
            AssertTransformBatches(wide, wideMax, objects);
            Assert.All(small.Deliveries.Where(m => m.Id == FakePeer.Transform), m =>
                Assert.Equal(smallLimit < 74 ? Delivery.Reliable : Delivery.Unreliable, m.Delivery));

            StaticRpcMember rpc = new(typeof(NetworkingTests), 998, "Broadcast", RpcMode.Broadcast, RpcFlags.Unreliable,
                static (ref NetReader reader, bool execute) => { });
            using NetWriter arguments = new();
            arguments.WriteString(new string('x', 512));
            Networking.Networking.SendStaticRpc(rpc, arguments);
            Pump(() => peers.All(p => p.Has(FakePeer.StaticRpcFromHost)), peers);
            Assert.Equal(Delivery.Reliable, small.Deliveries.Last(m => m.Id == FakePeer.StaticRpcFromHost).Delivery);
            Assert.Equal(Delivery.Reliable, wide.Deliveries.Last(m => m.Id == FakePeer.StaticRpcFromHost).Delivery);
            Assert.Equal(deliveries == DeliverySupport.Reliable ? Delivery.Reliable : Delivery.Unreliable,
                sender.Deliveries.Last(m => m.Id == FakePeer.StaticRpcFromHost).Delivery);

            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => p.Name == "sender");
            foreach (GameObject go in objects) go.Network.AssignOwnership(owner);
            Pump(() => sender.WireReceived.Count(m => m.Id == FakePeer.Owner) == 5, peers);
            foreach (FakePeer peer in peers) peer.WireReceived.Clear();

            using NetWriter message = new();
            foreach (GameObject go in objects)
            {
                message.WriteGuid(go.Identifier);
                message.WriteUInt32(Networking.Networking.GenerationOf(go));
                message.WriteUInt32(500);
                message.WriteDouble(10);
                NetSerializers.WriteFloat3(message, new Float3(42, 0, 0));
                NetSerializers.WriteQuaternion(message, Quaternion.Identity);
                NetSerializers.WriteFloat3(message, new Float3(1, 1, 1));
            }
            sender.Session.SendRaw(sender.Session.HostPeer!, FakePeer.Transform, message.WrittenSpan,
                deliveries == DeliverySupport.Reliable ? Delivery.Reliable : Delivery.Unreliable);
            Pump(() => TransformIds(small).Count == 5 && TransformIds(wide).Count == 5, peers);
            AssertTransformBatches(small, smallMax, objects);
            AssertTransformBatches(wide, wideMax, objects);
            Assert.Empty(TransformIds(sender));
            Assert.Equal(SessionState.Active, Networking.Networking.Session!.State);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void ReliableOnlyClientReplicatesTransformsAndUnreliableRpcs(NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        Scene scene = CreateScene();
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(scene, snapshot);
        GameObject ship = CreateGameObject("Ship");
        ship.Network.Mode = NetworkMode.Object;
        using NetWriter spawn = new();
        Networking.Networking.WriteSpawn(ship, owner: 1, NetworkOrphaned.Destroy, spawn);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        using LimitedTransport transport = new(network, _ => 0, DeliverySupport.Reliable, "client");
        Networking.Networking.Join(transport, "host", "client");
        try
        {
            Pump(() => Networking.Networking.IsReady && host.Has(FakePeer.Transform), host);
            GameObject local = Find(Scene.Current, ship.Identifier);
            host.WireReceived.Clear();
            local.Transform.LocalPosition = new Float3(42, 0, 0);
            Pump(() => host.Has(FakePeer.Transform) && host.WireReceived.Any(m => m.Id == FakePeer.Transform), host);
            Assert.All(host.Deliveries.Where(m => m.Id == FakePeer.Transform), m => Assert.Equal(Delivery.Reliable, m.Delivery));
            Assert.Contains(ship.Identifier, TransformIds(host));
            NetReader transform = new(host.WireReceived.Last(m => m.Id == FakePeer.Transform).Data);
            transform.ReadBytes(32); // Identifier, ownership generation, tick, and sender time.
            Assert.Equal(new Float3(42, 0, 0), NetSerializers.ReadFloat3(ref transform));

            StaticRpcMember rpc = new(typeof(NetworkingTests), 999, "Test", RpcMode.Host, RpcFlags.Unreliable,
                static (ref NetReader reader, bool execute) => { });
            using NetWriter arguments = new();
            arguments.WriteString("request");
            Networking.Networking.SendStaticRpc(rpc, arguments);
            Pump(() => host.Has(FakePeer.StaticRpcToHost), host);
            Assert.Equal(Delivery.Reliable, host.Deliveries.Last(m => m.Id == FakePeer.StaticRpcToHost).Delivery);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    private static HashSet<Guid> TransformIds(FakePeer peer)
    {
        HashSet<Guid> ids = new();
        foreach (var message in peer.WireReceived.Where(m => m.Id == FakePeer.Transform))
        {
            NetReader reader = new(message.Data);
            while (!reader.IsAtEnd) { ids.Add(reader.ReadGuid()); reader.ReadBytes(56); }
        }
        return ids;
    }

    private static void AssertTransformBatches(FakePeer peer, int maxPayload, GameObject[] objects)
    {
        Assert.True(TransformIds(peer).SetEquals(objects.Select(go => go.Identifier)));
        Assert.All(peer.WireReceived.Where(m => m.Id == FakePeer.Transform), m =>
        {
            Assert.InRange(m.Data.Length, 72, maxPayload);
            Assert.Equal(0, m.Data.Length % 72);
        });
    }

    // Different per-connection limits, with reliable packets smaller than a single transform record.
    private sealed class LimitedTransport(LocalNetwork network, Func<string, int> limit, DeliverySupport deliveries = DeliverySupport.All, string address = "host") : Transport
    {
        private readonly LocalTransport _inner = new(network, address);
        private readonly Dictionary<Connection, LimitedConnection> _peers = new();
        public int Starts;
        public override TransportCapabilities Capabilities => new(true, deliveries, TransportExecution.InlinePreferred);
        public override bool IsListening => _inner.IsListening;
        public override void Listen() { Starts++; _inner.Listen(); }
        public override Connection Connect(string address) { Starts++; return Wrap(_inner.Connect(address)); }
        private LimitedConnection Wrap(Connection connection)
        {
            if (!_peers.TryGetValue(connection, out LimitedConnection? peer))
            {
                peer = new(this, connection, limit(connection.Address));
                _peers.Add(connection, peer);
                AddConnection(peer);
            }
            return peer;
        }
        public override void Receive()
        {
            _inner.Receive();
            while (_inner.Poll(out TransportEvent ev))
            {
                LimitedConnection peer = Wrap(ev.Connection);
                if (ev.Type == TransportEventType.Connected) RaiseConnected(peer);
                else if (ev.Type == TransportEventType.Disconnected) RaiseDisconnected(peer, ev.Reason);
                else RaiseData(peer, ev.Data.Span, ev.Delivery);
            }
        }
        public override void Flush() => _inner.Flush();
        public override void Shutdown() => _inner.Shutdown();
        protected override void DisposeCore() => _inner.Dispose();
        private sealed class LimitedConnection(LimitedTransport owner, Connection inner, int limit)
            : Connection(owner, inner.Id, inner.Address, inner.IsOutgoing)
        {
            public override TimeSpan RoundTripTime => inner.RoundTripTime;
            public override int GetMaxMessageSize(Delivery delivery) => delivery == Delivery.Unreliable ? limit : 64;
            public override void Send(ReadOnlySpan<byte> data, Delivery delivery) { ValidateSend(data, delivery); inner.Send(data, delivery); }
            public override void Disconnect() => inner.Disconnect();
        }
    }
}
