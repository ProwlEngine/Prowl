using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void WithoutASessionTeleportOnlyMovesTheObject()
    {
        GameObject go = CreateGameObject("Loose");
        go.Network.Teleport(new Float3(3, 4, 5), Quaternion.Identity);
        Assert.Equal(new Float3(3, 4, 5), go.Transform.Position);
    }

    [Fact]
    public void AHostTeleportReachesPeersTaggedAndAClientCannotTeleportWhatItDoesNotControl()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject crate = CreateGameObject("Crate");
            scene.Add(crate);
            crate.Network.SpawnSerialized();
            uint netId = NetworkTestScene.Current.NetIdOf(crate);
            PumpFor(10, alice);

            crate.Network.Teleport(new Float3(500, 0, 0), Quaternion.Identity);
            Pump(() => alice.WireReceived.Any(m => m.Id == FakePeer.Snapshot &&
                ReadTransforms(m.Data).Any(r => r.NetId == netId && (r.Channels & TeleportChannel) != 0)), alice);
            TransformRecord jumped = alice.WireReceived.Where(m => m.Id == FakePeer.Snapshot).SelectMany(m => ReadTransforms(m.Data))
                .First(r => r.NetId == netId && (r.Channels & TeleportChannel) != 0);
            Assert.Equal(new Float3(500, 0, 0), jumped.Position);
            Assert.Equal(0, jumped.Channels & ResumedChannel);
        }
        finally { Networking.Networking.Shutdown(); }

        SceneInstance hostWorld = CreateScene();
        using NetWriter snapshot = new(), spawn = new();
        NetworkTestScene.Current.WriteSnapshot(hostWorld, snapshot);
        GameObject remote = CreateGameObject("Remote");
        remote.Network.Mode = NetworkMode.Object;
        NetworkTestScene.Current.WriteSpawn(remote, owner: -1, NetworkOrphaned.Destroy, spawn, generation: 1);
        LocalNetwork other = new();
        using FakePeer host = FakePeer.Host(other);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(other, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == remote.Identifier), host);
            GameObject copy = Find(SceneInstance.Main, remote.Identifier);
            Assert.Throws<InvalidOperationException>(() => copy.Network.Teleport(Float3.One, Quaternion.Identity));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
