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
            SceneInstance.Main.Add(local);
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
        SceneInstance scene = CreateScene();
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
    public void SpawningAPlayerObjectAgainCannotTakeItFromItsPeer()
    {
        SceneInstance scene = CreateScene();
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

            Assert.Throws<InvalidOperationException>(() => avatar.SpawnBuilt());
            Assert.Same(aliceHere, avatar.Network.Owner);
            avatar.SpawnBuilt(aliceHere);
            Assert.Same(aliceHere, avatar.Network.Owner);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
