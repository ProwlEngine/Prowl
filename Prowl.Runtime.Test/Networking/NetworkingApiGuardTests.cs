using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AnObjectThatIsNotNetworkedIsAnOrdinaryObjectOnAClient()
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer _);
        using (host)
        try
        {
            Assert.True(proxy.IsProxy);

            // Nobody else has it, so nobody else drives it, and its values are this peer's to set.
            GameObject local = CreateGameObject("Local");
            NetTestComponent component = local.AddComponent<NetTestComponent>();
            Scene.Main.Add(local);
            Assert.False(local.IsProxy);
            Assert.False(local.Network.IsProxy);
            component.Score = 5;
            Assert.Equal(5, component.Score);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASpawnedObjectCannotBeTakenOffTheNetworkByItsSettings()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Spawned"), part = CreateGameObject("Part");
        part.SetParent(root);
        root.Network.Networked = true;
        scene.Add(root); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            Assert.True(root.Network.Active);
            Assert.Throws<InvalidOperationException>(() => root.Network.Networked = false);
            Assert.Throws<InvalidOperationException>(() => root.Network.ServerOnly = true);
            Assert.Throws<InvalidOperationException>(() => part.Network.ServerOnly = true);
            Assert.True(root.Network.Active);

            // Setting what it already is does nothing, and once despawned it is free to change.
            root.Network.Networked = true;
            root.Network.ServerOnly = false;
            Networking.Networking.Despawn(root);
            root.Network.Networked = false;
            Assert.False(root.Network.Networked);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SpawningASpawnedObjectAgainChangesNothing()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "alice");
            GameObject avatar = CreateGameObject("Alice avatar");
            scene.Add(avatar);
            avatar.SpawnBuilt();
            Networking.Networking.AddPlayer(aliceHere, avatar);

            // Spawn warns and leaves a spawned object alone: its owner is AssignOwnership's to change.
            uint netId = avatar.Network.NetId;
            Pump(() => alice.Has(FakePeer.Owner), alice);
            alice.WireReceived.Clear();
            Networking.Networking.Spawn(avatar);
            Assert.Same(aliceHere, avatar.Network.Owner);

            GameObject crate = CreateGameObject("Crate");
            scene.Add(crate);
            crate.SpawnBuilt();
            Networking.Networking.Spawn(crate, aliceHere);
            Assert.Null(crate.Network.Owner);
            Assert.Equal(netId, avatar.Network.NetId);
            PumpFor(100, alice);
            Assert.DoesNotContain(alice.WireReceived, m => m.Id == FakePeer.Owner);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ControlAndLookupByNetIdAreAnsweredForSpawnedObjectsOnly()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        GameObject plain = CreateGameObject("Plain");
        scene.Add(plain);
        Assert.False(plain.Network.HasControl);
        Assert.Null(Networking.Networking.Find(1));

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "alice");
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent state = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);

            // Not spawned, nobody controls it over the network, though it is no proxy either.
            Assert.False(crate.Network.HasControl);
            Assert.False(crate.IsProxy);

            crate.SpawnBuilt();
            Assert.True(crate.Network.HasControl);
            Assert.True(state.HasControl);
            Assert.Same(crate, Networking.Networking.Find(crate.Network.NetId));
            Assert.Null(Networking.Networking.Find(crate.Network.NetId + 1000));
            Assert.Null(Networking.Networking.Find(0));

            crate.Network.AssignOwnership(aliceHere);
            Assert.False(crate.Network.HasControl);
            Assert.True(crate.IsProxy);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnObjectWithMoreTransformsThanAnIndexHoldsIsRefused()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        Networking.Networking.Host(new LocalTransport(new LocalNetwork(), "host"));
        try
        {
            GameObject swarm = CreateGameObject("Swarm");
            for (int i = 0; i < 256; i++)
            {
                GameObject part = CreateGameObject($"Part {i}");
                part.AddComponent<NetworkTransform>();
                part.SetParent(swarm);
            }

            scene.Add(swarm);
            Assert.Throws<InvalidOperationException>(() => swarm.SpawnBuilt());
            Assert.False(swarm.Network.Active);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
