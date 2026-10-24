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

public partial class NetworkingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RpcCarriesStateAndOlderSyncCannotOverwriteIt(bool unreliable)
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("RPC state");
        root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        root.AddComponent<NetworkLifecycleListener>().TryEditOnSpawn = true;
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        byte[] snapshot, rpc, oldSync, nextSync;
        byte[]? stateSync = null;
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
            int rpcIndex = client.WireReceived.FindLastIndex(m => m.Id == FakePeer.RpcFromHost);
            rpc = client.WireReceived[rpcIndex].Data;
            // A reliable call sends its pending Sync changes ahead of itself instead of inside it.
            if (!unreliable)
            {
                Assert.Equal(FakePeer.Sync, client.WireReceived[rpcIndex - 1].Id);
                stateSync = client.WireReceived[rpcIndex - 1].Data;
            }
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
            // Give the proxy interpolation history. Ticks just below the wrap, so the call's tick is newer.
            uint generation = NetworkGeneration(proxy.GameObject);
            host.Send(FakePeer.Transform, w =>
            {
                for (uint i = 0; i < 2; i++)
                {
                    w.WriteGuid(proxy.GameObject.Identifier); w.WriteUInt32(generation);
                    w.WriteUInt32(uint.MaxValue - 1 + i); w.WriteDouble(1 + i * 0.05);
                    NetSerializers.WriteFloat3(w, new Float3(i, 0, 0));
                    NetSerializers.WriteQuaternion(w, Quaternion.Identity);
                    NetSerializers.WriteFloat3(w, Float3.One);
                }
            }, client);
            Pump(() => Samples(proxy.GameObject) > 0, host);
            if (stateSync != null)
            {
                NetReader call = new(rpc);
                call.ReadVarUInt32(); call.ReadGuid(); call.ReadUInt32(); call.ReadBytesWithLength();
                NetReader states = new(call.ReadBytesWithLength());
                Assert.Equal(1u, states.ReadVarUInt32());
                NetReader frame = new(states.ReadBytesWithLength());
                frame.ReadInt32(); frame.ReadUInt64(); frame.ReadGuid(); frame.ReadUInt32();
                NetReader syncs = new(frame.ReadBytesWithLength());
                Assert.Equal(0u, syncs.ReadVarUInt32());
                host.Session.SendRaw(client, FakePeer.Sync, stateSync);
            }

            host.Session.SendRaw(client, FakePeer.RpcFromHost, rpc, unreliable ? Delivery.Unreliable : Delivery.Reliable);
            host.Session.Flush();
            Pump(() => proxy.Observed.Count == 1, host);
            Assert.Equal((0, 1, 10f), proxy.Observed[0]);
            // The call's pose is for its body; the proxy keeps interpolating rather than snapping.
            Assert.True(Samples(proxy.GameObject) > 0);
            host.Session.SendRaw(client, FakePeer.Sync, oldSync);
            host.Session.SendRaw(client, FakePeer.Sync, nextSync);
            host.Session.Flush(); PumpFor(10, host);
            Assert.Equal(0, proxy.Health);
            Assert.Equal(new[] { 7 }, proxy.Scores);
            Assert.Single(proxy.ScoreChanges);
            Assert.True(proxy.Transform.LocalPosition.X <= 1f); // Back on its interpolated path.
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
        root.Network.Mode = NetworkMode.Object;
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
                int rpcIndex = observer.WireReceived.FindLastIndex(m => m.Id == FakePeer.RpcFromHost);
                NetReader relay = new(observer.WireReceived[rpcIndex].Data);
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
                if (method == 701)
                {
                    // Reliable: the host's value went out as a Sync message just ahead of the relay.
                    Assert.Equal(0u, sync.ReadVarUInt32());
                    Assert.Equal(FakePeer.Sync, observer.WireReceived[rpcIndex - 1].Id);
                    sync = new(observer.WireReceived[rpcIndex - 1].Data);
                    sync.ReadInt32(); sync.ReadUInt64();
                }

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
        GameObject root = CreateGameObject(); root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            if (stale) root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
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

    private static int Samples(GameObject root)
    {
        List<Networking.Networking.ObjectDiagnostic> objects = new();
        Networking.Networking.GetObjectDiagnostics(objects);
        return objects.Single(o => o.Identifier == root.Identifier).BufferedSamples;
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
        GameObject root = CreateGameObject(); root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
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
        GameObject root = CreateGameObject(); root.Network.Mode = NetworkMode.Object;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
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
