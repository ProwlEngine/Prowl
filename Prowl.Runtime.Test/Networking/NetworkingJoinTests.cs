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
}
