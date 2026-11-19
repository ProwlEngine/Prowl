using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static string Incompressible(int length) =>
        string.Concat(Enumerable.Range(0, length / 32 + 1).Select(_ => Guid.NewGuid().ToString("N")))[..length];

    [Fact]
    public void HostKicksAPeerWhoseMessageLimitCannotCarryReplication()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer small = FakePeer.Join(network, "small", FakePeer.SchemaOptions with { MaxMessageSize = 64 });
        SessionEndReason? reason = null;
        small.Session.Ended += (r, _) => reason = r;
        try
        {
            Pump(() => small.Session.State == SessionState.Ended, small);
            Assert.Equal(SessionEndReason.Kicked, reason);
            Assert.True(Networking.Networking.IsActive);
            Assert.DoesNotContain(Networking.Networking.Session!.Peers, p => p.Name == "small");
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void HostKicksAPeerThatCannotReceiveAJoinSpawn()
    {
        SceneInstance scene = CreateScene();
        GameObject big = CreateGameObject(Incompressible(2000));
        big.Network.Networked = true;
        scene.Add(big);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer small = FakePeer.Join(network, "small", FakePeer.SchemaOptions with { MaxMessageSize = 256 });
        SessionEndReason? reason = null;
        small.Session.Ended += (r, _) => reason = r;
        try
        {
            Pump(() => small.Session.State == SessionState.Ended, small);
            Assert.Equal(SessionEndReason.Kicked, reason);
            Assert.True(small.Has(FakePeer.SpawnStarted));
            Assert.False(small.Has(FakePeer.Spawn));
            Assert.True(Networking.Networking.IsActive);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASpawnTooLargeForOnePeerKicksOnlyThatPeer()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer small = FakePeer.Join(network, "small", FakePeer.SchemaOptions with { MaxMessageSize = 4096 });
        using FakePeer wide = FakePeer.Join(network, "wide");
        SessionEndReason? reason = null;
        small.Session.Ended += (r, _) => reason = r;
        try
        {
            Pump(() => small.IsReady && wide.IsReady, small, wide);
            GameObject big = CreateGameObject(Incompressible(8000));
            scene.Add(big);
            big.Network.SpawnSerialized();
            Pump(() => wide.Has(FakePeer.Spawn) && small.Session.State == SessionState.Ended, small, wide);
            Assert.Equal(SessionEndReason.Kicked, reason);
            Assert.Equal(SessionState.Active, wide.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SnapshotsAreSplitToThePeersOwnMessageLimit()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer small = FakePeer.Join(network, "small", FakePeer.SchemaOptions with { MaxMessageSize = 2048 });
        try
        {
            Pump(() => small.IsReady, small);
            GameObject[] objects = Enumerable.Range(0, 200).Select(i =>
            {
                GameObject go = CreateGameObject($"Object {i}");
                go.AddComponent<NetworkTransform>();
                scene.Add(go);
                go.Network.SpawnSerialized();
                return go;
            }).ToArray();
            Pump(() => TransformIds(small).Count == objects.Length, small);
            AssertTransformBatches(small, 2048 - 2, objects);
            Assert.Equal(SessionState.Active, small.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ABroadcastTooLargeForOnePeerKicksThatPeerAndKeepsTheOthers()
    {
        SceneInstance scene = CreateScene();
        GameObject ship = CreateGameObject("Ship");
        NetTestComponent component = ship.AddComponent<NetTestComponent>();
        ship.Network.Networked = true;
        scene.Add(ship);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer small = FakePeer.Join(network, "small", FakePeer.SchemaOptions with { MaxMessageSize = 4096 });
        using FakePeer alice = FakePeer.Join(network, "alice");
        SessionEndReason? reason = null;
        small.Session.Ended += (r, _) => reason = r;
        try
        {
            Pump(() => small.IsReady && alice.IsReady, small, alice);
            string text = Incompressible(8000);
            component.Shout(text);
            Pump(() => small.Session.State == SessionState.Ended, small, alice);
            Assert.Equal(SessionEndReason.Kicked, reason);
            PumpFor(30, small, alice);
            Assert.Equal(SessionState.Active, alice.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
