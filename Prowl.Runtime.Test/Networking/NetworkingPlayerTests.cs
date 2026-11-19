using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public class LocalPlayerListener : Component, INetworkEvents
{
    public int Started, Stopped;

    public void OnStartLocalPlayer() => Started++;

    public void OnStopLocalPlayer() => Stopped++;
}

public partial class NetworkingTests
{
    [Fact]
    public void TheHostAddsPlayersAndTellsEachClientWhichIsItsOwn()
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
        SceneInstance hostWorld = CreateScene();
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
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
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
}
