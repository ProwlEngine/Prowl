using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkSpawnCleanupComponent : NetCollectionTestComponent, INetworkEvents
{
    public static readonly List<NetworkSpawnCleanupComponent> Decoded = new();
    public bool EndSessionOnDeserialize;
    [SerializeIgnore] public int DisposeCalls;
    [SerializeIgnore] public int SpawnCalls;

    public override void OnAfterDeserialize()
    {
        Decoded.Add(this);
        if (EndSessionOnDeserialize) Networking.Networking.Shutdown();
    }
    public void OnNetworkSpawn() => SpawnCalls++;
    protected override void OnDispose() { DisposeCalls++; base.OnDispose(); }
}

public partial class NetworkingTests
{
    [Theory]
    [InlineData("id", NetworkExecution.Inline)]
    [InlineData("sync", NetworkExecution.Inline)]
    [InlineData("trailing", NetworkExecution.Inline)]
    [InlineData("cancel", NetworkExecution.Inline)]
    [InlineData("success", NetworkExecution.Inline)]
    [InlineData("id", NetworkExecution.DedicatedThread)]
    [InlineData("sync", NetworkExecution.DedicatedThread)]
    [InlineData("cancel", NetworkExecution.DedicatedThread)]
    [InlineData("success", NetworkExecution.DedicatedThread)]
    public void SpawnConstructionDisposesRejectedTreesAndPreservesSuccessfulOnes(string kind, NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        Scene original = CreateScene(); MakeCurrent(original);
        using NetWriter snapshot = new(); Networking.Networking.WriteSnapshot(original, snapshot);
        GameObject root = CreateGameObject("Spawn root"); root.Network.Mode = NetworkMode.Object;
        NetworkSpawnCleanupComponent component = root.AddComponent<NetworkSpawnCleanupComponent>();
        component.EndSessionOnDeserialize = kind == "cancel";
        component.Scores.Add(9);
        GameObject child = CreateGameObject("Spawn child"); child.SetParent(root);
        child.AddComponent<NetworkSpawnCleanupComponent>();
        using NetWriter spawn = new();
        Networking.Networking.WriteSpawn(root, -1, NetworkOrphaned.Destroy, spawn);
        byte[] bytes = spawn.ToArray();
        if (kind == "id") Guid.NewGuid().TryWriteBytes(bytes);
        if (kind == "sync")
        {
            NetReader header = new(bytes);
            header.ReadGuid(); header.ReadGuid(); header.ReadVarUInt32(); header.ReadByte(); header.ReadUInt32(); header.ReadBytesWithLength();
            using NetWriter malformed = new(); malformed.WriteBytes(bytes.AsSpan(0, header.Position));
            malformed.WriteVarUInt32(1); malformed.WriteGuid(component.Identifier);
            malformed.WriteVarUInt32(NetCollectionTestComponent.HealthSlot); malformed.WriteUInt32(0);
            malformed.WriteBytesWithLength(ReadOnlySpan<byte>.Empty);
            bytes = malformed.ToArray();
        }
        if (kind == "trailing") bytes = [.. bytes, 255];
        LocalNetwork network = new(); using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, bytes);
        };
        NetworkSpawnCleanupComponent.Decoded.Clear();
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            SessionEndReason? ended = null;
            session.Ended += (reason, _) => ended = reason;
            if (kind == "success")
            {
                Pump(() => Networking.Networking.IsReady && Networking.Networking.NetworkObjectCount == 1, host);
                Assert.Equal(2, NetworkSpawnCleanupComponent.Decoded.Count);
                Assert.All(NetworkSpawnCleanupComponent.Decoded, c =>
                {
                    Assert.False(c.IsDisposed); Assert.Equal(0, c.DisposeCalls); Assert.Equal(1, c.SpawnCalls);
                    Assert.Empty(c.HealthChanges); Assert.Empty(c.ScoreChanges);
                });
                Assert.Equal(new[] { 9 }, NetworkSpawnCleanupComponent.Decoded.Single(c => c.GameObject.Identifier == root.Identifier).Scores);
            }
            else
            {
                Pump(() => ended.HasValue, host);
                Assert.Equal(kind == "cancel" ? SessionEndReason.Left : SessionEndReason.ProtocolError, ended);
                Assert.Equal(2, NetworkSpawnCleanupComponent.Decoded.Count);
                Assert.All(NetworkSpawnCleanupComponent.Decoded, c =>
                {
                    Assert.True(c.IsDisposed); Assert.Equal(1, c.DisposeCalls); Assert.Equal(0, c.SpawnCalls);
                });
                Assert.Equal(0, Networking.Networking.NetworkObjectCount);
                Assert.False(Scene.IsLoadPending);
            }
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
            // Clean up the incoming scene after a successful handoff, as scene teardown normally does.
            foreach (NetworkSpawnCleanupComponent c in NetworkSpawnCleanupComponent.Decoded.ToArray())
                if (!c.IsDisposed && c.GameObject.IsValid()) c.GameObject.Dispose();
            NetworkSpawnCleanupComponent.Decoded.Clear();
        }
    }

    [Fact]
    public void SnapshotMaterializationRejectsAmplifiedArrayDimensionsBeforeAllocation()
    {
        EchoObject tree = EchoObject.NewCompound();
        tree["dimensions"] = Serializer.Serialize(new int[] { 1000, 1000 });
        tree["elements"] = EchoObject.NewList();
        byte[] compressed = Networking.Networking.Compress(tree);
        EchoObject decoded = SnapshotDecoder.Decode(compressed, new()
        {
            MaxSnapshotDecompressedBytes = 1024, MaxSnapshotNodes = 32, MaxSnapshotDepth = 8
        });
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<System.IO.InvalidDataException>(() => Serializer.Deserialize<int[,]>(decoded));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000,
            "Malformed dimensions must not allocate the four-megabyte array.");
    }
}
