using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void TransformBatchesCarryTheNetworkTickTheGameSees()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject("Ticking");
        go.AddComponent<NetworkTransform>();
        go.Network.Networked = true;
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
            go.Transform.Position = new Prowl.Vector.Float3(1, 2, 3);
            Networking.Networking.System!.Transforms.Send();
            uint tick = Networking.Networking.Tick;
            Pump(() => alice.Has(FakePeer.Transform), alice);
            Assert.Equal(tick, TransformTick(alice.WireReceived.First(m => m.Id == FakePeer.Transform).Data));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
