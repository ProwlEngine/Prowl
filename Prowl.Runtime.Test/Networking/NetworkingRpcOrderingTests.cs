using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public class RpcStateTestComponent : NetCollectionTestComponent
{
    private int _hostValue;
    private static readonly SyncMember<RpcStateTestComponent, int> HostState = new(799, "HostValue", SyncFlags.FromHost,
        static c => c._hostValue, static (c, v) => c._hostValue = v);
    public int HostValue { get => _hostValue; set => HostState.Set(this, ref _hostValue, value); }
    [SerializeIgnore] public readonly List<(int Health, int Scores, float Position)> Observed = new();
    private static readonly RpcMember<RpcStateTestComponent> Broadcast = new(701, "Observe()", RpcMode.Broadcast, RpcFlags.None,
        static (c, ref r, execute) => { if (execute) { RpcMember.ArgumentsRead(); c.Observe(); } });
    private static readonly RpcMember<RpcStateTestComponent> Unreliable = new(702, "ObserveUnreliable()", RpcMode.Broadcast, RpcFlags.Unreliable,
        static (c, ref r, execute) => { if (execute) { RpcMember.ArgumentsRead(); c.ObserveUnreliable(); } });
    private static readonly RpcMember<RpcStateTestComponent> Host = new(703, "ObserveHost()", RpcMode.Host, RpcFlags.None,
        static (c, ref r, execute) => { if (execute) { RpcMember.ArgumentsRead(); c.ObserveHost(); } });
    private static readonly RpcMember<RpcStateTestComponent> Owner = new(704, "ObserveOwner()", RpcMode.Owner, RpcFlags.None,
        static (c, ref r, execute) => { if (execute) { RpcMember.ArgumentsRead(); c.ObserveOwner(); } });
    private void Record() => Observed.Add((Health, Scores.Count, Transform.LocalPosition.X));
    public void Observe() { if (Broadcast.Begin(this, out var w) && !Broadcast.Send(this, w)) return; Record(); }
    public void ObserveUnreliable() { if (Unreliable.Begin(this, out var w) && !Unreliable.Send(this, w)) return; Record(); }
    public void ObserveHost() { if (Host.Begin(this, out var w) && !Host.Send(this, w)) return; Record(); }
    public void ObserveOwner() { if (Owner.Begin(this, out var w) && !Owner.Send(this, w)) return; Record(); }
}

/// <summary>A broadcast RPC on channel 1, as the script compiler generates for <c>[Rpc.Broadcast(Channel = 1)]</c>.</summary>
public class RpcChannelTestComponent : NetCollectionTestComponent
{
    public const uint TalkId = 711;
    [SerializeIgnore] public int Talks;
    private static readonly RpcMember<RpcChannelTestComponent> Talking = new(TalkId, "Talk()", RpcMode.Broadcast, RpcFlags.None,
        static (c, ref r, execute) => { if (execute) { RpcMember.ArgumentsRead(); c.Talks++; } }, 1);
    public void Talk() { if (Talking.Begin(this, out var w) && !Talking.Send(this, w)) return; Talks++; }
    public const uint ShoutId = 712;
    private static readonly RpcMember<RpcChannelTestComponent> Shouting = new(ShoutId, "Shout()", RpcMode.Broadcast, RpcFlags.Unreliable,
        static (c, ref r, execute) => { if (execute) { RpcMember.ArgumentsRead(); c.Talks++; } }, 1);
    public void Shout() { if (Shouting.Begin(this, out var w) && !Shouting.Send(this, w)) return; Talks++; }
}

public partial class NetworkingTests
{
    [Fact]
    public void RpcsOnAnotherChannelDoNotPushSyncAheadAndAreRelayedOnTheirChannel()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        using FakePeer bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot) && bob.Has(FakePeer.SceneSnapshot), alice, bob);
            GameObject radio = CreateGameObject("Radio");
            RpcChannelTestComponent component = radio.AddComponent<RpcChannelTestComponent>();
            scene.Add(radio);
            radio.Network.Spawn();
            Pump(() => bob.Has(FakePeer.Spawn), alice, bob);

            // A channel 0 call would send the pending collection change first; this one leaves it for the next tick.
            component.Scores.Add(5);
            component.Talk();
            Assert.Equal(1, component.Talks);
            Assert.NotEmpty(NetworkTestScene.Current.Sync.Dirty);
            Pump(() => bob.Has(FakePeer.RpcFromHost), alice, bob);
            Assert.Equal(1, bob.Channels.Single(m => m.Id == FakePeer.RpcFromHost).Channel);

            // The host relays on the RPC's own channel, whichever channel the caller used.
            bob.Channels.Clear(); bob.Received.Clear();
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(RpcChannelTestComponent.TalkId); });
            Pump(() => component.Talks == 2 && bob.Has(FakePeer.RpcFromHost), alice, bob);
            Assert.Equal(1, bob.Channels.Single(m => m.Id == FakePeer.RpcFromHost).Channel);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void UnreliableRpcsIgnoreTheirChannel()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            GameObject radio = CreateGameObject("Radio");
            RpcChannelTestComponent component = radio.AddComponent<RpcChannelTestComponent>();
            scene.Add(radio);
            radio.Network.Spawn();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            // Unreliable messages have no order to keep, so they go on channel 0 whatever the RPC asks.
            component.Shout();
            Pump(() => alice.Has(FakePeer.RpcFromHost), alice);
            Assert.Equal((Delivery.Unreliable, (byte)0), (alice.Deliveries.Single(m => m.Id == FakePeer.RpcFromHost).Delivery,
                alice.Channels.Single(m => m.Id == FakePeer.RpcFromHost).Channel));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ReplicationMessagesOffChannelZeroAreIgnored()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.Network.Spawn(Networking.Networking.Session!.Peers.Single(p => p.Name == "alice"));
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            alice.Send(FakePeer.Sync, w =>
            {
                w.WriteVarUInt32(1);
                w.WriteGuid(component.Identifier);
                w.WriteVarUInt32(NetTestComponent.HealthSlot);
                using NetWriter value = new();
                NetSerializer<int>.Write(value, 42);
                w.WriteBytesWithLength(value.WrittenSpan);
            }, channel: 1);
            PumpFor(30, alice);
            Assert.Equal(100, component.Health);

            alice.SendSync(component.Identifier, NetTestComponent.HealthSlot, 42);
            Pump(() => component.Health == 42, alice);
            Assert.Equal(SessionState.Active, alice.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void RpcChannelsFallBackToChannelZeroOnSmallerTransports()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        using LimitedTransport transport = new(network, _ => 1000);
        Assert.Equal(1, transport.Capabilities.Channels);
        Networking.Networking.Host(transport);
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            GameObject radio = CreateGameObject("Radio");
            RpcChannelTestComponent component = radio.AddComponent<RpcChannelTestComponent>();
            scene.Add(radio);
            radio.Network.Spawn();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            component.Talk();
            Pump(() => alice.Has(FakePeer.RpcFromHost), alice);
            Assert.Equal(0, alice.Channels.Single(m => m.Id == FakePeer.RpcFromHost).Channel);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReliableRpcsFollowTheirCallersSyncAndCarryNoState(bool unreliable)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("RPC state");
        root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        root.AddComponent<NetworkLifecycleListener>().TryEditOnSpawn = true;
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        byte[] snapshot, rpc, nextSync, health;
        byte[]? stateSync = null;
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            snapshot = client.Last(FakePeer.SceneSnapshot);
            component.Health = 0;
            component.Scores.Add(7);
            if (unreliable) component.ObserveUnreliable(); else component.Observe();
            Pump(() => client.Has(FakePeer.RpcFromHost), client);
            int rpcIndex = client.WireReceived.FindLastIndex(m => m.Id == FakePeer.RpcFromHost);
            rpc = client.WireReceived[rpcIndex].Data;
            NetReader call = new(rpc);
            call.ReadVarUInt32(); call.ReadGuid(); call.ReadUInt32(); call.ReadBytesWithLength();
            Assert.True(call.IsAtEnd);
            // Nothing goes ahead of an unreliable call; its state follows with the next tick, in no set order.
            // A reliable call's collection changes go just ahead of it; other values travel in snapshots, unordered.
            if (!unreliable)
            {
                Assert.Equal(FakePeer.Sync, client.WireReceived[rpcIndex - 1].Id);
                stateSync = client.WireReceived[rpcIndex - 1].Data;
            }

            SendSyncChanges();
            Pump(() => client.SnapshotInt(component.Identifier, NetCollectionTestComponent.HealthSlot) == 0, client);
            nextSync = client.WireReceived.Last(m => m.Id == FakePeer.Sync).Data;
            health = client.WireReceived.Last(m => m.Id == FakePeer.Snapshot &&
                client.SnapshotValues(m.Data).Any(v => v.Slot == NetCollectionTestComponent.HealthSlot)).Data;
        }
        finally { Networking.Networking.Shutdown(); }

        LocalNetwork receiving = new();
        using FakePeer host = FakePeer.Host(receiving);
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(receiving, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && Scene.Current.AllObjects.Any(go => go.Name == "RPC state" && !ReferenceEquals(go, root)), host);
            RpcStateTestComponent proxy = Scene.Current.AllObjects.Single(go => go.Name == "RPC state").GetComponent<RpcStateTestComponent>()!;
            NetworkLifecycleListener lifecycle = proxy.GameObject.GetComponent<NetworkLifecycleListener>()!;
            Assert.Equal(new[] { "spawn" }, lifecycle.Events);
            Assert.Equal(100, lifecycle.HealthAtSpawn);
            Assert.True(lifecycle.StructuralEditApplied);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            if (stateSync != null) host.Session.SendRaw(client, FakePeer.Sync, stateSync);
            host.Session.SendRaw(client, FakePeer.RpcFromHost, rpc, unreliable ? Delivery.Unreliable : Delivery.Reliable);
            host.Session.Flush();
            Pump(() => proxy.Observed.Count == 1, host);
            Assert.Equal(unreliable ? (100, 0, 0f) : (100, 1, 0f), proxy.Observed[0]);
            // The state follows; collection changes the proxy already has aren't applied twice.
            host.Session.SendRaw(client, FakePeer.Sync, nextSync);
            host.Session.SendRaw(client, FakePeer.Snapshot, health);
            host.Session.Flush(); PumpFor(10, host);
            Assert.Equal(0, proxy.Health);
            Assert.Equal(new[] { 7 }, proxy.Scores);
            Assert.Single(proxy.ScoreChanges);
            host.Send(FakePeer.Despawn, w => w.WriteGuid(proxy.GameObject.Identifier), client);
            Pump(() => proxy.IsDisposed, host);
            Assert.Equal(new[] { "spawn", "despawn" }, lifecycle.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(701)]
    [InlineData(702)]
    [InlineData(703)]
    [InlineData(704)]
    public void ClientRpcRunsAfterItsSyncAndRelaysAfterTheHostsOwn(int method)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Client RPC");
        root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.Network.AssignOwnership(owner);
            using FakePeer observer = FakePeer.Join(network, "observer");
            Pump(() => observer.IsReady, client, observer);
            component.HostValue = 9;
            uint generation = NetworkGeneration(root);
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.Sync,
                ClientSync(component, owner.Id, generation, 0));
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.RpcToHost, Call(component, (uint)method));
            client.Session.Flush();
            // An Owner call addressed to its own caller has no body anywhere else.
            if (method == 704) Pump(() => component.Health == 0, client);
            else
            {
                Pump(() => component.Observed.Count == 1, client);
                Assert.Equal((0, 0, 0f), component.Observed[0]);
            }

            Assert.Equal(SessionState.Active, client.Session.State);
            if (method is 701 or 702)
            {
                Pump(() => observer.Has(FakePeer.RpcFromHost), client, observer);
                int rpcIndex = observer.WireReceived.FindLastIndex(m => m.Id == FakePeer.RpcFromHost);
                NetReader relay = new(observer.WireReceived[rpcIndex].Data);
                Assert.Equal((uint)owner.Id, relay.ReadVarUInt32());
                relay.ReadGuid(); relay.ReadUInt32(); relay.ReadBytesWithLength();
                Assert.True(relay.IsAtEnd);
                if (method == 701)
                {
                    // The host's own value isn't ordered with the call; it arrives in a snapshot.
                    Pump(() => observer.SnapshotInt(component.Identifier, 799) == 9, client, observer);
                }
            }
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void RpcWithTrailingBytesDropsItsCaller()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject(); root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);
            using NetWriter message = new();
            message.WriteBytes(Call(component, 703));
            message.WriteVarUInt32(0); message.WriteByte(1); // An empty recipient list, then junk.
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.RpcToHost, message.WrittenSpan);
            client.Session.Flush();
            Pump(() => client.Session.State == SessionState.Ended, client);
            Assert.Empty(component.Observed);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void EachRpcBetweenTicksFollowsItsOwnSync()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject(); root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);
            client.WireReceived.Clear();
            // Collection changes travel reliably, each just ahead of the call that follows it.
            for (int i = 0; i < 3; i++)
            {
                component.Scores.Add(i);
                component.Observe();
            }

            Pump(() => client.WireReceived.Count(m => m.Id == FakePeer.RpcFromHost) == 3, client);
            ushort[] order = client.WireReceived.Where(m => m.Id is FakePeer.Sync or FakePeer.RpcFromHost)
                .Select(m => m.Id).ToArray();
            Assert.Equal(new[] { FakePeer.Sync, FakePeer.RpcFromHost, FakePeer.Sync, FakePeer.RpcFromHost,
                FakePeer.Sync, FakePeer.RpcFromHost }, order);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private static void SendSyncChanges() =>
        Networking.Networking.System!.Sync.SendSyncChanges();

    private static byte[] Call(Component component, uint method)
    {
        using NetWriter rpc = new();
        rpc.WriteGuid(component.Identifier); rpc.WriteUInt32(method); rpc.WriteBytesWithLength(ReadOnlySpan<byte>.Empty);
        return rpc.ToArray();
    }

    private static byte[] ClientSync(Component component, int origin, uint generation, int value,
        uint slot = NetCollectionTestComponent.HealthSlot)
    {
        using NetWriter sync = new(), payload = new();
        NetSerializer<int>.Write(payload, value);
        sync.WriteInt32(origin); sync.WriteVarUInt32(1);
        sync.WriteGuid(component.Identifier); sync.WriteVarUInt32(slot);
        sync.WriteVarUInt32(generation); sync.WriteBytesWithLength(payload.WrittenSpan);
        return sync.ToArray();
    }

    private static uint NetworkGeneration(GameObject root)
    {
        using NetWriter writer = new();
        NetworkTestScene.Current.WriteSnapshot(Scene.Current, writer);
        NetReader reader = new(writer.WrittenSpan);
        reader.ReadByte(); reader.ReadUInt64(); reader.ReadString(); reader.ReadBytesWithLength();
        uint count = reader.ReadVarUInt32();
        for (uint i = 0; i < count; i++)
        {
            Guid id = reader.ReadGuid(); reader.ReadVarUInt32(); reader.ReadByte();
            uint generation = reader.ReadUInt32();
            reader.ReadVarUInt32(); // Network id.
            if (id == root.Identifier) return generation;
        }
        throw new Exception("Missing network root");
    }

    [Fact]
    public void OldSyncCannotCrossOwnershipGenerationWhenOwnerReturns()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject(); root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.Network.AssignOwnership(owner);
            uint generation = NetworkGeneration(root);
            root.Network.AssignOwnership(null); root.Network.AssignOwnership(owner);
            using NetWriter sync = new(), value = new();
            sync.WriteInt32(owner.Id); sync.WriteVarUInt32(1);
            sync.WriteGuid(component.Identifier); sync.WriteVarUInt32(NetCollectionTestComponent.HealthSlot);
            sync.WriteVarUInt32(generation); NetSerializer<int>.Write(value, 0); sync.WriteBytesWithLength(value.WrittenSpan);
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.Sync, sync.WrittenSpan);
            client.Session.Flush(); PumpFor(20, client);
            Assert.Equal(100, component.Health);
            Assert.Equal(SessionState.Active, client.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    /// <summary>A handoff's poses: just the root's, at <paramref name="x" /> along X.</summary>
    private static byte[] Poses(GameObject root, float x)
    {
        using NetWriter poses = new();
        poses.WriteVarUInt32(1); poses.WriteGuid(root.Identifier);
        NetSerializers.WriteFloat3(poses, new Float3(x, 0, 0));
        NetSerializers.WriteQuaternion(poses, Quaternion.Identity);
        NetSerializers.WriteFloat3(poses, Float3.One);
        return poses.ToArray();
    }
}
