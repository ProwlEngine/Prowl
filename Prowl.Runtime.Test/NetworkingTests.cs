// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exactly what Prowl's script compiler generates for
/// <code>
/// [Sync] public int Health { get; set; } = 100;
/// [Sync(SyncFlags.FromHost)] public int Score { get; set; }
/// [Rpc.Broadcast] public void Shout(string text) => Shouts.Add((text, Rpc.Caller));
/// [Rpc.Host] public void AskHost(int value) => Asked.Add(value);
/// [Rpc.Host] public void Explode() => throw new InvalidOperationException("boom");
/// </code>
/// written out by hand, since runtime tests don't go through the script compiler.
/// </summary>
public class NetTestComponent : Component
{
    public const uint HealthSlot = 101, ScoreSlot = 102, ShoutId = 201, AskHostId = 202, ExplodeId = 203;

    public readonly List<(string Text, NetPeer? Caller)> Shouts = new();
    public readonly List<int> Asked = new();

    private int __sync_Health = 100;
    private static readonly SyncMember<NetTestComponent, int> __syncnet_Health = new(HealthSlot, "Health", SyncFlags.None, static s => s.__sync_Health, static (s, v) => s.__sync_Health = v);
    public int Health { get => __sync_Health; set => __syncnet_Health.Set(this, ref __sync_Health, value); }

    private int __sync_Score;
    private static readonly SyncMember<NetTestComponent, int> __syncnet_Score = new(ScoreSlot, "Score", SyncFlags.FromHost, static s => s.__sync_Score, static (s, v) => s.__sync_Score = v);
    public int Score { get => __sync_Score; set => __syncnet_Score.Set(this, ref __sync_Score, value); }

    private static readonly RpcMember<NetTestComponent> __rpc_201 = new(ShoutId, "Shout(string)", RpcMode.Broadcast, RpcFlags.None,
        static (NetTestComponent s, ref NetReader r, bool execute) => { string a0 = NetSerializer<string>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Shout(a0); });

    public void Shout(string text)
    {
        if (__rpc_201.Begin(this, out NetWriter w)) { NetSerializer<string>.Write(w, text); if (!__rpc_201.Send(this, w)) return; }
        Shouts.Add((text, Rpc.Caller));
    }

    private static readonly RpcMember<NetTestComponent> __rpc_204 = new(204, "TellOwner(string)", RpcMode.Owner, RpcFlags.None,
        static (NetTestComponent s, ref NetReader r, bool execute) => { string value = NetSerializer<string>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.TellOwner(value); });

    public void TellOwner(string value)
    {
        if (__rpc_204.Begin(this, out NetWriter w)) { w.WriteString(value); if (!__rpc_204.Send(this, w)) return; }
        Shouts.Add((value, Rpc.Caller));
    }

    private static readonly RpcMember<NetTestComponent> __rpc_202 = new(AskHostId, "AskHost(int)", RpcMode.Host, RpcFlags.None,
        static (NetTestComponent s, ref NetReader r, bool execute) => { int a0 = NetSerializer<int>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.AskHost(a0); });

    public void AskHost(int value)
    {
        if (__rpc_202.Begin(this, out NetWriter w)) { NetSerializer<int>.Write(w, value); if (!__rpc_202.Send(this, w)) return; }
        Asked.Add(value);
    }

    private static readonly RpcMember<NetTestComponent> __rpc_203 = new(ExplodeId, "Explode()", RpcMode.Host, RpcFlags.None,
        static (NetTestComponent s, ref NetReader r, bool execute) => { if (!execute) return; RpcMember.ArgumentsRead(); s.Explode(); });

    public void Explode()
    {
        if (__rpc_203.Begin(this, out NetWriter w)) { if (!__rpc_203.Send(this, w)) return; }
        throw new InvalidOperationException("boom");
    }
}

/// <summary>
/// The other end of a session, as a bare <see cref="NetSession"/> that records the engine's messages
/// and can send hand-made ones. <see cref="Networking"/> runs one session per process, so each test
/// drives one side of the protocol for real and plays the other with this.
/// </summary>
public sealed class FakePeer : IDisposable
{
    public const ushort SceneSnapshot = Networking.Networking.EngineMessageIds, Spawn = SceneSnapshot + 1, Despawn = SceneSnapshot + 2,
        Owner = SceneSnapshot + 3, Sync = SceneSnapshot + 4, Transform = SceneSnapshot + 5, RpcToHost = SceneSnapshot + 6, RpcFromHost = SceneSnapshot + 7, SceneLoaded = SceneSnapshot + 8, SceneCatchUp = SceneSnapshot + 9,
        SceneReady = SceneSnapshot + 10, SceneActivate = SceneSnapshot + 11, StaticRpcToHost = SceneSnapshot + 12, StaticRpcFromHost = SceneSnapshot + 13,
        Orphaned = SceneSnapshot + 14, OwnershipRequest = SceneSnapshot + 15, OwnershipState = SceneSnapshot + 16,
        OwnerTransfer = SceneSnapshot + 17, NetworkFlags = SceneSnapshot + 18;

    private FakePeer(NetSession session)
    {
        Session = session;
        for (ushort id = SceneSnapshot; id <= NetworkFlags; id++)
        {
            ushort captured = id;
            session.OnRaw(id, (NetPeer sender, ref NetReader reader, Delivery delivery) =>
            {
                NetReader wire = reader;
                WireReceived.Add((captured, wire.ReadBytes(wire.Remaining).ToArray()));
                Deliveries.Add((captured, delivery));
                Received.Add((captured, ReadEnginePayload(captured, ref reader)));
                if (AutoReady) HandleReadiness(sender, captured, WireReceived[^1].Data);
            });
        }
    }

    public bool AutoReady { get; set; } = true;
    public ulong SceneGeneration { get; private set; }
    public bool IsReady { get; private set; }

    private void HandleReadiness(NetPeer sender, ushort kind, byte[] payload)
    {
        NetReader reader = new(payload);
        if (kind == SceneSnapshot && !Session.IsHost)
        {
            reader.ReadByte();
            SceneGeneration = reader.ReadUInt64();
            IsReady = false;
            Send(SceneLoaded, w => w.WriteUInt64(SceneGeneration));
        }
        else if (kind == SceneCatchUp && !Session.IsHost)
        {
            ulong generation = reader.ReadUInt64();
            Send(SceneReady, w => w.WriteUInt64(generation));
        }
        else if (kind == SceneActivate && !Session.IsHost)
            IsReady = reader.ReadUInt64() == SceneGeneration;
        else if (kind == SceneLoaded && Session.IsHost)
        {
            ulong generation = reader.ReadUInt64();
            Send(SceneCatchUp, w => { w.WriteUInt64(generation); w.WriteVarUInt32(0); w.WriteVarUInt32(0); }, sender);
        }
        else if (kind == SceneReady && Session.IsHost)
        {
            ulong generation = reader.ReadUInt64();
            Send(SceneActivate, w => w.WriteUInt64(generation), sender);
        }
    }


    public NetSession Session { get; }

    public List<(ushort Id, byte[] Data)> Received { get; } = new();
    public List<(ushort Id, byte[] Data)> WireReceived { get; } = new();
    public List<(ushort Id, Delivery Delivery)> Deliveries { get; } = new();

    public static FakePeer Join(LocalNetwork network, string name) =>
        new(NetSession.Join(new LocalTransport(network, name), "host", name));

    public static FakePeer Host(LocalNetwork network) => new(NetSession.Host(new LocalTransport(network, "host"), "FakeHost"));

    private static byte[] ReadEnginePayload(ushort id, ref NetReader reader)
    {
        if (id == Sync)
        {
            reader.ReadInt32();
            using NetWriter logical = new();
            uint count = reader.ReadVarUInt32(); logical.WriteVarUInt32(count);
            for (uint i = 0; i < count; i++)
            {
                logical.WriteGuid(reader.ReadGuid()); logical.WriteVarUInt32(reader.ReadVarUInt32());
                reader.ReadUInt32(); logical.WriteBytesWithLength(reader.ReadBytesWithLength());
            }
            return logical.ToArray();
        }
        if (id == RpcFromHost)
        {
            using NetWriter logical = new();
            logical.WriteVarUInt32(reader.ReadVarUInt32());
            logical.WriteGuid(reader.ReadGuid());
            logical.WriteUInt32(reader.ReadUInt32());
            logical.WriteBytes(reader.ReadBytesWithLength());
            return logical.ToArray();
        }
        return reader.ReadBytes(reader.Remaining).ToArray();
    }

    public bool Has(ushort id) => Received.Any(m => m.Id == id) &&
        (id != SceneSnapshot || Session.IsHost || !AutoReady || IsReady);

    public byte[] Last(ushort id) => Received.Last(m => m.Id == id).Data;

    public void Send(ushort id, Action<NetWriter> write, NetPeer? to = null)
    {
        using NetWriter writer = new();
        write(writer);
        using NetWriter wire = new();
        if (id == Sync)
        {
            wire.WriteInt32(Session.LocalPeer!.Id);
        }
        if (id == RpcToHost || id == RpcFromHost)
        {
            NetReader arguments = new(writer.WrittenSpan);
            if (id == RpcFromHost) wire.WriteVarUInt32(arguments.ReadVarUInt32());
            wire.WriteGuid(arguments.ReadGuid());
            wire.WriteUInt32(arguments.ReadUInt32());
            wire.WriteBytesWithLength(arguments.ReadBytes(arguments.Remaining));
        }
        else if (id == Sync)
        {
            NetReader block = new(writer.WrittenSpan);
            uint count = block.ReadVarUInt32(); wire.WriteVarUInt32(count);
            for (uint i = 0; i < count; i++)
            {
                Guid componentId = block.ReadGuid(); wire.WriteGuid(componentId);
                wire.WriteVarUInt32(block.ReadVarUInt32());
                uint generation = Networking.Networking.FindByIdentifier(componentId) is Component part
                    ? Networking.Networking.GenerationOf(part.GameObject) : 0;
                wire.WriteUInt32(generation);
                wire.WriteBytesWithLength(block.ReadBytesWithLength());
            }
        }
        else wire.WriteBytes(writer.WrittenSpan);
        Session.SendRaw(to ?? Session.HostPeer!, id, wire.WrittenSpan);
        Session.Flush();
    }

    public void SendSync(Guid component, uint slot, int value) => Send(Sync, w =>
    {
        w.WriteVarUInt32(1);
        w.WriteGuid(component);
        w.WriteVarUInt32(slot);
        using NetWriter v = new();
        NetSerializer<int>.Write(v, value);
        w.WriteBytesWithLength(v.WrittenSpan);
    });

    public void Dispose() => Session.Dispose();
}

public partial class NetworkingTests : RuntimeTestBase
{
    private static Scene RoundTrip(Scene scene)
    {
        using NetWriter writer = new();
        Networking.Networking.WriteSnapshot(scene, writer);
        NetReader reader = new(writer.WrittenSpan);
        return Networking.Networking.ReadSnapshot(ref reader);
    }

    private static GameObject Find(Scene scene, Guid id) => scene.AllObjects.Single(o => o.Identifier == id);

    private static GameObject Find(Scene scene, string name) => scene.AllObjects.Single(o => o.Name == name);

    /// <summary>Runs frames, the way the game loop does, until <paramref name="condition"/> holds.</summary>
    private static void Pump(Func<bool> condition, params FakePeer[] others)
    {
        for (int i = 0; i < 1000 && !condition(); i++)
            Frame(others);
        Assert.True(condition(), "The expected network state never arrived.");
    }

    private static void PumpFor(int milliseconds, params FakePeer[] others)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
            Frame(others);
    }

    private static void Frame(FakePeer[] others)
    {
        Networking.Networking.Receive();
        Networking.Networking.Flush();
        foreach (FakePeer other in others)
            other.Session.Update();
        Scene.ProcessPendingLoad();
        EngineObject.ProcessDestroyed();
        Thread.Sleep(1);
    }

    private void MakeCurrent(Scene scene)
    {
        Scene.Load(scene);
        Scene.ProcessPendingLoad();
    }

    // --- Scene sharing ---------------------------------------------------------------------------

    [Fact]
    public void SnapshotSerializationIsRecordedOutsideTheFramePump()
    {
        Scene scene = CreateScene();
        scene.Add(CreateGameObject("Snapshot object"));

        FrameStats.Start();
        using Scene received = RoundTrip(scene);
        FrameStats.EndFrame();

        Assert.Equal(2, FrameStats.Network.GetSample(0).Calls);
        Assert.True(FrameStats.Network.GetSample(0).Ms > 0);
        Assert.Contains(received.AllObjects, o => o.Name == "Snapshot object");
    }

    [Fact]
    public void NetworkModeIsOnlyStoredWhenSet()
    {
        GameObject go = CreateGameObject();
        EchoObject plain = Serializer.Serialize(go);
        Assert.False(plain.TryGet("Network", out _));

        go.Network.Mode = NetworkMode.Object;
        go.Network.Orphaned = NetworkOrphaned.ClearOwner;
        go.Network.Flags = NetworkFlags.NoTransformSync;
        EchoObject stored = Serializer.Serialize(go);
        Assert.Equal((byte)NetworkMode.Object, stored["Network"]["Mode"].ByteValue);

        GameObject copy = Serializer.Deserialize<GameObject>(stored)!;
        Assert.Equal(NetworkMode.Object, copy.Network.Mode);
        Assert.Equal(NetworkOrphaned.ClearOwner, copy.Network.Orphaned);
        Assert.Equal(NetworkFlags.NoTransformSync, copy.Network.Flags);
        copy.Dispose();

        GameObject fresh = Serializer.Deserialize<GameObject>(plain)!;
        Assert.Equal(NetworkFlags.None, fresh.Network.Flags);
        fresh.Dispose();
    }

    [Fact]
    public void SnapshotLeavesOutNeverObjectsAndKeepsIdentifiers()
    {
        Scene scene = CreateScene();
        GameObject level = CreateGameObject("Level");
        GameObject prop = CreateGameObject("Prop");
        GameObject secret = CreateGameObject("Secret");
        prop.SetParent(level);
        secret.SetParent(level);
        secret.Network.Mode = NetworkMode.Never;

        GameObject hostOnly = CreateGameObject("HostOnly");
        GameObject hostChild = CreateGameObject("HostChild");
        hostChild.SetParent(hostOnly);
        hostOnly.Network.Mode = NetworkMode.Never;

        scene.Add(level);
        scene.Add(hostOnly);

        Scene received = RoundTrip(scene);
        try
        {
            Assert.Equal(new[] { "Level", "Prop" }, received.AllObjects.Select(o => o.Name).OrderBy(n => n));
            Assert.Equal(level.Identifier, Find(received, "Level").Identifier);
            Assert.Equal(prop.Identifier, Find(received, "Prop").Identifier);
            Assert.Equal(Find(received, "Level"), Find(received, "Prop").Parent);

            // Only the snapshot leaves them out; an ordinary save still has everything.
            Assert.False(NetworkSerialization.WritingSnapshot);
            Scene saved = Serializer.Deserialize<Scene>(Serializer.Serialize(scene))!;
            Assert.Equal(5, saved.AllObjects.Count());
            saved.Dispose();
        }
        finally
        {
            received.Dispose();
        }
    }

    public class ReferenceHolder : Component
    {
        public GameObject? Target;
        public Component? Part;
        public Transform? Pose;
    }

    public class SecretHolder : Component { public string Value = ""; }

    private const string HostSecret = "host-only-7f3a";

    private GameObject Secret(string name)
    {
        GameObject secret = CreateGameObject(name);
        secret.Network.Mode = NetworkMode.Never;
        secret.AddComponent<SecretHolder>().Value = HostSecret;
        return secret;
    }

    private static ReferenceHolder ReferTo(GameObject holder, GameObject target)
    {
        ReferenceHolder refs = holder.AddComponent<ReferenceHolder>();
        refs.Target = target;
        refs.Part = target.GetComponent<SecretHolder>();
        refs.Pose = target.Transform;
        return refs;
    }

    [Fact]
    public void SnapshotDoesNotCarryNeverObjectsThroughReferences()
    {
        Scene scene = CreateScene();
        GameObject level = CreateGameObject("Level");
        GameObject secret = Secret("Secret");
        ReferTo(level, secret);
        scene.Add(level); scene.Add(secret);

        using NetWriter writer = new();
        Networking.Networking.WriteSnapshot(scene, writer);
        NetReader header = new(writer.WrittenSpan);
        header.ReadByte(); header.ReadUInt64(); header.ReadString();
        Assert.DoesNotContain(HostSecret, Networking.Networking.Decompress(header.ReadBytesWithLength()).WriteToJson());

        using Scene received = RoundTrip(scene);
        Assert.Equal(new[] { "Level" }, received.AllObjects.Select(o => o.Name));
        ReferenceHolder refs = Find(received, "Level").GetComponent<ReferenceHolder>()!;
        Assert.Null(refs.Target);
        Assert.Null(refs.Part);
        Assert.Null(refs.Pose);
    }

    [Fact]
    public void SpawnDoesNotCarryNeverObjectsThroughReferences()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Root");
        root.Network.Mode = NetworkMode.Object;
        GameObject inside = Secret("Inside");
        inside.SetParent(root);
        GameObject outside = Secret("Outside");
        ReferTo(root, inside);
        ReferTo(root, outside);
        scene.Add(root); scene.Add(outside);

        using NetWriter writer = new();
        Networking.Networking.WriteSpawn(root, -1, NetworkOrphaned.Destroy, writer);
        NetReader spawn = new(writer.WrittenSpan);
        spawn.ReadGuid(); spawn.ReadGuid(); spawn.ReadVarUInt32(); spawn.ReadByte(); spawn.ReadUInt32();
        string json = Networking.Networking.Decompress(spawn.ReadBytesWithLength()).WriteToJson();
        Assert.DoesNotContain(HostSecret, json);
        Assert.DoesNotContain(inside.Identifier.ToString(), json);
        Assert.DoesNotContain(outside.Identifier.ToString(), json);
    }

    [Fact]
    public void JoiningClientsGetTheHostSceneAndEverySceneChange()
    {
        Scene first = CreateScene();
        GameObject level = CreateGameObject("Level");
        first.Add(level);
        MakeCurrent(first);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            NetReader reader = new(alice.Last(FakePeer.SceneSnapshot));
            using (Scene received = Networking.Networking.ReadSnapshot(ref reader))
                Assert.Equal(level.Identifier, Find(received, "Level").Identifier);

            Scene second = CreateScene();
            second.Add(CreateGameObject("Arena"));
            FrameStats.Start();
            MakeCurrent(second);
            FrameStats.EndFrame();
            Assert.True(FrameStats.Network.GetSample(0).Calls > 0);
            Assert.True(FrameStats.Network.GetSample(0).Ms > 0);

            Pump(() => alice.Received.Count(m => m.Id == FakePeer.SceneSnapshot) == 2, alice);
            reader = new NetReader(alice.Last(FakePeer.SceneSnapshot));
            using (Scene received = Networking.Networking.ReadSnapshot(ref reader))
                Assert.Contains(received.AllObjects, o => o.Name == "Arena");
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    // --- Host side -------------------------------------------------------------------------------

    [Fact]
    public void HostSpawnsAndFollowsClientOwnedObjects()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);

            GameObject ship = CreateGameObject("Ship");
            NetTestComponent component = ship.AddComponent<NetTestComponent>();
            scene.Add(ship);
            ship.Network.Spawn(aliceHere);

            Assert.Equal(NetworkMode.Object, ship.Network.Mode);
            Assert.Same(aliceHere, ship.Network.Owner);
            Assert.True(ship.IsProxy && component.IsProxy);

            // Alice drives it, so the host's own changes are ignored, except for what only the host may set.
            component.Health = 1;
            component.Score = 5;
            Assert.Equal(100, component.Health);
            Assert.Equal(5, component.Score);

            Pump(() => alice.Has(FakePeer.Spawn) && alice.Has(FakePeer.Sync), alice);
            NetReader spawn = new(alice.Last(FakePeer.Spawn));
            Assert.Equal(ship.Identifier, spawn.ReadGuid());

            // Alice's changes land; she can't touch the host-only one.
            alice.SendSync(component.Identifier, NetTestComponent.HealthSlot, 42);
            alice.SendSync(component.Identifier, NetTestComponent.ScoreSlot, 99);
            Pump(() => component.Health == 42, alice);
            PumpFor(30, alice);
            Assert.Equal(5, component.Score);

            // Her transform updates move it on the host, smoothed.
            alice.Send(FakePeer.Transform, w =>
            {
                w.WriteGuid(ship.Identifier);
                NetReader spawned = new(alice.Last(FakePeer.Spawn));
                spawned.ReadGuid(); spawned.ReadGuid(); spawned.ReadVarUInt32(); spawned.ReadByte();
                w.WriteUInt32(spawned.ReadUInt32());
                w.WriteUInt32(1);
                w.WriteDouble(1);
                w.WriteSingle(3); w.WriteSingle(4); w.WriteSingle(5);
                w.WriteSingle(0); w.WriteSingle(0); w.WriteSingle(0); w.WriteSingle(1);
                w.WriteSingle(1); w.WriteSingle(1); w.WriteSingle(1);
            });
            Pump(() => ship.Transform.LocalPosition.Equals(new Float3(3, 4, 5)), alice);

            // Her broadcast RPC runs here as hers; the host's runs here and goes to her.
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.ShoutId); w.WriteString("hello"); });
            Pump(() => component.Shouts.Count == 1, alice);
            Assert.Equal(("hello", aliceHere), component.Shouts[0]);

            component.Shout("from host");
            Assert.Equal("from host", component.Shouts[1].Text);
            Pump(() => alice.Has(FakePeer.RpcFromHost), alice);

            // When she leaves, what she owned goes with her.
            alice.Session.Shutdown();
            Pump(() => !ship.IsValid(), alice);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void HostOwnedObjectsReplicateToClients()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);

            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.Network.Spawn();
            Assert.False(crate.IsProxy);
            Assert.Null(crate.Network.Owner);

            component.Health = 7;
            crate.Transform.LocalPosition = new Float3(1, 2, 3);
            Pump(() => alice.Has(FakePeer.Sync) && alice.Has(FakePeer.Transform), alice);

            NetReader sync = new(alice.Last(FakePeer.Sync));
            Assert.Equal(1u, sync.ReadVarUInt32());
            Assert.Equal(component.Identifier, sync.ReadGuid());
            Assert.Equal(NetTestComponent.HealthSlot, sync.ReadVarUInt32());
            NetReader value = new(sync.ReadBytesWithLength());
            Assert.Equal(7, NetSerializer<int>.Read(ref value));

            // A client can't change what it doesn't own, nor call host-only things through a broadcast.
            alice.SendSync(component.Identifier, NetTestComponent.HealthSlot, 1);
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(3); });
            Pump(() => component.Asked.Count == 1, alice);
            Assert.Equal(7, component.Health);
            Assert.Equal(3, component.Asked[0]);

            crate.Destroy();
            Pump(() => alice.Has(FakePeer.Despawn), alice);
            NetReader despawn = new(alice.Last(FakePeer.Despawn));
            Assert.Equal(crate.Identifier, despawn.ReadGuid());
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void SpawningASceneObjectHandsItToTheOwner()
    {
        Scene scene = CreateScene();
        GameObject ship = CreateGameObject("Ship");
        ship.Network.Mode = NetworkMode.Object;
        scene.Add(ship);
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);

            ship.Network.Spawn(aliceHere, NetworkOrphaned.ClearOwner);

            Assert.Same(aliceHere, ship.Network.Owner);
            Assert.Equal(NetworkOrphaned.ClearOwner, ship.Network.Orphaned);
            Pump(() => alice.Has(FakePeer.Owner), alice);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void SpawningInsideANetworkedObjectThrowsAndChangesNothing()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        try
        {
            GameObject parent = CreateGameObject("Parent");
            GameObject child = CreateGameObject("Child");
            child.SetParent(parent);
            scene.Add(parent);
            parent.Network.Spawn();

            Assert.Throws<InvalidOperationException>(() => child.Network.Spawn(orphaned: NetworkOrphaned.ClearOwner));
            Assert.Equal(NetworkMode.Snapshot, child.Network.Mode);
            Assert.Equal(NetworkOrphaned.Destroy, child.Network.Orphaned);
            Assert.Same(parent, child.Network.Root);
        }
        finally
        {
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void AnRpcThatThrowsNeitherCrashesTheHostNorDropsTheCaller()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.Network.Spawn();

            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.ExplodeId); });
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(3); });
            Pump(() => component.Asked.Count == 1, alice);
            Assert.Equal(1, Networking.Networking.Session!.Peers.Count(p => !p.IsLocal));
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MalformedRpcsAreRejectedBeforeRelay(bool ownerRpc, bool trailingBytes)
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        using FakePeer bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot) && bob.Has(FakePeer.SceneSnapshot), alice, bob);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.Network.Spawn(Networking.Networking.Session!.Peers.Single(p => p.Name == "bob"));
            alice.Send(FakePeer.RpcToHost, w =>
            {
                w.WriteGuid(component.Identifier);
                w.WriteUInt32(ownerRpc ? 204u : NetTestComponent.ShoutId);
                if (trailingBytes) { w.WriteString("hello"); w.WriteByte(42); }
            });
            for (int i = 0; i < 10; i++) Frame([alice, bob]);
            Assert.False(bob.Has(FakePeer.RpcFromHost));
            Assert.Empty(component.Shouts);
            Assert.Equal(SessionState.Ended, alice.Session.State);
            Assert.Equal(SessionState.Active, bob.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private GameObject JoinTransformProxy(out FakePeer host, out NetPeer client)
    {
        Scene scene = CreateScene();
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(scene, snapshot);
        GameObject original = CreateGameObject("Proxy");
        original.Network.Mode = NetworkMode.Object;
        using NetWriter spawn = new();
        Networking.Networking.WriteSpawn(original, -1, NetworkOrphaned.ClearOwner, spawn);
        LocalNetwork network = new();
        host = FakePeer.Host(network);
        FakePeer remote = host;
        host.Session.PeerJoined += peer =>
        {
            remote.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            remote.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
        Pump(() => Networking.Networking.IsReady && Scene.Current.AllObjects.Any(o => o.Identifier == original.Identifier), remote);
        client = host.Session.Peers.Single(p => !p.IsLocal);
        return Find(Scene.Current, original.Identifier);
    }

    private static void SendProxyTransform(FakePeer host, NetPeer client, Guid id, uint tick, float position, uint generation = 0)
    {
        host.Send(FakePeer.Transform, w =>
        {
            w.WriteGuid(id); w.WriteUInt32(generation); w.WriteUInt32(tick); w.WriteDouble(tick / 30.0);
            w.WriteSingle(position); w.WriteSingle(0); w.WriteSingle(0);
            w.WriteSingle(0); w.WriteSingle(0); w.WriteSingle(0); w.WriteSingle(1);
            w.WriteSingle(1); w.WriteSingle(1); w.WriteSingle(1);
        }, client);
    }

    [Fact]
    public void DelayedTransformsCannotCrossOwnershipChanges()
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        TimeSpan delay = Networking.Networking.InterpolationDelay;
        using (host)
        try
        {
            Networking.Networking.InterpolationDelay = TimeSpan.Zero;
            host.Send(FakePeer.Owner, w => { w.WriteGuid(proxy.Identifier); w.WriteVarUInt32(3); w.WriteUInt32(1); }, client);
            SendProxyTransform(host, client, proxy.Identifier, 100, 100, generation: 1);
            Frame([host]);
            host.Send(FakePeer.Owner, w => { w.WriteGuid(proxy.Identifier); w.WriteVarUInt32(4); w.WriteUInt32(2); }, client);
            // The previous owner's packet arrives after the reliable ownership change.
            SendProxyTransform(host, client, proxy.Identifier, 101, 999, generation: 1);
            SendProxyTransform(host, client, proxy.Identifier, 1, 5, generation: 2);
            Frame([host]);
            Assert.Equal(5f, proxy.Transform.LocalPosition.X);
        }
        finally
        {
            Networking.Networking.InterpolationDelay = delay;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void HostRejectsAnOldGenerationWhenOwnershipReturnsToTheSamePeer()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        TimeSpan delay = Networking.Networking.InterpolationDelay;
        try
        {
            Networking.Networking.InterpolationDelay = TimeSpan.Zero;
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject ship = CreateGameObject("Ship");
            scene.Add(ship);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            ship.Network.Spawn(owner);
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            NetReader spawn = new(alice.Last(FakePeer.Spawn));
            spawn.ReadGuid(); spawn.ReadGuid(); spawn.ReadVarUInt32(); spawn.ReadByte();
            uint oldGeneration = spawn.ReadUInt32();
            ship.Network.AssignOwnership(null);
            ship.Network.AssignOwnership(owner);
            Pump(() => alice.Has(FakePeer.Owner), alice);
            NetReader change = new(alice.Last(FakePeer.Owner));
            change.ReadGuid(); change.ReadVarUInt32();
            uint generation = change.ReadUInt32();
            Assert.NotEqual(oldGeneration, generation);
            SendProxyTransform(alice, alice.Session.HostPeer!, ship.Identifier, 500, 999, oldGeneration);
            SendProxyTransform(alice, alice.Session.HostPeer!, ship.Identifier, 1, 5, generation);
            Frame([alice]);
            Assert.Equal(5f, ship.Transform.LocalPosition.X);
        }
        finally
        {
            Networking.Networking.InterpolationDelay = delay;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void BunchedTransformsKeepTheirSenderSpacing()
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            SendProxyTransform(host, client, proxy.Identifier, 1, 1);
            SendProxyTransform(host, client, proxy.Identifier, 2, 2);
            SendProxyTransform(host, client, proxy.Identifier, 3, 3);
            // Receive all three at once, before interpolation can discard samples.
            Networking.Networking.Receive();
            var entries = (System.Collections.IDictionary)typeof(Networking.Networking).GetField("s_entries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            object entry = entries[proxy.Identifier]!;
            var samples = (System.Collections.IEnumerable)entry.GetType().GetField("Samples")!.GetValue(entry)!;
            double[] times = samples.Cast<object>().Select(sample => (double)sample.GetType().GetProperty("Time")!.GetValue(sample)!).ToArray();
            Assert.Equal(3, times.Length);
            Assert.InRange(times[2] - times[0], 0.06, 0.07);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(float.NaN, false)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(0, true)]
    public void InvalidTransformsNeverReachTheScene(float position, bool zeroRotation)
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            host.Send(FakePeer.Transform, w =>
            {
                w.WriteGuid(proxy.Identifier); w.WriteUInt32(0); w.WriteUInt32(1); w.WriteDouble(1);
                w.WriteSingle(position); w.WriteSingle(0); w.WriteSingle(0);
                w.WriteSingle(0); w.WriteSingle(0); w.WriteSingle(0); w.WriteSingle(zeroRotation ? 0 : 1);
                w.WriteSingle(1); w.WriteSingle(1); w.WriteSingle(1);
            }, client);
            Frame([host]);
            Assert.False(Networking.Networking.IsActive);
            Assert.True(float.IsFinite(proxy.Transform.LocalPosition.X));
            Assert.Equal(Quaternion.Identity, proxy.Transform.LocalRotation);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    // --- Client side -----------------------------------------------------------------------------

    [Fact]
    public void ClientLoadsTheHostSceneSpawnsAndControlsWhatItOwns()
    {
        // What a host would send, made with the host side's own writers.
        Scene hostScene = CreateScene();
        GameObject level = CreateGameObject("Level");
        level.AddComponent<NetTestComponent>().Health = 3;
        hostScene.Add(level);
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(hostScene, snapshot);

        GameObject ship = CreateGameObject("Ship");
        ship.AddComponent<NetTestComponent>().Health = 9;
        ship.Network.Mode = NetworkMode.Object;
        using NetWriter shipSpawn = new();
        Networking.Networking.WriteSpawn(ship, owner: 1, NetworkOrphaned.Destroy, shipSpawn); // the first client is peer 1

        GameObject crate = CreateGameObject("Crate");
        crate.Network.Mode = NetworkMode.Object;
        using NetWriter crateSpawn = new();
        Networking.Networking.WriteSpawn(crate, owner: -1, NetworkOrphaned.Destroy, crateSpawn);

        Guid levelId = level.Identifier, shipId = ship.Identifier, crateId = crate.Identifier;

        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, shipSpawn.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, crateSpawn.WrittenSpan);
        };

        Networking.Networking.Join(new LocalTransport(network, "bob"), "host", "bob");
        try
        {
            Pump(() => Networking.Networking.IsReady && Scene.Current.AllObjects.Any(o => o.Identifier == crateId), host);

            // [Sync] values come with the snapshot and spawns, not from the scene data.
            NetTestComponent levelHere = Find(Scene.Current, levelId).GetComponent<NetTestComponent>()!;
            GameObject shipHere = Find(Scene.Current, shipId);
            NetTestComponent shipComponent = shipHere.GetComponent<NetTestComponent>()!;
            GameObject crateHere = Find(Scene.Current, crateId);
            Assert.Equal(3, levelHere.Health);
            Assert.Equal(9, shipComponent.Health);

            Assert.Same(Networking.Networking.Session!.LocalPeer, shipHere.Network.Owner);
            Assert.False(shipHere.IsProxy);
            Assert.True(crateHere.IsProxy);
            Assert.True(levelHere.IsProxy);

            // Ours to change: it goes to the host. The host's: ignored here.
            shipComponent.Health = 10;
            levelHere.Health = 50;
            Assert.Equal(3, levelHere.Health);
            Pump(() => host.Has(FakePeer.Sync), host);

            // A host RPC goes to the host instead of running here.
            shipComponent.AskHost(7);
            Assert.Empty(shipComponent.Asked);
            Pump(() => host.Has(FakePeer.RpcToHost), host);

            // The host moves the crate; it follows, smoothed.
            NetPeer bob = host.Session.Peers.Single(p => !p.IsLocal);
            host.Send(FakePeer.Transform, w =>
            {
                w.WriteGuid(crateId);
                w.WriteUInt32(0);
                w.WriteUInt32(1);
                w.WriteDouble(1);
                w.WriteSingle(7); w.WriteSingle(8); w.WriteSingle(9);
                w.WriteSingle(0); w.WriteSingle(0); w.WriteSingle(0); w.WriteSingle(1);
                w.WriteSingle(1); w.WriteSingle(1); w.WriteSingle(1);
            }, to: bob);
            Pump(() => crateHere.Transform.LocalPosition.Equals(new Float3(7, 8, 9)), host);

            // And removes it.
            host.Send(FakePeer.Despawn, w => w.WriteGuid(crateId), to: bob);
            Pump(() => !crateHere.IsValid(), host);
        }
        finally
        {
            Networking.Networking.Shutdown();
            host.Dispose();
        }
    }

    // --- Offline and lifecycle -------------------------------------------------------------------

    [Fact]
    public void WithoutASessionNetworkingStaysOutOfTheWay()
    {
        Assert.Null(Networking.Networking.Session);
        Networking.Networking.Receive();
        Networking.Networking.Flush();
        Networking.Networking.Shutdown();

        GameObject go = CreateGameObject();
        NetTestComponent component = go.AddComponent<NetTestComponent>();
        component.Health = 5;
        component.Shout("alone");
        go.Network.Spawn();

        Assert.Equal(5, component.Health);
        Assert.Equal("alone", component.Shouts.Single().Text);
        Assert.Null(component.Shouts.Single().Caller);
        Assert.False(go.IsProxy);
        Assert.Equal(NetworkMode.Object, go.Network.Mode);
    }

    [Fact]
    public void OnlyOneSessionRunsAtATime()
    {
        Networking.Networking.Host(new LocalTransport(new LocalNetwork()));
        try
        {
            Assert.True(Networking.Networking.IsHost && Networking.Networking.IsActive);
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.Host(new LocalTransport(new LocalNetwork())));
        }
        finally
        {
            Networking.Networking.Shutdown();
        }

        Assert.Null(Networking.Networking.Session);
        Assert.False(Networking.Networking.IsActive);
    }

    [Fact]
    public void ClientsCannotSpawn()
    {
        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        Networking.Networking.Join(new LocalTransport(network, "bob"), "host", "bob");
        try
        {
            Pump(() => Networking.Networking.IsActive, host);
            GameObject go = CreateGameObject();
            Scene.Current.Add(go);
            Assert.Throws<InvalidOperationException>(() => go.Network.Spawn());
        }
        finally
        {
            Networking.Networking.Shutdown();
            host.Dispose();
        }
    }

    [Theory]
    [InlineData(typeof(bool))]
    [InlineData(typeof(Float3))]
    [InlineData(typeof(Quaternion))]
    [InlineData(typeof(Color))]
    [InlineData(typeof(NetworkMode))]
    public void SerializerSupportsCommonTypes(Type type)
    {
        bool supported = (bool)typeof(NetSerializer<>).MakeGenericType(type).GetProperty("IsSupported")!.GetValue(null)!;
        Assert.True(supported);
    }

    [Fact]
    public void SerializerRoundTripsValues()
    {
        using NetWriter writer = new();
        NetSerializer<Float3>.Write(writer, new Float3(1, 2, 3));
        NetSerializer<NetworkMode>.Write(writer, NetworkMode.Object);
        NetSerializer<string>.Write(writer, "hi");
        NetSerializer<long>.Write(writer, -5);

        NetReader reader = new(writer.WrittenSpan);
        Assert.Equal(new Float3(1, 2, 3), NetSerializer<Float3>.Read(ref reader));
        Assert.Equal(NetworkMode.Object, NetSerializer<NetworkMode>.Read(ref reader));
        Assert.Equal("hi", NetSerializer<string>.Read(ref reader));
        Assert.Equal(-5, NetSerializer<long>.Read(ref reader));
        Assert.False(NetSerializer<List<int>>.IsSupported);
        Assert.Throws<NotSupportedException>(() => NetSerializer<List<int>>.Write(writer, new List<int>()));
    }
}
