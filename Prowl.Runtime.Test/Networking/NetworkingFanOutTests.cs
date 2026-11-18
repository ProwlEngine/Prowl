using System.Diagnostics;

using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ChangingAValueManyTimesBetweenTicksCostsTheSameWithManyPeers()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Counter");
        NetTestComponent component = go.AddComponent<NetTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        List<FakePeer> peers = [];
        try
        {
            for (int i = 0; i < 16; i++) peers.Add(FakePeer.Join(network, $"peer{i}"));
            Pump(() => peers.All(p => p.IsReady), [.. peers]);

            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < 200_000; i++) component.Health = i;
            long elapsed = watch.ElapsedMilliseconds;
            Assert.True(elapsed < 60, $"200k changes took {elapsed} ms");

            Pump(() => peers.All(p => p.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot) == 199_999), [.. peers]);
        }
        finally
        {
            Networking.Networking.Shutdown();
            foreach (FakePeer peer in peers) peer.Dispose();
        }
    }
}
