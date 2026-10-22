using System.IO;
using System.IO.Compression;

using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static byte[] CompressedEcho(Action<BinaryWriter> write)
    {
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        using (BinaryWriter writer = new(zlib)) write(writer);
        return compressed.ToArray();
    }

    [Fact]
    public void SnapshotDecoderRoundTripsEveryEchoTypeAtExactBudgetBoundaries()
    {
        EchoObject root = EchoObject.NewList();
        EchoObject[] values = [new(EchoType.Null, null), new((byte)1), new((sbyte)-1), new((short)-2),
            new(-3), new(-4L), new((ushort)2), new(3u), new(4UL), new(1.25f), new(2.5), new(3.75m),
            new("héllo"), new(EchoType.ByteArray, new byte[] { 1, 2, 3 }), new(true), EchoObject.NewList(), EchoObject.NewCompound()];
        foreach (EchoObject value in values) root.ListAdd(value);
        using MemoryStream binary = new();
        using (BinaryWriter writer = new(binary, System.Text.Encoding.UTF8, leaveOpen: true)) root.WriteToBinary(writer);
        NetworkConfiguration limits = new() { MaxSnapshotDecompressedBytes = (int)binary.Length,
            MaxSnapshotDepth = 2, MaxSnapshotNodes = values.Length + 1 };
        byte[] compressed = Networking.Networking.Compress(root);
        Assert.Equal(root, SnapshotDecoder.Decode(compressed, limits));
        limits.MaxSnapshotDecompressedBytes--;
        Assert.Throws<InvalidDataException>(() => SnapshotDecoder.Decode(compressed, limits));
    }

    [Theory]
    [InlineData(EchoType.String, -1)]
    [InlineData(EchoType.String, int.MaxValue)]
    [InlineData(EchoType.ByteArray, -1)]
    [InlineData(EchoType.ByteArray, int.MaxValue)]
    [InlineData(EchoType.List, -1)]
    [InlineData(EchoType.List, int.MaxValue)]
    [InlineData(EchoType.Compound, -1)]
    [InlineData(EchoType.Compound, int.MaxValue)]
    public void SnapshotDecoderRejectsInvalidLengthsBeforeAllocating(EchoType type, int length)
    {
        byte[] compressed = CompressedEcho(w => { w.Write((byte)type); w.Write(length); });
        Assert.Throws<InvalidDataException>(() => SnapshotDecoder.Decode(compressed, new()));
    }

    [Fact]
    public void SnapshotDecoderRejectsOversizedCompoundKeysBeforeAllocating()
    {
        byte[] compressed = CompressedEcho(w =>
        {
            w.Write((byte)EchoType.Compound); w.Write(1);
            w.Write(int.MaxValue); w.Write((byte)EchoType.Null);
        });
        Assert.Throws<InvalidDataException>(() => SnapshotDecoder.Decode(compressed, new()));
    }

    [Fact]
    public void SnapshotDecoderCountsNodesAcrossSiblingContainers()
    {
        byte[] compressed = CompressedEcho(w =>
        {
            w.Write((byte)EchoType.List); w.Write(2);
            for (int i = 0; i < 2; i++)
            {
                w.Write((byte)EchoType.List); w.Write(2);
                w.Write((byte)EchoType.Null); w.Write((byte)EchoType.Null);
            }
        });
        Assert.Equal(2, SnapshotDecoder.Decode(compressed, new() { MaxSnapshotNodes = 7 }).Count);
        Assert.Throws<InvalidDataException>(() => SnapshotDecoder.Decode(compressed, new() { MaxSnapshotNodes = 6 }));
    }

    [Theory]
    [InlineData(false, "bytes", NetworkExecution.Inline)]
    [InlineData(true, "bytes", NetworkExecution.Inline)]
    [InlineData(false, "bytes", NetworkExecution.DedicatedThread)]
    [InlineData(true, "bytes", NetworkExecution.DedicatedThread)]
    [InlineData(false, "depth", NetworkExecution.Inline)]
    [InlineData(true, "depth", NetworkExecution.Inline)]
    [InlineData(false, "nodes", NetworkExecution.Inline)]
    [InlineData(true, "nodes", NetworkExecution.Inline)]
    [InlineData(false, "length", NetworkExecution.Inline)]
    [InlineData(true, "length", NetworkExecution.Inline)]
    [InlineData(false, "zlib", NetworkExecution.Inline)]
    [InlineData(true, "zlib", NetworkExecution.Inline)]
    public void InvalidCompressedSnapshotsEndSessionsBeforeCreatingScenes(bool spawn, string kind, NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        // The payload expands to far more than this budget while remaining tiny on the wire.
        Networking.Networking.Configure(new() { Execution = execution, MaxSnapshotDecompressedBytes = 1024,
            MaxSnapshotDepth = 8, MaxSnapshotNodes = 256 });
        Scene original = CreateScene();
        MakeCurrent(original);
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(original, snapshot);
        byte[] malformed = CompressedEcho(w =>
        {
            switch (kind)
            {
                case "bytes":
                    w.Write((byte)EchoType.ByteArray); w.Write(1_000_000); w.Write(new byte[1_000_000]); break;
                case "depth":
                    for (int i = 0; i < 9; i++) { w.Write((byte)EchoType.List); w.Write(1); }
                    w.Write((byte)EchoType.Null); break;
                case "nodes": w.Write((byte)EchoType.Compound); w.Write(257); break;
                default: w.Write((byte)EchoType.String); w.Write(int.MaxValue); break;
            }
        });
        if (kind == "zlib") malformed = [0, 0, 0, 0];
        Assert.True(malformed.Length < 16 * 1024);
        HashSet<Scene> liveBefore = Scene.Live.ToHashSet();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SessionEndReason? ended = null;
        host.Session.PeerJoined += peer =>
        {
            if (spawn) host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
            host.Send(spawn ? FakePeer.Spawn : FakePeer.SceneSnapshot, w =>
            {
                if (spawn)
                {
                    w.WriteGuid(Guid.NewGuid()); w.WriteGuid(Guid.Empty); w.WriteVarUInt32(0);
                    w.WriteByte((byte)NetworkOrphaned.Destroy); w.WriteUInt32(1);
                }
                else
                {
                    w.WriteByte(8); w.WriteUInt64(1); w.WriteString("Oversized");
                }
                w.WriteBytesWithLength(malformed);
            }, peer);
        };
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            session.Ended += (reason, _) => ended = reason;
            Pump(() => Networking.Networking.Session == null, host);
            Assert.Equal(SessionEndReason.ProtocolError, ended);
            Assert.Same(original, Scene.Current);
            Assert.False(Scene.IsLoadPending);
            Assert.All(Scene.Live, scene => Assert.Contains(scene, liveBefore));
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Theory]
    [InlineData("depth")]
    [InlineData("nodes")]
    [InlineData("key")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    [InlineData("unknown")]
    public void SnapshotPreflightRejectsMalformedTreesAsProtocolErrors(string kind)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { MaxSnapshotDepth = 8, MaxSnapshotNodes = 32 });
        byte[] compressed = CompressedEcho(w =>
        {
            switch (kind)
            {
                case "depth":
                    for (int i = 0; i < 1000; i++) { w.Write((byte)EchoType.List); w.Write(1); }
                    w.Write((byte)EchoType.Null); break;
                case "nodes": w.Write((byte)EchoType.List); w.Write(33); break;
                case "key":
                    w.Write((byte)EchoType.Compound); w.Write(1); w.Write(int.MaxValue); w.Write((byte)EchoType.Null); break;
                case "truncated": w.Write((byte)EchoType.Long); w.Write((byte)0); break;
                case "trailing": w.Write((byte)EchoType.Null); w.Write((byte)EchoType.Null); break;
                default: w.Write(byte.MaxValue); break;
            }
        });
        try
        {
            Assert.Throws<InvalidDataException>(() => Networking.Networking.Decompress(compressed));
        }
        finally { Networking.Networking.Configure(previous); }
    }
}
