using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public class VisibilityListener : Component, INetworkEvents
{
    [SerializeIgnore] public readonly List<(int Peer, bool Visible)> Changes = new();
    [SerializeIgnore] public NetworkDespawnReason? DespawnReason;

    public void OnNetworkVisibilityChanged(NetPeer peer, bool visible) => Changes.Add((peer.Id, visible));

    public void OnNetworkDespawn() => DespawnReason = GameObject.Network.DespawnReason;
}

public class ShowOnSpawn : Component, INetworkEvents
{
    [SerializeIgnore] public static NetPeer? Peer;

    public void OnNetworkSpawn()
    {
        if (Peer != null) GameObject.Network.ShowTo(Peer);
    }
}

public class VisibilityPeerListener : Component, INetworkListener
{
    [SerializeIgnore] public Action<NetPeer>? Connected, Disconnected;

    public void OnPeerConnected(NetPeer peer) => Connected?.Invoke(peer);

    public void OnPeerDisconnected(NetPeer peer, SessionEndReason reason) => Disconnected?.Invoke(peer);
}

/// <summary>A host RPC taking a GameObject, as the script compiler generates it.</summary>
public class PointerTestComponent : Component
{
    public const uint PointId = 501;

    [SerializeIgnore] public readonly List<(GameObject? Target, NetPeer? Caller)> Points = new();

    private static readonly RpcMember<PointerTestComponent> __rpc_501 = new(PointId, "Point(GameObject)", RpcMode.Host, RpcFlags.None,
        static (PointerTestComponent s, ref NetReader r, bool execute) => { GameObject? a0 = NetSerializer<GameObject?>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Point(a0); });

    public void Point(GameObject? target)
    {
        if (__rpc_501.Begin(this, out NetWriter w)) { NetSerializer<GameObject?>.Write(w, target); if (!__rpc_501.Send(this, w)) return; }
        Points.Add((target, Rpc.Caller));
    }
}

public partial class NetworkingTests
{
    private sealed record VisibilitySession(SceneInstance World, FakePeer Alice, FakePeer Bob, LocalNetwork Network) : IDisposable
    {
        public NetPeer AliceHere => HostPeerOf(Alice);
        public NetPeer BobHere => HostPeerOf(Bob);

        public void Dispose()
        {
            Networking.Networking.Shutdown();
            Alice.Dispose();
            Bob.Dispose();
        }
    }

    private static NetPeer HostPeerOf(FakePeer peer) =>
        Networking.Networking.Session!.Peers.Single(p => p.Id == peer.Session.LocalPeer!.Id);

    private VisibilitySession VisibilityHost(Action<SceneInstance>? setUp = null)
    {
        SceneInstance world = CreateScene();
        world.Add(CreateGameObject("Level"));
        setUp?.Invoke(world);
        MakeCurrent(world);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        Pump(() => alice.IsReady && bob.IsReady, alice, bob);
        return new VisibilitySession(world, alice, bob, network);
    }

    private GameObject Crate(SceneInstance world, NetworkVisibility visibility, string name = "Crate")
    {
        GameObject crate = CreateGameObject(name);
        crate.AddComponent<NetTestComponent>();
        crate.AddComponent<VisibilityListener>();
        crate.Network.Visibility = visibility;
        world.Add(crate);
        return crate;
    }

    private static bool Contains(byte[] data, byte[] part)
    {
        for (int i = 0; i + part.Length <= data.Length; i++)
        {
            if (data.AsSpan(i, part.Length).SequenceEqual(part)) return true;
        }

        return false;
    }

    /// <summary>True if any message <paramref name="peer" /> received carries one of these identifiers as raw bytes.</summary>
    private static bool Mentions(FakePeer peer, params Guid[] ids) =>
        peer.WireReceived.Any(m => ids.Any(id => Contains(m.Data, id.ToByteArray())));

    private static int Count(FakePeer peer, ushort id) => peer.Received.Count(m => m.Id == id);

    private static int IndexOf(FakePeer peer, ushort id, int from = 0) => peer.Received.FindIndex(from, m => m.Id == id);

    private static (Guid Id, int Owner, bool Replace, int? Health) ReadSpawn(byte[] payload, Guid component)
    {
        NetReader reader = new(payload);
        SpawnMessage message = SpawnMessage.Read(ref reader);
        (Guid id, int owner, bool replace) = (message.Identifier, message.OwnerId, message.Replace);
        int? health = null;
        uint count = reader.ReadVarUInt32();
        for (uint i = 0; i < count; i++)
        {
            Guid target = reader.ReadGuid();
            uint slot = reader.ReadVarUInt32();
            reader.ReadVarUInt32();
            NetReader value = new(reader.ReadBytesWithLength());
            if (target == component && slot == NetTestComponent.HealthSlot) health = NetSerializer<int>.Read(ref value);
        }

        return (id, owner, replace, health);
    }

    /// <summary>Whether the Health value in this Snapshot names its component by guid, or null if it holds none.</summary>
    private static bool? DefinesHealth(byte[] snapshot, int health)
    {
        NetReader r = new(snapshot);
        r.ReadVarUInt32(); r.ReadVarUInt32(); r.ReadVarUInt32();
        FakePeer.SkipTransformGroups(ref r);
        uint count = r.ReadVarUInt32();
        for (uint i = 0; i < count; i++)
        {
            uint key = r.ReadVarUInt32();
            if ((key & 1) != 0) r.ReadGuid();
            uint slot = r.ReadVarUInt32(); r.ReadVarUInt32();
            NetReader value = new(r.ReadBytesWithLength());
            if (slot == NetTestComponent.HealthSlot && NetSerializer<int>.Read(ref value) == health) return (key & 1) != 0;
        }

        return null;
    }

    private static List<Guid> SnapshotObjectIds(byte[] snapshot)
    {
        NetReader reader = new(snapshot);
        SceneSnapshotMessage.Read(ref reader);
        uint count = ObjectStateBlock.Read(ref reader).Count;
        List<Guid> ids = new();
        for (uint i = 0; i < count; i++) ids.Add(SnapshotObjectMessage.Read(ref reader).Identifier);
        return ids;
    }

    private static string SnapshotJson(byte[] snapshot)
    {
        NetReader reader = new(snapshot);
        return NetworkTestScene.Current.Decompress(SceneSnapshotMessage.Read(ref reader).Sections).WriteToJson();
    }

    [Fact]
    public void AHiddenPeerReceivesNothingAboutTheObject()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Manual);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        NetCollectionTestComponent bag = crate.AddComponent<NetCollectionTestComponent>();
        crate.Network.OwnerTransfer = OwnerTransfer.Takeover;
        crate.Network.Spawn();
        crate.Network.ShowTo(session.AliceHere);

        stats.Health = 7;
        bag.Scores.Add(3);
        crate.Transform.LocalPosition = new Float3(1, 2, 3);
        stats.Shout("hello");
        crate.Network.Flags = NetworkFlags.NoInterpolation;
        crate.Network.Orphaned = NetworkOrphaned.ClearOwner;
        crate.Network.OwnerTransfer = OwnerTransfer.Fixed;
        Pump(() => session.Alice.SnapshotInt(stats.Identifier, NetTestComponent.HealthSlot) == 7 &&
                   session.Alice.Has(FakePeer.Sync) && session.Alice.Has(FakePeer.RpcFromHost), session.Alice, session.Bob);
        crate.Network.AssignOwnership(session.AliceHere);
        crate.Network.AssignOwnership(null);
        Pump(() => session.Alice.Has(FakePeer.OwnershipState), session.Alice, session.Bob);
        PumpFor(50, session.Alice, session.Bob);

        uint netId = NetworkTestScene.Current.NetIdOf(crate);
        Assert.False(Mentions(session.Bob, crate.Identifier, stats.Identifier, bag.Identifier));
        Assert.DoesNotContain(session.Bob.WireReceived.Where(m => m.Id == FakePeer.Snapshot)
            .SelectMany(m => ReadTransforms(m.Data)), r => r.NetId == netId);
        Assert.Contains(session.Alice.WireReceived.Where(m => m.Id == FakePeer.Snapshot)
            .SelectMany(m => ReadTransforms(m.Data)), r => r.NetId == netId);
        Assert.False(crate.Network.IsVisibleTo(session.BobHere));
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void ShowToSendsTheWholeObjectAheadOfAnRpcCalledRightAfter()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Manual);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.Network.Spawn();
        stats.Health = 7;
        PumpFor(30, session.Alice, session.Bob);
        Assert.False(session.Alice.Has(FakePeer.Spawn));

        crate.Network.ShowTo(session.AliceHere);
        stats.Shout("hi");
        Pump(() => session.Alice.Has(FakePeer.RpcFromHost), session.Alice, session.Bob);

        Assert.InRange(IndexOf(session.Alice, FakePeer.Spawn), 0, IndexOf(session.Alice, FakePeer.RpcFromHost) - 1);
        (Guid id, _, bool replace, int? health) = ReadSpawn(session.Alice.Last(FakePeer.Spawn), stats.Identifier);
        Assert.Equal(crate.Identifier, id);
        Assert.True(replace);
        Assert.Equal(7, health);
        Assert.Equal(new[] { (session.AliceHere.Id, true) }, crate.GetComponent<VisibilityListener>()!.Changes);
        Assert.False(Mentions(session.Bob, crate.Identifier));
    }

    [Fact]
    public void HideFromDespawnsTheObjectThereAsOutOfInterest()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.Network.Spawn();
        Pump(() => session.Alice.Has(FakePeer.Spawn) && session.Bob.Has(FakePeer.Spawn), session.Alice, session.Bob);

        crate.Network.HideFrom(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        NetReader reader = new(session.Alice.Last(FakePeer.Despawn));
        DespawnMessage despawn = DespawnMessage.Read(ref reader);
        Assert.Equal(crate.Identifier, despawn.Identifier);
        Assert.Equal(NetworkDespawnReason.OutOfInterest, despawn.Reason);
        Assert.False(session.Bob.Has(FakePeer.Despawn));
        Assert.True(crate.IsValid());
        Assert.False(crate.Network.IsVisibleTo(session.AliceHere));

        crate.Network.ClearVisibilityOverride(session.AliceHere);
        Pump(() => Count(session.Alice, FakePeer.Spawn) == 2, session.Alice, session.Bob);
        Assert.True(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
        Assert.Equal(new[] { (session.AliceHere.Id, false), (session.AliceHere.Id, true) },
            crate.GetComponent<VisibilityListener>()!.Changes);
    }

    [Fact]
    public void DestroyingAnObjectSendsTheSameDespawnAsBefore()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.Network.Spawn();
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);
        Assert.False(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
        crate.Destroy();
        Pump(() => session.Alice.Has(FakePeer.Despawn) && session.Bob.Has(FakePeer.Despawn), session.Alice, session.Bob);
        Assert.Equal(crate.Identifier.ToByteArray(), session.Alice.Last(FakePeer.Despawn));
    }

    [Fact]
    public void TheOwnerAlwaysHasItsObject()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Manual);
        crate.Network.Spawn(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);
        Assert.False(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
        Assert.False(crate.Network.IsVisibleTo(session.BobHere));

        crate.Network.AssignOwnership(session.BobHere);
        Pump(() => session.Bob.Has(FakePeer.Spawn) && session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        (Guid id, int owner, bool replace, _) = ReadSpawn(session.Bob.Last(FakePeer.Spawn), Guid.Empty);
        Assert.Equal(crate.Identifier, id);
        Assert.Equal(session.BobHere.Id, owner);
        Assert.True(replace);
        Assert.False(session.Bob.Has(FakePeer.Owner));
        Assert.False(session.Bob.Has(FakePeer.OwnershipState));
        Assert.InRange(IndexOf(session.Alice, FakePeer.Owner), 0, session.Alice.Received.FindLastIndex(m => m.Id == FakePeer.Despawn) - 1);
        Assert.False(crate.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void HidingAnObjectFromItsOwnerIsIgnored()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.Network.Spawn(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);

        crate.Network.HideFrom(session.AliceHere);
        PumpFor(50, session.Alice, session.Bob);
        Assert.False(session.Alice.Has(FakePeer.Despawn));
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void OverridesWinOverAlwaysOwnerOnlyAndManual()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject always = Crate(session.World, NetworkVisibility.Always, "Always");
        always.Network.Spawn();
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);
        always.Network.HideFrom(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        Assert.False(always.Network.IsVisibleTo(session.AliceHere));
        Assert.True(always.Network.IsVisibleTo(session.BobHere));

        GameObject secret = Crate(session.World, NetworkVisibility.OwnerOnly, "Secret");
        secret.Network.Spawn(session.AliceHere);
        Assert.False(secret.Network.IsVisibleTo(session.BobHere));
        secret.Network.ShowTo(session.BobHere);
        Assert.True(secret.Network.IsVisibleTo(session.BobHere));
        Pump(() => session.Bob.Received.Any(m => m.Id == FakePeer.Spawn && ReadSpawn(m.Data, Guid.Empty).Id == secret.Identifier),
            session.Alice, session.Bob);

        secret.Network.Visibility = NetworkVisibility.Default;
        secret.Network.ClearVisibilityOverride(session.BobHere);
        Assert.True(secret.Network.IsVisibleTo(session.BobHere));
        secret.Network.Visibility = NetworkVisibility.Manual;
        Assert.False(secret.Network.IsVisibleTo(session.BobHere));
        Assert.True(secret.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void HidingAndShowingInOneFrameSendsADespawnThenAReplacingSpawn()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.Network.Spawn();
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);

        crate.Network.HideFrom(session.AliceHere);
        crate.Network.ShowTo(session.AliceHere);
        Pump(() => Count(session.Alice, FakePeer.Spawn) == 2, session.Alice, session.Bob);
        int despawn = IndexOf(session.Alice, FakePeer.Despawn);
        Assert.InRange(despawn, 0, session.Alice.Received.FindLastIndex(m => m.Id == FakePeer.Spawn) - 1);
        Assert.True(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
    }

    [Fact]
    public void AClientReplacesAnObjectThatLeftAndCameBackInTheSameFrame()
    {
        SceneInstance scene = CreateScene();
        using NetWriter snapshot = new(), spawn = new(), despawn = new(), back = new();
        NetworkTestScene.Current.WriteSnapshot(scene, snapshot);
        GameObject crate = CreateGameObject("Crate");
        crate.Network.Mode = NetworkMode.Object;
        NetTestComponent component = crate.AddComponent<NetTestComponent>();
        crate.AddComponent<VisibilityListener>();
        NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, spawn, generation: 1, netId: 5);
        new DespawnMessage(crate.Identifier, NetworkDespawnReason.OutOfInterest).Write(despawn);
        component.Health = 9;
        NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, back, generation: 1, netId: 5, replace: true);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, spawn.WrittenSpan);
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == crate.Identifier), host);
            GameObject first = Find(SceneInstance.Main, crate.Identifier);
            VisibilityListener listener = first.GetComponent<VisibilityListener>()!;

            host.Session.SendRaw(client, FakePeer.Despawn, despawn.WrittenSpan);
            host.Session.SendRaw(client, FakePeer.Spawn, back.WrittenSpan);
            host.Session.Flush();
            Pump(() => first.IsDisposed, host);
            PumpFor(30, host);

            GameObject second = Find(SceneInstance.Main, crate.Identifier);
            Assert.NotSame(first, second);
            Assert.False(second.IsDisposed);
            Assert.Equal(9, second.GetComponent<NetTestComponent>()!.Health);
            Assert.True(second.Network.Active);
            Assert.Equal(NetworkDespawnReason.OutOfInterest, listener.DespawnReason);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnObjectComingBackWithinAPruneIntervalKeepsItsComponentIds()
    {
        SceneInstance scene = CreateScene();
        using NetWriter snapshot = new(), spawn = new(), despawn = new(), back = new();
        NetworkTestScene.Current.WriteSnapshot(scene, snapshot);
        GameObject crate = CreateGameObject("Crate");
        crate.Network.Mode = NetworkMode.Object;
        NetTestComponent component = crate.AddComponent<NetTestComponent>();
        NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, spawn, generation: 1, netId: 5);
        new DespawnMessage(crate.Identifier, NetworkDespawnReason.OutOfInterest).Write(despawn);
        NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, back, generation: 1, netId: 5, replace: true);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        float tickRate = Networking.Networking.TickRate;
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, spawn.WrittenSpan);
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == crate.Identifier), host);
            NetTestComponent first = Find(SceneInstance.Main, crate.Identifier).GetComponent<NetTestComponent>()!;
            host.SendSnapshotValues(client, 7, 1, (component.Identifier, NetTestComponent.HealthSlot, 1u, IntValue(5)));
            Pump(() => first.Health == 5, host);

            host.Session.SendRaw(client, FakePeer.Despawn, despawn.WrittenSpan);
            host.Session.Flush();
            Pump(() => first.IsDisposed, host);
            host.Session.SendRaw(client, FakePeer.Spawn, back.WrittenSpan);
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == crate.Identifier && !o.IsDisposed), host);
            NetTestComponent second = Find(SceneInstance.Main, crate.Identifier).GetComponent<NetTestComponent>()!;
            host.SendSnapshotValues(client, 8, 2, (component.Identifier, NetTestComponent.HealthSlot, 1u, IntValue(6)));
            Pump(() => second.Health == 6, host);

            Networking.Networking.TickRate = 1000;
            uint pruned = (Networking.Networking.System!.Tick / 64 + 1) * 64;
            Pump(() => Networking.Networking.System?.Tick > pruned, host);
            using NetWriter byId = new();
            byId.WriteVarUInt32(9); byId.WriteVarUInt32((uint)NetworkTestScene.Current.SceneGeneration); byId.WriteVarUInt32(3);
            byId.WriteVarUInt32(0); byId.WriteVarUInt32(1);
            byId.WriteVarUInt32(1u << 1); byId.WriteVarUInt32(NetTestComponent.HealthSlot); byId.WriteVarUInt32(1);
            byId.WriteBytesWithLength(IntValue(11));
            host.Session.SendRaw(client, FakePeer.Snapshot, byId.WrittenSpan, Delivery.Unreliable);
            host.Session.Flush();
            Pump(() => second.Health == 11, host);
            Assert.True(Networking.Networking.IsActive);
        }
        finally
        {
            Networking.Networking.TickRate = tickRate;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void AnAcknowledgementArrivingAfterTheObjectLeftDoesNotSkipItsDefinitionLater()
    {
        using VisibilitySession session = VisibilityHost();
        FakePeer alice = session.Alice;
        alice.AutoAck = false;
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.Network.Spawn();
        Pump(() => alice.Has(FakePeer.Spawn), alice, session.Bob);
        stats.Health = 5;
        Pump(() => alice.WireReceived.Any(m => m.Id == FakePeer.Snapshot && DefinesHealth(m.Data, 5) == true), alice, session.Bob);
        byte[] early = alice.WireReceived.First(m => m.Id == FakePeer.Snapshot && DefinesHealth(m.Data, 5) == true).Data;

        crate.Network.HideFrom(session.AliceHere);
        Pump(() => alice.Has(FakePeer.Despawn), alice, session.Bob);
        NetReader header = new(early);
        uint snapshotId = header.ReadVarUInt32();
        alice.Send(FakePeer.SnapshotAck, w => { w.WriteVarUInt32(1); w.WriteVarUInt32(snapshotId); w.WriteVarUInt32(0); w.WriteVarUInt32(0); });
        PumpFor(50, alice, session.Bob);

        crate.Network.ShowTo(session.AliceHere);
        stats.Health = 6;
        Pump(() => alice.WireReceived.Any(m => m.Id == FakePeer.Snapshot && DefinesHealth(m.Data, 6) != null), alice, session.Bob);
        Assert.True(DefinesHealth(alice.WireReceived.First(m => m.Id == FakePeer.Snapshot && DefinesHealth(m.Data, 6) != null).Data, 6));
    }

    [Fact]
    public void AHiddenPeerCannotCallRpcsOnTheObjectOrRequestIt()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Manual);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.Network.OwnerTransfer = OwnerTransfer.Takeover;
        crate.Network.Spawn();
        crate.Network.ShowTo(session.AliceHere);
        uint generation = NetworkTestScene.Current.GenerationOf(crate);

        session.Bob.Send(FakePeer.RpcToHost, w => { w.WriteGuid(stats.Identifier); w.WriteUInt32(NetTestComponent.ShoutId); w.WriteString("bob"); });
        RequestOwnership(session.Bob, crate, generation, true);
        PumpFor(50, session.Alice, session.Bob);
        session.Alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(stats.Identifier); w.WriteUInt32(NetTestComponent.ShoutId); w.WriteString("alice"); });
        Pump(() => stats.Shouts.Count > 0, session.Alice, session.Bob);

        Assert.Equal(new[] { ("alice", (NetPeer?)session.AliceHere) }, stats.Shouts);
        Assert.Null(crate.Network.Owner);
    }

    public class LinkHolder : Component
    {
        public GameObject? Target;
        public Component? Part;
    }

    [Fact]
    public void AJoiningClientGetsNoHiddenObjectAndLinksToItAsNull()
    {
        SceneInstance world = CreateScene();
        GameObject chest = CreateGameObject("Chest");
        chest.Network.Mode = NetworkMode.Object;
        chest.Network.Visibility = NetworkVisibility.Manual;
        chest.AddComponent<SecretHolder>().Value = HostSecret;
        NetTestComponent stats = chest.AddComponent<NetTestComponent>();
        GameObject door = CreateGameObject("Door");
        LinkHolder link = door.AddComponent<LinkHolder>();
        link.Target = chest;
        link.Part = stats;
        world.Add(door);
        world.Add(chest);
        MakeCurrent(world);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            byte[] payload = alice.Last(FakePeer.SceneSnapshot);
            string json = SnapshotJson(payload);
            Assert.DoesNotContain(HostSecret, json);
            Assert.DoesNotContain(chest.Identifier.ToString(), json);
            Assert.DoesNotContain(stats.Identifier.ToString(), json);
            Assert.Empty(SnapshotObjectIds(payload));
            Assert.False(Mentions(alice, chest.Identifier, stats.Identifier));

            NetReader reader = new(payload);
            using SnapshotSections received = NetworkTestScene.Current.ReadSnapshot(ref reader);
            Assert.DoesNotContain(received.Objects, o => o.Name == "Chest");
            LinkHolder linked = Find(received, "Door").GetComponent<LinkHolder>()!;
            Assert.Null(linked.Target);
            Assert.Null(linked.Part);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASceneChangeSendsEachClientOnlyThePersistentObjectsItHas()
    {
        GameObject? chest = null;
        using VisibilitySession session = VisibilityHost(world =>
        {
            chest = Crate(world, NetworkVisibility.Manual, "Chest");
            chest.Network.Mode = NetworkMode.Object;
            chest.Persistent = true;
        });
        chest!.Network.ShowTo(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);

        session.World.ReplaceContent(new SceneContent { Objects = [new GameObject("Next")] }, "Next", null);
        Pump(() => Count(session.Alice, FakePeer.SceneSnapshot) == 2 && Count(session.Bob, FakePeer.SceneSnapshot) == 2,
            session.Alice, session.Bob);
        Assert.Contains(chest.Identifier, SnapshotObjectIds(session.Alice.Last(FakePeer.SceneSnapshot)));
        Assert.DoesNotContain(chest.Identifier, SnapshotObjectIds(session.Bob.Last(FakePeer.SceneSnapshot)));
        Assert.False(Mentions(session.Bob, chest.Identifier));
        Assert.True(chest.Network.IsVisibleTo(session.AliceHere));
        Assert.False(chest.Network.IsVisibleTo(session.BobHere));
    }

    [Fact]
    public void AnAddedSectionLeavesOutHiddenObjectsAndLinksToThemAsNull()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject chest = new("Chest");
            chest.Network.Mode = NetworkMode.Object;
            chest.Network.Visibility = NetworkVisibility.Manual;
            chest.AddComponent<SecretHolder>().Value = HostSecret;
            GameObject door = new("Door");
            door.AddComponent<LinkHolder>().Target = chest;
            world.AddContent(new SceneContent { Objects = [chest, door] }, "Arena", null);

            Pump(() => alice.Has(FakePeer.SectionAdd), alice);
            byte[] payload = alice.Last(FakePeer.SectionAdd);
            NetReader reader = new(payload);
            string json = NetworkTestScene.Current.Decompress(SectionAddMessage.Read(ref reader).Sections).WriteToJson();
            Assert.DoesNotContain(HostSecret, json);
            Assert.DoesNotContain(chest.Identifier.ToString(), json);
            List<SectionData> sections = ReadSectionAdd(payload, out uint objectCount, world);
            try
            {
                Assert.Equal(0u, objectCount);
                Assert.DoesNotContain(sections[0].Content.Objects, o => o.Name == "Chest");
                Assert.Null(sections[0].Content.Objects.Single(o => o.Name == "Door").GetComponent<LinkHolder>()!.Target);
            }
            finally { DisposeSections(sections); }
            Assert.False(Mentions(alice, chest.Identifier));
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void APeerLeavingTakesItsOverridesWithIt()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject shown = Crate(session.World, NetworkVisibility.Manual, "Shown");
        GameObject hidden = Crate(session.World, NetworkVisibility.Default, "Hidden");
        shown.Network.Spawn();
        hidden.Network.Spawn();
        shown.Network.ShowTo(session.AliceHere);
        hidden.Network.HideFrom(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);

        int aliceId = session.AliceHere.Id;
        session.Alice.Session.Shutdown();
        Pump(() => Networking.Networking.Session!.Peers.All(p => p.Id != aliceId), session.Alice, session.Bob);
        NetworkSystem system = NetworkTestScene.Current;
        Assert.DoesNotContain(system.Entries.Values, e => e.HiddenFrom?.Contains(aliceId) == true);
        Assert.DoesNotContain(system.VisibilityOverrides.Values, forced => forced.ContainsKey(aliceId));
        using FakePeer carol = FakePeer.Join(session.Network, "carol");
        Pump(() => carol.IsReady, carol, session.Bob);

        List<Guid> carolHas = SnapshotObjectIds(carol.Last(FakePeer.SceneSnapshot));
        Assert.DoesNotContain(shown.Identifier, carolHas);
        Assert.Contains(hidden.Identifier, carolHas);
        NetPeer carolHere = HostPeerOf(carol);
        Assert.False(shown.Network.IsVisibleTo(carolHere));
        Assert.True(hidden.Network.IsVisibleTo(carolHere));
    }

    [Fact]
    public void UnloadingASectionWithAHiddenObjectSendsNothingAboutIt()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject chest = new("Chest");
            chest.Network.Mode = NetworkMode.Object;
            chest.Network.Visibility = NetworkVisibility.Manual;
            SceneSection arena = world.AddContent(new SceneContent { Objects = [chest] }, "Arena", null);
            Pump(() => alice.Has(FakePeer.SectionAdd), alice);
            Assert.Equal(1, NetworkDiagnostics.NetworkObjectCount);

            world.UnloadNow(arena);
            Pump(() => alice.Has(FakePeer.SectionRemove), alice);
            Assert.False(alice.Has(FakePeer.Despawn));
            Assert.False(Mentions(alice, chest.Identifier));
            Assert.Equal(0, NetworkDiagnostics.NetworkObjectCount);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void SpawningHiddenAnObjectClientsHaveAsSceneContentIsRefused()
    {
        GameObject? lamp = null;
        using VisibilitySession session = VisibilityHost(world =>
        {
            lamp = CreateGameObject("Lamp");
            world.Add(lamp);
        });
        lamp!.Network.Visibility = NetworkVisibility.Manual;
        Assert.Throws<InvalidOperationException>(() => lamp.Network.Spawn());
        Assert.Equal(NetworkMode.Snapshot, lamp.Network.Mode);
        Assert.False(lamp.Network.Active);

        lamp.Network.Visibility = NetworkVisibility.Default;
        lamp.Network.Spawn();
        Assert.True(lamp.Network.Active);

        GameObject fresh = Crate(session.World, NetworkVisibility.Manual, "Fresh");
        fresh.Network.Spawn();
        Assert.True(fresh.Network.Active);
    }

    [Fact]
    public void NetworkVisibilityIsStoredOnlyWhenSet()
    {
        GameObject go = CreateGameObject();
        go.Network.Mode = NetworkMode.Object;
        EchoObject plain = Serializer.Serialize(go);
        Assert.False(plain["Network"].TryGet("Visibility", out _));

        go.Network.Visibility = NetworkVisibility.Manual;
        EchoObject stored = Serializer.Serialize(go);
        Assert.Equal((byte)NetworkVisibility.Manual, stored["Network"]["Visibility"].ByteValue);
        GameObject copy = Serializer.Deserialize<GameObject>(stored)!;
        Assert.Equal(NetworkVisibility.Manual, copy.Network.Visibility);
        copy.Dispose();

        GameObject reset = Serializer.Deserialize<GameObject>(plain)!;
        Assert.Equal(NetworkVisibility.Default, reset.Network.Visibility);
        reset.Dispose();
    }

    [Fact]
    public void ASnapshotSentBeforeAnObjectLeftCannotRollBackItsReplacement()
    {
        SceneInstance scene = CreateScene();
        using NetWriter snapshot = new(), spawn = new(), despawn = new(), back = new();
        NetworkTestScene.Current.WriteSnapshot(scene, snapshot);
        GameObject crate = CreateGameObject("Crate");
        crate.Network.Mode = NetworkMode.Object;
        crate.Network.Flags = NetworkFlags.NoInterpolation;
        NetTestComponent component = crate.AddComponent<NetTestComponent>();
        NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, spawn, generation: 1, netId: 5);
        new DespawnMessage(crate.Identifier, NetworkDespawnReason.OutOfInterest).Write(despawn);
        component.Health = 9;
        uint tick = NetworkTestScene.Current.Tick;
        NetworkTestScene.Current.Tick = 6;
        NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, back, generation: 1, netId: 5, replace: true);
        NetworkTestScene.Current.Tick = tick;

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, spawn.WrittenSpan);
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == crate.Identifier), host);
            NetTestComponent first = Find(SceneInstance.Main, crate.Identifier).GetComponent<NetTestComponent>()!;
            host.SendSnapshotValues(client, 7, 5, (component.Identifier, NetTestComponent.HealthSlot, 1u, IntValue(5)));
            Pump(() => first.Health == 5, host);

            host.Session.SendRaw(client, FakePeer.Despawn, despawn.WrittenSpan);
            host.Session.SendRaw(client, FakePeer.Spawn, back.WrittenSpan);
            host.Session.Flush();
            Pump(() => first.IsDisposed && SceneInstance.Main.Objects.Any(o => o.Identifier == crate.Identifier && !o.IsDisposed), host);
            GameObject replaced = Find(SceneInstance.Main, crate.Identifier);
            NetTestComponent second = replaced.GetComponent<NetTestComponent>()!;
            Assert.Equal(9, second.Health);

            host.SendSnapshotValues(client, 8, 4, (component.Identifier, NetTestComponent.HealthSlot, 1u, IntValue(4)));
            host.Send(FakePeer.Snapshot, w => WriteTransforms(w, 4, 4 / 30.0,
                (5u, 1u, new Float3(4, 0, 0), Quaternion.Identity, Float3.One)), client);
            PumpFor(30, host);
            Assert.Equal(9, second.Health);
            Assert.Equal(0f, replaced.Transform.LocalPosition.X);

            host.SendSnapshotValues(client, 9, 7, (component.Identifier, NetTestComponent.HealthSlot, 1u, IntValue(10)));
            host.Send(FakePeer.Snapshot, w => WriteTransforms(w, 7, 7 / 30.0,
                (5u, 1u, new Float3(7, 0, 0), Quaternion.Identity, Float3.One)), client);
            Pump(() => second.Health == 10 && replaced.Transform.LocalPosition.X == 7, host);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ShowToFromOnNetworkSpawnOfALoadedSectionTravelsInsideTheSection()
    {
        using VisibilitySession session = VisibilityHost();
        try
        {
            ShowOnSpawn.Peer = session.AliceHere;
            GameObject chest = new("Chest");
            chest.Network.Mode = NetworkMode.Object;
            chest.Network.Visibility = NetworkVisibility.Manual;
            chest.AddComponent<ShowOnSpawn>();
            session.World.AddContent(new SceneContent { Objects = [chest] }, "Arena", null);
            Pump(() => session.Alice.Has(FakePeer.SectionAdd) && session.Bob.Has(FakePeer.SectionAdd), session.Alice, session.Bob);
            PumpFor(10, session.Alice, session.Bob);

            Assert.False(session.Alice.Has(FakePeer.Spawn));
            DisposeSections(ReadSectionAdd(session.Alice.Last(FakePeer.SectionAdd), out uint aliceObjects));
            Assert.Equal(1u, aliceObjects);
            Assert.False(Mentions(session.Bob, chest.Identifier));
            Assert.True(chest.Network.IsVisibleTo(session.AliceHere));
        }
        finally { ShowOnSpawn.Peer = null; }
    }

    [Fact]
    public void ShowToFromOnNetworkSpawnOfALoadedSceneTravelsInsideTheSnapshot()
    {
        using VisibilitySession session = VisibilityHost();
        try
        {
            ShowOnSpawn.Peer = session.AliceHere;
            GameObject chest = new("Chest");
            chest.Network.Mode = NetworkMode.Object;
            chest.Network.Visibility = NetworkVisibility.Manual;
            chest.AddComponent<ShowOnSpawn>();
            session.World.ReplaceContent(new SceneContent { Objects = [chest] }, "Next", null);
            Pump(() => Count(session.Alice, FakePeer.SceneSnapshot) == 2 && Count(session.Bob, FakePeer.SceneSnapshot) == 2,
                session.Alice, session.Bob);
            PumpFor(10, session.Alice, session.Bob);

            Assert.False(session.Alice.Has(FakePeer.Spawn));
            Assert.Contains(chest.Identifier, SnapshotObjectIds(session.Alice.Last(FakePeer.SceneSnapshot)));
            Assert.False(Mentions(session.Bob, chest.Identifier));
        }
        finally { ShowOnSpawn.Peer = null; }
    }

    [Fact]
    public void ReferencesFromAClientToObjectsItDoesNotHaveReadAsNullOnTheHost()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject lever = CreateGameObject("Lever");
        PointerTestComponent pointer = lever.AddComponent<PointerTestComponent>();
        SyncReferenceTestComponent holder = lever.AddComponent<SyncReferenceTestComponent>();
        session.World.Add(lever);
        lever.Network.Spawn(session.BobHere);
        GameObject crate = Crate(session.World, NetworkVisibility.Manual);
        crate.Network.Spawn();
        crate.Network.ShowTo(session.AliceHere);
        Pump(() => session.Bob.Has(FakePeer.Spawn), session.Alice, session.Bob);

        session.Bob.Send(FakePeer.RpcToHost, w => { w.WriteGuid(pointer.Identifier); w.WriteUInt32(PointerTestComponent.PointId); w.WriteGuid(crate.Identifier); });
        session.Alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(pointer.Identifier); w.WriteUInt32(PointerTestComponent.PointId); w.WriteGuid(crate.Identifier); });
        Pump(() => pointer.Points.Count == 2, session.Alice, session.Bob);
        Assert.Contains((null, session.BobHere), pointer.Points);
        Assert.Contains((crate, session.AliceHere), pointer.Points);

        void SendTarget(GameObject target) => session.Bob.Send(FakePeer.Sync, w =>
        {
            w.WriteVarUInt32(1);
            w.WriteGuid(holder.Identifier);
            w.WriteVarUInt32(SyncReferenceTestComponent.TargetSlot);
            using NetWriter v = new();
            v.WriteGuid(target.Identifier);
            w.WriteBytesWithLength(v.WrittenSpan);
        });

        SendTarget(lever);
        Pump(() => holder.Target == lever, session.Alice, session.Bob);
        SendTarget(crate);
        Pump(() => holder.Target != lever, session.Alice, session.Bob);
        Assert.Null(holder.Target);
    }

    [Fact]
    public void VisibilityCallsForAPeerThatLeftAreIgnored()
    {
        VisibilityPeerListener? listener = null;
        using VisibilitySession session = VisibilityHost(world =>
        {
            GameObject rules = CreateGameObject("Rules");
            listener = rules.AddComponent<VisibilityPeerListener>();
            world.Add(rules);
        });
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.Network.Spawn();
        crate.Network.HideFrom(session.AliceHere);
        Exception? failure = null;
        void Touch(NetPeer peer)
        {
            try
            {
                crate.Network.ClearVisibilityOverride(peer);
                crate.Network.HideFrom(peer);
                crate.Network.ShowTo(peer);
            }
            catch (Exception e) { failure = e; }
        }

        bool called = false;
        listener!.Disconnected = peer => { called = true; Touch(peer); };
        NetPeer alice = session.AliceHere;
        session.Alice.Session.Shutdown();
        Pump(() => called, session.Alice, session.Bob);
        Touch(alice);

        Assert.Null(failure);
        Assert.DoesNotContain(NetworkTestScene.Current.VisibilityOverrides.Values, forced => forced.ContainsKey(alice.Id));
    }

    [Fact]
    public void APeerIsHiddenFromRestrictedObjectsWhileItConnects()
    {
        VisibilityPeerListener? listener = null;
        using VisibilitySession session = VisibilityHost(world =>
        {
            GameObject rules = CreateGameObject("Rules");
            listener = rules.AddComponent<VisibilityPeerListener>();
            world.Add(rules);
        });
        GameObject crate = Crate(session.World, NetworkVisibility.Manual);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.Network.Spawn();
        bool? visible = null;
        listener!.Connected = peer =>
        {
            visible = crate.Network.IsVisibleTo(peer);
            stats.Shout("welcome");
        };

        using FakePeer carol = FakePeer.Join(session.Network, "carol");
        Pump(() => carol.IsReady, carol, session.Alice, session.Bob);
        Assert.False(visible);
        Assert.False(Mentions(carol, crate.Identifier, stats.Identifier));
    }

    [Fact]
    public void PeersMissingTheSameObjectsShareOneSectionWrite()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject chest = new("Chest");
        chest.Network.Mode = NetworkMode.Object;
        chest.Network.Visibility = NetworkVisibility.Manual;
        GameObject counter = new("Counter");
        counter.AddComponent<SerializeCounter>();
        SerializeCounter.Count = 0;
        session.World.AddContent(new SceneContent { Objects = [chest, counter] }, "Arena", null);
        Pump(() => session.Alice.Has(FakePeer.SectionAdd) && session.Bob.Has(FakePeer.SectionAdd), session.Alice, session.Bob);
        Assert.Equal(1, SerializeCounter.Count);
        Assert.False(Mentions(session.Alice, chest.Identifier));
        Assert.False(Mentions(session.Bob, chest.Identifier));
    }

    [Fact]
    public void AnObjectMadeBeforeTheWorldWasSentButNeverInItCanSpawnHidden()
    {
        GameObject pooled = CreateGameObject("Pooled");
        using VisibilitySession session = VisibilityHost();
        session.World.Add(pooled);
        pooled.Network.Visibility = NetworkVisibility.Manual;
        pooled.Network.Spawn();
        Assert.True(pooled.Network.Active);
        PumpFor(10, session.Alice, session.Bob);
        Assert.False(Mentions(session.Alice, pooled.Identifier));
    }
}
