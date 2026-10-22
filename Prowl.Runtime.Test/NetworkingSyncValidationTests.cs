using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static void WriteSyncRecord(NetWriter writer, Component component, uint slot, ReadOnlySpan<byte> value, uint? generation = null)
    {
        writer.WriteGuid(component.Identifier);
        writer.WriteVarUInt32(slot);
        writer.WriteUInt32(generation ?? Networking.Networking.GenerationOf(component.GameObject));
        writer.WriteBytesWithLength(value);
    }

    private static byte[] SyncWithMalformedTail(int origin, NetCollectionTestComponent component, string kind)
    {
        using NetWriter message = new();
        message.WriteInt32(origin); message.WriteUInt64(100);
        message.WriteVarUInt32(kind == "trailing-block" ? 2u : 3u);
        using NetWriter health = new(); health.WriteVarInt32(42);
        WriteSyncRecord(message, component, NetCollectionTestComponent.HealthSlot, health.WrittenSpan);
        byte[] add = ListChange(0, w => { w.WriteByte((byte)NetChangeKind.Added); w.WriteVarUInt32(0); w.WriteVarInt32(7); });
        WriteSyncRecord(message, component, NetCollectionTestComponent.ScoresSlot, add);
        if (kind == "trailing-block") message.WriteByte(255);
        else if (kind == "truncated-record") message.WriteGuid(component.Identifier);
        else if (kind == "collection")
        {
            // A valid dictionary addition followed by an invalid operation.
            using NetWriter dictionary = new();
            dictionary.WriteByte(1); dictionary.WriteVarUInt32(0); dictionary.WriteVarUInt32(2);
            dictionary.WriteByte((byte)NetChangeKind.Added); dictionary.WriteString("x"); dictionary.WriteVarInt32(1);
            dictionary.WriteByte(255);
            WriteSyncRecord(message, component, NetCollectionTestComponent.StatsSlot, dictionary.WrittenSpan);
        }
        else
        {
            using NetWriter loadout = new();
            loadout.WriteVarInt32(10); // Ammo, with the weapon missing unless testing trailing bytes.
            if (kind == "trailing-value") { loadout.WriteString("Bow"); loadout.WriteByte(255); }
            WriteSyncRecord(message, component, NetCollectionTestComponent.LoadoutSlot, loadout.WrittenSpan);
        }
        return message.ToArray();
    }

    [Theory]
    [InlineData("scalar", NetworkExecution.Inline)]
    [InlineData("collection", NetworkExecution.Inline)]
    [InlineData("truncated-record", NetworkExecution.Inline)]
    [InlineData("trailing-value", NetworkExecution.Inline)]
    [InlineData("trailing-block", NetworkExecution.Inline)]
    [InlineData("scalar", NetworkExecution.DedicatedThread)]
    [InlineData("collection", NetworkExecution.DedicatedThread)]
    public void MalformedClientSyncLeavesAllStateAndCallbacksUnchanged(string kind, NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        Scene scene = CreateScene(); MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer owner = FakePeer.Join(network, "owner"), observer = FakePeer.Join(network, "observer");
        try
        {
            Pump(() => owner.IsReady && observer.IsReady, owner, observer);
            NetPeer peer = Networking.Networking.Session!.Peers.Single(p => p.Name == "owner");
            GameObject root = CreateGameObject();
            NetCollectionTestComponent component = root.AddComponent<NetCollectionTestComponent>();
            scene.Add(root); root.Network.Spawn(peer, NetworkOrphaned.ClearOwner);
            Pump(() => owner.Has(FakePeer.Spawn) && observer.Has(FakePeer.Spawn), owner, observer);
            component.ClearSeen(); observer.Received.Clear();
            SessionEndReason? reason = null;
            Networking.Networking.Session.PeerLeft += (left, why) => { if (left == peer) reason = why; };
            owner.Session.SendRaw(owner.Session.HostPeer!, FakePeer.Sync, SyncWithMalformedTail(peer.Id, component, kind));
            Pump(() => reason.HasValue, owner, observer);
            Assert.Equal(SessionEndReason.ProtocolError, reason);
            Assert.Equal(100, component.Health);
            Assert.Empty(component.Scores);
            Assert.Empty(component.Stats);
            Assert.Empty(component.HealthChanges);
            Assert.Empty(component.ScoreChanges);
            Assert.Empty(component.StatChanges);
            Assert.False(observer.Has(FakePeer.Sync));
            Assert.True(root.IsValid());
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Theory]
    [InlineData("scalar", NetworkExecution.Inline)]
    [InlineData("collection", NetworkExecution.Inline)]
    [InlineData("trailing-block", NetworkExecution.Inline)]
    [InlineData("scalar", NetworkExecution.DedicatedThread)]
    [InlineData("collection", NetworkExecution.DedicatedThread)]
    public void MalformedHostSyncLeavesClientValuesUnchanged(string kind, NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        Scene source = CreateScene(); GameObject root = CreateGameObject();
        root.AddComponent<NetCollectionTestComponent>();
        source.Add(root);
        using NetWriter snapshot = new(); Networking.Networking.WriteSnapshot(source, snapshot);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            SessionEndReason? reason = null;
            session.Ended += (why, _) => reason = why;
            Pump(() => Networking.Networking.IsReady, host);
            NetCollectionTestComponent component = Find(Scene.Current, root.Identifier).GetComponent<NetCollectionTestComponent>()!;
            component.ClearSeen();
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Sync, SyncWithMalformedTail(NetPeer.HostId, component, kind));
            Pump(() => reason.HasValue, host);
            Assert.Equal(SessionEndReason.ProtocolError, reason);
            Assert.Equal(100, component.Health);
            Assert.Empty(component.Scores);
            Assert.Empty(component.Stats);
            Assert.Empty(component.HealthChanges);
            Assert.Empty(component.ScoreChanges);
            Assert.Empty(component.StatChanges);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidInitialCollectionStateDoesNotPartiallyApplyCatchUp(bool rpcPayload)
    {
        Scene source = CreateScene(); GameObject root = CreateGameObject();
        NetCollectionTestComponent original = root.AddComponent<NetCollectionTestComponent>();
        original.Scores.Add(9); source.Add(root);
        using NetWriter snapshot = new(); Networking.Networking.WriteSnapshot(source, snapshot);
        NetReader header = new(snapshot.WrittenSpan); header.ReadByte(); ulong generation = header.ReadUInt64();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            SessionEndReason? reason = null;
            session.Ended += (why, _) => reason = why;
            Pump(() => Networking.Networking.JoinState == NetworkJoinState.Synchronizing && host.Has(FakePeer.SceneLoaded), host);
            NetCollectionTestComponent component = Find(Scene.Current, root.Identifier).GetComponent<NetCollectionTestComponent>()!;
            using NetWriter collection = new();
            if (rpcPayload) original.Scores.WriteRpc(collection);
            else original.Scores.WriteChanges(collection);
            host.Send(FakePeer.SceneCatchUp, w =>
            {
                w.WriteUInt64(generation); w.WriteVarUInt32(2);
                using NetWriter health = new(); health.WriteVarInt32(42);
                WriteSyncRecord(w, component, NetCollectionTestComponent.HealthSlot, health.WrittenSpan);
                WriteSyncRecord(w, component, NetCollectionTestComponent.ScoresSlot, collection.WrittenSpan);
                w.WriteVarUInt32(0); // No transform records.
            }, host.Session.Peers.Single(p => !p.IsLocal));
            Pump(() => reason.HasValue, host);
            Assert.Equal(SessionEndReason.ProtocolError, reason);
            Assert.Equal(100, component.Health);
            Assert.Equal(new[] { 9 }, component.Scores);
            Assert.Empty(component.HealthChanges);
            Assert.Empty(component.ScoreChanges);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("unauthorized")]
    [InlineData("stale")]
    [InlineData("duplicate")]
    public void ValidSyncSkipsRejectedValuesAndNotifiesOnceAfterTheWholeBlock(string skipped)
    {
        Scene scene = CreateScene(); MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer owner = FakePeer.Join(network, "owner"), observer = FakePeer.Join(network, "observer");
        try
        {
            Pump(() => owner.IsReady && observer.IsReady, owner, observer);
            NetPeer peer = Networking.Networking.Session!.Peers.Single(p => p.Name == "owner");
            GameObject root = CreateGameObject();
            NetCollectionTestComponent component = root.AddComponent<NetCollectionTestComponent>();
            scene.Add(root); root.Network.Spawn(peer);
            GameObject foreign = CreateGameObject();
            NetCollectionTestComponent unauthorized = foreign.AddComponent<NetCollectionTestComponent>();
            scene.Add(foreign); foreign.Network.Spawn();
            Pump(() => owner.Received.Count(m => m.Id == FakePeer.Spawn) == 2, owner, observer);
            component.ClearSeen(); observer.Received.Clear();
            List<int> healthAtScoreCallback = new();
            component.Scores.Changed += _ => healthAtScoreCallback.Add(component.Health);
            using NetWriter message = new();
            message.WriteInt32(peer.Id); message.WriteUInt64(100); message.WriteVarUInt32(3);
            byte[] scores = ListChange(0, w => { w.WriteByte((byte)NetChangeKind.Added); w.WriteVarUInt32(0); w.WriteVarInt32(7); });
            WriteSyncRecord(message, component, NetCollectionTestComponent.ScoresSlot, scores);
            using NetWriter health = new(); health.WriteVarInt32(42);
            WriteSyncRecord(message, component, NetCollectionTestComponent.HealthSlot, health.WrittenSpan);
            WriteSyncRecord(message, skipped == "unauthorized" ? unauthorized : component,
                skipped == "unknown" ? uint.MaxValue : NetCollectionTestComponent.HealthSlot,
                new byte[] { 0x80 }, skipped == "stale" ? Networking.Networking.GenerationOf(root) - 1 : null);
            owner.Session.SendRaw(owner.Session.HostPeer!, FakePeer.Sync, message.WrittenSpan);
            Pump(() => component.Health == 42 && observer.Has(FakePeer.Sync), owner, observer);
            Assert.Equal(SessionState.Active, owner.Session.State);
            Assert.Equal(new[] { 7 }, component.Scores);
            Assert.Equal(new[] { (100, 42) }, component.HealthChanges);
            Assert.Single(component.ScoreChanges);
            Assert.Equal(new[] { 42 }, healthAtScoreCallback);
            Assert.Equal(100, unauthorized.Health);
            NetReader relayed = new(observer.Last(FakePeer.Sync));
            Assert.Equal(2u, relayed.ReadVarUInt32());
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
