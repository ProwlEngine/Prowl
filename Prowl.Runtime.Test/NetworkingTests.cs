// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
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
    public int Health { get => __sync_Health; set => __Sync_Set(ref __sync_Health, value, __syncnet_Health); }

    private int __sync_Score;
    private static readonly SyncMember<NetTestComponent, int> __syncnet_Score = new(ScoreSlot, "Score", SyncFlags.FromHost, static s => s.__sync_Score, static (s, v) => s.__sync_Score = v);
    public int Score { get => __sync_Score; set => __Sync_Set(ref __sync_Score, value, __syncnet_Score); }

    private static readonly RpcMember<NetTestComponent> __rpc_201 = new(ShoutId, "Shout(string)", RpcMode.Broadcast, RpcFlags.None,
        static (NetTestComponent s, ref NetReader r) => { string a0 = NetSerializer<string>.Read(ref r); __Rpc_ArgumentsRead(); s.Shout(a0); });

    public void Shout(string text)
    {
        if (__Rpc_Begin(__rpc_201, out NetWriter w)) { NetSerializer<string>.Write(w, text); if (!__Rpc_Send(__rpc_201, w)) return; }
        Shouts.Add((text, Rpc.Caller));
    }

    private static readonly RpcMember<NetTestComponent> __rpc_202 = new(AskHostId, "AskHost(int)", RpcMode.Host, RpcFlags.None,
        static (NetTestComponent s, ref NetReader r) => { int a0 = NetSerializer<int>.Read(ref r); __Rpc_ArgumentsRead(); s.AskHost(a0); });

    public void AskHost(int value)
    {
        if (__Rpc_Begin(__rpc_202, out NetWriter w)) { NetSerializer<int>.Write(w, value); if (!__Rpc_Send(__rpc_202, w)) return; }
        Asked.Add(value);
    }

    private static readonly RpcMember<NetTestComponent> __rpc_203 = new(ExplodeId, "Explode()", RpcMode.Host, RpcFlags.None,
        static (NetTestComponent s, ref NetReader r) => { __Rpc_ArgumentsRead(); s.Explode(); });

    public void Explode()
    {
        if (__Rpc_Begin(__rpc_203, out NetWriter w)) { if (!__Rpc_Send(__rpc_203, w)) return; }
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
    public const ushort SceneSnapshot = Networking.EngineMessageIds, Spawn = SceneSnapshot + 1, Despawn = SceneSnapshot + 2,
        Owner = SceneSnapshot + 3, Sync = SceneSnapshot + 4, Transform = SceneSnapshot + 5, RpcToHost = SceneSnapshot + 6, RpcFromHost = SceneSnapshot + 7;

    private FakePeer(NetSession session)
    {
        Session = session;
        for (ushort id = SceneSnapshot; id <= RpcFromHost; id++)
        {
            ushort captured = id;
            session.OnRaw(id, (NetPeer sender, ref NetReader reader, Delivery delivery) => Received.Add((captured, reader.ReadBytes(reader.Remaining).ToArray())));
        }
    }

    public NetSession Session { get; }

    public List<(ushort Id, byte[] Data)> Received { get; } = new();

    public static FakePeer Join(LocalNetwork network, string name) =>
        new(NetSession.Join(new LocalTransport(network, name), "host", name));

    public static FakePeer Host(LocalNetwork network) => new(NetSession.Host(new LocalTransport(network, "host"), "FakeHost"));

    public bool Has(ushort id) => Received.Any(m => m.Id == id);

    public byte[] Last(ushort id) => Received.Last(m => m.Id == id).Data;

    public void Send(ushort id, Action<NetWriter> write, NetPeer? to = null)
    {
        using NetWriter writer = new();
        write(writer);
        Session.SendRaw(to ?? Session.HostPeer!, id, writer.WrittenSpan);
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

public class NetworkingTests : RuntimeTestBase
{
    private static Scene RoundTrip(Scene scene)
    {
        using NetWriter writer = new();
        Networking.WriteSnapshot(scene, writer);
        NetReader reader = new(writer.WrittenSpan);
        return Networking.ReadSnapshot(ref reader);
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
        Networking.Receive();
        Networking.Flush();
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
        Assert.False(plain.TryGet("NetworkMode", out _));
        Assert.False(plain.TryGet("NetworkOrphaned", out _));
        Assert.False(plain.TryGet("NetworkSyncTransform", out _));

        go.NetworkMode = NetworkMode.Object;
        go.NetworkOrphaned = NetworkOrphaned.ClearOwner;
        go.NetworkSyncTransform = false;
        EchoObject stored = Serializer.Serialize(go);
        Assert.Equal((byte)NetworkMode.Object, stored["NetworkMode"].ByteValue);

        GameObject copy = Serializer.Deserialize<GameObject>(stored)!;
        Assert.Equal(NetworkMode.Object, copy.NetworkMode);
        Assert.Equal(NetworkOrphaned.ClearOwner, copy.NetworkOrphaned);
        Assert.False(copy.NetworkSyncTransform);
        copy.Dispose();

        GameObject fresh = Serializer.Deserialize<GameObject>(plain)!;
        Assert.True(fresh.NetworkSyncTransform);
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
        secret.NetworkMode = NetworkMode.Never;

        GameObject hostOnly = CreateGameObject("HostOnly");
        GameObject hostChild = CreateGameObject("HostChild");
        hostChild.SetParent(hostOnly);
        hostOnly.NetworkMode = NetworkMode.Never;

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
            Assert.False(GameObject.WritingNetworkSnapshot);
            Scene saved = Serializer.Deserialize<Scene>(Serializer.Serialize(scene))!;
            Assert.Equal(5, saved.AllObjects.Count());
            saved.Dispose();
        }
        finally
        {
            received.Dispose();
        }
    }

    [Fact]
    public void JoiningClientsGetTheHostSceneAndEverySceneChange()
    {
        Scene first = CreateScene();
        GameObject level = CreateGameObject("Level");
        first.Add(level);
        MakeCurrent(first);

        LocalNetwork network = new();
        Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            NetReader reader = new(alice.Last(FakePeer.SceneSnapshot));
            using (Scene received = Networking.ReadSnapshot(ref reader))
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
            using (Scene received = Networking.ReadSnapshot(ref reader))
                Assert.Contains(received.AllObjects, o => o.Name == "Arena");
        }
        finally
        {
            Networking.Shutdown();
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
        Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            NetPeer aliceHere = Networking.Session!.Peers.Single(p => !p.IsLocal);

            GameObject ship = CreateGameObject("Ship");
            NetTestComponent component = ship.AddComponent<NetTestComponent>();
            scene.Add(ship);
            ship.NetworkSpawn(aliceHere);

            Assert.Equal(NetworkMode.Object, ship.NetworkMode);
            Assert.Same(aliceHere, ship.NetworkOwner);
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
                w.WriteUInt32(1);
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
            Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void HostOwnedObjectsReplicateToClients()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);

            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.NetworkSpawn();
            Assert.False(crate.IsProxy);
            Assert.Null(crate.NetworkOwner);

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
            Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void SpawningASceneObjectHandsItToTheOwner()
    {
        Scene scene = CreateScene();
        GameObject ship = CreateGameObject("Ship");
        ship.NetworkMode = NetworkMode.Object;
        scene.Add(ship);
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            NetPeer aliceHere = Networking.Session!.Peers.Single(p => !p.IsLocal);

            ship.NetworkSpawn(aliceHere, NetworkOrphaned.ClearOwner);

            Assert.Same(aliceHere, ship.NetworkOwner);
            Assert.Equal(NetworkOrphaned.ClearOwner, ship.NetworkOrphaned);
            Pump(() => alice.Has(FakePeer.Owner), alice);
        }
        finally
        {
            Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void SpawningInsideANetworkedObjectThrowsAndChangesNothing()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Host(new LocalTransport(network, "host"));
        try
        {
            GameObject parent = CreateGameObject("Parent");
            GameObject child = CreateGameObject("Child");
            child.SetParent(parent);
            scene.Add(parent);
            parent.NetworkSpawn();

            Assert.Throws<InvalidOperationException>(() => child.NetworkSpawn(orphaned: NetworkOrphaned.ClearOwner));
            Assert.Equal(NetworkMode.Snapshot, child.NetworkMode);
            Assert.Equal(NetworkOrphaned.Destroy, child.NetworkOrphaned);
            Assert.Same(parent, child.NetworkRoot);
        }
        finally
        {
            Networking.Shutdown();
        }
    }

    [Fact]
    public void AnRpcThatThrowsNeitherCrashesTheHostNorDropsTheCaller()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.NetworkSpawn();

            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.ExplodeId); });
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(3); });
            Pump(() => component.Asked.Count == 1, alice);
            Assert.Equal(1, Networking.Session!.Peers.Count(p => !p.IsLocal));
        }
        finally
        {
            Networking.Shutdown();
            alice.Dispose();
        }
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
        Networking.WriteSnapshot(hostScene, snapshot);

        GameObject ship = CreateGameObject("Ship");
        ship.AddComponent<NetTestComponent>().Health = 9;
        ship.NetworkMode = NetworkMode.Object;
        using NetWriter shipSpawn = new();
        Networking.WriteSpawn(ship, owner: 1, NetworkOrphaned.Destroy, shipSpawn); // the first client is peer 1

        GameObject crate = CreateGameObject("Crate");
        crate.NetworkMode = NetworkMode.Object;
        using NetWriter crateSpawn = new();
        Networking.WriteSpawn(crate, owner: -1, NetworkOrphaned.Destroy, crateSpawn);

        Guid levelId = level.Identifier, shipId = ship.Identifier, crateId = crate.Identifier;

        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, shipSpawn.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, crateSpawn.WrittenSpan);
        };

        Networking.Join(new LocalTransport(network, "bob"), "host", "bob");
        try
        {
            Pump(() => Scene.Current.AllObjects.Any(o => o.Identifier == crateId), host);

            // [Sync] values come with the snapshot and spawns, not from the scene data.
            NetTestComponent levelHere = Find(Scene.Current, levelId).GetComponent<NetTestComponent>()!;
            GameObject shipHere = Find(Scene.Current, shipId);
            NetTestComponent shipComponent = shipHere.GetComponent<NetTestComponent>()!;
            GameObject crateHere = Find(Scene.Current, crateId);
            Assert.Equal(3, levelHere.Health);
            Assert.Equal(9, shipComponent.Health);

            Assert.Same(Networking.Session!.LocalPeer, shipHere.NetworkOwner);
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
                w.WriteUInt32(1);
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
            Networking.Shutdown();
            host.Dispose();
        }
    }

    // --- Offline and lifecycle -------------------------------------------------------------------

    [Fact]
    public void WithoutASessionNetworkingStaysOutOfTheWay()
    {
        Assert.Null(Networking.Session);
        Networking.Receive();
        Networking.Flush();
        Networking.Shutdown();

        GameObject go = CreateGameObject();
        NetTestComponent component = go.AddComponent<NetTestComponent>();
        component.Health = 5;
        component.Shout("alone");
        go.NetworkSpawn();

        Assert.Equal(5, component.Health);
        Assert.Equal("alone", component.Shouts.Single().Text);
        Assert.Null(component.Shouts.Single().Caller);
        Assert.False(go.IsProxy);
        Assert.Equal(NetworkMode.Object, go.NetworkMode);
    }

    [Fact]
    public void OnlyOneSessionRunsAtATime()
    {
        Networking.Host(new LocalTransport(new LocalNetwork()));
        try
        {
            Assert.True(Networking.IsHost && Networking.IsActive);
            Assert.Throws<InvalidOperationException>(() => Networking.Host(new LocalTransport(new LocalNetwork())));
        }
        finally
        {
            Networking.Shutdown();
        }

        Assert.Null(Networking.Session);
        Assert.False(Networking.IsActive);
    }

    [Fact]
    public void ClientsCannotSpawn()
    {
        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        Networking.Join(new LocalTransport(network, "bob"), "host", "bob");
        try
        {
            Pump(() => Networking.IsActive, host);
            GameObject go = CreateGameObject();
            Scene.Current.Add(go);
            Assert.Throws<InvalidOperationException>(() => go.NetworkSpawn());
        }
        finally
        {
            Networking.Shutdown();
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
