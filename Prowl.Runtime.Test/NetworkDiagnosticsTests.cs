using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkDiagnosticsTests : RuntimeTestBase
{
    private sealed class BrokenValue
    {
        public override string ToString() => throw new InvalidOperationException("Diagnostic formatting must not break replication.");
    }

    [Fact]
    public void ValueFormattingIsBoundedAndDoesNotPropagateUserExceptions()
    {
        Assert.Equal("42", NetworkDiagnostics.FormatValue(42));
        Assert.Equal("null", NetworkDiagnostics.FormatValue(null));
        Assert.Equal(161, NetworkDiagnostics.FormatValue(new string('x', 10000)).Length);
        Assert.Equal("<value unavailable>", NetworkDiagnostics.FormatValue(new BrokenValue()));
    }

    [Fact]
    public void CaptureIsOptInBoundedAndKeepsOnlyACappedPayloadPreview()
    {
        NetworkDiagnostics.Clear();
        NetworkDiagnostics.Enabled = false;
        try
        {
            NetworkDiagnostics.Record("RPC", "In", null, Guid.Empty, "ignored");
            Assert.Equal(0, NetworkDiagnostics.Count);
            NetworkDiagnostics.Enabled = true;
            byte[] payload = Enumerable.Repeat((byte)0xAB, 100).ToArray();
            for (int i = 0; i < NetworkDiagnostics.Capacity + 7; i++)
                NetworkDiagnostics.Record("RPC", "In", null, Guid.Empty, i.ToString(), payload);

            Assert.Equal(NetworkDiagnostics.Capacity, NetworkDiagnostics.Count);
            Assert.Equal(7, NetworkDiagnostics.Overwritten);
            NetworkDiagnostics.Entry oldest = NetworkDiagnostics.GetEntry(0);
            Assert.Equal("7", oldest.Detail);
            Assert.Equal(100, oldest.PayloadBytes);
            Assert.Equal(string.Concat(Enumerable.Repeat("AB", 64)), oldest.Preview);
            Assert.Throws<ArgumentOutOfRangeException>(() => NetworkDiagnostics.GetEntry(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => NetworkDiagnostics.GetEntry(NetworkDiagnostics.Count));
            NetworkDiagnostics.Clear();
            NetworkDiagnostics.Record("Session", "Local", null, Guid.Empty, "new");
            Assert.True(NetworkDiagnostics.GetEntry(0).Sequence > oldest.Sequence);
            Assert.Equal(0, NetworkDiagnostics.Overwritten);
        }
        finally
        {
            NetworkDiagnostics.Enabled = false;
            NetworkDiagnostics.Clear();
        }
    }

    [Fact]
    public void CaptureObservesRpcAndRejectedSyncWithoutConsumingTheReader()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject("Diagnostic target");
        go.NetworkMode = NetworkMode.Object;
        NetTestComponent component = go.AddComponent<NetTestComponent>();
        scene.Add(go);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        NetworkDiagnostics.Clear();
        NetworkDiagnostics.Enabled = true;
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            client.SendSync(component.Identifier, NetTestComponent.HealthSlot, 1);
            client.Send(FakePeer.RpcToHost, w =>
            {
                w.WriteGuid(component.Identifier);
                w.WriteUInt32(NetTestComponent.AskHostId);
                w.WriteVarInt32(42);
            });
            Pump(() => component.Asked.Count > 0, client);
            Assert.Equal(42, component.Asked[0]);
            Assert.Equal(100, component.Health);
            var entries = Enumerable.Range(0, NetworkDiagnostics.Count).Select(NetworkDiagnostics.GetEntry).ToArray();
            Assert.Contains(entries, e => e.Kind == "RpcToHost" && e.Direction == "In");
            NetworkDiagnostics.Entry rpcMessage = Assert.Single(entries.Where(e => e.Kind == "RpcToHost" && e.Direction == "In"));
            Assert.Contains("Diagnostic target", rpcMessage.Context);
            Assert.Contains("AskHost", rpcMessage.Context);
            Assert.Contains("AskHost", rpcMessage.Name);
            NetworkDiagnostics.Entry syncMessage = Assert.Single(entries.Where(e => e.Kind == "Sync" && e.Direction == "In"));
            Assert.Contains("Health", syncMessage.Context);
            Assert.Contains("Health", syncMessage.Name);
            Assert.Contains("Value: 1", syncMessage.Context);
            Assert.Contains(entries, e => e.Kind == "RPC" && e.Direction == "Invoke" && e.Target == component.Identifier);
            Assert.Contains(entries, e => e.Kind == "SyncVar" && e.Direction == "Rejected" && e.Target == component.Identifier);
            var objects = new List<Networking.Networking.ObjectDiagnostic>();
            Networking.Networking.GetObjectDiagnostics(objects);
            Assert.Contains(objects, o => o.Identifier == go.Identifier && o.Controlled && o.OwnerId == -1);
            Networking.Networking.Shutdown();
            Assert.True(NetworkDiagnostics.Count > 0);
            Assert.Contains(Enumerable.Range(0, NetworkDiagnostics.Count).Select(NetworkDiagnostics.GetEntry),
                e => e.Sequence == rpcMessage.Sequence && e.Context!.Contains("AskHost"));
            Assert.Contains(Enumerable.Range(0, NetworkDiagnostics.Count).Select(NetworkDiagnostics.GetEntry),
                e => e.Kind == "Session" && e.Detail.StartsWith("Ended:"));
        }
        finally
        {
            Networking.Networking.Shutdown();
            NetworkDiagnostics.Enabled = false;
            NetworkDiagnostics.Clear();
        }
    }

    private static void Pump(Func<bool> condition, FakePeer peer)
    {
        for (int i = 0; i < 1000 && !condition(); i++)
        {
            Networking.Networking.Receive();
            Networking.Networking.Flush();
            peer.Session.Update();
            Thread.Sleep(1);
        }
        Assert.True(condition());
    }
}
