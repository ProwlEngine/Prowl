using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Records every network callback it gets, server ones included, in the order they come.</summary>
public class BehaviourOrderListener : NetworkBehaviour
{
    [SerializeIgnore] public readonly List<string> Events = new();

    public override void OnStartServer() => Events.Add("start server");
    public override void OnStartClient() => Events.Add("start client");
    public override void OnStartAuthority() => Events.Add("start authority");
    public override void OnStopAuthority() => Events.Add("stop authority");
    public override void OnStopClient() => Events.Add("stop client");
    public override void OnStopServer() => Events.Add("stop server");
}

public partial class NetworkingTests
{
    [Fact]
    public void TheHostStartsAsServerThenClientAndStopsTheOtherWayRound()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Ordered");
        BehaviourOrderListener listener = root.AddComponent<BehaviourOrderListener>();
        scene.Add(root);
        MakeCurrent(scene);
        try
        {
            Networking.Networking.Host(new LocalTransport(new LocalNetwork(), "host"));
            root.SpawnBuilt(Networking.Networking.Session!.LocalPeer);
            Assert.Equal(new[] { "start server", "start client", "start authority" }, listener.Events);
            listener.Events.Clear();
            root.Destroy();
            EngineObject.ProcessDestroyed();
            Assert.Equal(new[] { "stop client", "stop authority", "stop server" }, listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AuthorityFollowsTheDirectionAndWhoOwnsTheObject()
    {
        Scene scene = CreateScene();
        GameObject unowned = CreateGameObject("Unowned"), mine = CreateGameObject("Mine");
        NetTestComponent hostWritten = unowned.AddComponent<NetTestComponent>();
        NetCollectionTestComponent ownerWritten = unowned.AddComponent<NetCollectionTestComponent>().OwnerWritten();
        NetCollectionTestComponent ownedByHost = mine.AddComponent<NetCollectionTestComponent>().OwnerWritten();
        scene.Add(unowned);
        scene.Add(mine);
        MakeCurrent(scene);
        try
        {
            Assert.False(hostWritten.Authority);
            Networking.Networking.Host(new LocalTransport(new LocalNetwork(), "host"));
            unowned.SpawnBuilt();
            mine.SpawnBuilt(Networking.Networking.Session!.LocalPeer);

            // The host writes what goes from it to clients, and what goes the other way only on an object it owns.
            Assert.True(hostWritten.Authority);
            Assert.False(ownerWritten.Authority);
            Assert.True(ownedByHost.Authority);
            Assert.True(hostWritten.IsServer && hostWritten.IsClient && !hostWritten.IsOwner);
            Assert.True(ownedByHost.IsOwner);

            // Nobody writes a client's behaviour on an object nobody owns.
            ownerWritten.Health = 5;
            Assert.Equal(100, ownerWritten.Health);
            ownedByHost.Health = 5;
            Assert.Equal(5, ownedByHost.Health);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientWritesOnlyItsOwnClientToServerBehaviours()
    {
        Scene hostWorld = CreateScene();
        using NetWriter snapshot = new(), spawn = new();
        NetworkTestScene.Current.WriteScene(hostWorld, snapshot);
        GameObject original = CreateGameObject("Owned");
        original.AddComponent<NetTestComponent>();
        original.AddComponent<NetCollectionTestComponent>().OwnerWritten();
        original.Network.Networked = true;
        NetworkTestScene.Current.WriteSpawn(original, owner: 1, NetworkOrphaned.Destroy, spawn, generation: 1);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot.ToArray(), spawn.ToArray());
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && Scene.Main.Objects.Any(o => o.Identifier == original.Identifier), host);
            GameObject copy = Find(Scene.Main, original.Identifier);
            Assert.True(copy.GetComponent<NetCollectionTestComponent>()!.Authority);
            Assert.False(copy.GetComponent<NetTestComponent>()!.Authority);
            Assert.True(copy.GetComponent<NetTestComponent>()!.IsClient);
            Assert.False(copy.GetComponent<NetTestComponent>()!.IsServer);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ACollectionOnAnOwnerOnlyBehaviourReachesTheOwnerAlone()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "alice");
            GameObject bag = CreateGameObject("Bag");
            NetCollectionTestComponent inventory = bag.AddComponent<NetCollectionTestComponent>();
            inventory.SyncMode = SyncMode.Owner;
            inventory.Scores.Add(1);
            scene.Add(bag);
            bag.SpawnBuilt(aliceHere);
            Pump(() => alice.Has(FakePeer.Spawn) && bob.Has(FakePeer.Spawn), alice, bob);

            inventory.Scores.Add(2);
            Pump(() => alice.SyncValues().Any(v => v.Component == inventory.Identifier && v.Slot == NetCollectionTestComponent.ScoresSlot), alice, bob);
            PumpFor(20, alice, bob);
            Assert.DoesNotContain(bob.SyncValues(), v => v.Component == inventory.Identifier);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SetDirtySendsEveryValueAgain()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent stats = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.SpawnBuilt();
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            alice.WireReceived.Clear();

            stats.SetDirty();
            Pump(() => alice.SyncValues().Any(v => v.Component == stats.Identifier), alice);
            Assert.Contains(alice.SyncValues(), v => v.Component == stats.Identifier && v.Slot == NetTestComponent.HealthSlot);
            Assert.Contains(alice.SyncValues(), v => v.Component == stats.Identifier && v.Slot == NetTestComponent.ScoreSlot);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ABaseTypesMembersKeepTheirBitsInEveryTypeDerivedFromIt()
    {
        NetworkMembers parent = NetworkMembers.Of(typeof(NetCollectionTestComponent));
        NetworkMembers child = NetworkMembers.Of(typeof(RpcStateTestComponent));
        for (int bit = 0; bit < parent.SyncCount; bit++)
        {
            Assert.Same(parent.SyncAt(bit), child.SyncAt(bit));
        }

        Assert.Equal(parent.SyncCount + 1, child.SyncCount);
        Assert.Equal(parent.SyncCount, child.Sync(799)!.Bit);

        // Own state is always last, so what is left of a block is its.
        NetworkMembers custom = NetworkMembers.Of(typeof(PackedGridTestComponent));
        Assert.IsType<NetworkSerializeMember>(custom.SyncAt(custom.SyncCount - 1));
    }
}
