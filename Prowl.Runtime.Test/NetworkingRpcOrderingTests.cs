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
    public int HostValue { get => _hostValue; set => __Sync_Set(ref _hostValue, value, HostState); }
    [SerializeIgnore] public readonly List<(int Health, int Scores, float Position)> Observed = new();
    private static readonly RpcMember<RpcStateTestComponent> Broadcast = new(701, "Observe()", RpcMode.Broadcast, RpcFlags.None,
        static (c, ref r, execute) => { if (execute) { __Rpc_ArgumentsRead(); c.Observe(); } });
    private static readonly RpcMember<RpcStateTestComponent> Unreliable = new(702, "ObserveUnreliable()", RpcMode.Broadcast, RpcFlags.Unreliable,
        static (c, ref r, execute) => { if (execute) { __Rpc_ArgumentsRead(); c.ObserveUnreliable(); } });
    private static readonly RpcMember<RpcStateTestComponent> Host = new(703, "ObserveHost()", RpcMode.Host, RpcFlags.None,
        static (c, ref r, execute) => { if (execute) { __Rpc_ArgumentsRead(); c.ObserveHost(); } });
    private static readonly RpcMember<RpcStateTestComponent> Owner = new(704, "ObserveOwner()", RpcMode.Owner, RpcFlags.None,
        static (c, ref r, execute) => { if (execute) { __Rpc_ArgumentsRead(); c.ObserveOwner(); } });
    private void Record() => Observed.Add((Health, Scores.Count, Transform.LocalPosition.X));
    public void Observe() { if (__Rpc_Begin(Broadcast, out var w) && !__Rpc_Send(Broadcast, w)) return; Record(); }
    public void ObserveUnreliable() { if (__Rpc_Begin(Unreliable, out var w) && !__Rpc_Send(Unreliable, w)) return; Record(); }
    public void ObserveHost() { if (__Rpc_Begin(Host, out var w) && !__Rpc_Send(Host, w)) return; Record(); }
    public void ObserveOwner() { if (__Rpc_Begin(Owner, out var w) && !__Rpc_Send(Owner, w)) return; Record(); }
}

public partial class NetworkingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RpcCarriesStateAndOlderSyncCannotOverwriteIt(bool unreliable)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("RPC state");
        root.NetworkMode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        root.AddComponent<NetworkLifecycleListener>().TryEditOnSpawn = true;
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        byte[] snapshot, rpc, oldSync, nextSync;
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            snapshot = client.Last(FakePeer.SceneSnapshot);
            component.Health = 50;
            typeof(Networking.Networking).GetMethod("SendSyncChanges", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null);
            Pump(() => client.Has(FakePeer.Sync), client);
            oldSync = client.WireReceived.Last(m => m.Id == FakePeer.Sync).Data;
            component.Health = 0;
            component.Scores.Add(7);
            root.Transform.LocalPosition = new Float3(10, 0, 0);
            if (unreliable) component.ObserveUnreliable(); else component.Observe();
            Pump(() => client.Has(FakePeer.RpcFromHost), client);
            rpc = client.WireReceived.Last(m => m.Id == FakePeer.RpcFromHost).Data;
            typeof(Networking.Networking).GetMethod("SendSyncChanges", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null);
            PumpFor(10, client);
            nextSync = client.WireReceived.Last(m => m.Id == FakePeer.Sync).Data;
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
            Assert.True(lifecycle.StructuralEditRejected);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.RpcFromHost, rpc, unreliable ? Delivery.Unreliable : Delivery.Reliable);
            host.Session.Flush();
            Pump(() => proxy.Observed.Count == 1, host);
            Assert.Equal((0, 1, 10f), proxy.Observed[0]);
            host.Session.SendRaw(client, FakePeer.Sync, oldSync);
            host.Session.SendRaw(client, FakePeer.Sync, nextSync);
            host.Session.Flush(); PumpFor(10, host);
            Assert.Equal(0, proxy.Health);
            Assert.Equal(new[] { 7 }, proxy.Scores);
            Assert.Single(proxy.ScoreChanges);
            Assert.Equal(10f, proxy.Transform.LocalPosition.X);
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
    public void ClientRpcAppliesAuthorizedStateBeforeHostExecution(int method)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Client RPC");
        root.NetworkMode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.SetNetworkOwner(owner);
            using FakePeer observer = FakePeer.Join(network, "observer");
            Pump(() => observer.Has(FakePeer.SceneSnapshot), client, observer);
            component.HostValue = 9;
            uint generation = NetworkGeneration(root);
            byte[] message = StateRpc(root, component, (uint)method, owner.Id, generation, 1, 0, 20);
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.RpcToHost, message);
            client.Session.Flush();
            // Owner RPC addressed to its own caller has no host body, but its state is still checked/applied.
            if (method == 704) Pump(() => component.Health == 0, client);
            else
            {
                Pump(() => component.Observed.Count == 1, client);
                Assert.Equal((0, 0, 20f), component.Observed[0]);
            }
            Assert.Equal(SessionState.Active, client.Session.State);
            if (method is 701 or 702)
            {
                Pump(() => observer.Has(FakePeer.RpcFromHost), client, observer);
                NetReader relay = new(observer.WireReceived.Last(m => m.Id == FakePeer.RpcFromHost).Data);
                Assert.Equal((uint)owner.Id, relay.ReadVarUInt32());
                relay.ReadGuid(); relay.ReadUInt32(); relay.ReadBytesWithLength();
                NetReader states = new(relay.ReadBytesWithLength());
                Assert.Equal(2u, states.ReadVarUInt32());
                NetReader original = new(states.ReadBytesWithLength());
                Assert.Equal(owner.Id, original.ReadInt32());
                NetReader hostState = new(states.ReadBytesWithLength());
                Assert.Equal(NetPeer.HostId, hostState.ReadInt32());
                hostState.ReadUInt64(); hostState.ReadGuid(); hostState.ReadUInt32();
                NetReader sync = new(hostState.ReadBytesWithLength());
                Assert.Equal(1u, sync.ReadVarUInt32());
                sync.ReadGuid(); Assert.Equal(799u, sync.ReadVarUInt32()); sync.ReadUInt32();
                NetReader hostValue = new(sync.ReadBytesWithLength());
                Assert.Equal(9, NetSerializer<int>.Read(ref hostValue));
            }
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaleOrUnauthorizedRpcStateNeverRunsTheBody(bool stale)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject(); root.NetworkMode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            if (stale) root.SetNetworkOwner(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
            uint generation = NetworkGeneration(root);
            byte[] message = StateRpc(root, component, 703, client.Session.LocalPeer!.Id,
                stale ? generation - 1 : generation, 1, 0, 20);
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.RpcToHost, message);
            client.Session.Flush(); PumpFor(20, client);
            Assert.Empty(component.Observed);
            Assert.Equal(100, component.Health);
            Assert.Equal(0f, root.Transform.LocalPosition.X);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private static uint NetworkGeneration(GameObject root)
    {
        using NetWriter writer = new();
        Networking.Networking.WriteSnapshot(Scene.Current, writer);
        NetReader reader = new(writer.WrittenSpan);
        reader.ReadByte(); reader.ReadUInt64(); reader.ReadString(); reader.ReadBytesWithLength();
        uint count = reader.ReadVarUInt32();
        for (uint i = 0; i < count; i++)
        {
            Guid id = reader.ReadGuid(); reader.ReadVarUInt32(); reader.ReadByte();
            uint generation = reader.ReadUInt32();
            if (id == root.Identifier) return generation;
        }
        throw new Exception("Missing network root");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidDependentStateIsRejectedBeforeAnyStateOrBodyChanges(bool hostOnlyValue)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject(); root.NetworkMode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            root.SetNetworkOwner(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
            byte[] message = StateRpc(root, component, 703, client.Session.LocalPeer!.Id,
                NetworkGeneration(root), 1, 0, hostOnlyValue ? 20 : float.NaN,
                hostOnlyValue ? 799u : NetCollectionTestComponent.HealthSlot);
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.RpcToHost, message);
            client.Session.Flush(); PumpFor(20, client);
            Assert.Empty(component.Observed);
            Assert.Equal(100, component.Health);
            Assert.Equal(0, component.HostValue);
            Assert.Equal(0f, root.Transform.LocalPosition.X);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void MultipleRpcsBetweenTicksEachObserveTheirPrecedingState()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject(); root.NetworkMode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            root.SetNetworkOwner(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
            uint generation = NetworkGeneration(root);
            for (int i = 0; i < 3; i++)
                client.Session.SendRaw(client.Session.HostPeer!, FakePeer.RpcToHost,
                    StateRpc(root, component, 703, client.Session.LocalPeer!.Id, generation, (ulong)i + 1, i, i + 10));
            client.Session.Flush();
            Pump(() => component.Observed.Count == 3, client);
            Assert.Equal(new[] { (0, 0, 10f), (1, 0, 11f), (2, 0, 12f) }, component.Observed);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void OldSyncCannotCrossOwnershipGenerationWhenOwnerReturns()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject(); root.NetworkMode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.SetNetworkOwner(owner);
            uint generation = NetworkGeneration(root);
            root.SetNetworkOwner(null); root.SetNetworkOwner(owner);
            using NetWriter sync = new(), value = new();
            sync.WriteInt32(owner.Id); sync.WriteUInt64(1); sync.WriteVarUInt32(1);
            sync.WriteGuid(component.Identifier); sync.WriteVarUInt32(NetCollectionTestComponent.HealthSlot);
            sync.WriteUInt32(generation); NetSerializer<int>.Write(value, 0); sync.WriteBytesWithLength(value.WrittenSpan);
            client.Session.SendRaw(client.Session.HostPeer!, FakePeer.Sync, sync.WrittenSpan);
            client.Session.Flush(); PumpFor(20, client);
            Assert.Equal(100, component.Health);
            Assert.Equal(SessionState.Active, client.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private static byte[] StateRpc(GameObject root, Component component, uint method, int origin,
        uint generation, ulong revision, int health, float position, uint slot = NetCollectionTestComponent.HealthSlot)
    {
        using NetWriter value = new(), sync = new(), pose = new(), state = new(), states = new(), rpc = new();
        NetSerializer<int>.Write(value, health);
        sync.WriteVarUInt32(1); sync.WriteGuid(component.Identifier); sync.WriteVarUInt32(slot); sync.WriteUInt32(generation);
        sync.WriteBytesWithLength(value.WrittenSpan);
        pose.WriteGuid(root.Identifier); pose.WriteUInt32((uint)revision);
        NetSerializers.WriteFloat3(pose, new Float3(position, 0, 0));
        NetSerializers.WriteQuaternion(pose, Quaternion.Identity);
        NetSerializers.WriteFloat3(pose, Float3.One);
        state.WriteInt32(origin); state.WriteUInt64(revision); state.WriteGuid(root.Identifier); state.WriteUInt32(generation);
        state.WriteBytesWithLength(sync.WrittenSpan); state.WriteBytesWithLength(pose.WrittenSpan);
        states.WriteVarUInt32(1); states.WriteBytesWithLength(state.WrittenSpan);
        rpc.WriteGuid(component.Identifier); rpc.WriteUInt32(method); rpc.WriteBytesWithLength(ReadOnlySpan<byte>.Empty);
        rpc.WriteBytesWithLength(states.WrittenSpan);
        return rpc.ToArray();
    }
}
