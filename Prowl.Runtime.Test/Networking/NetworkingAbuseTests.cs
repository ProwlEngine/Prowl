using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private (FakePeer Alice, NetTestComponent Target, NetworkConfiguration Previous) AbuseHost(Action<NetworkConfiguration> configure)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        NetworkConfiguration configuration = new();
        configure(configuration);
        Networking.Networking.Configure(configuration);
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Target");
        NetTestComponent target = go.AddComponent<NetTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        Pump(() => alice.IsReady, alice);
        go.SpawnBuilt();
        Pump(() => alice.Has(FakePeer.Spawn), alice);
        return (alice, target, previous);
    }

    private static bool AliceIsGone() => Networking.Networking.Session!.Peers.All(p => p.Name != "alice");

    [Fact]
    public void AClientMessageOverTheClientLimitDropsTheClient()
    {
        var (alice, target, previous) = AbuseHost(c => c.MaxClientMessageSize = 2048);
        try
        {
            string big = new('x', 4096);
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(target.Identifier); w.WriteUInt32(NetTestComponent.ShoutId); w.WriteString(big); });
            Pump(AliceIsGone, alice);
            Assert.Empty(target.Shouts);
        }
        finally { Networking.Networking.Shutdown(); alice.Dispose(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void AClientSendingFasterThanTheRateLimitIsKicked()
    {
        var (alice, target, previous) = AbuseHost(c => c.MaxClientMessagesPerSecond = 50);
        try
        {
            for (int i = 0; i < 200; i++)
                alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(target.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(i); });
            Pump(AliceIsGone, alice);
            Assert.True(target.Asked.Count < 200);
        }
        finally { Networking.Networking.Shutdown(); alice.Dispose(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void AClientWhoseMessagesKeepFailingOnTheHostIsKicked()
    {
        var (alice, _, previous) = AbuseHost(_ => { });
        const EngineMessage failing = (EngineMessage)(Networking.Networking.EngineMessageIds + 200);
        NetworkTestScene.Current.RegisterEngineMessage(Networking.Networking.Session!, failing,
            (NetPeer sender, ref NetReader reader, Delivery delivery) => throw new KeyNotFoundException("bug"));
        try
        {
            for (int i = 0; i < 20; i++) alice.Send((ushort)(Networking.Networking.EngineMessageIds + 200), w => w.WriteByte(1));
            Pump(AliceIsGone, alice);
        }
        finally { Networking.Networking.Shutdown(); alice.Dispose(); Networking.Networking.Configure(previous); }
    }
}
