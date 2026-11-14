using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AClientForgetsTheComponentIdsOfDespawnedObjects()
    {
        SceneInstance scene = CreateScene();
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteSnapshot(scene, snapshot);
        GameObject crate = CreateGameObject("Crate");
        crate.Network.Mode = NetworkMode.Object;
        NetTestComponent component = crate.AddComponent<NetTestComponent>();
        using NetWriter spawn = new();
        NetworkTestScene.Current.WriteSpawn(crate, owner: -1, NetworkOrphaned.Destroy, spawn, generation: 1);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        float tickRate = Networking.Networking.TickRate;
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, spawn.WrittenSpan);
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == crate.Identifier), host);
            NetTestComponent here = Find(SceneInstance.Main, crate.Identifier).GetComponent<NetTestComponent>()!;
            host.SendSnapshotValues(client, 7, 1, (component.Identifier, NetTestComponent.HealthSlot, 1u, IntValue(5)));
            Pump(() => here.Health == 5, host);
            NetPeer hostHere = Networking.Networking.Session!.HostPeer!;
            Networking.Networking.System!.Snapshots.TryGetComponentIds(hostHere, out _, out var received);
            Assert.Single(received);

            host.Send(FakePeer.Despawn, w => w.WriteGuid(crate.Identifier), client);
            Pump(() => here.IsDisposed, host);
            Networking.Networking.TickRate = 1000;
            Pump(() => received.Count == 0, host);
        }
        finally
        {
            Networking.Networking.TickRate = tickRate;
            Networking.Networking.Shutdown();
        }
    }
}
