using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void StatisticsCountEngineMessagesByKindBothWays()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        NetworkStatistics.Reset();
        List<NetworkMessageInfo> sent = new();
        void Record(NetworkMessageInfo info) => sent.Add(info);
        NetworkStatistics.MessageSent += Record;
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            (string Kind, NetworkTraffic Received, NetworkTraffic Sent) snapshot =
                NetworkStatistics.ByKind().Single(k => k.Kind == "SceneSnapshot");
            Assert.Equal(1, snapshot.Sent.Messages);
            Assert.True(snapshot.Sent.Bytes > 0);
            Assert.Equal(1, NetworkStatistics.ByKind().Single(k => k.Kind == "SceneLoaded").Received.Messages);
            Assert.True(NetworkStatistics.Sent.Messages >= 3);
            Assert.Contains(sent, info => info.Kind == "SceneSnapshot" && info.Peer.Name == "alice");
        }
        finally
        {
            NetworkStatistics.MessageSent -= Record;
            Networking.Networking.Shutdown();
        }
    }
}
