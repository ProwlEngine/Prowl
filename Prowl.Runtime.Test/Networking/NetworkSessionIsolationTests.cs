using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public sealed class NetworkSessionIsolationTests : RuntimeTestBase
{
    [Fact]
    public void TimedOutWorkerRetainsItsTransportUntilShutdownCanBeRetried()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        using BlockingTransport transport = new();
        try
        {
            Networking.Networking.Configure(new() { Execution = NetworkExecution.DedicatedThread });
            SceneInstance scene = CreateScene();
            Bind(scene);
            Networking.Networking.Host(transport);
            Assert.True(transport.Entered.Wait(TimeSpan.FromSeconds(2)));
            Networking.Networking.Shutdown();
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
        using LocalTransport again = new(new(), "again");
        Networking.Networking.Host(again);
        Networking.Networking.Shutdown();
    }

    [Fact]
    public void UnsentCollectionChangesDoNotCarryIntoTheNextSession()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject();
        root.Network.Networked = true;
        NetCollectionTestComponent component = root.AddComponent<NetCollectionTestComponent>();
        scene.Add(root);
        Bind(scene);
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
    public void ObjectReferencesWithoutASessionStillConsumeTheirBytes()
    {
        // A reference is its object's NetId and a component index.
        using NetWriter writer = new();
        writer.WriteVarUInt32(7);
        writer.WriteByte(0);
        writer.WriteInt32(42);
        NetReader reader = new(writer.WrittenSpan);
        Assert.Null(NetSerializer<GameObject>.Read(ref reader));
        Assert.Equal(42, reader.ReadInt32());
        Assert.True(reader.IsAtEnd);
    }

    /// <summary>Holds whoever pumps it inside Receive until released.</summary>
    private sealed class BlockingTransport : Transport
    {
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false);
        public bool Disposed;
        public override bool IsListening => true;
        public override void Listen() { }
        public override Connection Connect(string address) => throw new NotSupportedException();
        public override void Receive() { Entered.Set(); Release.Wait(); }
        public override void Flush() { }
        public override void Shutdown() { }
        protected override void DisposeCore() { Disposed = true; Entered.Dispose(); Release.Dispose(); }
    }
}
