using Prowl.Runtime.Networking;
using Prowl.Runtime.Networking.Diagnostics;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Serialization;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public sealed class NetworkSessionIsolationTests : RuntimeTestBase
{
    [Fact]
    public void TimedOutWorkerRetainsItsTransportUntilShutdownCanBeRetried()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        using NetworkingThreadTests.BlockingTransport transport = new();
        try
        {
            Networking.Networking.Configure(new() { Execution = NetworkExecution.DedicatedThread });
            Scene scene = CreateScene();
            Scene.Load(scene);
            Scene.ProcessPendingLoad();
            Networking.Networking.Host(transport);
            Assert.True(transport.Entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.Throws<TimeoutException>(() => Networking.Networking.Shutdown());
            Assert.Null(Networking.Networking.Session);
            Assert.Null(Networking.Networking.System);
            Assert.False(transport.Disposed);
            using LocalTransport next = new(new(), "next");
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.Host(next));
        }
        finally
        {
            transport.Release.Set();
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
        }

        Assert.Null(Networking.Networking.System);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public void UnsentCollectionChangesDoNotCarryIntoTheNextSession()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject();
        root.Network.Mode = NetworkMode.Object;
        NetCollectionTestComponent component = root.AddComponent<NetCollectionTestComponent>();
        scene.Add(root);
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "first"));
            component.Scores.Add(1);
            Assert.Equal(1, NetworkDiagnostics.PendingSyncCount);
            Networking.Networking.Shutdown();

            Networking.Networking.Host(new LocalTransport(network, "second"));
            using NetWriter writer = new();
            component.Scores.WriteChanges(writer);
            NetReader reader = new(writer.WrittenSpan);
            reader.ReadByte(); // Collection delta marker.
            reader.ReadVarUInt32(); // Sequence before the delta.
            Assert.Equal(0u, reader.ReadVarUInt32());
            Assert.True(reader.IsAtEnd);
            Assert.Equal(new[] { 1 }, component.Scores);

            component.Scores.Add(2);
            writer.Reset();
            component.Scores.WriteChanges(writer);
            reader = new(writer.WrittenSpan);
            reader.ReadByte();
            reader.ReadVarUInt32();
            Assert.Equal(1u, reader.ReadVarUInt32());
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ObjectReferencesWithoutASessionStillConsumeTheirIdentifier()
    {
        using NetWriter writer = new();
        writer.WriteGuid(Guid.NewGuid());
        writer.WriteInt32(42);
        NetReader reader = new(writer.WrittenSpan);
        Assert.Null(NetSerializer<GameObject>.Read(ref reader));
        Assert.Equal(42, reader.ReadInt32());
        Assert.True(reader.IsAtEnd);
    }
}
