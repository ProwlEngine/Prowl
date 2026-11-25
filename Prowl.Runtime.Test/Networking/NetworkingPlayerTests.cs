using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public class LocalPlayerListener : NetworkBehaviour
{
    public int Started, Stopped;

    public override void OnStartLocalPlayer() => Started++;

    public override void OnStopLocalPlayer() => Stopped++;
}

public partial class NetworkingTests
{
    [Fact]
    public void TheHostAddsPlayersAndTellsEachClientWhichIsItsOwn()
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
            Assert.True(avatar.Network.Active);
            Assert.Same(aliceHere, avatar.Network.Owner);
            Assert.Same(avatar, Networking.Networking.PlayerOf(aliceHere));
            Assert.False(avatar.Network.IsLocalPlayer);
            Pump(() => alice.Has(FakePeer.Player), alice);
            Assert.Equal(avatar.Identifier, new NetReader(alice.Last(FakePeer.Player)).ReadGuid());

            GameObject mine = CreateGameObject("Host avatar");
            LocalPlayerListener listener = mine.AddComponent<LocalPlayerListener>();
            scene.Add(mine);
            mine.SpawnBuilt();
            Networking.Networking.AddPlayer(Networking.Networking.Session!.LocalPeer!, mine);
            Assert.Same(mine, Networking.Networking.LocalPlayer);
            Assert.True(mine.Network.IsLocalPlayer);
            Assert.Equal(1, listener.Started);

            Networking.Networking.RemovePlayer(Networking.Networking.Session!.LocalPeer!);
            Assert.Null(Networking.Networking.LocalPlayer);
            Assert.Equal(1, listener.Stopped);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientLearnsItsPlayerAndLosesItWhenItDespawns()
    {
        Scene hostWorld = CreateScene();
        using NetWriter snapshot = new(), spawn = new(), player = new(), despawn = new();
        NetworkTestScene.Current.WriteScene(hostWorld, snapshot);
        GameObject avatar = CreateGameObject("Avatar");
        avatar.AddComponent<LocalPlayerListener>();
        avatar.Network.Networked = true;
        NetworkTestScene.Current.WriteSpawn(avatar, owner: -1, NetworkOrphaned.Destroy, spawn, generation: 1);
        player.WriteGuid(avatar.Identifier);
        new DespawnMessage(avatar.Identifier).Write(despawn);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.Scene, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Player, player.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.LocalPlayer.IsValid(), host);
            GameObject copy = Networking.Networking.LocalPlayer!;
            Assert.Equal(avatar.Identifier, copy.Identifier);
            Assert.True(copy.Network.IsLocalPlayer);
            Assert.Equal(1, copy.GetComponent<LocalPlayerListener>()!.Started);

            LocalPlayerListener listener = copy.GetComponent<LocalPlayerListener>()!;
            host.Session.SendRaw(host.Session.Peers.Single(p => !p.IsLocal), FakePeer.Despawn, despawn.WrittenSpan);
            Pump(() => Networking.Networking.LocalPlayer.IsNotValid(), host);
            Assert.Equal(1, listener.Stopped);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void APlayerObjectKeepsItsOwnerWhileItIsAPlayer()
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
            NetPeer bobHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "bob");
            GameObject avatar = CreateGameObject("Alice avatar"), spare = CreateGameObject("Spare avatar");
            scene.Add(avatar); scene.Add(spare);
            avatar.SpawnBuilt();
            Networking.Networking.AddPlayer(aliceHere, avatar);

            Assert.Throws<InvalidOperationException>(() => avatar.Network.RemoveOwnership());
            Assert.Throws<InvalidOperationException>(() => avatar.Network.AssignOwnership(bobHere));
            Assert.Same(aliceHere, avatar.Network.Owner);
            avatar.Network.AssignOwnership(aliceHere);

            // A peer that has a player is refused another.
            spare.SpawnBuilt();
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.AddPlayer(aliceHere, spare));
            Assert.Same(avatar, Networking.Networking.PlayerOf(aliceHere));
            Assert.Null(spare.Network.Owner);

            // Replaced, the first is an ordinary object again.
            Networking.Networking.ReplacePlayer(aliceHere, spare);
            Assert.Same(spare, Networking.Networking.PlayerOf(aliceHere));
            Assert.Same(aliceHere, spare.Network.Owner);
            avatar.Network.RemoveOwnership();
            Assert.Null(avatar.Network.Owner);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
