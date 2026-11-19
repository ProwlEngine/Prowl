using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AJoinerGetsNetworkedObjectsAsSpawnsAndNoPlainObjectsOfACodeBuiltScene()
    {
        SceneInstance scene = CreateScene();
        GameObject prop = CreateGameObject("Prop");
        prop.AddComponent<PlainTestComponent>();
        GameObject ship = CreateGameObject("Ship");
        ship.Network.Networked = true;
        NetTestComponent stats = ship.AddComponent<NetTestComponent>();
        scene.Add(prop);
        scene.Add(ship);
        MakeCurrent(scene);
        Guid propId = prop.Identifier, shipId = ship.Identifier;
        uint netId;
        byte[] snapshot;
        List<byte[]> spawns;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            stats.Score = 9;
            netId = NetworkTestScene.Current.NetIdOf(ship);
            using FakePeer recorder = FakePeer.Join(source, "recorder");
            Pump(() => recorder.Has(FakePeer.SceneSnapshot) && recorder.Has(FakePeer.Spawn), recorder);
            snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.SceneSnapshot).Data;
            spawns = [.. recorder.WireReceived.Where(m => m.Id == FakePeer.Spawn).Select(m => m.Data)];
            NetReader spawn = new(Assert.Single(spawns));
            Assert.Equal(shipId, spawn.ReadGuid());
        }
        finally { Networking.Networking.Shutdown(); }

        // The client has a world of its own, so anything of the host's found there came over the wire.
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot), peer);
            foreach (byte[] spawn in spawns)
            {
                host.Send(FakePeer.Spawn, w => w.WriteBytes(spawn), peer);
            }
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == shipId), host);
            Assert.DoesNotContain(SceneInstance.Main.Objects, o => o.Identifier == propId);
            GameObject copy = Find(SceneInstance.Main, shipId);
            Assert.True(copy.Network.Active);
            Assert.Equal(netId, NetworkTestScene.Current.NetIdOf(copy));
            Assert.Equal(9, copy.GetComponent<NetTestComponent>()!.Score);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
