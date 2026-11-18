using Jitter2.Dynamics;

using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void WritingTheWorldLeavesProxyPhysicsBodiesAlone()
    {
        SceneInstance scene = CreateScene();
        GameObject ball = CreateGameObject("Ball");
        Rigidbody3D body = ball.AddComponent<Rigidbody3D>();
        scene.Add(ball);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            ball.Network.SpawnSerialized(Networking.Networking.Session!.Peers.Single(p => p.Name == "alice"));
            PumpFor(3, alice);
            Assert.Equal(MotionType.Kinematic, body.Native!.MotionType);

            (MotionType Authored, MotionType Live) during = default;
            NetworkTestScene.Current.AsSetUp(() => during = (body.MotionType, body.Native!.MotionType));
            Assert.Equal(MotionType.Dynamic, during.Authored);
            Assert.Equal(MotionType.Kinematic, during.Live);
            Assert.Equal(MotionType.Kinematic, body.MotionType);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
