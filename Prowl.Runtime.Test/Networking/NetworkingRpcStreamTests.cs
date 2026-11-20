using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Records its spawn callback and a broadcast RPC in the order they run.</summary>
public class RpcSpawnOrderTestComponent : Component, INetworkEvents
{
    public const uint PingId = 721;
    [SerializeIgnore] public List<string> Order = new();
    private static readonly RpcMember<RpcSpawnOrderTestComponent> Pinging = new(PingId, "Ping()", RpcMode.Broadcast, RpcFlags.AnyCaller,
        static (c, ref r, execute) => { if (execute) { RpcMember.ArgumentsRead(); c.Order.Add("call"); } });
    public void Ping() { if (Pinging.Begin(this, out var w) && !Pinging.Send(this, w)) return; Order.Add("call"); }
    public void OnNetworkSpawn() => Order.Add("spawn");
    public void OnNetworkDespawn() { }
    public void OnNetworkAuthorityGained() { }
    public void OnNetworkAuthorityLost() { }
    public void OnNetworkOwnerChanged(NetPeer? previousOwner, NetPeer? owner) { }
}

public partial class NetworkingTests
{
    [Fact]
    public void AnRpcFollowsTheStateOfItsObjectEvenWhenAComponentSendsSlowly()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject(); root.Network.Networked = true;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        root.Network.SetSendInterval(component, 255);
        scene.Add(root); Bind(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);
            client.WireReceived.Clear();

            component.HostValue = 5;
            component.Observe();
            Pump(() => client.WireReceived.Any(m => m.Id == FakePeer.RpcFromHost), client);
            ushort[] order = client.WireReceived.Where(m => m.Id is FakePeer.Sync or FakePeer.RpcFromHost)
                .Select(m => m.Id).ToArray();
            Assert.Equal(new[] { FakePeer.Sync, FakePeer.RpcFromHost }, order);
            Assert.Equal(5, client.SnapshotInt(component.Identifier, 799));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void TheHostDropsACallForAnObjectItDoesNotHaveAndKeepsTheCaller()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject(); root.Network.Networked = true;
        RpcStateTestComponent component = root.AddComponent<RpcStateTestComponent>();
        scene.Add(root); Bind(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);

            client.Send(FakePeer.RpcToHost, w => { w.WriteGuid(Guid.NewGuid()); w.WriteUInt32(703); });
            PumpFor(20, client);
            Assert.Equal(SessionState.Active, client.Session.State);
            Assert.Empty(component.Observed);

            client.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(703); });
            Pump(() => component.Observed.Count == 1, client);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientDropsACallForAnObjectItDoesNotHave()
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            host.Send(FakePeer.RpcFromHost, w =>
            {
                w.WriteVarUInt32((uint)NetPeer.HostId); w.WriteGuid(Guid.NewGuid()); w.WriteUInt32(701);
            }, client);
            PumpFor(20, host);
            Assert.True(Networking.Networking.IsActive);
            Assert.True(proxy.Network.Active);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void CallsReceivedDuringASpawnRunWaitForTheSpawnCallbacks()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Pinged");
        go.Network.Networked = true;
        RpcSpawnOrderTestComponent component = go.AddComponent<RpcSpawnOrderTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);

        byte[] snapshot;
        byte[][] spawns;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            (snapshot, spawns) = RecordJoin(source);
        }
        finally { Networking.Networking.Shutdown(); }

        MakeCurrent(CreateScene());
        ulong generation = SceneGenerationOf(snapshot);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => host.Has(FakePeer.Ready), host);
            NetPeer peer = host.Session.Peers.Single(p => !p.IsLocal);
            host.Send(FakePeer.SpawnStarted, w => w.WriteUInt64(generation), peer);
            foreach (byte[] spawn in spawns) host.Session.SendRaw(peer, FakePeer.Spawn, spawn);
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == go.Identifier), host);
            RpcSpawnOrderTestComponent here = Find(SceneInstance.Main, go.Identifier).GetComponent<RpcSpawnOrderTestComponent>()!;

            // On the same stream as the spawns, in the middle of the run.
            for (int i = 0; i < 2; i++)
                host.Send(FakePeer.RpcFromHost, w =>
                {
                    w.WriteVarUInt32((uint)NetPeer.HostId); w.WriteGuid(component.Identifier);
                    w.WriteUInt32(RpcSpawnOrderTestComponent.PingId);
                }, peer);
            PumpFor(20, host);
            Assert.False(Networking.Networking.IsReady);
            Assert.Empty(here.Order);

            host.Send(FakePeer.SpawnFinished, w => w.WriteUInt64(generation), peer);
            Pump(() => here.Order.Count == 3, host);
            Assert.Equal(new[] { "spawn", "call", "call" }, here.Order);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
