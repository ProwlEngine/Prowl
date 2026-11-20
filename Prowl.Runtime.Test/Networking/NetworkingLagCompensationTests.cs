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
    public void TheHostRemembersWhereAnObjectWasAndWhatAClientSaw()
    {
        SceneInstance scene = CreateScene();
        GameObject target = CreateGameObject("Target");
        LagCompensator history = target.AddComponent<LagCompensator>();
        scene.Add(target);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            target.Network.SpawnSerialized();
            List<(double Time, float X)> seen = new();
            for (int i = 0; i < 30; i++)
            {
                target.Transform.Position = new Float3(i, 0, 0);
                uint tick = Networking.Networking.Tick;
                Pump(() => Networking.Networking.Tick != tick, alice);
                seen.Add((Networking.Networking.ServerTime, i));
            }

            (double time, float x) = seen[10];
            Assert.True(history.TrySample(time, out Float3 position, out _));
            Assert.Equal(x, position.X, 0.5f);
            Assert.False(history.TrySample(seen[0].Time - 10, out _, out _));

            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "alice");
            Assert.True(LagCompensator.ViewTimeOf(aliceHere) < Networking.Networking.ServerTime);
            Assert.True(history.TrySampleSeenBy(aliceHere, out Float3 seenBy, out _));
            Assert.True(seenBy.X < target.Transform.Position.X);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
