using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void TheCatchUpCarriesOnlyTheValuesThatChangedSinceTheSnapshot()
    {
        SceneInstance scene = CreateScene();
        GameObject a = CreateGameObject("A");
        a.AddComponent<NetTestComponent>();
        scene.Add(a);
        GameObject b = CreateGameObject("B");
        NetTestComponent changing = b.AddComponent<NetTestComponent>();
        scene.Add(b);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        alice.AutoReady = false;
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            changing.Health = 42;
            NetReader snapshot = new(alice.Last(FakePeer.SceneSnapshot));
            snapshot.ReadByte();
            ulong generation = snapshot.ReadUInt64();
            alice.Send(FakePeer.SceneLoaded, w => w.WriteUInt64(generation));

            Pump(() => alice.Has(FakePeer.SceneCatchUp), alice);
            NetReader catchUp = new(alice.Last(FakePeer.SceneCatchUp));
            catchUp.ReadUInt64();
            Assert.Equal(1u, SyncBlock.Read(ref catchUp).Count);
            SyncValue value = SyncValue.Read(ref catchUp);
            Assert.Equal(changing.Identifier, value.ComponentId);
            Assert.Equal(NetTestComponent.HealthSlot, value.Slot);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SnapshotsCarryTheNetworkTickTheGameSees()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Ticking");
        NetTestComponent component = go.AddComponent<NetTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetworkTestScene.Current.NetworkTick += 5;
            alice.WireReceived.Clear();
            component.Health = 3;
            Networking.Networking.System!.Snapshots.Send();
            uint tick = Networking.Networking.Tick;
            Pump(() => alice.Has(FakePeer.Snapshot), alice);
            Assert.Equal(tick, SnapshotTick(alice.WireReceived.First(m => m.Id == FakePeer.Snapshot).Data));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
